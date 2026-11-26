using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Typescape.Analyzer.Assembling;
using Typescape.Analyzer.Extraction;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.Pipeline;
using Typescape.Analyzer.Tests.Extraction;
using Typescape.Analyzer.Tests.Fakes;
using Typescape.Snapshot;
using Typescape.Snapshot.Json;
using Typescape.Snapshot.Records;
using Typescape.Testing;

namespace Typescape.Analyzer.Tests.Assembling;

/// <summary>A project as Assemble receives it: its Walk parts, in the order the pipeline would hand them over.</summary>
/// <param name="Key">The project's key; its file name is the assembly name.</param>
internal sealed record TestProject(string Key, IReadOnlyList<ProjectFacts> Parts)
{
    public TestProject(string key, params ProjectFacts[] parts)
        : this(key, (IReadOnlyList<ProjectFacts>)parts)
    {
    }

    public string Assembly => Path.GetFileNameWithoutExtension(Key);

    public IReadOnlyList<string> References { get; init; } = [];

    public ProjectDiagnostics Diagnostics { get; init; } = new(new CompileErrorsRecord(0, []), ObjectResolves: true);

    /// <summary>Whether MSBuildWorkspace gave the project any metadata reference.</summary>
    public bool HasMetadataReferences { get; init; } = true;
}

/// <summary>Builds Assemble's input over real Roslyn projects and runs <see cref="SnapshotAssembler"/>.</summary>
internal static class AssembleTest
{
    private static readonly MetadataReference ObjectReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);

    public static SnapshotRecord Assemble(params TestProject[] projects) => Assemble(projects, unopened: []);

    public static SnapshotRecord Assemble(
        IEnumerable<TestProject> projects,
        IReadOnlyList<UnopenedProject> unopened,
        bool occurrences = true)
    {
        var workspace = new AdhocWorkspace();
        var results = new List<ProjectResult>();
        foreach (var project in projects)
        {
            var roslyn = workspace.AddProject(ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                project.Assembly,
                project.Assembly,
                LanguageNames.CSharp,
                metadataReferences: project.HasMetadataReferences ? [ObjectReference] : []));
            var loaded = new LoadedProject(project.Key, roslyn, "net10.0", ["net10.0"]) { References = project.References, Style = ProjectStyle.Sdk };
            var compiled = new CompiledProject(
                loaded,
                CSharpCompilation.Create(project.Assembly),
                Context(project.Key, occurrences));
            results.Add(new ProjectResult(compiled, project.Diagnostics, project.Parts));
        }

        using var solution = new LoadedSolution(workspace, workspace.CurrentSolution, RuleTest.SolutionDirectory, [.. results.Select(result => result.Project.Project)], unopened, [], [], []);
        var request = new AnalysisRequest { SolutionPath = Path.Combine(RuleTest.SolutionDirectory, "Fake.slnx"), Occurrences = occurrences };
        return SnapshotAssembler.Assemble(
            new AssembleInput(request, solution, results, Snapshots.WithStatuses().Provenance),
            TestContext.Current.CancellationToken);
    }

    public static ProjectContext Context(string projectKey, bool occurrences = true) =>
        new(projectKey, RuleTest.SolutionDirectory, [], Test: false, new HashSet<string>(), occurrences);

    /// <summary>The Walk parts of <paramref name="compilation"/>, as the pipeline produces them: declarations, then each document.</summary>
    public static ProjectFacts[] Walk(Compilation compilation, ProjectContext context)
    {
        var token = TestContext.Current.CancellationToken;
        return
        [
            ProjectExtractor.ExtractDeclarations(compilation, context, token),
            .. compilation.SyntaxTrees.Select(document => ProjectExtractor.ExtractDocument(compilation, context, document, token)),
        ];
    }

    public static string Render(SnapshotRecord snapshot) =>
        JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(SnapshotJson.Options) { WriteIndented = true }) + "\n";

    /// <summary>Compares with <c>Golden/&lt;test file&gt;.&lt;test method&gt;.verified.json</c>.</summary>
    public static void Match(SnapshotRecord snapshot, [CallerFilePath] string file = "", [CallerMemberName] string test = "") =>
        Golden.Match(Render(snapshot), $"{Path.GetFileNameWithoutExtension(file)}.{test}", "json");

    public static TypeFact Type(string key, string document, int start = 1, int end = 1, string ns = "") =>
        new(key, key[(key.LastIndexOf('.') + 1)..], DeclaredTypeKind.Class, DeclaredAccessibility.Public, false, false, false, 0, ns, null,
            [new TypeDeclarationFact(document, new LineSpan(start, end), end - start + 1, Generated: false)], end - start + 1);

    public static ExternalTypeFact External(string key) =>
        new(key, key[(key.LastIndexOf('.') + 1)..], key[..key.IndexOf('|', StringComparison.Ordinal)], "System", DeclaredTypeKind.Class);

    public static ProjectFacts Document(string path, params DependencyFact[] dependencies) =>
        ProjectFacts.Empty with { Documents = [new DocumentFact(path, null)], Dependencies = dependencies };
}
