using System.Collections.Immutable;

namespace Typescape.Analyzer;

/// <summary>What <see cref="Analyzer.AnalyzeAsync"/> analyzes and how. It mirrors the <c>typescape analyze</c> flags.</summary>
public sealed record AnalysisRequest
{
    public required string SolutionPath { get; init; }

    public string? Framework { get; init; }

    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    public bool Occurrences { get; init; } = true;

    public bool Restore { get; init; } = true;

    public IReadOnlyDictionary<string, string> Properties { get; init; } = ImmutableDictionary<string, string>.Empty;

    public IReadOnlyList<string> TestPatterns { get; init; } = [];

    public IReadOnlyList<string> NotTestPatterns { get; init; } = [];
}
