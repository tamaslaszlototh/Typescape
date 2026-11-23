namespace Typescape.Snapshot.Records;

/// <summary>A member of an External Type that is the target of at least one Occurrence.</summary>
/// <param name="Key">Stable key: the documentation comment ID.</param>
/// <param name="Type">Index into <see cref="SnapshotRecord.ExternalTypes"/> of the containing External Type.</param>
/// <param name="Name">The display name.</param>
public sealed record ExternalMemberRecord(string Key, int Type, string Name);
