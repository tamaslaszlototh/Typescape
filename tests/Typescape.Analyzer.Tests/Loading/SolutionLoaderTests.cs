using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Infrastructure;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.MSBuild;
using Typescape.Analyzer.Packages;
using Typescape.Analyzer.Restore;
using Typescape.Analyzer.Tests.Fakes;
using Typescape.Snapshot.Records;
using static Typescape.Analyzer.AnalysisProgress;
using Kind = Typescape.Snapshot.Records.WorkspaceDiagnosticKind;
using RoslynKind = Microsoft.CodeAnalysis.WorkspaceDiagnosticKind;

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
                Assert.Equal(ProjectStyle.Sdk, multi.Style);
                Assert.False(multi.SdkFallback);
                Assert.NotEqual(0, multi.MetadataReferences);
                Assert.Empty(multi.WorkspaceDiagnostics);
            },
            single =>
            {
                Assert.Equal("Single/Single.csproj", single.Key);
                Assert.Equal("net10.0", single.Tfm);
                Assert.Equal(["net10.0"], single.AvailableTfms);
            });
        Assert.Equal(CanonicalPath.Of(_directory), solution.Directory);
        Assert.Empty(solution.SolutionDiagnostics);
    }

    [Fact]
    public async Task A_solution_reached_through_a_symbolic_link_is_loaded_by_its_canonical_path()
    {
        // Creating a symbolic link on Windows needs a privilege.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows paths are only made full.");
        Project("App", "<TargetFramework>net10.0</TargetFramework>");
        var link = Path.Combine(Directory.CreateTempSubdirectory("typescape-link-").FullName, "Link");
        Directory.CreateSymbolicLink(link, _directory);
        try
        {
            using var solution = await LoadAsync(Path.Combine(link, Path.GetFileName(Solution("App"))));

            Assert.Equal(CanonicalPath.Of(_directory), solution.Directory);
            var app = Assert.Single(solution.Projects);
            Assert.Equal("App/App.csproj", app.Key);
            Assert.All(app.Project.Documents, document => Assert.StartsWith(solution.Directory, document.FilePath!, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(link)!, recursive: true);
        }
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
        var (broken, good) = (solution.Projects[0], solution.Projects[1]);
        Assert.Null(broken.Tfm);
        Assert.Empty(broken.AvailableTfms);
        Assert.Equal(ProjectStyle.Sdk, broken.Style);
        var brokenFailure = Assert.Single(broken.WorkspaceDiagnostics);
        Assert.Equal(Kind.Failure, brokenFailure.Kind);
        Assert.StartsWith("Msbuild failed when processing the file 'Broken/Broken.csproj' with message: ", brokenFailure.Message);
        Assert.Empty(good.WorkspaceDiagnostics);

        var missing = Assert.Single(solution.UnopenedProjects);
        Assert.Equal(("Missing/Missing.csproj", null), (missing.Key, missing.Style));
        Assert.Equal([new WorkspaceDiagnosticRecord(Kind.Failure, "Project file not found: 'Missing/Missing.csproj'")], missing.WorkspaceDiagnostics);

        // The F# project is not analyzed, so what the workspace says about it belongs to the solution.
        Assert.All(solution.SolutionDiagnostics, d => Assert.Contains("Fsharp/Fsharp.fsproj", d.Message, StringComparison.Ordinal));
        Assert.Equal(solution.Diagnostics.Count, broken.WorkspaceDiagnostics.Count + missing.WorkspaceDiagnostics.Count + solution.SolutionDiagnostics.Count);
        Assert.DoesNotContain(_directory, string.Join(" ", solution.SolutionDiagnostics.Select(d => d.Message)), StringComparison.OrdinalIgnoreCase);
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
        LegacyProject("Legacy", "v4.7.2", targets: """
            <Target Name="WarnFramework" BeforeTargets="CoreCompile">
              <Warning Code="MSB3268" Text="The primary reference &quot;Lib&quot; depends on framework assemblies that are not in the currently targeted framework &quot;.NETFramework,Version=v4.7.2&quot;." />
            </Target>
            """);
        Project("Modern", "<TargetFramework>net10.0</TargetFramework>");
        var progress = new RecordingProgress();

        using var solution = await LoadAsync(Solution("Legacy", "Modern"), cache, progress);

        Assert.Equal(["Legacy/Legacy.csproj", "Modern/Modern.csproj"], solution.Projects.Select(project => project.Key));
        Assert.Empty(ReferencePaths(solution.Projects[0]));
        var failure = Assert.Single(solution.ReferenceAssemblyFailures);
        Assert.Equal("v4.7.2", failure.Version);
        Assert.Equal(["Legacy/Legacy.csproj"], failure.ProjectKeys);

        // MSBuild's advice to install a Developer Pack gives way to why the fetch failed; another diagnostic that names
        // the version keeps its own text.
        var legacy = solution.Projects[0];
        Assert.Contains(new WorkspaceDiagnosticRecord(Kind.Failure, failure.ProjectMessage), legacy.WorkspaceDiagnostics);
        Assert.DoesNotContain(legacy.WorkspaceDiagnostics, d => d.Message.Contains("Developer Pack", StringComparison.Ordinal));
        Assert.Contains(legacy.WorkspaceDiagnostics, d => d.Message.EndsWith("not in the currently targeted framework \".NETFramework,Version=v4.7.2\".", StringComparison.Ordinal));
        Assert.Empty(solution.Projects[1].WorkspaceDiagnostics);
        Assert.StartsWith("not found in any reachable source; test: ", failure.Reason);
        Assert.StartsWith(
            "The reference assemblies for .NET Framework v4.7.2 could not be fetched from the NuGet sources; "
            + "Legacy/Legacy.csproj load without framework references. not found in any reachable source",
            failure.Message);
        Assert.Equal([Counted(0, 2), Counted(1, 2), Counted(2, 2)], progress.Events);
    }

    [Fact]
    public async Task An_unresolved_reference_is_a_workspace_diagnostic()
    {
        Project("App", "<TargetFramework>net10.0</TargetFramework>", "<Reference Include=\"Missing.Lib\"><HintPath>../lib/Missing.Lib.dll</HintPath></Reference>");

        using var solution = await LoadAsync(Solution("App"));

        var app = Assert.Single(solution.Projects);
        var failure = Assert.Single(app.WorkspaceDiagnostics, d => d.Message.Contains("Missing.Lib", StringComparison.Ordinal));
        Assert.Equal(Kind.Failure, failure.Kind);
        Assert.StartsWith("Msbuild failed when processing the file 'App/App.csproj' with message: ", failure.Message);
    }

    [Fact]
    public async Task A_classic_project_is_analyzed_for_its_NET_Framework_version_and_compiles_cleanly()
    {
        var cache = new ReferenceAssemblies(Path.Combine(_directory, "cache"));
        FakeReferenceAssemblies.NuGetConfig(_directory);
        FakeReferenceAssemblies.Publish(_directory, "v4.8");
        LegacyProject("Legacy", "v4.8");

        using var solution = await LoadAsync(Solution("Legacy"), cache, new RecordingProgress());

        var legacy = Assert.Single(solution.Projects);
        Assert.Equal("net48", legacy.Tfm);
        Assert.Equal(["net48"], legacy.AvailableTfms);
        Assert.Equal(ProjectStyle.Legacy, legacy.Style);

        // On Windows with Visual Studio, this project goes to Visual Studio MSBuild; on macOS there is no Mono MSBuild.
        Assert.Equal(solution.Diagnostics.Any(d => d.Message.Contains("will be loaded with the .NET Core SDK", StringComparison.Ordinal)), legacy.SdkFallback);
        if (OperatingSystem.IsMacOS())
        {
            Assert.True(legacy.SdkFallback);
        }

        Assert.Equal(
            legacy.SdkFallback,
            legacy.WorkspaceDiagnostics.Any(d => d.Kind == Kind.Warning && d.Message.Contains("; Legacy/Legacy.csproj will be loaded with the .NET Core SDK", StringComparison.Ordinal)));
        var compilation = await legacy.Project.GetCompilationAsync(TestContext.Current.CancellationToken);
        Assert.Empty(compilation!.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task A_file_left_from_before_a_retarget_does_not_change_the_target_framework()
    {
        var cache = new ReferenceAssemblies(Path.Combine(_directory, "cache"));
        FakeReferenceAssemblies.NuGetConfig(_directory);
        FakeReferenceAssemblies.Publish(_directory, "v4.8");
        LegacyProject("Legacy", "v4.8", compile: "**\\*.cs");
        var stale = Path.Combine(_directory, "Legacy", "obj", "Debug", ".NETFramework,Version=v4.7.2.AssemblyAttributes.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(1));

        using var solution = await LoadAsync(Solution("Legacy"), cache, new RecordingProgress());

        var legacy = Assert.Single(solution.Projects);
        Assert.Contains(legacy.Project.Documents, document => document.FilePath == CanonicalPath.Of(stale));
        Assert.Equal("net48", legacy.Tfm);
        Assert.Equal(["net48"], legacy.AvailableTfms);
    }

    [Fact]
    public async Task A_version_only_a_reference_was_built_against_is_not_fetched()
    {
        var cache = new ReferenceAssemblies(Path.Combine(_directory, "cache"));
        FakeReferenceAssemblies.NuGetConfig(_directory);
        FakeReferenceAssemblies.Publish(_directory, "v4.7.2");
        Assert.Empty(await cache.FetchAsync(["v4.7.2"], _directory, maxParallelism: 1, TestContext.Current.CancellationToken));

        // MSB3274's text, raised by the project itself, so no real assembly built against v4.8 is needed.
        LegacyProject("Legacy", "v4.7.2", targets: """
            <Target Name="WarnNewer" BeforeTargets="CoreCompile">
              <Warning Code="MSB3274" Text="The primary reference &quot;Newer&quot; could not be resolved because it was built against the &quot;.NETFramework,Version=v4.8&quot; framework. This is a higher version than the currently targeted framework &quot;.NETFramework,Version=v4.7.2&quot;." />
            </Target>
            """);
        var progress = new RecordingProgress();

        using var solution = await LoadAsync(Solution("Legacy"), cache, progress);

        Assert.Contains(solution.Diagnostics, d => d.Message.Contains(".NETFramework,Version=v4.8", StringComparison.Ordinal));
        Assert.Equal("net472", Assert.Single(solution.Projects).Tfm);
        Assert.Empty(solution.ReferenceAssemblyFailures);
        Assert.Equal([Counted(0, 1), Counted(1, 1)], progress.Events);
    }

    [Fact]
    public async Task Each_project_gets_its_direct_references_and_they_agree_with_the_workspace()
    {
        Project("Core", "<TargetFramework>net10.0</TargetFramework>");
        Project("Domain", "<TargetFramework>net10.0</TargetFramework>", "<ProjectReference Include=\"..\\Core\\Core.csproj\" />");
        Project("App", "<TargetFramework>net10.0</TargetFramework>", "<ProjectReference Include=\"$(SolutionDir)Domain/Domain.csproj\" />");

        using var solution = await LoadAsync(Solution("App", "Core", "Domain"));

        Assert.Equal(
            [("App/App.csproj", ["Domain/Domain.csproj"]), ("Core/Core.csproj", []), ("Domain/Domain.csproj", ["Core/Core.csproj"])],
            solution.Projects.Select(project => (project.Key, project.References.ToArray())));
        Assert.All(solution.Projects, project => Assert.False(project.ProjectReferenceMismatch));
    }

    [Fact]
    public async Task After_a_restore_the_workspace_reports_the_closure_and_that_is_no_mismatch()
    {
        FakeReferenceAssemblies.NuGetConfig(_directory);
        Project("Deep", "<TargetFramework>net10.0</TargetFramework>");
        Project("Lib", "<TargetFramework>net10.0</TargetFramework>", "<ProjectReference Include=\"../Deep/Deep.csproj\" Condition=\"'$(TargetFramework)' == 'net10.0'\" />");
        Project("GeneratorDependency", "<TargetFramework>net10.0</TargetFramework>");
        Project("Generator", "<TargetFramework>net10.0</TargetFramework>", "<ProjectReference Include=\"../GeneratorDependency/GeneratorDependency.csproj\" />");
        Project("ToolDependency", "<TargetFramework>net10.0</TargetFramework>");
        Project("Tool", "<TargetFramework>net10.0</TargetFramework>", "<ProjectReference Include=\"../ToolDependency/ToolDependency.csproj\" />");
        Project("App", "<TargetFramework>net10.0</TargetFramework>", """
            <ProjectReference Include="../Lib/Lib.csproj" />
            <ProjectReference Include="../Generator/Generator.csproj" OutputItemType="Analyzer" />
            <ProjectReference Include="../Tool/Tool.csproj" ReferenceOutputAssembly="false" />
            """);
        var request = new AnalysisRequest { SolutionPath = Solution("App", "Deep", "Generator", "GeneratorDependency", "Lib", "Tool", "ToolDependency") };
        var warning = await DotnetRestore.RunAsync(request, TestContext.Current.CancellationToken);
        Assert.True(warning is null, warning);

        using var solution = await LoadAsync(request.SolutionPath);

        // The analyzer and its references stay in the workspace; the reference that only builds first goes, with its own.
        var app = solution.Projects[0];
        Assert.Equal(["Deep", "Generator", "GeneratorDependency", "Lib"], WorkspaceReferences(solution, app));
        Assert.Equal(["Lib/Lib.csproj"], app.References);
        Assert.All(solution.Projects, project => Assert.False(project.ProjectReferenceMismatch));
    }

    [Fact]
    public async Task A_classic_project_s_direct_references_are_all_the_workspace_reports_and_that_is_no_mismatch()
    {
        var cache = new ReferenceAssemblies(Path.Combine(_directory, "cache"));
        FakeReferenceAssemblies.NuGetConfig(_directory);
        FakeReferenceAssemblies.Publish(_directory, "v4.8");
        LegacyProject("Core", "v4.8");
        LegacyProject("Domain", "v4.8", items: """<ProjectReference Include="..\Core\Core.csproj"><Name>Core</Name></ProjectReference>""");
        LegacyProject("App", "v4.8", items: """<ProjectReference Include="..\Domain\Domain.csproj"><Name>Domain</Name></ProjectReference>""");

        using var solution = await LoadAsync(Solution("App", "Core", "Domain"), cache, new RecordingProgress());

        var app = solution.Projects[0];
        Assert.Equal(["Domain"], WorkspaceReferences(solution, app));
        Assert.Equal(["Domain/Domain.csproj"], app.References);
        Assert.All(solution.Projects, project => Assert.False(project.ProjectReferenceMismatch));

        // The workspace says each reference has no matching metadata reference, yet keeps it: harmless.
        Assert.Contains(new WorkspaceDiagnosticRecord(Kind.Info, "Found project reference without a matching metadata reference: Domain/Domain.csproj"), app.WorkspaceDiagnostics);
    }

    [Fact]
    public async Task A_project_whose_reference_the_files_do_not_show_is_flagged()
    {
        Project("Hidden", "<TargetFramework>net10.0</TargetFramework>");
        Project("App", "<TargetFramework>net10.0</TargetFramework>");
        var path = Path.Combine(_directory, "App", "App.csproj");
        File.WriteAllText(path, File.ReadAllText(path).Replace("</Project>", """
              <Target Name="AddHidden" BeforeTargets="AssignProjectConfiguration">
                <ItemGroup><ProjectReference Include="../Hidden/Hidden.csproj" /></ItemGroup>
              </Target>
            </Project>
            """, StringComparison.Ordinal));

        using var solution = await LoadAsync(Solution("App", "Hidden"));

        var (app, hidden) = (solution.Projects[0], solution.Projects[1]);
        Assert.Equal(["Hidden"], WorkspaceReferences(solution, app));
        Assert.Empty(app.References);
        Assert.True(app.ProjectReferenceMismatch);
        Assert.False(hidden.ProjectReferenceMismatch);
    }

    /// <summary>
    /// The scale check against the restored Jellyfin checkout of the benchmark research; skipped where that checkout,
    /// which is not part of the repository, is missing.
    /// </summary>
    [Fact]
    [Trait("Category", "Scale")]
    public async Task Jellyfin_has_92_direct_references_where_the_workspace_reports_386()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Typescape.slnx")))
        {
            root = Path.GetDirectoryName(root);
        }

        var jellyfin = Path.Combine(root ?? "", "temp", "research", "jellyfin-benchmark", "jellyfin", "Jellyfin.slnx");
        Assert.SkipUnless(File.Exists(jellyfin), $"No Jellyfin checkout at {jellyfin}.");

        using var solution = await LoadAsync(jellyfin);

        Assert.Equal(41, solution.Projects.Count);
        Assert.Equal(92, solution.Projects.Sum(project => project.References.Count));
        Assert.Equal(386, solution.Projects.Sum(project => project.Project.ProjectReferences.Count()));
        Assert.DoesNotContain(solution.Projects, project => project.ProjectReferenceMismatch);
        var server = solution.Projects.Single(project => project.Key == "Jellyfin.Server/Jellyfin.Server.csproj");
        Assert.Equal((7, 23), (server.References.Count, server.Project.ProjectReferences.Count()));

        // Its only workspace diagnostics: the analyzer project, referenced without a matching metadata reference.
        Assert.All(
            solution.Projects.SelectMany(project => project.WorkspaceDiagnostics),
            diagnostic => Assert.Equal(
                new WorkspaceDiagnosticRecord(Kind.Info, "Found project reference without a matching metadata reference: src/Jellyfin.CodeAnalysis/Jellyfin.CodeAnalysis.csproj"),
                diagnostic));
        Assert.Empty(solution.SolutionDiagnostics);
    }

    [Fact]
    public async Task Test_projects_are_flagged_by_framework_reference_name_and_override()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "lib"));
        File.WriteAllBytes(Path.Combine(_directory, "lib", "nunit.framework.dll"), FakeAssemblies.Empty("nunit.framework"));
        const string nunit = "<Reference Include=\"nunit.framework\"><HintPath>../lib/nunit.framework.dll</HintPath></Reference>";
        Project("App", "<TargetFramework>net10.0</TargetFramework>");
        Project("Checks", "<TargetFramework>net10.0</TargetFramework>", nunit);
        Project("App.Tests", "<TargetFramework>net10.0</TargetFramework>");
        Project("App.Fixtures", "<TargetFramework>net10.0</TargetFramework>");
        Project("App.Specs", "<TargetFramework>net10.0</TargetFramework>", nunit);
        var request = new AnalysisRequest { SolutionPath = "", TestPatterns = ["*.Fixtures"], NotTestPatterns = ["*.Specs"] };

        using var solution = await LoadAsync(Solution("App", "App.Fixtures", "App.Specs", "App.Tests", "Checks"), request);

        Assert.Equal(
            [
                ("App.Fixtures/App.Fixtures.csproj", true, "override"),
                ("App.Specs/App.Specs.csproj", false, "override"),
                ("App.Tests/App.Tests.csproj", true, "name"),
                ("App/App.csproj", false, null),
                ("Checks/Checks.csproj", true, "framework:nunit"),
            ],
            solution.Projects.Select(project => (project.Key, project.Test, project.TestReason)));
    }

    // The real package's layout decides which assemblies stand for it, so the user's NuGet cache serves as an offline
    // feed; without the package there, the test skips.
    [Fact]
    public async Task A_restored_Microsoft_NET_Test_Sdk_makes_a_test_project()
    {
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var versions = Path.Combine(cache, "microsoft.net.test.sdk");
        var version = Directory.Exists(versions)
            ? Directory.GetDirectories(versions).Select(Path.GetFileName).OfType<string>().Where(v => !v.Contains('-', StringComparison.Ordinal)).Max(NuGet.Versioning.NuGetVersion.Parse)
            : null;
        Assert.SkipUnless(version is not null, $"No Microsoft.NET.Test.Sdk in {cache}.");
        FakeReferenceAssemblies.NuGetConfig(_directory, cache);
        Project("Checks", "<TargetFramework>net10.0</TargetFramework>", $"<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"{version}\" />");
        var request = new AnalysisRequest { SolutionPath = Solution("Checks") };
        var warning = await DotnetRestore.RunAsync(request, TestContext.Current.CancellationToken);
        Assert.True(warning is null, warning);

        using var solution = await LoadAsync(request.SolutionPath);

        var checks = Assert.Single(solution.Projects);
        Assert.Equal((true, "framework:microsoft.net.test.sdk"), (checks.Test, checks.TestReason));
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

    private static string[] WorkspaceReferences(LoadedSolution solution, LoadedProject project) =>
        [.. project.Project.ProjectReferences
            .Select(reference => Path.GetFileNameWithoutExtension(solution.Solution.GetProject(reference.ProjectId)!.FilePath!))
            .Order(StringComparer.Ordinal)];

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
    private void LegacyProject(string name, string frameworkVersion, string? compile = null, string targets = "", string items = "")
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
                <Compile Include="{compile ?? $"{name}.cs"}" />
                {items}
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
              {targets}
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
