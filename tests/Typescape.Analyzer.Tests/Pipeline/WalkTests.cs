using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Extraction;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.Pipeline;
using Typescape.Analyzer.Tests.Extraction;

namespace Typescape.Analyzer.Tests.Pipeline;

public class WalkTests
{
    [Fact]
    public void Walking_the_declarations_and_each_document_gives_the_facts_of_Extract()
    {
        var token = TestContext.Current.CancellationToken;
        var compilation = RuleTest.Compile(
        [
            ("App/A.cs", "partial class A { }"),
            ("App/B.cs", "partial class A { } class B { }"),
        ]);
        using var workspace = new AdhocWorkspace();
        var project = new CompiledProject(
            new LoadedProject(RuleTest.ProjectKey, workspace.AddProject("App", LanguageNames.CSharp), "net10.0", ["net10.0"]),
            compilation,
            RuleTest.Context);
        var stages = new MSBuildStages();

        var walked = ProjectFacts.Combine(
        [
            stages.WalkDeclarations(project, token),
            .. compilation.SyntaxTrees.Select(document => stages.WalkDocument(project, document, token)),
        ]);

        Assert.Equal(RuleTest.Render(ProjectExtractor.Extract(compilation, RuleTest.Context, token)), RuleTest.Render(walked));
    }

    [Fact]
    public void Cancelling_the_declarations_walk_is_not_an_analyzer_error()
    {
        var compilation = RuleTest.Compile([(RuleTest.SnippetPath, "class A { }")]);
        using var workspace = new AdhocWorkspace();
        var project = new CompiledProject(
            new LoadedProject(RuleTest.ProjectKey, workspace.AddProject("App", LanguageNames.CSharp), "net10.0", ["net10.0"]),
            compilation,
            RuleTest.Context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new MSBuildStages().WalkDeclarations(project, cancellation.Token));
    }
}
