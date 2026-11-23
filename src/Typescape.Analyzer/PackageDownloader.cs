using System.Collections.Concurrent;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace Typescape.Analyzer;

/// <summary>
/// Finds a package in the global packages folder or the enabled <c>nuget.config</c> sources, in order, and extracts it
/// into the <c>Id.Version</c> layout of <paramref name="resolver"/>'s folder. Safe to use from parallel workers.
/// </summary>
internal sealed class PackageDownloader(ISettings settings, PackagePathResolver resolver)
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

    /// <returns>The reason the package could not be fetched, or <c>null</c> when it was.</returns>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    public async Task<string?> FetchAsync(PackageIdentity package, CancellationToken cancellationToken)
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
