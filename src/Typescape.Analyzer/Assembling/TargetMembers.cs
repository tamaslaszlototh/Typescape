using Typescape.Analyzer.Extraction;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Assembling;

/// <summary>
/// The External members and own non-executable members that Occurrences target, deduplicated and sorted, and the
/// <see cref="OccurrenceRecord.TargetMember"/> of each Occurrence.
/// </summary>
/// <remarks>
/// An External member of a type that turned out to be own (see <see cref="SnapshotAssembler"/>) is an own member: its
/// Executable Member when one has its key, otherwise a non-executable member.
/// </remarks>
internal sealed class TargetMembers
{
    private readonly IReadOnlyDictionary<string, int> typeIndex;
    private readonly IReadOnlyDictionary<string, int> executableIndex;
    private readonly Dictionary<(string Type, string Key), int> externalIndex;
    private readonly Dictionary<string, int> nonExecutableIndex;

    /// <param name="occurrences">Whether the Snapshot has Occurrences; without them both tables are empty.</param>
    public TargetMembers(
        bool occurrences,
        IEnumerable<ProjectFacts> facts,
        IReadOnlyDictionary<string, int> typeIndex,
        IReadOnlyDictionary<string, int> externalTypeIndex,
        IReadOnlyDictionary<string, int> executableIndex)
    {
        this.typeIndex = typeIndex;
        this.executableIndex = executableIndex;
        var parts = occurrences ? facts.ToArray() : [];

        External =
        [
            .. parts
                .SelectMany(part => part.ExternalMembers)
                .Where(member => externalTypeIndex.ContainsKey(member.Type))
                .DistinctBy(member => (member.Type, member.Key))
                .Select(member => new ExternalMemberRecord(member.Key, externalTypeIndex[member.Type], member.Name))
                .OrderBy(member => member.Type)
                .ThenBy(member => member.Key, StringComparer.Ordinal),
        ];
        externalIndex = new Dictionary<(string Type, string Key), int>(External.Count);
        var externalTypeKeys = externalTypeIndex.ToDictionary(entry => entry.Value, entry => entry.Key);
        for (var i = 0; i < External.Count; i++)
        {
            externalIndex.Add((externalTypeKeys[External[i].Type], External[i].Key), i);
        }

        var ownedExternals = parts
            .SelectMany(part => part.ExternalMembers)
            .Where(member => typeIndex.ContainsKey(member.Type))
            .Select(member => new NonExecutableMemberFact(StableKeys.Member(member.Type, member.Key), member.Type, member.Name))
            .Where(member => !executableIndex.ContainsKey(member.Key));

        NonExecutable =
        [
            .. parts
                .SelectMany(part => part.NonExecutableMembers)
                .Concat(ownedExternals)
                .Where(member => typeIndex.ContainsKey(member.Type))
                .DistinctBy(member => member.Key, StringComparer.Ordinal)
                .Select(member => new NonExecutableMemberRecord(member.Key, typeIndex[member.Type], member.Name))
                .OrderBy(member => member.Key, StringComparer.Ordinal),
        ];
        nonExecutableIndex = new Dictionary<string, int>(NonExecutable.Count, StringComparer.Ordinal);
        for (var i = 0; i < NonExecutable.Count; i++)
        {
            nonExecutableIndex.Add(NonExecutable[i].Key, i);
        }
    }

    /// <summary>Sorted by External Type, then key.</summary>
    public IReadOnlyList<ExternalMemberRecord> External { get; }

    /// <summary>Sorted by key.</summary>
    public IReadOnlyList<NonExecutableMemberRecord> NonExecutable { get; }

    /// <summary>The <see cref="OccurrenceRecord.TargetMember"/> of <paramref name="occurrence"/>; <c>null</c> when it has none or it is missing.</summary>
    public int? Of(OccurrenceFact occurrence)
    {
        if (occurrence.TargetMember is not { } member)
        {
            return null;
        }

        if (!typeIndex.ContainsKey(occurrence.Target))
        {
            return externalIndex.TryGetValue((occurrence.Target, member), out var external) ? external : null;
        }

        return Own(member) ?? Own(StableKeys.Member(occurrence.Target, member));
    }

    private int? Own(string key) =>
        executableIndex.TryGetValue(key, out var executable) ? executable
        : nonExecutableIndex.TryGetValue(key, out var nonExecutable) ? OccurrenceRecord.NonExecutableTarget(nonExecutable)
        : null;
}
