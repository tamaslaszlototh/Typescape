namespace Typescape.Analyzer.Extraction;

/// <summary>
/// Facts one walk extracted from a project: from its declarations, or from one of its documents. Key-based and
/// unsorted, with no array indices; Assemble merges, sorts and indexes them.
/// </summary>
internal sealed record ProjectFacts;
