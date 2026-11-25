using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Extraction;
using Typescape.Analyzer.Loading;
using Typescape.Snapshot;

namespace Typescape.Analyzer.Pipeline;

/// <summary>
/// The work of each pipeline phase. <see cref="AnalysisPipeline"/> decides the order, the barriers and the parallelism;
/// the stages only do the work for one unit: the solution, a project, or a document.
/// </summary>
internal interface IAnalysisStages
{
    /// <summary>Restore: <c>dotnet restore</c> and the packages.config restore. A failure warns and continues.</summary>
    /// <returns>The warnings, one per failure; empty when everything restored.</returns>
    Task<IReadOnlyList<string>> RestoreAsync(AnalysisRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Load: opens the solution with its per-project load evidence. When it throws, it first disposes the workspace it
    /// opened, which stops the BuildHost.
    /// </summary>
    Task<LoadedSolution> LoadAsync(AnalysisRequest request, PhaseCounter projects, CancellationToken cancellationToken);

    /// <summary>Compile, for one project: its compilation and what Walk needs to know about it.</summary>
    Task<CompiledProject> CompileAsync(AnalysisRequest request, LoadedSolution solution, LoadedProject project, CancellationToken cancellationToken);

    /// <summary>
    /// Diagnostics, for one project: the full compiler diagnostics, summarized as Load Health evidence. Always runs,
    /// because it also pre-binds what Walk needs.
    /// </summary>
    ProjectDiagnostics Diagnose(CompiledProject project, CancellationToken cancellationToken);

    /// <summary>Walk, the part that runs once per project: the declarations.</summary>
    ProjectFacts WalkDeclarations(CompiledProject project, CancellationToken cancellationToken);

    /// <summary>Walk, the part that runs once per document of the compilation.</summary>
    ProjectFacts WalkDocument(CompiledProject project, SyntaxTree document, CancellationToken cancellationToken);

    /// <summary>Assemble: merges everything into the Snapshot. The only place where determinism is decided.</summary>
    SnapshotRecord Assemble(AssembleInput input, CancellationToken cancellationToken);
}
