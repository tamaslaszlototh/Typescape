using System.Collections.Concurrent;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

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

        var settings = Settings.LoadDefaultSettings(solutionDirectory);
        var packagesFolder = SettingsUtility.GetRepositoryPath(settings) ?? Path.Combine(solutionDirectory, "packages");
        var resolver = new PackagePathResolver(packagesFolder, useSideBySidePaths: true);
        var distinct = packages.DistinctBy(resolver.GetPackageDirectoryName, StringComparer.OrdinalIgnoreCase).ToArray();
        var missing = distinct.Where(package => resolver.GetInstalledPackageFilePath(package) is null).ToArray();

        var failed = new ConcurrentBag<string>();

        if (missing.Length > 0)
        {
            var downloader = new Downloader(settings, resolver);
            await Parallel.ForEachAsync(
                missing,
                new ParallelOptions { MaxDegreeOfParallelism = request.MaxParallelism, CancellationToken = cancellationToken },
                async (package, token) =>
                {
                    if (await downloader.RestoreAsync(package, token) is { } error)
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
        var serializer = SolutionSerializers.GetSerializerByMoniker(solution)
            ?? throw new InvalidOperationException($"'{Path.GetFileName(solution)}' is not a .sln or .slnx file.");
        var model = await serializer.OpenAsync(solution, cancellationToken);
        var solutionDirectory = Path.GetDirectoryName(solution)!;

        var projectConfigs = model.SolutionProjects
            .Select(project => Path.GetFullPath(project.FilePath.Replace('\\', Path.DirectorySeparatorChar), solutionDirectory))
            .Select(ProjectPackagesConfig);
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

    /// <summary>Finds a package in the global packages folder or the enabled sources, in order, and extracts it.</summary>
    private sealed class Downloader(ISettings settings, PackagePathResolver resolver)
    {
        private readonly VersionFolderPathResolver _globalPackages = new(SettingsUtility.GetGlobalPackagesFolder(settings));

        private readonly SourceRepository[] _sources = [.. new PackageSourceProvider(settings)
            .LoadPackageSources()
            .Where(source => source.IsEnabled)
            .Select(source => Repository.Factory.GetCoreV3(source))];

        private readonly PackageExtractionContext _extraction = new(
            PackageSaveMode.Defaultv2,
            XmlDocFileSaveMode.None,
            ClientPolicyContext.GetClientPolicy(settings, NullLogger.Instance),
            NullLogger.Instance);

        private readonly SourceCacheContext _cache = new();

        // A source that failed once (offline, bad credentials) is not asked again, so offline runs fail fast.
        private readonly ConcurrentDictionary<string, string> _deadSources = new(StringComparer.Ordinal);

        /// <returns>The reason the package could not be restored, or <c>null</c> when it was.</returns>
        public async Task<string?> RestoreAsync(PackageIdentity package, CancellationToken cancellationToken)
        {
            try
            {
                var cached = _globalPackages.GetPackageFilePath(package.Id, package.Version);
                if (File.Exists(cached))
                {
                    await using var stream = File.OpenRead(cached);
                    await ExtractAsync(cached, stream, cancellationToken);
                    return null;
                }

                var unreachable = new List<string>();
                foreach (var source in _sources)
                {
                    if (_deadSources.TryGetValue(source.PackageSource.Source, out var deadReason))
                    {
                        unreachable.Add(deadReason);
                        continue;
                    }

                    try
                    {
                        if (await TryDownloadAsync(source, package, cancellationToken))
                        {
                            return null;
                        }
                    }
                    catch (Exception e) when (e is FatalProtocolException or HttpRequestException)
                    {
                        var reason = $"{source.PackageSource.Name}: {e.Message}";
                        _deadSources.TryAdd(source.PackageSource.Source, reason);
                        unreachable.Add(reason);
                    }
                }

                return (_sources.Length, unreachable.Count) switch
                {
                    (0, _) => "no package source is enabled",
                    (_, 0) => $"not found in {string.Join(", ", _sources.Select(source => source.PackageSource.Name))}",
                    _ => $"not found in any reachable source; {string.Join("; ", unreachable)}",
                };
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return e.Message;
            }
        }

        private async Task<bool> TryDownloadAsync(SourceRepository source, PackageIdentity package, CancellationToken cancellationToken)
        {
            var finder = await source.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            if (finder is null)
            {
                return false;
            }

            await using var stream = new FileStream(
                Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
            if (!await finder.CopyNupkgToStreamAsync(package.Id, package.Version, stream, _cache, NullLogger.Instance, cancellationToken))
            {
                return false;
            }

            stream.Position = 0;
            await ExtractAsync(source.PackageSource.Source, stream, cancellationToken);
            return true;
        }

        private Task ExtractAsync(string source, Stream stream, CancellationToken cancellationToken) =>
            PackageExtractor.ExtractPackageAsync(source, stream, resolver, _extraction, cancellationToken);
    }
}
