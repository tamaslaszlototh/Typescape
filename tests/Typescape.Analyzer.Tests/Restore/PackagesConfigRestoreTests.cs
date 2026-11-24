using NuGet.Packaging;
using NuGet.Versioning;
using Typescape.Analyzer.Restore;

namespace Typescape.Analyzer.Tests.Restore;

/// <summary>
/// Restores throwaway solutions whose only package source is a local folder and whose global packages folder is their
/// own, so no test touches the network or the user's NuGet caches.
/// </summary>
public sealed class PackagesConfigRestoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-packages-config-").FullName;

    public PackagesConfigRestoreTests() => NuGetConfig();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Each_project_gets_the_package_versions_its_packages_config_lists()
    {
        Publish("Typescape.Lib", "1.0.0");
        Publish("Typescape.Lib", "2.0.0");
        var solution = Solution(("Old", PackagesConfig(("Typescape.Lib", "1.0.0"))), ("New", PackagesConfig(("Typescape.Lib", "2.0.0"))));

        var warning = await RestoreAsync(solution);

        Assert.Null(warning);
        foreach (var version in new[] { "1.0.0", "2.0.0" })
        {
            var folder = Path.Combine(_directory, "packages", $"Typescape.Lib.{version}");
            Assert.True(File.Exists(Path.Combine(folder, $"Typescape.Lib.{version}.nupkg")));
            Assert.Equal(version, File.ReadAllText(Path.Combine(folder, "lib", "net472", "Typescape.Lib.dll")));
            Assert.True(File.Exists(Path.Combine(folder, "build", "Typescape.Lib.targets")));
        }
    }

    [Fact]
    public async Task Packages_go_to_the_repository_path_of_nuget_config()
    {
        NuGetConfig("""<add key="repositoryPath" value="lib/nuget" />""");
        Publish("Typescape.Lib", "1.0.0");
        var solution = Solution(("App", PackagesConfig(("Typescape.Lib", "1.0.0"))));

        Assert.Null(await RestoreAsync(solution));

        Assert.True(Directory.Exists(Path.Combine(_directory, "lib", "nuget", "Typescape.Lib.1.0.0", "lib")));
        Assert.False(Directory.Exists(Path.Combine(_directory, "packages")));
    }

    [Fact]
    public async Task A_missing_package_warns_and_the_others_still_restore()
    {
        Publish("Typescape.Lib", "1.0.0");
        var solution = Solution(
            ("App", PackagesConfig(("Typescape.Lib", "1.0.0"), ("Typescape.Missing", "3.1.4"))),
            ("Other", PackagesConfig(("Typescape.Lib", "1.0.0"))));

        var warning = await RestoreAsync(solution);

        Assert.NotNull(warning);
        Assert.StartsWith("packages.config restore failed; the run continues", warning);
        Assert.Contains("1 of 2 packages", warning);
        Assert.Contains("Typescape.Missing 3.1.4: not found in local", warning);
        Assert.True(Directory.Exists(Path.Combine(_directory, "packages", "Typescape.Lib.1.0.0", "lib")));
    }

    [Fact]
    public async Task A_package_in_the_global_packages_folder_is_reused()
    {
        var global = Path.Combine(_directory, "global", "typescape.cached", "1.0.0");
        Directory.CreateDirectory(global);
        File.WriteAllBytes(Path.Combine(global, "typescape.cached.1.0.0.nupkg"), Package("Typescape.Cached", "1.0.0"));
        var solution = Solution(("App", PackagesConfig(("Typescape.Cached", "1.0.0"))));

        Assert.Null(await RestoreAsync(solution));

        Assert.True(Directory.Exists(Path.Combine(_directory, "packages", "Typescape.Cached.1.0.0", "lib")));
    }

    [Fact]
    public async Task An_installed_package_is_left_alone()
    {
        var installed = Path.Combine(_directory, "packages", "Typescape.Lib.1.0.0");
        Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "Typescape.Lib.1.0.0.nupkg"), "");
        var solution = Solution(("App", PackagesConfig(("Typescape.Lib", "1.0.0"))));

        // The feed does not have it: a download would fail.
        Assert.Null(await RestoreAsync(solution));
    }

    [Fact]
    public async Task A_project_named_packages_config_wins_over_the_plain_one()
    {
        Publish("Typescape.Named", "1.0.0");
        var solution = Solution(("App", PackagesConfig(("Typescape.Missing", "1.0.0"))));
        File.WriteAllText(Path.Combine(_directory, "App", "packages.App.config"), PackagesConfig(("Typescape.Named", "1.0.0")));

        Assert.Null(await RestoreAsync(solution));

        Assert.True(Directory.Exists(Path.Combine(_directory, "packages", "Typescape.Named.1.0.0")));
    }

    [Fact]
    public async Task The_solution_level_packages_config_is_restored()
    {
        Publish("Typescape.Tool", "1.0.0");
        var solution = Solution(("App", null));
        Directory.CreateDirectory(Path.Combine(_directory, ".nuget"));
        File.WriteAllText(Path.Combine(_directory, ".nuget", "packages.config"), PackagesConfig(("Typescape.Tool", "1.0.0")));

        Assert.Null(await RestoreAsync(solution));

        Assert.True(Directory.Exists(Path.Combine(_directory, "packages", "Typescape.Tool.1.0.0")));
    }

    [Fact]
    public async Task A_solution_without_packages_config_restores_nothing()
    {
        var solution = Solution(("App", null));

        Assert.Null(await RestoreAsync(solution));

        Assert.False(Directory.Exists(Path.Combine(_directory, "packages")));
    }

    [Fact]
    public async Task A_malformed_packages_config_warns()
    {
        var solution = Solution(("App", "<packages><package id="));

        var warning = await RestoreAsync(solution);

        Assert.NotNull(warning);
        Assert.Contains("1 packages.config files could not be read.", warning);
        Assert.Contains(Path.Combine("App", "packages.config"), warning);
    }

    [Fact]
    public async Task A_broken_nuget_config_warns()
    {
        File.WriteAllText(Path.Combine(_directory, "nuget.config"), "<configuration><packageSources>");
        var solution = Solution(("App", PackagesConfig(("Typescape.Lib", "1.0.0"))));

        var warning = await RestoreAsync(solution);

        Assert.NotNull(warning);
        Assert.Contains("The NuGet settings could not be read: ", warning);
    }

    [Fact]
    public async Task Cancellation_stops_the_restore()
    {
        Publish("Typescape.Lib", "1.0.0");
        var solution = Solution(("App", PackagesConfig(("Typescape.Lib", "1.0.0"))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PackagesConfigRestore.RunAsync(new AnalysisRequest { SolutionPath = solution }, new CancellationToken(canceled: true)));
    }

    private static Task<string?> RestoreAsync(string solution) =>
        PackagesConfigRestore.RunAsync(new AnalysisRequest { SolutionPath = solution }, TestContext.Current.CancellationToken);

    private void NuGetConfig(string config = "")
    {
        Directory.CreateDirectory(Path.Combine(_directory, "feed"));
        File.WriteAllText(Path.Combine(_directory, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="feed" />
              </packageSources>
              <config>
                <add key="globalPackagesFolder" value="global" />
                {config}
              </config>
            </configuration>
            """);
    }

    private void Publish(string id, string version) =>
        File.WriteAllBytes(Path.Combine(_directory, "feed", $"{id}.{version}.nupkg"), Package(id, version));

    /// <summary>A package with a <c>lib/net472</c> assembly whose content is its version, and a <c>build</c> targets file.</summary>
    private static byte[] Package(string id, string version)
    {
        var builder = new PackageBuilder { Id = id, Version = NuGetVersion.Parse(version), Description = id };
        builder.Authors.Add("Typescape");
        builder.Files.Add(File($"lib/net472/{id}.dll", version));
        builder.Files.Add(File($"build/{id}.targets", "<Project />"));

        using var stream = new MemoryStream();
        builder.Save(stream);
        return stream.ToArray();

        static PhysicalPackageFile File(string path, string content) =>
            new(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))) { TargetPath = path };
    }

    private static string PackagesConfig(params (string Id, string Version)[] packages) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <packages>
          {string.Concat(packages.Select(p => $"""<package id="{p.Id}" version="{p.Version}" targetFramework="net472" />"""))}
        </packages>
        """;

    /// <returns>The path of a <c>.slnx</c> with one classic project per entry, each with its packages.config, if any.</returns>
    private string Solution(params (string Name, string? PackagesConfig)[] projects)
    {
        foreach (var (name, packagesConfig) in projects)
        {
            Directory.CreateDirectory(Path.Combine(_directory, name));
            File.WriteAllText(Path.Combine(_directory, name, $"{name}.csproj"), """<Project ToolsVersion="15.0" />""");
            if (packagesConfig is not null)
            {
                File.WriteAllText(Path.Combine(_directory, name, "packages.config"), packagesConfig);
            }
        }

        var solution = Path.Combine(_directory, "Test.slnx");
        File.WriteAllText(solution, $"""
            <Solution>
              {string.Concat(projects.Select(p => $"""<Project Path="{p.Name}\{p.Name}.csproj" />"""))}
            </Solution>
            """);
        return solution;
    }
}
