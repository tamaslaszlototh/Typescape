namespace Typescape.Analyzer.Extraction;

/// <summary>What extraction needs to know about a project besides its compilation.</summary>
/// <param name="ProjectKey">The project's stable key: its file path relative to the solution directory, forward slashes.</param>
/// <param name="SolutionDirectory">The absolute solution directory, which every stored path is made relative to.</param>
/// <param name="PackageFolders">
/// The NuGet package folders, for the keys of documents compiled straight from a package (see
/// <see cref="Infrastructure.SolutionPaths.Key(string, string, IReadOnlyList{string})"/>).
/// </param>
/// <param name="Test">Whether the project counts as a test project.</param>
/// <param name="GeneratedDocumentPaths">The file paths of the documents source generators added to the compilation.</param>
/// <param name="Occurrences">
/// Whether to collect Occurrences and the members they target; without them (<c>--no-occurrences</c>) the type-level
/// Dependency counts remain.
/// </param>
internal sealed record ProjectContext(
    string ProjectKey,
    string SolutionDirectory,
    IReadOnlyList<string> PackageFolders,
    bool Test,
    IReadOnlySet<string> GeneratedDocumentPaths,
    bool Occurrences);
