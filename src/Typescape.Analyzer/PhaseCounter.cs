namespace Typescape.Analyzer;

internal sealed class PhaseCounter(AnalysisPhase phase, IProgress<AnalysisProgress>? progress)
{
    // Counting and reporting share one lock, so parallel workers never report a count out of order.
    private readonly Lock _lock = new();
    private int _done;
    private int? _total;

    public void Start(int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        lock (_lock)
        {
            if (_total is not null)
            {
                throw new InvalidOperationException($"The {phase} counter has already started.");
            }

            _total = total;
            progress?.Report(new AnalysisProgress.PhaseProgressed(phase, 0, total));
        }
    }

    /// <summary>Counts from zero again, towards the same total; e.g. when Load opens the solution a second time.</summary>
    public void Restart()
    {
        lock (_lock)
        {
            if (_total is not { } total)
            {
                throw new InvalidOperationException($"The {phase} counter has not started.");
            }

            _done = 0;
            progress?.Report(new AnalysisProgress.PhaseProgressed(phase, 0, total));
        }
    }

    public void Increment()
    {
        lock (_lock)
        {
            if (_total is not { } total)
            {
                throw new InvalidOperationException($"The {phase} counter has not started.");
            }

            if (_done == total)
            {
                throw new InvalidOperationException($"The {phase} counter is already at its total of {total}.");
            }

            _done++;
            progress?.Report(new AnalysisProgress.PhaseProgressed(phase, _done, total));
        }
    }
}
