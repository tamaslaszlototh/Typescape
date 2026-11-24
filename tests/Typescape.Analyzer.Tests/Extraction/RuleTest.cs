using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Typescape.Analyzer.Extraction;
using Typescape.Snapshot.Json;
using Typescape.Testing;

namespace Typescape.Analyzer.Tests.Extraction;

/// <summary>
/// Rule tests' way into extraction: snippets compiled in memory, a hand-built <see cref="ProjectContext"/>, and golden
/// files. Rule tests never reach past <see cref="ProjectExtractor.Extract"/>.
/// </summary>
internal static class RuleTest
{
    public const string ProjectKey = "App/App.csproj";

    public const string SnippetPath = "App/Snippet.cs";

    public static string SolutionDirectory { get; } = Path.GetFullPath("/repo");

    public static ProjectContext Context { get; } = new(ProjectKey, SolutionDirectory, Test: false, new HashSet<string>());

    private static MetadataReference[] Framework { get; } = FrameworkReferences();

    private static JsonSerializerOptions Json { get; } = new(SnapshotJson.Options) { WriteIndented = true };

    public static ProjectFacts Extract(string source) => Extract([(SnippetPath, source)]);

    /// <param name="documents">Paths relative to the solution directory.</param>
    /// <param name="cancellationToken">The test's token when <c>null</c>.</param>
    public static ProjectFacts Extract(IEnumerable<(string Path, string Source)> documents, CancellationToken? cancellationToken = null) =>
        Extract(Compile(documents), cancellationToken: cancellationToken);

    /// <param name="context"><see cref="Context"/> when <c>null</c>.</param>
    /// <param name="cancellationToken">The test's token when <c>null</c>.</param>
    public static ProjectFacts Extract(Compilation compilation, ProjectContext? context = null, CancellationToken? cancellationToken = null) =>
        ProjectExtractor.Extract(compilation, context ?? Context, cancellationToken ?? TestContext.Current.CancellationToken);

    /// <summary>The absolute file path a document compiled from <paramref name="path"/> has.</summary>
    /// <param name="path">Relative to the solution directory.</param>
    public static string FullPath(string path) => Path.GetFullPath(Path.Combine(SolutionDirectory, path));

    /// <param name="documents">Paths relative to the solution directory.</param>
    /// <param name="allowedErrors">The compiler error codes the snippet may have, e.g. <c>CS0116</c>.</param>
    /// <exception cref="InvalidOperationException">The documents have any other compile error.</exception>
    public static CSharpCompilation Compile(
        IEnumerable<(string Path, string Source)> documents,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        IReadOnlyCollection<string>? allowedErrors = null)
    {
        var trees = documents.Select(document => CSharpSyntaxTree.ParseText(
            document.Source,
            new CSharpParseOptions(LanguageVersion.Latest),
            FullPath(document.Path)));
        var compilation = CSharpCompilation.Create(
            "App",
            trees,
            Framework,
            new CSharpCompilationOptions(outputKind));

        // A typo in a snippet would otherwise surface as a puzzling golden diff.
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && allowedErrors?.Contains(diagnostic.Id) != true)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException($"The snippet does not compile:\n{string.Join('\n', errors)}");
        }

        return compilation;
    }

    /// <summary>
    /// Only the chosen fact lists of <paramref name="facts"/>, always with its analyzer errors, so a new fact list leaves
    /// existing goldens alone.
    /// </summary>
    public static ProjectFacts Keep(ProjectFacts facts, bool documents = false, bool types = false, bool members = false) =>
        ProjectFacts.Empty with
        {
            Documents = documents ? facts.Documents : [],
            Types = types ? facts.Types : [],
            ExecutableMembers = members ? facts.ExecutableMembers : [],
            AnalyzerErrors = facts.AnalyzerErrors,
        };

    public static string Render(ProjectFacts facts) => JsonSerializer.Serialize(facts, Json) + "\n";

    /// <summary>Compares with <c>Golden/&lt;test file&gt;.&lt;test method&gt;.verified.json</c>.</summary>
    public static void Match(ProjectFacts facts, [CallerFilePath] string file = "", [CallerMemberName] string test = "") =>
        Golden.Match(Render(facts), $"{Path.GetFileNameWithoutExtension(file)}.{test}", "json");

    // The targeting pack's reference assemblies, as a real build compiles against: the runtime's own assemblies would
    // put External Types in System.Private.CoreLib instead of System.Runtime.
    private static MetadataReference[] FrameworkReferences()
    {
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var dotnet = Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));
        var packs = Path.Combine(dotnet, "packs", "Microsoft.NETCore.App.Ref");
        var tfm = $"net{Environment.Version.Major}.{Environment.Version.Minor}";

        var exact = Path.Combine(packs, Environment.Version.ToString(), "ref", tfm);
        var directory = Directory.Exists(exact)
            ? exact
            : Directory.EnumerateDirectories(packs).Select(version => Path.Combine(version, "ref", tfm)).LastOrDefault(Directory.Exists)
              ?? throw new InvalidOperationException($"No {tfm} targeting pack under {packs}.");

        return [.. Directory.EnumerateFiles(directory, "*.dll").Order(StringComparer.Ordinal).Select(path => MetadataReference.CreateFromFile(path))];
    }
}
