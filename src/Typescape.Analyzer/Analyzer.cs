using Typescape.Analyzer.Pipeline;

namespace Typescape.Analyzer;

public static class Analyzer
{
    /// <summary>
    /// Analyzes one solution: Restore → Load → Compile → Diagnostics → Walk → Assemble, with a barrier between phases.
    /// </summary>
    /// <param name="request">What to analyze and how; it mirrors the <c>analyze</c> flags.</param>
    /// <param name="progress">Receives the progress events; <c>null</c> when nobody listens.</param>
    /// <param name="cancellationToken">Cancels the run at any phase.</param>
    /// <returns>The Snapshot and its Load Health summary.</returns>
    public static Task<AnalysisResult> AnalyzeAsync(
        AnalysisRequest request,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken) =>
        AnalysisPipeline.RunAsync(request, new MSBuildStages(), progress, cancellationToken);
}
