using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Loading;
using Typescape.Snapshot.Records;
using Kind = Typescape.Snapshot.Records.WorkspaceDiagnosticKind;
using RoslynKind = Microsoft.CodeAnalysis.WorkspaceDiagnosticKind;

namespace Typescape.Analyzer.Tests.Loading;

/// <summary>
/// Classifies hand-made workspace diagnostics over an AdhocWorkspace, for loads only Windows with Visual Studio gives: an
/// SDK-style project evaluating a classic reference that Visual Studio MSBuild loaded again, under .NET SDK MSBuild.
/// </summary>
public sealed class LoadEvidenceTests : IDisposable
{
    private const string SdkImport =
        "The imported project \"C:\\Program Files\\dotnet\\sdk\\10.0.100\\Microsoft\\VisualStudio\\v18.0\\WebApplications\\Microsoft.WebApplication.targets\" was not found.";

    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-evidence-").FullName;
    private readonly AdhocWorkspace _workspace = new();

    public void Dispose()
    {
        _workspace.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_classic_references_failed_sdk_evaluation_is_info_of_the_sdk_project_that_kept_it()
    {
        var web = Classic("Web");
        var app = Sdk("App", references: [web]);

        var (projects, _, solutionDiagnostics) = Collect([app, web], Failure(web));

        Assert.Equal([new WorkspaceDiagnosticRecord(Kind.Info, Failure("Web/Web.csproj"))], Of(projects, "App"));
        Assert.Empty(Of(projects, "Web"));
        Assert.Empty(solutionDiagnostics);
    }

    [Fact]
    public void It_is_a_warning_of_an_sdk_project_whose_reference_the_workspace_dropped()
    {
        var web = Classic("Web");
        var app = Sdk("App", references: []) with { References = ["Web/Web.csproj"] };

        var (projects, _, _) = Collect([app, web], Failure(web));

        Assert.Equal([new WorkspaceDiagnosticRecord(Kind.Warning, Failure("Web/Web.csproj"))], Of(projects, "App"));
        Assert.Empty(Of(projects, "Web"));
    }

    [Fact]
    public void It_stays_a_failure_of_the_classic_project_when_no_sdk_project_references_it()
    {
        var web = Classic("Web");
        var other = Classic("Other", references: [web]);

        var (projects, _, _) = Collect([other, web], Failure(web));

        Assert.Equal([new WorkspaceDiagnosticRecord(Kind.Failure, Failure("Web/Web.csproj"))], Of(projects, "Web"));
        Assert.Empty(Of(projects, "Other"));
    }

    [Fact]
    public void A_failure_that_cites_no_sdk_directory_stays_a_failure_of_the_classic_project()
    {
        var web = Classic("Web");
        var app = Sdk("App", references: [web]);
        var own = $"Msbuild failed when processing the file '{web.Project.FilePath}' with message: The target \"Build\" does not exist.";

        var (projects, _, _) = Collect([app, web], new WorkspaceDiagnostic(RoslynKind.Failure, own));

        Assert.Equal([new WorkspaceDiagnosticRecord(Kind.Failure, "Msbuild failed when processing the file 'Web/Web.csproj' with message: The target \"Build\" does not exist.")], Of(projects, "Web"));
        Assert.Empty(Of(projects, "App"));
    }

    [Fact]
    public void A_classic_project_that_fell_back_to_the_sdk_keeps_its_failure()
    {
        var web = Classic("Web");
        var app = Sdk("App", references: [web]);
        var fallback = new WorkspaceDiagnostic(
            RoslynKind.Warning,
            $"The project '{web.Project.FilePath}' will be loaded with the .NET Core SDK because it could not be loaded with .NET Framework MSBuild.");

        var (projects, _, _) = Collect([app, web], Failure(web), fallback);

        Assert.Contains(new WorkspaceDiagnosticRecord(Kind.Failure, Failure("Web/Web.csproj")), Of(projects, "Web"));
        Assert.Empty(Of(projects, "App"));
    }

    private (LoadedProject[] Projects, UnopenedProject[] Unopened, WorkspaceDiagnosticRecord[] SolutionDiagnostics) Collect(
        LoadedProject[] projects,
        params WorkspaceDiagnostic[] diagnostics) =>
        LoadEvidence.Collect(projects, [], _workspace.CurrentSolution, diagnostics, [], _directory);

    private static IReadOnlyList<WorkspaceDiagnosticRecord> Of(LoadedProject[] projects, string name) =>
        projects.Single(project => project.Key == $"{name}/{name}.csproj").WorkspaceDiagnostics;

    private static string Failure(string path) => $"Msbuild failed when processing the file '{path}' with message: {SdkImport}";

    private static WorkspaceDiagnostic Failure(LoadedProject project) => new(RoslynKind.Failure, Failure(project.Project.FilePath!));

    private LoadedProject Sdk(string name, LoadedProject[] references) =>
        Project(name, """<Project Sdk="Microsoft.NET.Sdk" />""", references);

    private LoadedProject Classic(string name, LoadedProject[]? references = null) =>
        Project(name, """<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003" />""", references ?? []);

    private LoadedProject Project(string name, string content, LoadedProject[] references)
    {
        var path = Path.Combine(_directory, name, $"{name}.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        var info = ProjectInfo.Create(
            ProjectId.CreateNewId(name),
            VersionStamp.Default,
            name,
            name,
            LanguageNames.CSharp,
            filePath: path,
            projectReferences: references.Select(reference => new ProjectReference(reference.Project.Id)));
        var project = _workspace.AddProject(info);
        return new LoadedProject($"{name}/{name}.csproj", project, "net48", ["net48"])
        {
            References = [.. references.Select(reference => reference.Key)],
        };
    }
}
