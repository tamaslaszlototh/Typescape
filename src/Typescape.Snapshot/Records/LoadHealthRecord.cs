namespace Typescape.Snapshot.Records;

/// <summary>
/// The Analyzer's own verdict on how completely each project was loaded, with the evidence behind it. It is needed
/// because MSBuildWorkspace often yields a project with 0 metadata references and no diagnostic at all.
/// </summary>
/// <param name="Projects">
/// One row per project, in the order of <see cref="SnapshotRecord.Projects"/>: <c>Projects[i].Project == i</c>.
/// </param>
/// <param name="SolutionDiagnostics">The workspace diagnostics that belong to no project, sorted by kind, then message.</param>
/// <remarks>
/// Messages come from Roslyn and MSBuild, which often name absolute paths. The producer rewrites every path under the
/// solution directory to its relative form, because a Snapshot holds no absolute path.
/// </remarks>
public sealed record LoadHealthRecord(
    IReadOnlyList<ProjectHealthRecord> Projects,
    IReadOnlyList<WorkspaceDiagnosticRecord> SolutionDiagnostics);

/// <summary>
/// The Load Health of one project. Its style and chosen TFM are the project's own <see cref="ProjectRecord.Style"/>
/// and <see cref="ProjectRecord.Tfm"/>, which are not repeated here.
/// </summary>
/// <param name="Project">Index into <see cref="SnapshotRecord.Projects"/>.</param>
/// <param name="Status">The verdict.</param>
/// <param name="AvailableTfms">
/// Every target framework the project file declares, sorted ordinally; the chosen one is <see cref="ProjectRecord.Tfm"/>.
/// Empty when the project could not be evaluated.
/// </param>
/// <param name="MetadataReferences">The number of metadata references MSBuildWorkspace gave the project.</param>
/// <param name="ObjectResolves">Whether <c>System.Object</c> resolves in the project's compilation.</param>
/// <param name="CompileErrors">The compiler errors of the project's compilation.</param>
/// <param name="UnresolvedTypes">The type references that did not resolve.</param>
/// <param name="WorkspaceDiagnostics">The project's workspace diagnostics, sorted by kind, then message.</param>
/// <param name="SdkFallback">Whether Roslyn fell back from VS or Mono MSBuild to .NET SDK MSBuild for this project.</param>
/// <param name="ProjectReferenceMismatch">
/// Whether the direct project references (<see cref="ProjectRecord.References"/>) disagree with the project references
/// MSBuildWorkspace reports, beyond its reporting the transitive closure.
/// </param>
/// <param name="AnalyzerErrors">
/// Exceptions in Typescape's own walker, and facts Assemble left out; sorted by document, then exception type, then frame.
/// </param>
public sealed record ProjectHealthRecord(
    int Project,
    LoadStatus Status,
    IReadOnlyList<string> AvailableTfms,
    int MetadataReferences,
    bool ObjectResolves,
    CompileErrorsRecord CompileErrors,
    UnresolvedTypesRecord UnresolvedTypes,
    IReadOnlyList<WorkspaceDiagnosticRecord> WorkspaceDiagnostics,
    bool SdkFallback,
    bool ProjectReferenceMismatch,
    IReadOnlyList<AnalyzerErrorRecord> AnalyzerErrors);

public enum LoadStatus
{
    /// <summary>None of the degraded or failed conditions hold.</summary>
    Ok,

    /// <summary>
    /// Compile errors, a suspiciously low reference count (<c>System.Object</c> unresolved, 0 metadata references), or
    /// any analyzer error.
    /// </summary>
    Degraded,

    /// <summary>The project could not be loaded. It is still listed, with no types.</summary>
    Failed,
}

/// <summary>The compiler errors of one project.</summary>
/// <param name="Count">The total number of errors.</param>
/// <param name="Top">
/// The <see cref="TopCount"/> most frequent error codes, sorted by count descending, then code ordinally.
/// </param>
public sealed record CompileErrorsRecord(int Count, IReadOnlyList<CompileErrorCodeRecord> Top)
{
    /// <summary>How many error codes <see cref="Top"/> holds at most.</summary>
    public const int TopCount = 5;
}

/// <summary>One compiler error code of a project.</summary>
/// <param name="Code">The diagnostic ID, e.g. <c>CS0518</c>.</param>
/// <param name="Count">How often the project has it.</param>
/// <param name="Sample">
/// One message with this code: the one at the first location in document path, then line, order, so it is deterministic.
/// </param>
public sealed record CompileErrorCodeRecord(string Code, int Count, string Sample);

/// <summary>The type references of one project that did not resolve.</summary>
/// <param name="Count">The total number of unresolved type references.</param>
/// <param name="Top">The <see cref="TopCount"/> most frequent names, sorted by count descending, then name ordinally.</param>
public sealed record UnresolvedTypesRecord(int Count, IReadOnlyList<UnresolvedTypeRecord> Top)
{
    /// <summary>How many names <see cref="Top"/> holds at most.</summary>
    public const int TopCount = 10;
}

/// <summary>One type name that did not resolve.</summary>
/// <param name="Name">The name as written in the source, e.g. <c>JsonConvert</c>.</param>
/// <param name="Count">How many references to it did not resolve.</param>
public sealed record UnresolvedTypeRecord(string Name, int Count);

/// <summary>A diagnostic MSBuildWorkspace reported while opening the solution, classified by Typescape.</summary>
/// <param name="Kind">The classification.</param>
/// <param name="Message">The message, with paths under the solution directory made relative.</param>
public sealed record WorkspaceDiagnosticRecord(WorkspaceDiagnosticKind Kind, string Message);

public enum WorkspaceDiagnosticKind
{
    /// <summary>
    /// Harmless, e.g. the "project reference without a matching metadata reference" warning for an analyzer reference.
    /// </summary>
    Info,

    /// <summary>Something may be missing from the project's semantic model.</summary>
    Warning,

    /// <summary>MSBuildWorkspace reported a failure (<c>WorkspaceDiagnosticKind.Failure</c>).</summary>
    Failure,
}

/// <summary>
/// An exception in Typescape's own walker, caught on one document, or a fact of the document that Assemble left out
/// because it conflicts with another project's. It makes the project degraded.
/// </summary>
/// <param name="Document">Index into <see cref="SnapshotRecord.Documents"/> of the document being walked.</param>
/// <param name="ExceptionType">
/// The exception's full type name, e.g. <c>System.InvalidCastException</c>; for a left-out fact, why:
/// <c>duplicate-type-key</c> (an earlier project by key declares a type of the same key) or <c>unplaced-member</c> (no
/// declaration of its type holds an Executable Member).
/// </param>
/// <param name="Frame">
/// The top stack frame as a method, e.g. <c>Typescape.Analyzer.Walk.DependencyWalker.VisitInvocation(InvocationExpressionSyntax)</c>.
/// Never its file and line, which hold an absolute path from the machine that built the tool. For a left-out fact, its key.
/// </param>
public sealed record AnalyzerErrorRecord(int Document, string ExceptionType, string Frame);
