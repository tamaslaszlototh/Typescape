using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Extraction;

namespace Typescape.Analyzer.Pipeline;

/// <summary>
/// Runs Restore → Load → Compile → Diagnostics → Walk → Assemble over <see cref="IAnalysisStages"/>.
/// </summary>
/// <remarks>
/// <para>
/// A phase starts only when the previous one has finished for every unit, so per-phase timings and progress counters
/// stay unambiguous.
/// </para>
/// <para>
/// Compile and Diagnostics run per project, Walk per document (plus the declarations once per project), all through
/// <see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, ParallelOptions, Func{TSource, CancellationToken, ValueTask})"/>
/// bounded by <see cref="AnalysisRequest.MaxParallelism"/>. The walk's unit is the document because a few giant
/// projects would otherwise run alone at the end. Results keep their input order, though Assemble does not rely on it.
/// </para>
/// </remarks>
internal static class AnalysisPipeline
{
    public static async Task<AnalysisResult> RunAsync(
        AnalysisRequest request,
        IAnalysisStages stages,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SolutionPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaxParallelism, 1);

        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = request.MaxParallelism,
            CancellationToken = cancellationToken,
        };

        if (request.Restore)
        {
            await RunPhaseAsync(AnalysisPhase.Restore, progress, cancellationToken, async () =>
            {
                await stages.RestoreAsync(request, cancellationToken);
                return true;
            });
        }

        using var solution = await RunPhaseAsync(AnalysisPhase.Load, progress, cancellationToken, () =>
            stages.LoadAsync(request, new PhaseCounter(AnalysisPhase.Load, progress), cancellationToken));

        var compiled = await RunPhaseAsync(AnalysisPhase.Compile, progress, cancellationToken, () =>
            ForEachAsync(
                solution.Projects,
                parallel,
                new PhaseCounter(AnalysisPhase.Compile, progress),
                (project, token) => new ValueTask<CompiledProject>(stages.CompileAsync(solution, project, token))));

        var diagnostics = await RunPhaseAsync(AnalysisPhase.Diagnostics, progress, cancellationToken, () =>
            ForEachAsync(
                compiled,
                parallel,
                new PhaseCounter(AnalysisPhase.Diagnostics, progress),
                (project, token) => ValueTask.FromResult(stages.Diagnose(project, token))));

        var facts = await RunPhaseAsync(AnalysisPhase.Walk, progress, cancellationToken, () =>
            WalkAsync(compiled, stages, parallel, new PhaseCounter(AnalysisPhase.Walk, progress)));

        var snapshot = await RunPhaseAsync(AnalysisPhase.Assemble, progress, cancellationToken, () =>
        {
            var results = compiled.Select((project, i) => new ProjectResult(project, diagnostics[i], facts[i])).ToArray();
            return Task.FromResult(stages.Assemble(new AssembleInput(request, solution, results), cancellationToken));
        });

        cancellationToken.ThrowIfCancellationRequested();

        var loadHealth = LoadHealthSummary.From(snapshot.LoadHealth);
        progress?.Report(new AnalysisProgress.LoadHealthReported(loadHealth));
        return new AnalysisResult(snapshot, loadHealth);
    }

    // No cancellation check after the body: the loaded solution must reach its `using` before the run stops.
    private static async Task<T> RunPhaseAsync<T>(
        AnalysisPhase phase,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken,
        Func<Task<T>> body)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new AnalysisProgress.PhaseStarted(phase));
        var stopwatch = Stopwatch.StartNew();

        var result = await body();

        progress?.Report(new AnalysisProgress.PhaseFinished(phase, stopwatch.Elapsed));
        return result;
    }

    /// <summary>Runs <paramref name="body"/> over every item in parallel and returns the results in input order.</summary>
    private static async Task<TResult[]> ForEachAsync<TItem, TResult>(
        IReadOnlyList<TItem> items,
        ParallelOptions parallel,
        PhaseCounter counter,
        Func<TItem, CancellationToken, ValueTask<TResult>> body)
    {
        var results = new TResult[items.Count];
        counter.Start(items.Count);
        await Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            parallel,
            async (i, token) =>
            {
                results[i] = await body(items[i], token);
                counter.Increment();
            });
        return results;
    }

    /// <summary>
    /// Walks every project's declarations and every document of every project as one flat set of units, so the
    /// documents of a big project spread over all workers.
    /// </summary>
    private static async Task<ProjectFacts[][]> WalkAsync(
        CompiledProject[] projects,
        IAnalysisStages stages,
        ParallelOptions parallel,
        PhaseCounter counter)
    {
        var documents = projects.Select(project => project.Compilation.SyntaxTrees.ToArray()).ToArray();
        var facts = documents.Select(trees => new ProjectFacts[trees.Length + 1]).ToArray();

        // Slot 0 of a project holds its declarations; slot d + 1 holds document d.
        var units = documents.SelectMany((trees, p) => Enumerable.Range(0, trees.Length + 1).Select(slot => (p, slot)));

        counter.Start(documents.Sum(trees => trees.Length));
        await Parallel.ForEachAsync(units, parallel, (unit, token) =>
        {
            var (p, slot) = unit;
            if (slot == 0)
            {
                facts[p][slot] = stages.WalkDeclarations(projects[p], token);
            }
            else
            {
                facts[p][slot] = stages.WalkDocument(projects[p], documents[p][slot - 1], token);
                counter.Increment();
            }

            return ValueTask.CompletedTask;
        });

        return facts;
    }
}
