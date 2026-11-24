namespace Typescape.Analyzer.Extraction;

/// <summary>
/// Facts extracted from a project, or from one part of it: its declarations or one of its documents. Key-based and
/// unsorted, with no array indices; Assemble merges, sorts and indexes them.
/// </summary>
internal sealed record ProjectFacts(IReadOnlyList<DocumentFact> Documents)
{
    public static ProjectFacts Empty { get; } = new([]);

    /// <summary>The facts of all <paramref name="parts"/> together, in their order.</summary>
    public static ProjectFacts Combine(IEnumerable<ProjectFacts> parts) =>
        new([.. parts.SelectMany(part => part.Documents)]);
}

/// <param name="Path">Stable key: the path relative to the solution directory, forward slashes.</param>
internal sealed record DocumentFact(string Path);
