using Typescape.Analyzer.Restore;

namespace Typescape.Analyzer.Tests.Restore;

/// <summary>
/// Runs the real <c>dotnet restore</c> against a throwaway solution whose only package source is an empty local folder,
/// so no test touches the network.
/// </summary>
public sealed class DotnetRestoreTests : IDisposable
{
    private const string Net10 = "<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>";

    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-restore-").FullName;

    public DotnetRestoreTests()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "feed"));
        File.WriteAllText(Path.Combine(_directory, "nuget.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="feed" />
              </packageSources>
            </configuration>
            """);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task A_restorable_solution_restores_without_a_warning()
    {
        var solution = Solution("App", Net10);

        var warning = await RestoreAsync(solution);

        Assert.Null(warning);
        Assert.True(File.Exists(Path.Combine(_directory, "App", "obj", "project.assets.json")));
    }

    [Fact]
    public async Task A_failed_restore_becomes_a_warning_with_the_restore_errors()
    {
        var solution = Solution("App", Net10 + """
            <ItemGroup>
              <PackageReference Include="Typescape.Missing" Version="1.0.0" />
            </ItemGroup>
            """);

        var warning = await RestoreAsync(solution);

        Assert.NotNull(warning);
        Assert.StartsWith("Restore failed; the run continues", warning);
        Assert.Contains("NU1101", warning);
        Assert.Contains("Typescape.Missing", warning);
    }

    [Fact]
    public async Task The_request_s_properties_reach_the_restore()
    {
        var solution = Solution("App", Net10 + """
            <ItemGroup Condition="'$(Flavor)' == 'Broken'">
              <PackageReference Include="Typescape.Missing" Version="1.0.0" />
            </ItemGroup>
            """);

        Assert.Null(await RestoreAsync(solution));
        Assert.Contains("Typescape.Missing", await RestoreAsync(solution, new Dictionary<string, string> { ["Flavor"] = "Broken" }));
    }

    [Fact]
    public async Task Windows_targeting_projects_get_past_the_operating_system_check()
    {
        var solution = Solution("App", "<PropertyGroup><TargetFramework>net10.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup>");

        // Off Windows, the Windows Desktop reference pack still has to come from a NuGet source; the empty feed lacks it.
        var warning = await RestoreAsync(solution);

        Assert.DoesNotContain("NETSDK1100", warning ?? "");
    }

    [Fact]
    public async Task A_restore_that_races_on_an_obj_folder_is_retried_serially()
    {
        // The real race (one project restored under two path spellings) fails only a few runs in a hundred, so this
        // project fails the way it does, but only while the restore is parallel.
        var solution = Solution("App", Net10 + """
            <Target Name="RaceOnObjFolder" BeforeTargets="CollectPackageReferences" Condition="'$(RestoreDisableParallel)' != 'true'">
              <Error Text="The file 'obj/App.csproj.nuget.dgspec.json' already exists." />
            </Target>
            """);

        var warning = await RestoreAsync(solution);

        Assert.Null(warning);
    }

    [Fact]
    public async Task Cancellation_stops_the_restore()
    {
        var solution = Solution("App", Net10);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DotnetRestore.RunAsync(new AnalysisRequest { SolutionPath = solution }, new CancellationToken(canceled: true)));
    }

    private Task<string?> RestoreAsync(string solution, IReadOnlyDictionary<string, string>? properties = null) =>
        DotnetRestore.RunAsync(
            new AnalysisRequest { SolutionPath = solution, Properties = properties ?? new Dictionary<string, string>() },
            TestContext.Current.CancellationToken);

    /// <returns>The path of a <c>.slnx</c> with one SDK-style project made of <paramref name="body"/>.</returns>
    private string Solution(string project, string body)
    {
        Directory.CreateDirectory(Path.Combine(_directory, project));
        File.WriteAllText(Path.Combine(_directory, project, $"{project}.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              {body}
            </Project>
            """);

        var solution = Path.Combine(_directory, "Test.slnx");
        File.WriteAllText(solution, $"""
            <Solution>
              <Project Path="{project}/{project}.csproj" />
            </Solution>
            """);
        return solution;
    }
}
