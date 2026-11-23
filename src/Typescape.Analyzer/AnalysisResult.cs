using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer;

/// <summary>What <see cref="Analyzer.AnalyzeAsync"/> produced.</summary>
/// <param name="Snapshot">The Snapshot, ready for <c>SnapshotWriter</c>.</param>
/// <param name="LoadHealth">The project counts per Load Health verdict, which the CLI prints and maps to exit codes.</param>
public sealed record AnalysisResult(SnapshotRecord Snapshot, LoadHealthSummary LoadHealth);

/// <summary>
/// How many projects got each Load Health verdict. The per-project evidence is in
/// <see cref="SnapshotRecord.LoadHealth"/>.
/// </summary>
/// <param name="Ok">Projects with the verdict <see cref="LoadStatus.Ok"/>.</param>
/// <param name="Degraded">Projects with the verdict <see cref="LoadStatus.Degraded"/>.</param>
/// <param name="Failed">Projects with the verdict <see cref="LoadStatus.Failed"/>.</param>
public sealed record LoadHealthSummary(int Ok, int Degraded, int Failed)
{
    public int Projects => Ok + Degraded + Failed;

    internal static LoadHealthSummary From(LoadHealthRecord loadHealth) =>
        new(
            Count(loadHealth, LoadStatus.Ok),
            Count(loadHealth, LoadStatus.Degraded),
            Count(loadHealth, LoadStatus.Failed));

    private static int Count(LoadHealthRecord loadHealth, LoadStatus status) =>
        loadHealth.Projects.Count(project => project.Status == status);
}
