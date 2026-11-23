using Typescape.Analyzer.Extraction;

namespace Typescape.Analyzer.Pipeline;

/// <summary>Everything the phases before Assemble produced.</summary>
/// <param name="Request">The request the run was started with.</param>
/// <param name="Solution">The output of Load.</param>
/// <param name="Projects">One entry per project of <see cref="LoadedSolution.Projects"/>, in the same order.</param>
internal sealed record AssembleInput(AnalysisRequest Request, LoadedSolution Solution, IReadOnlyList<ProjectResult> Projects);

/// <summary>The outputs of Compile, Diagnostics and Walk for one project.</summary>
/// <param name="Project">The output of Compile.</param>
/// <param name="Diagnostics">The output of Diagnostics.</param>
/// <param name="Facts">
/// The output of Walk: the declarations' facts first, then one entry per document in the order of
/// <see cref="Microsoft.CodeAnalysis.Compilation.SyntaxTrees"/>.
/// </param>
internal sealed record ProjectResult(CompiledProject Project, ProjectDiagnostics Diagnostics, IReadOnlyList<ProjectFacts> Facts);
