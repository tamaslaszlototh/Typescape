using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Typescape.Analyzer.Extraction;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.Pipeline;

namespace Typescape.Analyzer.Tests.Pipeline;

public class DiagnoseTests
{
    [Fact]
    public void A_compilation_with_its_core_library_resolves_System_Object_and_has_no_errors()
    {
        var diagnostics = Diagnose(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        Assert.True(diagnostics.ObjectResolves);
        Assert.Equal(0, diagnostics.CompileErrors.Count);
    }

    [Fact]
    public void A_compilation_without_references_does_not_resolve_System_Object_and_says_so_in_its_errors()
    {
        var diagnostics = Diagnose();

        Assert.False(diagnostics.ObjectResolves);
        Assert.Equal("CS0518", diagnostics.CompileErrors.Top[0].Code);
        Assert.Equal("Predefined type 'System.Object' is not defined or imported", diagnostics.CompileErrors.Top[0].Sample);
    }

    private static ProjectDiagnostics Diagnose(params MetadataReference[] references)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("App", LanguageNames.CSharp);
        var compilation = CSharpCompilation.Create(
            "App",
            [CSharpSyntaxTree.ParseText("class A { }", cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var compiled = new CompiledProject(
            new LoadedProject("App/App.csproj", project, "net10.0", ["net10.0"]),
            compilation,
            new ProjectContext("App/App.csproj", "/repo", Test: false, new HashSet<string>(), Occurrences: true));

        return new MSBuildStages().Diagnose(compiled, TestContext.Current.CancellationToken);
    }
}
