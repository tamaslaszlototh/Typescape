namespace Typescape.Snapshot.Records;

/// <summary>A source document of one project.</summary>
/// <param name="Path">Stable key: the path relative to the solution directory, forward slashes.</param>
/// <param name="Project">Index into <see cref="SnapshotRecord.Projects"/>.</param>
/// <param name="Generated">Whether the whole document is generated code.</param>
/// <param name="GeneratedReason">Why the document is generated; <c>null</c> when it is not.</param>
public sealed record DocumentRecord(string Path, int Project, bool Generated, GeneratedReason? GeneratedReason);

public enum GeneratedReason
{
    Generator,
    Convention,
}
