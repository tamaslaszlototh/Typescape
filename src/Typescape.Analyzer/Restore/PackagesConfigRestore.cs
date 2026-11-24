using System.Collections.Concurrent;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Typescape.Analyzer.Restore;

/// <summary>
/// Restores the packages.config packages of the solution as <c>nuget restore</c> would: every <c>id/version</c>, from
/// the <c>nuget.config</c> sources, into the <c>Id.Version</c> layout of the solution's <c>packages/</c> folder or the
/// configured <c>repositoryPath</c>. A failure becomes a warning, never an exception.
/// </summary>
/// <remarks>
/// The .NET SDK compiles packages.config restore out, and <c>nuget.exe</c> would need Mono off Windows.
/// </remarks>
internal static class PackagesConfigRestore
{
    private const int MaxErrorLines = 10;

    /// <returns>The warning to show, or <c>null</c> when every package restored or there was nothing to restore.</returns>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    public static async Task<string?> RunAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var solution = Path.GetFullPath(request.SolutionPath);
        var solutionDirectory = Path.GetDirectoryName(solution)!;

        IReadOnlyList<string> configs;
        try
        {
            configs = await PackagesConfigFilesAsync(solution, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Warning($"The solution could not be read: {e.Message}", []);
        }

        if (configs.Count == 0)
        {
            return null;
        }

        var unreadable = new List<string>();
        var packages = ReadPackages(configs, unreadable);

        ISettings settings;
        PackagePathResolver resolver;
        try
        {
            settings = Settings.LoadDefaultSettings(solutionDirectory);
            var packagesFolder = SettingsUtility.GetRepositoryPath(settings) ?? Path.Combine(solutionDirectory, "packages");
            resolver = new PackagePathResolver(packagesFolder, useSideBySidePaths: true);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Warning($"The NuGet settings could not be read: {e.Message}", []);
        }

        var distinct = packages.DistinctBy(resolver.GetPackageDirectoryName, StringComparer.OrdinalIgnoreCase).ToArray();
        var missing = distinct.Where(package => resolver.GetInstalledPackageFilePath(package) is null).ToArray();

        var failed = new ConcurrentBag<string>();

        if (missing.Length > 0)
        {
            PackageDownloader downloader;
            try
            {
                downloader = new PackageDownloader(settings, resolver);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Warning($"The NuGet settings could not be read: {e.Message}", []);
            }

            await Parallel.ForEachAsync(
                missing,
                new ParallelOptions { MaxDegreeOfParallelism = request.MaxParallelism, CancellationToken = cancellationToken },
                async (package, token) =>
                {
                    if (await downloader.FetchAsync(package, token) is { } error)
                    {
                        failed.Add($"{package.Id} {package.Version}: {error}");
                    }
                });
        }

        if (unreadable.Count == 0 && failed.IsEmpty)
        {
            return null;
        }

        string?[] reasons =
        [
            unreadable.Count > 0 ? $"{unreadable.Count} packages.config files could not be read." : null,
            failed.IsEmpty ? null : $"{failed.Count} of {distinct.Length} packages could not be restored.",
        ];
        return Warning(string.Join(" ", reasons.OfType<string>()), [.. unreadable, .. failed.Order(StringComparer.Ordinal)]);
    }

    /// <summary>Each project's packages.config, plus the solution-level <c>.nuget/packages.config</c>.</summary>
    private static async Task<IReadOnlyList<string>> PackagesConfigFilesAsync(string solution, CancellationToken cancellationToken)
    {
        var solutionDirectory = Path.GetDirectoryName(solution)!;

        var projectConfigs = (await SolutionFile.ProjectPathsAsync(solution, cancellationToken)).Select(ProjectPackagesConfig);
        string[] solutionConfig = [Path.Combine(solutionDirectory, ".nuget", NuGetConstants.PackageReferenceFile)];

        return [.. projectConfigs.Concat(solutionConfig).Where(File.Exists).Distinct(StringComparer.Ordinal)];
    }

    // NuGet's rule: packages.<project name>.config wins over packages.config.
    private static string ProjectPackagesConfig(string project)
    {
        var directory = Path.GetDirectoryName(project)!;
        var named = Path.Combine(directory, $"packages.{Path.GetFileNameWithoutExtension(project).Replace(' ', '_')}.config");
        return File.Exists(named) ? named : Path.Combine(directory, NuGetConstants.PackageReferenceFile);
    }

    private static List<PackageIdentity> ReadPackages(IReadOnlyList<string> configs, List<string> unreadable)
    {
        var packages = new List<PackageIdentity>();
        foreach (var config in configs)
        {
            try
            {
                using var stream = File.OpenRead(config);
                packages.AddRange(new PackagesConfigReader(stream).GetPackages().Select(entry => entry.PackageIdentity));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PackagesConfigReaderException or System.Xml.XmlException)
            {
                unreadable.Add($"{config}: {e.Message}");
            }
        }

        return packages;
    }

    private static string Warning(string reason, IReadOnlyList<string> details)
    {
        var shown = details.Count <= MaxErrorLines
            ? details.Select(line => $"  {line}")
            : [.. details.Take(MaxErrorLines).Select(line => $"  {line}"), $"  … and {details.Count - MaxErrorLines} more."];
        return string.Join(
            Environment.NewLine,
            [$"packages.config restore failed; the run continues and Load Health shows the projects it damaged. {reason}", .. shown]);
    }
}
