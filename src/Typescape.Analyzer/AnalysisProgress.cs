namespace Typescape.Analyzer;

/// <summary>
/// An event <see cref="Analyzer.AnalyzeAsync"/> reports while it runs.
/// </summary>
public abstract record AnalysisProgress
{
    private protected AnalysisProgress()
    {
    }
}
