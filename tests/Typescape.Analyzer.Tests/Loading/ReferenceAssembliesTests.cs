using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.Tests.Fakes;

namespace Typescape.Analyzer.Tests.Loading;

/// <summary>
/// Fetches fake reference-assembly packages from a local feed, into a throwaway root, with a throwaway global packages
/// folder, so no test touches the network or the user's caches.
/// </summary>
public sealed class ReferenceAssembliesTests : IDisposable
{
    private const string MissingV472 =
        "Msbuild failed when processing the file '/repo/App/App.csproj' with message: The reference assemblies for "
        + ".NETFramework,Version=v4.7.2 were not found. To resolve this, install the Developer Pack (SDK/Targeting Pack) "
        + "for this framework version or retarget your application.";

    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-refasm-").FullName;
    private readonly ReferenceAssemblies _cache;

    public ReferenceAssembliesTests()
    {
        _cache = new ReferenceAssemblies(Path.Combine(_directory, "cache"));
        FakeReferenceAssemblies.NuGetConfig(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("v4.7.2", "Microsoft.NETFramework.ReferenceAssemblies.net472")]
    [InlineData("v4.8", "Microsoft.NETFramework.ReferenceAssemblies.net48")]
    [InlineData("v4.8.1", "Microsoft.NETFramework.ReferenceAssemblies.net481")]
    [InlineData("v4.0", "Microsoft.NETFramework.ReferenceAssemblies.net40")]
    [InlineData("v3.5", "Microsoft.NETFramework.ReferenceAssemblies.net35")]
    public void Each_version_has_its_package(string version, string package)
    {
        Assert.Equal(package, ReferenceAssemblies.PackageId(version));
    }

    [Fact]
    public void The_missing_versions_come_from_the_diagnostics_each_once_without_the_cached_ones()
    {
        Directory.CreateDirectory(Path.Combine(_cache.Root, ".NETFramework", "v4.6.1"));
        WorkspaceDiagnostic[] diagnostics =
        [
            new(WorkspaceDiagnosticKind.Failure, MissingV472.Replace("v4.7.2", "v4.8", StringComparison.Ordinal)),
            new(WorkspaceDiagnosticKind.Failure, MissingV472),
            new(WorkspaceDiagnosticKind.Failure, MissingV472.Replace("App", "Other", StringComparison.Ordinal)),
            new(WorkspaceDiagnosticKind.Failure, MissingV472.Replace("v4.7.2", "v4.6.1", StringComparison.Ordinal)),
            new(WorkspaceDiagnosticKind.Failure, MissingV472.Replace("v4.7.2", "v4.0,Profile=Client", StringComparison.Ordinal)),
            new(WorkspaceDiagnosticKind.Warning, "Found project reference without a matching metadata reference: /repo/Lib/Lib.csproj"),
        ];

        Assert.Equal(["v4.0", "v4.7.2", "v4.8"], _cache.Missing(diagnostics));
    }

    [Fact]
    public async Task Versions_are_fetched_into_one_merged_root()
    {
        FakeReferenceAssemblies.Publish(_directory, "v4.7.2");
        FakeReferenceAssemblies.Publish(_directory, "v4.8");

        var failures = await FetchAsync("v4.7.2", "v4.8");

        Assert.Empty(failures);
        Assert.Equal([".NETFramework"], Directory.EnumerateFileSystemEntries(_cache.Root).Select(Path.GetFileName));
        foreach (var version in new[] { "v4.7.2", "v4.8" })
        {
            Assert.True(File.Exists(Path.Combine(_cache.Root, ".NETFramework", version, "mscorlib.dll")));
            Assert.True(File.Exists(Path.Combine(_cache.Root, ".NETFramework", version, "RedistList", "FrameworkList.xml")));
        }
    }

    [Fact]
    public async Task A_package_in_the_global_packages_folder_is_reused()
    {
        FakeReferenceAssemblies.CacheGlobally(_directory, "v4.8");

        Assert.Empty(await FetchAsync("v4.8"));

        Assert.True(File.Exists(Path.Combine(_cache.Root, ".NETFramework", "v4.8", "mscorlib.dll")));
    }

    [Fact]
    public async Task A_version_no_source_has_fails_with_the_reason_and_the_others_are_still_fetched()
    {
        FakeReferenceAssemblies.Publish(_directory, "v4.8");

        var failures = await FetchAsync("v4.7.2", "v4.8");

        var (version, reason) = Assert.Single(failures);
        Assert.Equal("v4.7.2", version);
        Assert.Equal("not found in test", reason);
        Assert.True(Directory.Exists(Path.Combine(_cache.Root, ".NETFramework", "v4.8")));
        Assert.False(Directory.Exists(Path.Combine(_cache.Root, ".NETFramework", "v4.7.2")));
    }

    [Fact]
    public async Task Offline_every_version_fails_with_the_unreachable_source()
    {
        // Nothing listens on the discard port, so the connection is refused at once; NuGet's own retries still take a
        // few seconds, but only the first version waits for them.
        FakeReferenceAssemblies.NuGetConfig(_directory, "http://127.0.0.1:9/v3/index.json");

        var failures = await _cache.FetchAsync(["v4.7.2", "v4.8"], _directory, maxParallelism: 1, TestContext.Current.CancellationToken);

        Assert.Equal(["v4.7.2", "v4.8"], failures.Keys.Order(StringComparer.Ordinal));
        Assert.All(failures.Values, reason => Assert.StartsWith("not found in any reachable source; test: ", reason));
        Assert.Equal([], Directory.EnumerateFileSystemEntries(_cache.Root));
    }

    [Fact]
    public async Task Cancellation_stops_the_fetch_and_leaves_no_staging_folder()
    {
        FakeReferenceAssemblies.Publish(_directory, "v4.8");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _cache.FetchAsync(["v4.8"], _directory, maxParallelism: 1, new CancellationToken(canceled: true)));

        Assert.Equal([], Directory.EnumerateFileSystemEntries(_cache.Root));
    }

    [Fact]
    public void The_default_root_is_the_operating_system_s_cache_directory()
    {
        var expected = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "Typescape")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Typescape");

        Assert.Equal(expected + Path.DirectorySeparatorChar, ReferenceAssemblies.Default.Root);
    }

    private Task<IReadOnlyDictionary<string, string>> FetchAsync(params string[] versions) =>
        _cache.FetchAsync(versions, _directory, maxParallelism: 2, TestContext.Current.CancellationToken);
}
