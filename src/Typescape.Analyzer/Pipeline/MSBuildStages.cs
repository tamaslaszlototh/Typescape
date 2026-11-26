using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Assembling;
using Typescape.Analyzer.Extraction;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.Restore;
using Typescape.Snapshot;

namespace Typescape.Analyzer.Pipeline;

/// <summary>The production stages: MSBuildWorkspace, Roslyn compilations and Typescape's own walkers.</summary>
internal sealed class MSBuildStages : IAnalysisStages
{
    public async Task<IReadOnlyList<string>> RestoreAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        string?[] warnings =
        [
            await DotnetRestore.RunAsync(request, cancellationToken),
            await PackagesConfigRestore.RunAsync(request, cancellationToken),
        ];
        return [.. warnings.OfType<string>()];
    }

    public Task<LoadedSolution> LoadAsync(AnalysisRequest request, PhaseCounter projects, CancellationToken cancellationToken) =>
        SolutionLoader.LoadAsync(request, projects, cancellationToken);

    public async Task<CompiledProject> CompileAsync(
        AnalysisRequest request,
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

        var context = new ProjectContext(project.Key, solution.Directory, solution.PackageFolders, project.Test, generatedPaths, request.Occurrences);
        return new CompiledProject(project, compilation, context);
    }

    public ProjectDiagnostics Diagnose(CompiledProject project, CancellationToken cancellationToken) =>
        new(
            CompileErrors.Summarize(project.Compilation.GetDiagnostics(cancellationToken), project.Context.SolutionDirectory),
            project.Compilation.GetSpecialType(SpecialType.System_Object).TypeKind != TypeKind.Error);

    public ProjectFacts WalkDeclarations(CompiledProject project, CancellationToken cancellationToken) =>
        ProjectExtractor.ExtractDeclarations(project.Compilation, project.Context, cancellationToken);

    public ProjectFacts WalkDocument(CompiledProject project, SyntaxTree document, CancellationToken cancellationToken) =>
        ProjectExtractor.ExtractDocument(project.Compilation, project.Context, document, cancellationToken);

    public SnapshotRecord Assemble(AssembleInput input, CancellationToken cancellationToken) =>
        SnapshotAssembler.Assemble(input, cancellationToken);
}
