using Typescape.Analyzer.Pipeline;
using static Typescape.Analyzer.AnalysisProgress;

namespace Typescape.Analyzer.Tests;

public class CancellationTests
{
    private static readonly string[] Phases = ["restore", "load", "compile", "diagnostics", "walk", "assemble"];

    public static TheoryData<string, bool> PhasesAndObservance =>
        new(Phases.SelectMany(phase => new[] { (phase, true), (phase, false) }));

    public static TheoryData<string> AllPhases => new(Phases);

    public static TheoryData<string, bool> PhasesAfterLoadAndObservance =>
        new(Phases.Skip(2).SelectMany(phase => new[] { (phase, true), (phase, false) }));

    [Theory]
    [MemberData(nameof(PhasesAndObservance))]
    public async Task Cancelling_during_a_phase_throws_and_starts_no_later_phase(string phase, bool observes)
    {
        using var cancellation = new CancellationTokenSource();
        var progress = new RecordingProgress();
        var stages = new FakeStages(2, 3)
        {
            OnStart = started => { if (started == phase) cancellation.Cancel(); },
            ObservesCancellation = observes,
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(stages, progress, cancellation.Token));

        var later = Phases.SkipWhile(p => p != phase).Skip(1);
        Assert.All(later, p => Assert.DoesNotContain($"{p} start", stages.Log));
        Assert.DoesNotContain(progress.Events, e => e is LoadHealthReported);
    }

    [Theory]
    [MemberData(nameof(AllPhases))]
    public async Task The_token_reaches_the_stage_calls_of_every_phase(string phase)
    {
        using var cancellation = new CancellationTokenSource();
        var stages = new FakeStages(2, 3)
        {
            OnStart = started => { if (started == phase) cancellation.Cancel(); },
            ObservesCancellation = true,
        };

        // One call at a time, so the call that cancels is the only one in flight.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RunAsync(stages, new RecordingProgress(), cancellation.Token, maxParallelism: 1));

        Assert.DoesNotContain($"{phase} end", stages.Log);
    }

    [Theory]
    [MemberData(nameof(PhasesAfterLoadAndObservance))]
    public async Task Cancelling_after_load_disposes_the_workspace(string phase, bool observes)
    {
        using var cancellation = new CancellationTokenSource();
        var stages = new FakeStages(2, 3)
        {
            OnStart = started => { if (started == phase) cancellation.Cancel(); },
            ObservesCancellation = observes,
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(stages, new RecordingProgress(), cancellation.Token));

        Assert.True(stages.Workspace?.Disposed);
    }

    [Fact]
    public async Task Cancelling_while_load_ignores_the_token_disposes_the_workspace_it_returned()
    {
        using var cancellation = new CancellationTokenSource();
        var stages = new FakeStages(2) { OnStart = started => { if (started == "load") cancellation.Cancel(); } };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(stages, new RecordingProgress(), cancellation.Token));

        Assert.True(stages.Workspace?.Disposed);
        Assert.DoesNotContain("compile start", stages.Log);
    }

    [Fact]
    public async Task An_already_cancelled_run_starts_no_phase()
    {
        var progress = new RecordingProgress();
        var stages = new FakeStages(1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(stages, progress, new CancellationToken(canceled: true)));

        Assert.Empty(stages.Log);
        Assert.Empty(progress.Events);
    }

    [Fact]
    public async Task The_workspace_is_disposed_after_a_successful_run_and_after_a_failing_stage()
    {
        var succeeded = new FakeStages(1, 1);
        var failed = new FakeStages(1, 1) { FailingPhase = "walk" };

        await RunAsync(succeeded, new RecordingProgress(), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(failed, new RecordingProgress(), TestContext.Current.CancellationToken));

        Assert.True(succeeded.Workspace?.Disposed);
        Assert.True(failed.Workspace?.Disposed);
    }

    [Fact]
    public async Task A_failing_phase_reports_no_finished_event()
    {
        var progress = new RecordingProgress();
        var stages = new FakeStages(1, 1) { FailingPhase = "compile" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(stages, progress, TestContext.Current.CancellationToken));

        Assert.Contains(new PhaseStarted(AnalysisPhase.Compile), progress.Events);
        Assert.DoesNotContain(progress.Events, e => e is PhaseFinished { Phase: AnalysisPhase.Compile });
    }

    private static Task<AnalysisResult> RunAsync(
        FakeStages stages,
        IProgress<AnalysisProgress> progress,
        CancellationToken cancellationToken,
        int maxParallelism = 2) =>
        AnalysisPipeline.RunAsync(
            new AnalysisRequest { SolutionPath = "/repo/Fake.slnx", MaxParallelism = maxParallelism },
            stages,
            progress,
            cancellationToken);
}
