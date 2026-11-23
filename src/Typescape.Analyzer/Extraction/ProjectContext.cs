namespace Typescape.Analyzer.Extraction;

/// <summary>What extraction needs to know about a project besides its compilation.</summary>
/// <param name="ProjectKey">The project's stable key: its file path relative to the solution directory, forward slashes.</param>
/// <param name="SolutionDirectory">The absolute solution directory, which every stored path is made relative to.</param>
/// <param name="Test">Whether the project counts as a test project.</param>
/// <param name="GeneratedDocumentPaths">The file paths of the documents source generators added to the compilation.</param>
internal sealed record ProjectContext(
    string ProjectKey,
    string SolutionDirectory,
    bool Test,
    IReadOnlySet<string> GeneratedDocumentPaths);
