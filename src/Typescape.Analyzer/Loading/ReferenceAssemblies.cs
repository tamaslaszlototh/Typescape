using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Typescape.Analyzer.Loading;

/// <summary>
/// Typescape's own .NET Framework reference assemblies: one merged root (<c>.NETFramework/v4.7.2</c>, <c>/v4.8</c>, …)
/// that every project gets as <c>TargetFrameworkRootPath</c>, so the same code binds to the same assemblies on every
/// machine, Developer Pack or not. Each version is fetched once, as
/// <c>Microsoft.NETFramework.ReferenceAssemblies.&lt;tfm&gt;</c>, from the solution's NuGet sources.
/// </summary>
/// <param name="root">The directory that holds <c>.NETFramework</c>.</param>
internal sealed partial class ReferenceAssemblies(string root)
{
    /// <summary>Pinned, so every machine gets the same reference assemblies.</summary>
    public static readonly NuGetVersion PackageVersion = new(1, 0, 3);

    public static ReferenceAssemblies Default { get; } = new(DefaultRoot());

    /// <summary>The absolute root, with a trailing separator, as the packages' own targets set it.</summary>
    public string Root { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

    /// <summary>
    /// The .NET Framework versions that the diagnostics name and <see cref="Root"/> lacks, e.g. <c>v4.7.2</c>, each once,
    /// sorted. MSB3644 names the versions MSBuild did not find, but other warnings name versions too, so the caller
    /// keeps only those a project targets.
    /// </summary>
    /// <remarks>
    /// MSB3644's text is localized and carries no code once it reaches the workspace, but the framework moniker in it
    /// is not translated.
    /// </remarks>
    public IReadOnlyList<string> Missing(IEnumerable<WorkspaceDiagnostic> diagnostics) =>
    [
        .. diagnostics
            .Select(diagnostic => MissingFramework().Match(diagnostic.Message))
            .Where(match => match.Success)
            .Select(match => match.Groups["version"].Value)
            .Where(version => !Directory.Exists(VersionDirectory(version)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Whether <paramref name="diagnostic"/> says that MSBuild did not find <paramref name="version"/>.</summary>
    public static bool Reports(WorkspaceDiagnostic diagnostic, string version) =>
        MissingFramework().Match(diagnostic.Message) is { Success: true } match && match.Groups["version"].Value == version;

    /// <summary>
    /// Whether <paramref name="diagnostic"/> is MSB3644 for <paramref name="version"/>, told from other diagnostics that
    /// name the version by its link to the Developer Packs, which no translation changes.
    /// </summary>
    public static bool AdvisesDeveloperPack(WorkspaceDiagnostic diagnostic, string version) =>
        Reports(diagnostic, version) && diagnostic.Message.Contains(DeveloperPackLink, StringComparison.OrdinalIgnoreCase);

    private const string DeveloperPackLink = "aka.ms/msbuild/developerpacks";

    /// <summary>
    /// Fetches each version into <see cref="Root"/>, from the global packages folder when it holds the package, else
    /// from the sources of the <c>nuget.config</c> files that apply to <paramref name="solutionDirectory"/>.
    /// </summary>
    /// <returns>The reason per version that could not be fetched; empty when every version was.</returns>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    public async Task<IReadOnlyDictionary<string, string>> FetchAsync(
        IReadOnlyCollection<string> versions,
        string solutionDirectory,
        int maxParallelism,
        CancellationToken cancellationToken)
    {
        var failures = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        ISettings settings;
        try
        {
            settings = Settings.LoadDefaultSettings(solutionDirectory);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return versions.ToDictionary(version => version, _ => e.Message, StringComparer.Ordinal);
        }

        // Extracted next to the root, so moving a version into place is one rename on the same volume.
        var staging = Directory.CreateDirectory(Path.Combine(Root, $".staging-{Guid.NewGuid():N}")).FullName;
        try
        {
            var resolver = new PackagePathResolver(staging, useSideBySidePaths: true);
            PackageDownloader downloader;
            try
            {
                downloader = new PackageDownloader(settings, resolver);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return versions.ToDictionary(version => version, _ => e.Message, StringComparer.Ordinal);
            }

            await Parallel.ForEachAsync(
                versions,
                new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = cancellationToken },
                async (version, token) =>
                {
                    var package = new PackageIdentity(PackageId(version), PackageVersion);
                    if (await downloader.FetchAsync(package, token) is { } error)
                    {
                        failures[version] = error;
                        return;
                    }

                    MoveIntoRoot(Path.Combine(staging, resolver.GetPackageDirectoryName(package), "build", ".NETFramework"));
                    if (!Directory.Exists(VersionDirectory(version)))
                    {
                        failures[version] = $"{package.Id} {package.Version} has no build/.NETFramework/{version} folder";
                    }
                });
        }
        finally
        {
            TryDelete(staging);
        }

        return failures;
    }

    /// <summary>E.g. <c>v4.7.2</c> → <c>Microsoft.NETFramework.ReferenceAssemblies.net472</c>.</summary>
    public static string PackageId(string version) =>
        $"Microsoft.NETFramework.ReferenceAssemblies.net{version[1..].Replace(".", "", StringComparison.Ordinal)}";

    private string VersionDirectory(string version) => Path.Combine(Root, ".NETFramework", version);

    // A package can hold several versions; one already in the root, e.g. from a parallel run, wins.
    private void MoveIntoRoot(string extracted)
    {
        if (!Directory.Exists(extracted))
        {
            return;
        }

        var frameworks = Directory.CreateDirectory(Path.Combine(Root, ".NETFramework")).FullName;
        foreach (var source in Directory.EnumerateDirectories(extracted))
        {
            var target = Path.Combine(frameworks, Path.GetFileName(source));
            try
            {
                if (!Directory.Exists(target))
                {
                    Directory.Move(source, target);
                }
            }
            catch (IOException) when (Directory.Exists(target))
            {
            }
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary><c>%LOCALAPPDATA%\Typescape</c> on Windows, <c>~/Library/Caches/Typescape</c> on macOS.</summary>
    private static string DefaultRoot() =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "Typescape")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Typescape");

    [GeneratedRegex(@"\.NETFramework,Version=(?<version>v\d+(?:\.\d+){1,2})\b")]
    private static partial Regex MissingFramework();
}
