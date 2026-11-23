using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.Pipeline;
using Typescape.Analyzer.Tests.Fakes;
using static Typescape.Analyzer.AnalysisProgress;

namespace Typescape.Analyzer.Tests.Loading;

/// <summary>
/// Opens throwaway solutions with the real MSBuildWorkspace. Nothing is restored, so no test touches the network:
/// MSBuild still evaluates every target framework, which is all the target framework choice needs.
/// </summary>
public sealed class SolutionLoaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-load-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Each_project_is_analyzed_once_for_its_highest_target_framework()
    {
        Project("Multi", "<TargetFrameworks>net8.0;net10.0</TargetFrameworks>");
        Project("Single", "<TargetFramework>net10.0</TargetFramework>", "<ProjectReference Include=\"../Multi/Multi.csproj\" />");

        using var solution = await LoadAsync(Solution("Multi", "Single"));

        Assert.Collection(
            solution.Projects,
            multi =>
            {
                Assert.Equal("Multi/Multi.csproj", multi.Key);
                Assert.Equal("net10.0", multi.Tfm);
                Assert.Equal(["net10.0", "net8.0"], multi.AvailableTfms);
                Assert.Equal("Multi(net10.0)", multi.Project.Name);
            },
            single =>
            {
                Assert.Equal("Single/Single.csproj", single.Key);
                Assert.Equal("net10.0", single.Tfm);
                Assert.Equal(["net10.0"], single.AvailableTfms);
            });
        Assert.Equal(Path.GetFullPath(_directory), solution.Directory);
    }

    [Fact]
    public async Task The_requested_framework_wins_and_a_project_without_it_falls_back_to_its_highest()
    {
        Project("Multi", "<TargetFrameworks>net8.0;net10.0</TargetFrameworks>");
        Project("Single", "<TargetFramework>net10.0</TargetFramework>");

        using var solution = await LoadAsync(Solution("Multi", "Single"), new AnalysisRequest { SolutionPath = "", Framework = "net8.0" });

        Assert.Equal([("Multi/Multi.csproj", "net8.0"), ("Single/Single.csproj", "net10.0")], solution.Projects.Select(p => (p.Key, p.Tfm)));
        Assert.Equal("Multi(net8.0)", solution.Projects[0].Project.Name);
    }

    [Fact]
    public async Task A_project_referenced_from_outside_the_solution_is_loaded_with_it()
    {
        Project("Outside", "<TargetFramework>net10.0</TargetFramework>");
        Project("App", "<TargetFramework>net10.0</TargetFramework>", "<ProjectReference Include=\"../Outside/Outside.csproj\" />");

        using var solution = await LoadAsync(Solution("App"));

        Assert.Equal(["App/App.csproj", "Outside/Outside.csproj"], solution.Projects.Select(project => project.Key));
    }

    [Fact]
    public async Task The_request_s_properties_reach_the_projects_and_the_configuration_defaults_to_Debug()
    {
        Project("App", "<TargetFramework>net10.0</TargetFramework><DefineConstants>$(DefineConstants);$(Flavor)</DefineConstants>");
        var path = Solution("App");

        using (var debug = await LoadAsync(path, new AnalysisRequest { SolutionPath = "", Properties = Properties(("Flavor", "BLUE")) }))
        {
            var symbols = Assert.Single(debug.Projects).Project.ParseOptions!.PreprocessorSymbolNames.ToArray();
            Assert.Contains("DEBUG", symbols);
            Assert.Contains("BLUE", symbols);
        }

        using var release = await LoadAsync(path, new AnalysisRequest { SolutionPath = "", Properties = Properties(("Configuration", "Release")) });
        var releaseSymbols = Assert.Single(release.Projects).Project.ParseOptions!.PreprocessorSymbolNames.ToArray();
        Assert.Contains("RELEASE", releaseSymbols);
        Assert.DoesNotContain("DEBUG", releaseSymbols);
    }

    [Fact]
    public async Task Windows_targeting_projects_load_on_every_operating_system()
    {
        Project("Desktop", "<TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF>");

        using var solution = await LoadAsync(Solution("Desktop"));

        // Off Windows, without EnableWindowsTargeting, MSBuild fails the project and Roslyn gives it no references.
        var desktop = Assert.Single(solution.Projects);
        Assert.Equal("net10.0-windows", desktop.Tfm);
        Assert.Empty(solution.Diagnostics);
        Assert.NotEmpty(desktop.Project.MetadataReferences);
    }

    [Fact]
    public async Task Load_failures_are_recorded_and_every_C_sharp_project_of_the_solution_is_counted()
    {
        Project("Good", "<TargetFramework>net10.0</TargetFramework>");
        Directory.CreateDirectory(Path.Combine(_directory, "Broken"));
        File.WriteAllText(Path.Combine(_directory, "Broken", "Broken.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>");
        Directory.CreateDirectory(Path.Combine(_directory, "Fsharp"));
        File.WriteAllText(Path.Combine(_directory, "Fsharp", "Fsharp.fsproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>
            """);
        var path = SolutionOf("Good/Good.csproj", "Broken/Broken.csproj", "Missing/Missing.csproj", "Fsharp/Fsharp.fsproj");
        var progress = new RecordingProgress();

        using var solution = await SolutionLoader.LoadAsync(
            new AnalysisRequest { SolutionPath = path },
            new PhaseCounter(AnalysisPhase.Load, progress),
            TestContext.Current.CancellationToken);

        Assert.Equal(["Broken/Broken.csproj", "Good/Good.csproj"], solution.Projects.Select(project => project.Key));
        var broken = solution.Projects[0];
        Assert.Null(broken.Tfm);
        Assert.Empty(broken.AvailableTfms);
        Assert.Equal(["Missing/Missing.csproj"], solution.UnopenedProjects);
        Assert.Contains(solution.Diagnostics, d => d.Kind == WorkspaceDiagnosticKind.Failure && d.Message.Contains("Broken.csproj", StringComparison.Ordinal));
        Assert.Contains(solution.Diagnostics, d => d.Kind == WorkspaceDiagnosticKind.Failure && d.Message.Contains("Missing.csproj", StringComparison.Ordinal));
        Assert.Equal(
            [Counted(0), Counted(1), Counted(2), Counted(3)],
            progress.Events);
    }

    [Fact]
    public async Task A_missing_NET_Framework_version_is_fetched_and_the_solution_is_opened_again_against_it()
    {
        var cache = new ReferenceAssemblies(Path.Combine(_directory, "cache"));
        FakeReferenceAssemblies.NuGetConfig(_directory);
        FakeReferenceAssemblies.Publish(_directory, "v4.8");
        LegacyProject("Legacy", "v4.8");
        var progress = new RecordingProgress();

        using var solution = await LoadAsync(Solution("Legacy"), cache, progress);

        var legacy = Assert.Single(solution.Projects);
        Assert.Contains(Path.Combine(cache.Root, ".NETFramework", "v4.8", "mscorlib.dll"), ReferencePaths(legacy));
        Assert.Empty(solution.ReferenceAssemblyFailures);
        Assert.Equal([Counted(0, 1), Counted(1, 1), Counted(0, 1), Counted(1, 1)], progress.Events);
    }

    [Fact]
    public async Task A_cached_NET_Framework_version_is_used_without_a_source_and_the_solution_is_opened_once()
    {
        var cache = new ReferenceAssemblies(Path.Combine(_directory, "cache"));
        FakeReferenceAssemblies.NuGetConfig(_directory);
        FakeReferenceAssemblies.Publish(_directory, "v4.8");
        Assert.Empty(await cache.FetchAsync(["v4.8"], _directory, maxParallelism: 1, TestContext.Current.CancellationToken));
        File.Delete(Directory.GetFiles(Path.Combine(_directory, "feed")).Single());
        LegacyProject("Legacy", "v4.8");
        var progress = new RecordingProgress();

        using var solution = await LoadAsync(Solution("Legacy"), cache, progress);

        Assert.Contains(Path.Combine(cache.Root, ".NETFramework", "v4.8", "mscorlib.dll"), ReferencePaths(Assert.Single(solution.Projects)));
        Assert.Empty(solution.ReferenceAssemblyFailures);
        Assert.Equal([Counted(0, 1), Counted(1, 1)], progress.Events);
    }

    [Fact]
    public async Task Offline_the_projects_of_a_missing_version_load_without_framework_references_and_say_why()
    {
        var cache = new ReferenceAssemblies(Path.Combine(_directory, "cache"));
        FakeReferenceAssemblies.NuGetConfig(_directory, "http://127.0.0.1:9/v3/index.json");
        LegacyProject("Legacy", "v4.7.2");
        Project("Modern", "<TargetFramework>net10.0</TargetFramework>");
        var progress = new RecordingProgress();

        using var solution = await LoadAsync(Solution("Legacy", "Modern"), cache, progress);

        Assert.Equal(["Legacy/Legacy.csproj", "Modern/Modern.csproj"], solution.Projects.Select(project => project.Key));
        Assert.Empty(ReferencePaths(solution.Projects[0]));
        var failure = Assert.Single(solution.ReferenceAssemblyFailures);
        Assert.Equal("v4.7.2", failure.Version);
        Assert.Equal(["Legacy/Legacy.csproj"], failure.ProjectKeys);
        Assert.StartsWith("not found in any reachable source; test: ", failure.Reason);
        Assert.StartsWith(
            "The reference assemblies for .NET Framework v4.7.2 could not be fetched from the NuGet sources; "
            + "Legacy/Legacy.csproj load without framework references. not found in any reachable source",
            failure.Message);
        Assert.Equal([Counted(0, 2), Counted(1, 2), Counted(2, 2)], progress.Events);
    }

    [Fact]
    public async Task A_file_that_is_not_a_solution_fails_the_load()
    {
        var path = Path.Combine(_directory, "App.csproj");
        File.WriteAllText(path, "<Project />");

        await Assert.ThrowsAsync<InvalidOperationException>(() => LoadAsync(path));
    }

    [Fact]
    public async Task Cancellation_stops_the_load()
    {
        Project("App", "<TargetFramework>net10.0</TargetFramework>");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SolutionLoader.LoadAsync(
            new AnalysisRequest { SolutionPath = Solution("App") },
            new PhaseCounter(AnalysisPhase.Load, progress: null),
            new CancellationToken(canceled: true)));
    }

    private static AnalysisProgress Counted(int done, int total = 3) => new PhaseProgressed(AnalysisPhase.Load, done, total);

    private static string?[] ReferencePaths(LoadedProject project) =>
        [.. project.Project.MetadataReferences.OfType<PortableExecutableReference>().Select(reference => reference.FilePath)];

    private static Task<LoadedSolution> LoadAsync(string solution, ReferenceAssemblies cache, RecordingProgress progress) =>
        SolutionLoader.LoadAsync(
            new AnalysisRequest { SolutionPath = solution },
            cache,
            new PhaseCounter(AnalysisPhase.Load, progress),
            TestContext.Current.CancellationToken);

    private static Dictionary<string, string> Properties(params (string Name, string Value)[] properties) =>
        properties.ToDictionary(property => property.Name, property => property.Value);

    private static Task<LoadedSolution> LoadAsync(string solution, AnalysisRequest? request = null) =>
        SolutionLoader.LoadAsync(
            (request ?? new AnalysisRequest { SolutionPath = "" }) with { SolutionPath = solution },
            new PhaseCounter(AnalysisPhase.Load, progress: null),
            TestContext.Current.CancellationToken);

    private void Project(string name, string properties, string items = "")
    {
        var directory = Path.Combine(_directory, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>{properties}</PropertyGroup>
              <ItemGroup>{items}</ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, $"{name}.cs"), $"namespace {name}; public class {name}Type {{ }}");
    }

    /// <summary>A classic project, as Visual Studio writes it, with one source file.</summary>
    private void LegacyProject(string name, string frameworkVersion)
    {
        var directory = Path.Combine(_directory, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}.csproj"), $"""
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <AssemblyName>{name}</AssemblyName>
                <TargetFrameworkVersion>{frameworkVersion}</TargetFrameworkVersion>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="{name}.cs" />
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
            </Project>
            """);
        File.WriteAllText(Path.Combine(directory, $"{name}.cs"), $"namespace {name} {{ public class {name}Type {{ }} }}");
    }

    private string Solution(params string[] projects) =>
        SolutionOf([.. projects.Select(project => $"{project}/{project}.csproj")]);

    private string SolutionOf(params string[] projectPaths)
    {
        var path = Path.Combine(_directory, "Test.slnx");
        File.WriteAllText(path, $"""
            <Solution>
            {string.Join(Environment.NewLine, projectPaths.Select(project => $"  <Project Path=\"{project}\" />"))}
            </Solution>
            """);
        return path;
    }
}
