using Typescape.Analyzer.Pipeline;
using Typescape.Analyzer.Tests.Fakes;
using Typescape.Snapshot.Records;
using static Typescape.Analyzer.AnalysisProgress;

namespace Typescape.Analyzer.Tests.Pipeline;

public class AnalysisProgressTests
{
    [Fact]
    public async Task Each_phase_reports_started_its_counter_and_finished_then_the_Load_Health_summary_comes_last()
    {
        var progress = new RecordingProgress();
        var stages = new FakeStages(2, 1) { Snapshot = Snapshots.WithStatuses(LoadStatus.Ok, LoadStatus.Failed) };

        await RunAsync(stages, progress, maxParallelism: 1);

        var events = progress.Events.Select(e => e is PhaseFinished finished ? finished with { Duration = TimeSpan.Zero } : e);
        Assert.Equal<AnalysisProgress>(
            [
                Started(AnalysisPhase.Restore), Finished(AnalysisPhase.Restore),
                Started(AnalysisPhase.Load), Counted(AnalysisPhase.Load, 0, 2), Counted(AnalysisPhase.Load, 1, 2),
                Counted(AnalysisPhase.Load, 2, 2), Finished(AnalysisPhase.Load),
                Started(AnalysisPhase.Compile), Counted(AnalysisPhase.Compile, 0, 2), Counted(AnalysisPhase.Compile, 1, 2),
                Counted(AnalysisPhase.Compile, 2, 2), Finished(AnalysisPhase.Compile),
                Started(AnalysisPhase.Diagnostics), Counted(AnalysisPhase.Diagnostics, 0, 2),
                Counted(AnalysisPhase.Diagnostics, 1, 2), Counted(AnalysisPhase.Diagnostics, 2, 2),
                Finished(AnalysisPhase.Diagnostics),
                Started(AnalysisPhase.Walk), Counted(AnalysisPhase.Walk, 0, 3), Counted(AnalysisPhase.Walk, 1, 3),
                Counted(AnalysisPhase.Walk, 2, 3), Counted(AnalysisPhase.Walk, 3, 3), Finished(AnalysisPhase.Walk),
                Started(AnalysisPhase.Assemble), Finished(AnalysisPhase.Assemble),
                new LoadHealthReported(new LoadHealthSummary(Ok: 1, Degraded: 0, Failed: 1)),
            ],
            events);
    }

    [Fact]
    public async Task A_skipped_restore_reports_nothing()
    {
        var progress = new RecordingProgress();

        await RunAsync(new FakeStages(1), progress, maxParallelism: 1, restore: false);

        Assert.DoesNotContain(progress.Events, e => e is PhaseStarted { Phase: AnalysisPhase.Restore });
        Assert.IsType<PhaseStarted>(progress.Events[0]);
        Assert.Equal(AnalysisPhase.Load, ((PhaseStarted)progress.Events[0]).Phase);
    }

    [Fact]
    public async Task Restore_warnings_are_reported_inside_the_restore_phase_and_the_run_continues()
    {
        var progress = new RecordingProgress();
        var stages = new FakeStages(1) { RestoreWarnings = ["dotnet restore failed", "packages.config restore failed"] };

        await RunAsync(stages, progress, maxParallelism: 1);

        Assert.Equal<AnalysisProgress>(
            [
                Started(AnalysisPhase.Restore),
                new Warning(AnalysisPhase.Restore, "dotnet restore failed"),
                new Warning(AnalysisPhase.Restore, "packages.config restore failed"),
            ],
            progress.Events.Take(3));
        Assert.Contains(progress.Events, e => e is PhaseFinished { Phase: AnalysisPhase.Assemble });
    }

    [Fact]
    public async Task Finished_phases_carry_their_measured_duration()
    {
        var progress = new RecordingProgress();

        await RunAsync(new FakeStages(1, 1), progress, maxParallelism: 1);

        var finished = progress.Events.OfType<PhaseFinished>().ToArray();
        Assert.Equal(Enum.GetValues<AnalysisPhase>(), finished.Select(e => e.Phase));
        Assert.All(finished, e => Assert.True(e.Duration >= TimeSpan.FromMilliseconds(1), $"{e.Phase} took {e.Duration}."));
    }

    [Fact]
    public async Task Parallel_counters_never_go_backwards_and_reach_their_total()
    {
        var progress = new RecordingProgress();

        await RunAsync(new FakeStages(7, 3, 9, 1, 4), progress, maxParallelism: 4);

        var totals = new Dictionary<AnalysisPhase, int>
        {
            [AnalysisPhase.Load] = 5,
            [AnalysisPhase.Compile] = 5,
            [AnalysisPhase.Diagnostics] = 5,
            [AnalysisPhase.Walk] = 24,
        };
        Assert.All(totals, pair =>
        {
            var counts = progress.Events.OfType<PhaseProgressed>().Where(e => e.Phase == pair.Key).ToArray();
            Assert.All(counts, e => Assert.Equal(pair.Value, e.Total));
            Assert.Equal(Enumerable.Range(0, pair.Value + 1), counts.Select(e => e.Done));
        });
    }

    [Fact]
    public async Task Counter_events_sit_between_their_phase_s_started_and_finished()
    {
        var progress = new RecordingProgress();

        await RunAsync(new FakeStages(3, 3, 3), progress, maxParallelism: 3);

        AnalysisPhase? current = null;
        foreach (var e in progress.Events)
        {
            switch (e)
            {
                case PhaseStarted started:
                    Assert.Null(current);
                    current = started.Phase;
                    break;
                case PhaseProgressed counted:
                    Assert.Equal(current, counted.Phase);
                    break;
                case PhaseFinished finished:
                    Assert.Equal(current, finished.Phase);
                    current = null;
                    break;
            }
        }

        Assert.Null(current);
    }

    [Fact]
    public void A_counter_rejects_a_second_start_and_counting_past_its_total()
    {
        var counter = new PhaseCounter(AnalysisPhase.Load, progress: null);

        Assert.Throws<InvalidOperationException>(counter.Increment);
        counter.Start(1);
        counter.Increment();
        Assert.Throws<InvalidOperationException>(counter.Increment);
        Assert.Throws<InvalidOperationException>(() => counter.Start(1));
    }

    private static PhaseStarted Started(AnalysisPhase phase) => new(phase);

    private static PhaseProgressed Counted(AnalysisPhase phase, int done, int total) => new(phase, done, total);

    private static PhaseFinished Finished(AnalysisPhase phase) => new(phase, TimeSpan.Zero);

    private static Task<AnalysisResult> RunAsync(FakeStages stages, IProgress<AnalysisProgress> progress, int maxParallelism, bool restore = true) =>
        AnalysisPipeline.RunAsync(
            new AnalysisRequest { SolutionPath = "/repo/Fake.slnx", MaxParallelism = maxParallelism, Restore = restore },
            stages,
            new FakeProvenanceSources(),
            progress,
            TestContext.Current.CancellationToken);
}
