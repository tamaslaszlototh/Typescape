namespace Typescape.Snapshot;

/// <summary>A project of the analyzed solution. Listed even when it failed to load (then it has no types).</summary>
/// <param name="Key">Stable key: the project file path relative to the solution directory, forward slashes.</param>
/// <param name="Name">The project name.</param>
/// <param name="Assembly">The assembly name.</param>
/// <param name="Style">Whether the project file is SDK-style or legacy.</param>
/// <param name="Tfm">The target framework the project was analyzed for, or <c>null</c> when none could be chosen.</param>
/// <param name="Test">Whether the project counts as a test project.</param>
/// <param name="TestReason">
/// Why <paramref name="Test"/> has its value: <c>framework:&lt;name&gt;</c> (e.g. <c>framework:xunit</c>), <c>name</c>,
/// or <c>override</c> (<c>--test</c> / <c>--not-test</c>); <c>null</c> for a non-test project without an override.
/// </param>
/// <param name="References">Indices into <see cref="SnapshotRecord.Projects"/> of the direct project references.</param>
public sealed record ProjectRecord(
    string Key,
    string Name,
    string Assembly,
    ProjectStyle Style,
    string? Tfm,
    bool Test,
    string? TestReason,
    IReadOnlyList<int> References);

public enum ProjectStyle
{
    /// <summary>An SDK-style project file.</summary>
    Sdk,

    /// <summary>A classic (non-SDK) project file.</summary>
    Legacy,
}
