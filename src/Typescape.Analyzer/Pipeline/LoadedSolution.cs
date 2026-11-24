using Microsoft.CodeAnalysis;

namespace Typescape.Analyzer.Pipeline;

/// <summary>The output of Load: the Roslyn solution and the projects to analyze. Disposing it disposes the workspace.</summary>
/// <param name="workspace">The workspace that owns <paramref name="solution"/>.</param>
/// <param name="solution">The loaded solution.</param>
/// <param name="directory">The absolute solution directory.</param>
/// <param name="projects">The projects to analyze, each once, sorted by key.</param>
/// <param name="unopenedProjects">The keys of the solution's projects that MSBuildWorkspace opened no project for, sorted.</param>
/// <param name="diagnostics">Every diagnostic MSBuildWorkspace reported while opening the solution.</param>
/// <param name="referenceAssemblyFailures">The .NET Framework versions whose reference assemblies could not be fetched.</param>
internal sealed class LoadedSolution(
    Workspace workspace,
    Solution solution,
    string directory,
    IReadOnlyList<LoadedProject> projects,
    IReadOnlyList<string> unopenedProjects,
    IReadOnlyList<WorkspaceDiagnostic> diagnostics,
    IReadOnlyList<ReferenceAssemblyFailure> referenceAssemblyFailures) : IDisposable
{
    public Solution Solution { get; } = solution;

    public string Directory { get; } = directory;

    public IReadOnlyList<LoadedProject> Projects { get; } = projects;

    /// <summary>
    /// The keys of the C# projects the solution file lists but MSBuildWorkspace did not open, e.g. a missing project
    /// file. They are failed projects; <see cref="Diagnostics"/> says why.
    /// </summary>
    public IReadOnlyList<string> UnopenedProjects { get; } = unopenedProjects;

    public IReadOnlyList<WorkspaceDiagnostic> Diagnostics { get; } = diagnostics;

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
}

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
}
