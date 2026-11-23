using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Pipeline;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Tests;

public class AnalysisPipelineTests
{
    private static readonly string[] Phases = ["restore", "load", "compile", "diagnostics", "walk", "assemble"];

    [Fact]
    public async Task Phases_run_in_order_with_a_barrier_between_them()
    {
        var stages = new FakeStages(3, 1, 4, 0);

        await RunAsync(stages, maxParallelism: 4);

        // Every call logs its start and end, so a phase that overlapped another would interleave with it.
        var runs = stages.Log
            .Select(entry => entry.Split(' ')[0])
            .Aggregate(new List<string>(), (phases, phase) =>
            {
                if (phases.Count == 0 || phases[^1] != phase)
                {
                    phases.Add(phase);
                }

                return phases;
            });
        Assert.Equal(Phases, runs);
    }

    [Fact]
    public async Task Restore_is_skipped_when_the_request_turns_it_off()
    {
        var stages = new FakeStages(1);

        await RunAsync(stages, maxParallelism: 1, restore: false);

        Assert.DoesNotContain("restore start", stages.Log);
        Assert.Contains("load start", stages.Log);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Compile_diagnostics_and_walk_stay_within_the_parallelism_bound(int maxParallelism)
    {
        var stages = new FakeStages(5, 5, 5, 5, 5, 5);

        await RunAsync(stages, maxParallelism);

        Assert.All(["compile", "diagnostics", "walk"], phase => Assert.InRange(stages.MaxInFlight(phase), 1, maxParallelism));
    }

    [Fact]
    public async Task Compile_and_diagnostics_run_projects_in_parallel_up_to_the_bound()
    {
        var stages = new FakeStages(1, 1, 1);
        stages.Gates["compile"] = 3;
        stages.Gates["diagnostics"] = 3;

        await RunAsync(stages, maxParallelism: 3);

        Assert.Equal(3, stages.MaxInFlight("compile"));
        Assert.Equal(3, stages.MaxInFlight("diagnostics"));
    }

    [Fact]
    public async Task Walk_runs_the_documents_of_one_project_in_parallel()
    {
        var stages = new FakeStages(3);
        stages.Gates["walk"] = 4;

        await RunAsync(stages, maxParallelism: 4);

        Assert.Equal(4, stages.MaxInFlight("walk"));
    }

    [Fact]
    public async Task Walk_covers_each_project_s_declarations_once_and_each_document_once_in_order()
    {
        IReadOnlyList<ProjectResult>? results = null;
        var stages = new FakeStages(2, 0, 3) { OnAssemble = input => results = input.Projects };

        await RunAsync(stages, maxParallelism: 3);

        Assert.NotNull(results);
        Assert.Equal(["P0/P0.csproj", "P1/P1.csproj", "P2/P2.csproj"], results.Select(result => result.Project.Project.Key));
        Assert.All(results, result =>
        {
            var expected = result.Project.Compilation.SyntaxTrees.Prepend(null).Select(tree => (result.Project.Project.Key, tree));
            Assert.Equal(expected, result.Facts.Select(facts => stages.FactSources[facts]));
        });
    }

    [Fact]
    public async Task Diagnostics_and_walk_reuse_the_compilations_that_stay_alive_through_assemble()
    {
        var alive = new List<bool>();
        FakeStages stages = null!;
        stages = new FakeStages(2, 2)
        {
            OnAssemble = input =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                alive.AddRange(stages.Compilations.Values.Select(reference => reference.TryGetTarget(out _)));
            },
        };

        await RunAsync(stages, maxParallelism: 2);

        Assert.Equal([true, true], alive);
        Assert.All(stages.Received, received =>
        {
            Assert.True(stages.Compilations[received.Project].TryGetTarget(out var compiled));
            Assert.Same(compiled, received.Compilation);
        });
        Assert.Equal(2 + 2 * 3, stages.Received.Count);
    }

    [Fact]
    public async Task The_result_carries_the_assembled_Snapshot_and_its_Load_Health_summary()
    {
        var snapshot = Snapshots.WithStatuses(LoadStatus.Ok, LoadStatus.Degraded, LoadStatus.Failed, LoadStatus.Ok);
        var stages = new FakeStages(1) { Snapshot = snapshot };

        var result = await RunAsync(stages, maxParallelism: 1);

        Assert.Same(snapshot, result.Snapshot);
        Assert.Equal(new LoadHealthSummary(Ok: 2, Degraded: 1, Failed: 1), result.LoadHealth);
        Assert.Equal(4, result.LoadHealth.Projects);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_parallelism_below_one_is_rejected(int maxParallelism)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RunAsync(new FakeStages(1), maxParallelism));
    }

    [Fact]
    public void The_parallelism_defaults_to_the_core_count()
    {
        Assert.Equal(Environment.ProcessorCount, new AnalysisRequest { SolutionPath = "Fake.slnx" }.MaxParallelism);
    }

    private static Task<AnalysisResult> RunAsync(FakeStages stages, int maxParallelism, bool restore = true) =>
        AnalysisPipeline.RunAsync(
            new AnalysisRequest { SolutionPath = "/repo/Fake.slnx", MaxParallelism = maxParallelism, Restore = restore },
            stages,
            progress: null,
            TestContext.Current.CancellationToken);
}
