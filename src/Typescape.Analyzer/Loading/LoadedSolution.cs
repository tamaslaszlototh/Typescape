using Microsoft.CodeAnalysis;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Loading;

/// <summary>The output of Load: the Roslyn solution and the projects to analyze. Disposing it disposes the workspace.</summary>
/// <param name="workspace">The workspace that owns <paramref name="solution"/>.</param>
/// <param name="solution">The loaded solution.</param>
/// <param name="directory">The absolute solution directory.</param>
/// <param name="projects">The projects to analyze, each once, sorted by key.</param>
/// <param name="unopenedProjects">The solution's projects that MSBuildWorkspace opened no project for, sorted by key.</param>
/// <param name="diagnostics">Every diagnostic MSBuildWorkspace reported while opening the solution.</param>
/// <param name="solutionDiagnostics">The classified workspace diagnostics that belong to no project, sorted by kind, then message.</param>
/// <param name="referenceAssemblyFailures">The .NET Framework versions whose reference assemblies could not be fetched.</param>
internal sealed class LoadedSolution(
    Workspace workspace,
    Solution solution,
    string directory,
    IReadOnlyList<LoadedProject> projects,
    IReadOnlyList<UnopenedProject> unopenedProjects,
    IReadOnlyList<WorkspaceDiagnostic> diagnostics,
    IReadOnlyList<WorkspaceDiagnosticRecord> solutionDiagnostics,
    IReadOnlyList<ReferenceAssemblyFailure> referenceAssemblyFailures) : IDisposable
{
    public Solution Solution { get; } = solution;

    public string Directory { get; } = directory;

    public IReadOnlyList<LoadedProject> Projects { get; } = projects;

    /// <summary>
    /// The C# projects the solution file lists but MSBuildWorkspace did not open, e.g. a missing project file. They
    /// are failed projects; their workspace diagnostics say why.
    /// </summary>
    public IReadOnlyList<UnopenedProject> UnopenedProjects { get; } = unopenedProjects;

    /// <summary>Raw; the projects and <see cref="SolutionDiagnostics"/> hold them classified.</summary>
    public IReadOnlyList<WorkspaceDiagnostic> Diagnostics { get; } = diagnostics;

    public IReadOnlyList<WorkspaceDiagnosticRecord> SolutionDiagnostics { get; } = solutionDiagnostics;

    /// <summary>Sorted by version. Their projects loaded without framework references.</summary>
    public IReadOnlyList<ReferenceAssemblyFailure> ReferenceAssemblyFailures { get; } = referenceAssemblyFailures;

    public void Dispose() => workspace.Dispose();
}

/// <summary>A project Load opened.</summary>
/// <param name="Key">The project's stable key: its file path relative to the solution directory, forward slashes.</param>
/// <param name="Project">The Roslyn project, the one for <paramref name="Tfm"/>.</param>
/// <param name="Tfm">
/// The target framework the project is analyzed for, or <c>null</c> when none is known: MSBuild reported none and, for a
/// classic project, no generated attributes file names it.
/// </param>
/// <param name="AvailableTfms">
/// Every target framework MSBuild reported for the project file, sorted ordinally; for a classic project, the one its
/// <c>TargetFrameworkVersion</c> stands for.
/// </param>
internal sealed record LoadedProject(string Key, Project Project, string? Tfm, IReadOnlyList<string> AvailableTfms)
{
    /// <summary>
    /// The keys of the loaded projects this one references directly, read from its project file and its imports, sorted
    /// ordinally. <see cref="Project.ProjectReferences"/> holds the transitive closure instead.
    /// </summary>
    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>
    /// Whether <see cref="References"/> and MSBuildWorkspace's project references disagree, as
    /// <see cref="Loading.DirectReferences.Resolve"/> decides it.
    /// </summary>
    public bool ProjectReferenceMismatch { get; init; }

    /// <summary>Whether the project counts as a test project, as <see cref="Loading.TestProjects.Detect"/> decides it.</summary>
    public bool Test { get; init; }

    /// <summary>Why <see cref="Test"/> has its value, spelled as <see cref="ProjectRecord.TestReason"/>.</summary>
    public string? TestReason { get; init; }

    /// <summary>Whether the project file is SDK-style; <c>null</c> when it cannot be read.</summary>
    public ProjectStyle? Style { get; init; }

    /// <summary>Whether Roslyn fell back from Visual Studio or Mono MSBuild to .NET SDK MSBuild for the project.</summary>
    public bool SdkFallback { get; init; }

    /// <summary>
    /// The workspace diagnostics of the project file, for every target framework, classified as
    /// <see cref="Loading.LoadEvidence"/> decides it; sorted by kind, then message.
    /// </summary>
    public IReadOnlyList<WorkspaceDiagnosticRecord> WorkspaceDiagnostics { get; init; } = [];

    /// <summary>The number of metadata references MSBuildWorkspace gave the project.</summary>
    public int MetadataReferences => Project.MetadataReferences.Count;
}

/// <summary>A C# project of the solution file that MSBuildWorkspace opened no project for.</summary>
/// <param name="Key">The project's stable key, as <see cref="LoadedProject.Key"/>.</param>
/// <param name="Style">Whether the project file is SDK-style; <c>null</c> when it cannot be read, e.g. it is missing.</param>
/// <param name="WorkspaceDiagnostics">As <see cref="LoadedProject.WorkspaceDiagnostics"/>.</param>
internal sealed record UnopenedProject(string Key, ProjectStyle? Style, IReadOnlyList<WorkspaceDiagnosticRecord> WorkspaceDiagnostics);

/// <summary>The reference assemblies of one .NET Framework version could not be fetched, e.g. offline.</summary>
/// <param name="Version">The framework version, e.g. <c>v4.7.2</c>.</param>
/// <param name="Reason">Why, e.g. the unreachable NuGet sources.</param>
/// <param name="ProjectKeys">The keys of the projects that target it, sorted.</param>
internal sealed record ReferenceAssemblyFailure(string Version, string Reason, IReadOnlyList<string> ProjectKeys)
{
    public string Message =>
        $"The reference assemblies for .NET Framework {Version} could not be fetched from the NuGet sources; "
        + $"{(ProjectKeys.Count == 0 ? "the projects that target it" : string.Join(", ", ProjectKeys))} load without framework references. "
        + Reason;

    /// <summary>The message for one of <see cref="ProjectKeys"/>, in place of MSBuild's advice to install a Developer Pack.</summary>
    public string ProjectMessage =>
        $"The reference assemblies for .NET Framework {Version} could not be fetched from the NuGet sources, so the project "
        + $"loads without framework references. {Reason}";
}
