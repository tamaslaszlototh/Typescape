using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Extraction;
using Typescape.Snapshot;

namespace Typescape.Analyzer.Pipeline;

/// <summary>The production stages: MSBuildWorkspace, Roslyn compilations and Typescape's own walkers.</summary>
internal sealed class MSBuildStages : IAnalysisStages
{
    public Task RestoreAsync(AnalysisRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Restore is not implemented yet.");

    public Task<LoadedSolution> LoadAsync(AnalysisRequest request, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Load is not implemented yet.");

    public async Task<CompiledProject> CompileAsync(
        LoadedSolution solution,
        LoadedProject project,
        CancellationToken cancellationToken)
    {
        var compilation = await project.Project.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Project '{project.Key}' does not support compilation.");

        var generated = await project.Project.GetSourceGeneratedDocumentsAsync(cancellationToken);
        var generatedPaths = generated
            .Select(document => document.FilePath)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        // Test detection is not implemented yet: every project is a non-test project until then.
        var context = new ProjectContext(project.Key, solution.Directory, Test: false, generatedPaths);
        return new CompiledProject(project, compilation, context);
    }

    public ProjectDiagnostics Diagnose(CompiledProject project, CancellationToken cancellationToken) =>
        new([.. project.Compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error)]);

    public ProjectFacts WalkDeclarations(CompiledProject project, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Declaration extraction is not implemented yet.");

    public ProjectFacts WalkDocument(CompiledProject project, SyntaxTree document, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Document extraction is not implemented yet.");

    public SnapshotRecord Assemble(AssembleInput input, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Assemble is not implemented yet.");
}
