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
            await stages.RestoreAsync(request, cancellationToken);
        }

        using var solution = await stages.LoadAsync(request, cancellationToken);

        var compiled = await ForEachAsync(
            solution.Projects,
            parallel,
            (project, token) => new ValueTask<CompiledProject>(stages.CompileAsync(solution, project, token)));

        var diagnostics = await ForEachAsync(
            compiled,
            parallel,
            (project, token) => ValueTask.FromResult(stages.Diagnose(project, token)));

        var facts = await WalkAsync(compiled, stages, parallel);

        var results = compiled.Select((project, i) => new ProjectResult(project, diagnostics[i], facts[i])).ToArray();
        var snapshot = stages.Assemble(new AssembleInput(request, solution, results), cancellationToken);

        return new AnalysisResult(snapshot, LoadHealthSummary.From(snapshot.LoadHealth));
    }

    /// <summary>Runs <paramref name="body"/> over every item in parallel and returns the results in input order.</summary>
    private static async Task<TResult[]> ForEachAsync<TItem, TResult>(
        IReadOnlyList<TItem> items,
        ParallelOptions parallel,
        Func<TItem, CancellationToken, ValueTask<TResult>> body)
    {
        var results = new TResult[items.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            parallel,
            async (i, token) => results[i] = await body(items[i], token));
        return results;
    }

    /// <summary>
    /// Walks every project's declarations and every document of every project as one flat set of units, so the
    /// documents of a big project spread over all workers.
    /// </summary>
    private static async Task<ProjectFacts[][]> WalkAsync(
        CompiledProject[] projects,
        IAnalysisStages stages,
        ParallelOptions parallel)
    {
        var documents = projects.Select(project => project.Compilation.SyntaxTrees.ToArray()).ToArray();
        var facts = documents.Select(trees => new ProjectFacts[trees.Length + 1]).ToArray();

        // Slot 0 of a project holds its declarations; slot d + 1 holds document d.
        var units = documents.SelectMany((trees, p) => Enumerable.Range(0, trees.Length + 1).Select(slot => (p, slot)));

        await Parallel.ForEachAsync(units, parallel, (unit, token) =>
        {
            var (p, slot) = unit;
            facts[p][slot] = slot == 0
                ? stages.WalkDeclarations(projects[p], token)
                : stages.WalkDocument(projects[p], documents[p][slot - 1], token);
            return ValueTask.CompletedTask;
        });

        return facts;
    }
}
