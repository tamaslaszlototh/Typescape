using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Typescape.Analyzer.Pipeline;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Tests.Pipeline;

public class CompileErrorsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "repo");

    [Fact]
    public void Errors_are_counted_and_the_most_frequent_codes_come_first_with_a_sample_each()
    {
        var compilation = Compile(
            ("B.cs", "class B { Missing1 a; Missing2 b; void M() { undefined1(); } }"),
            ("A.cs", "class A { Missing3 c; void M() { undefined2(); } }"),
            ("C.cs", "class C { Missing4 d; }"));

        var errors = CompileErrors.Summarize(compilation.GetDiagnostics(TestContext.Current.CancellationToken), Root);

        Assert.Equal(6, errors.Count);
        Assert.Equal(
            [
                new CompileErrorCodeRecord("CS0246", 4, "The type or namespace name 'Missing3' could not be found (are you missing a using directive or an assembly reference?)"),
                new CompileErrorCodeRecord("CS0103", 2, "The name 'undefined2' does not exist in the current context"),
            ],
            errors.Top);
    }

    [Fact]
    public void Only_the_top_five_codes_are_kept_and_a_tie_goes_by_code()
    {
        var compilation = Compile(("A.cs", """
            class A
            {
                Missing a;
                void M() { undefined(); int x = "s"; }
                int N() { }
                void O() { const int c; }
                void P() { break; }
                void Q() { int y = 1 / 0; }
            }
            """));

        var errors = CompileErrors.Summarize(compilation.GetDiagnostics(TestContext.Current.CancellationToken), Root);

        Assert.Equal(7, errors.Count);
        Assert.Equal(["CS0020", "CS0029", "CS0103", "CS0139", "CS0145"], errors.Top.Select(code => code.Code));
        Assert.All(errors.Top, code => Assert.Equal(1, code.Count));
    }

    [Fact]
    public void Warnings_do_not_count_and_a_clean_compilation_has_no_errors()
    {
        var compilation = Compile(("A.cs", "class A { void M() { int unused; } }"));
        Assert.Contains(compilation.GetDiagnostics(TestContext.Current.CancellationToken), d => d.Severity == DiagnosticSeverity.Warning);

        var errors = CompileErrors.Summarize(compilation.GetDiagnostics(TestContext.Current.CancellationToken), Root);

        Assert.Equal(new CompileErrorsRecord(0, []), errors with { Top = [] });
        Assert.Empty(errors.Top);
    }

    [Fact]
    public void A_sample_names_paths_under_the_solution_directory_relatively()
    {
        var generated = Path.Combine(Root, "obj", "Generated.cs");
        var compilation = Compile(("A.cs", $"#error {generated} is stale"));

        var errors = CompileErrors.Summarize(compilation.GetDiagnostics(TestContext.Current.CancellationToken), Root);

        var code = Assert.Single(errors.Top);
        Assert.Equal("CS1029", code.Code);
        Assert.Equal($"#error: '{Path.Combine("obj", "Generated.cs")} is stale'", code.Sample);
    }

    private static CSharpCompilation Compile(params (string Path, string Source)[] documents) =>
        CSharpCompilation.Create(
            "Test",
            documents.Select(document => CSharpSyntaxTree.ParseText(
                document.Source,
                path: Path.Combine(Root, document.Path),
                cancellationToken: TestContext.Current.CancellationToken)),
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
