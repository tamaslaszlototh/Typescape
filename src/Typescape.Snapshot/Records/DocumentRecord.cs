namespace Typescape.Snapshot.Records;

/// <summary>A source document of one project.</summary>
/// <param name="Path">
/// Stable key: the path relative to the solution directory, forward slashes; for a file outside it in a NuGet package
/// folder, the path in that folder under <c>{nuget}/</c>, e.g. <c>{nuget}/xunit.v3.core.mtp-v2/4.0.3/_content/A.cs</c>.
/// </param>
/// <param name="Project">Index into <see cref="SnapshotRecord.Projects"/>.</param>
/// <param name="Generated">Whether the whole document is generated code.</param>
/// <param name="GeneratedReason">Why the document is generated; <c>null</c> when it is not.</param>
public sealed record DocumentRecord(string Path, int Project, bool Generated, GeneratedReason? GeneratedReason);

public enum GeneratedReason
{
    Generator,
    Convention,
}
