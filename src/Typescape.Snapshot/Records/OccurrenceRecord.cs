namespace Typescape.Snapshot.Records;

/// <summary>
/// One place in the source where a Dependency appears. <see cref="Json.SnapshotJson"/> writes it as the tuple
/// <c>[dependency, sourceMember, targetMember, document, line]</c>, because Occurrences outnumber every other row.
/// </summary>
/// <param name="Dependency">Index into <see cref="SnapshotRecord.Dependencies"/>.</param>
/// <param name="SourceMember">
/// Index into <see cref="SnapshotRecord.ExecutableMembers"/> of the using member; <c>null</c> when the use is outside
/// every Executable Member (field and property initializers, declaration-level Kinds such as <c>inherits</c>).
/// </param>
/// <param name="TargetMember">
/// The used member, <c>null</c> when there is none (e.g. <c>inherits</c>, <c>type-usage</c>). Which table it indexes:
/// <list type="bullet">
/// <item>the Dependency's target is External: <see cref="SnapshotRecord.ExternalMembers"/>;</item>
/// <item>the target is an own type and the value is <c>&gt;= 0</c>: <see cref="SnapshotRecord.ExecutableMembers"/>;</item>
/// <item>
/// the target is an own type and the value is negative: <see cref="SnapshotRecord.NonExecutableMembers"/> at index
/// <c>~value</c> (<c>-1</c> is row 0). Build it with <see cref="NonExecutableTarget"/>.
/// </item>
/// </list>
/// <see cref="SnapshotRecord.ResolveTargetMember"/> resolves all three cases.
/// </param>
/// <param name="Document">Index into <see cref="SnapshotRecord.Documents"/>.</param>
/// <param name="Line">The 1-based physical line, as in the original file (not <c>#line</c>-mapped).</param>
public sealed record OccurrenceRecord(int Dependency, int? SourceMember, int? TargetMember, int Document, int Line)
{
    /// <summary>The <see cref="TargetMember"/> value that points at row <paramref name="index"/> of <see cref="SnapshotRecord.NonExecutableMembers"/>.</summary>
    public static int NonExecutableTarget(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return ~index;
    }
}
