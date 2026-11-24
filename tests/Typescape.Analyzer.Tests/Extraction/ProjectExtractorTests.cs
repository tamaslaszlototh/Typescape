namespace Typescape.Analyzer.Tests.Extraction;

public class ProjectExtractorTests
{
    [Fact]
    public void Each_document_is_keyed_by_its_path_relative_to_the_solution_directory()
    {
        var facts = RuleTest.Extract(
        [
            ("App/Program.cs", "class Program { }"),
            ("App/Orders/Order.cs", "namespace Orders; class Order { }"),
            ("../Shared/Link.cs", "class Link { }"),
        ]);

        RuleTest.Match(facts with { Types = [] });
    }

    [Fact]
    public void Extract_stops_when_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            RuleTest.Extract([(RuleTest.SnippetPath, "class A { }")], cancellationToken: cancellation.Token));
    }

    [Fact]
    public void A_snippet_that_does_not_compile_is_rejected_before_extraction()
    {
        var error = Assert.Throws<InvalidOperationException>(() => RuleTest.Extract("class A { Missing field; }"));

        Assert.Contains("CS0246", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_snippets_compile_against_the_reference_assemblies_as_a_real_build_does()
    {
        var compilation = RuleTest.Compile(
        [
            (RuleTest.SnippetPath, """
                using System.CodeDom.Compiler;
                using System.Linq;

                [GeneratedCode("tool", "1.0")]
                class A { int[] Sorted(int[] items) => items.Order().ToArray(); }
                """),
        ]);

        Assert.Equal("System.Runtime", compilation.GetSpecialType(Microsoft.CodeAnalysis.SpecialType.System_Object).ContainingAssembly.Name);
    }
}
