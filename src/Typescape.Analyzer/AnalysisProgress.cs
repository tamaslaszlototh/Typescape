namespace Typescape.Analyzer;

/// <summary>An event <see cref="Analyzer.AnalyzeAsync"/> reports while it runs.</summary>
public abstract record AnalysisProgress
{
    private protected AnalysisProgress()
    {
    }

    public sealed record PhaseStarted(AnalysisPhase Phase) : AnalysisProgress;

    /// <summary>Load, Compile and Diagnostics count projects, Walk counts documents.</summary>
    public sealed record PhaseProgressed(AnalysisPhase Phase, int Done, int Total) : AnalysisProgress;

    public sealed record PhaseFinished(AnalysisPhase Phase, TimeSpan Duration) : AnalysisProgress;

    /// <summary>Something went wrong, but the run continues; e.g. a failed restore.</summary>
    public sealed record Warning(AnalysisPhase Phase, string Message) : AnalysisProgress;

    /// <summary>Reported after Assemble: a verdict needs the compiler errors of Diagnostics.</summary>
    public sealed record LoadHealthReported(LoadHealthSummary Summary) : AnalysisProgress;
}

public enum AnalysisPhase
{
    Restore,
    Load,
    Compile,
    Diagnostics,
    Walk,
    Assemble,
}
