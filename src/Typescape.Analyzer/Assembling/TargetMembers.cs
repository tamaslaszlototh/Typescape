using Typescape.Analyzer.Extraction;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Assembling;

/// <summary>
/// The target members of the Occurrences: the External members and own non-executable members they target, sorted,
/// and each Occurrence's <see cref="OccurrenceRecord.TargetMember"/>.
/// </summary>
/// <remarks>
/// An External member of a type that turned out to be own is an own member: the Executable Member of its doc ID, else a
/// non-executable member.
/// </remarks>
internal sealed class TargetMembers
{
    private readonly Dictionary<OccurrenceFact, Target> targets = [];
    private readonly Dictionary<(string Type, string Key), int> externalIndex;
    private readonly Dictionary<string, int> nonExecutableIndex;

    /// <param name="occurrences">The Occurrences the Snapshot keeps; their source and target types are in <paramref name="indexes"/>.</param>
    public TargetMembers(IReadOnlyList<OccurrenceFact> occurrences, IEnumerable<ProjectFacts> facts, RowIndexes indexes)
    {
        var parts = facts.ToArray();
        var externalFacts = parts
            .SelectMany(part => part.ExternalMembers)
            .DistinctBy(member => (member.Type, member.Key))
            .ToDictionary(member => (member.Type, member.Key));
        var nonExecutableFacts = parts
            .SelectMany(part => part.NonExecutableMembers)
            .DistinctBy(member => member.Key, StringComparer.Ordinal)
            .ToDictionary(member => member.Key, StringComparer.Ordinal);

        var external = new Dictionary<(string Type, string Key), ExternalMemberFact>();
        var nonExecutable = new Dictionary<string, NonExecutableMemberFact>(StringComparer.Ordinal);

        Target? ResolveExternal(OccurrenceFact occurrence, string member)
        {
            if (!externalFacts.TryGetValue((occurrence.Target, member), out var fact))
            {
                return null;
            }

            external.TryAdd((fact.Type, fact.Key), fact);
            return new Target.External((fact.Type, fact.Key));
        }

        Target? ResolveOwn(OccurrenceFact occurrence, string member)
        {
            if (indexes.ExecutableMembers.TryGetValue(member, out var executable))
            {
                return new Target.Executable(executable);
            }

            if (nonExecutableFacts.TryGetValue(member, out var fact) && indexes.Types.ContainsKey(fact.Type))
            {
                nonExecutable.TryAdd(member, fact);
                return new Target.NonExecutable(member);
            }

            if (externalFacts.TryGetValue((occurrence.Target, member), out var formerlyExternal))
            {
                var key = StableKeys.Member(occurrence.Target, member);
                if (indexes.ExecutableMembers.TryGetValue(key, out var implemented))
                {
                    return new Target.Executable(implemented);
                }

                nonExecutable.TryAdd(key, new NonExecutableMemberFact(key, occurrence.Target, formerlyExternal.Name));
                return new Target.NonExecutable(key);
            }

            return null;
        }

        foreach (var occurrence in occurrences)
        {
            if (occurrence.TargetMember is not { } member || targets.ContainsKey(occurrence))
            {
                continue;
            }

            var target = indexes.ExternalTypes.ContainsKey(occurrence.Target) ? ResolveExternal(occurrence, member) : ResolveOwn(occurrence, member);
            if (target is not null)
            {
                targets.Add(occurrence, target);
            }
        }

        var externalRows = external.Values
            .OrderBy(member => indexes.ExternalTypes[member.Type])
            .ThenBy(member => member.Key, StringComparer.Ordinal)
            .ToArray();
        External = [.. externalRows.Select(member => new ExternalMemberRecord(member.Key, indexes.ExternalTypes[member.Type], member.Name))];
        externalIndex = RowIndexes.Of(externalRows, member => (member.Type, member.Key));

        NonExecutable =
        [
            .. nonExecutable.Values
                .OrderBy(member => member.Key, StringComparer.Ordinal)
                .Select(member => new NonExecutableMemberRecord(member.Key, indexes.Types[member.Type], member.Name)),
        ];
        nonExecutableIndex = RowIndexes.Of(NonExecutable, member => member.Key);
    }

    /// <summary>Sorted by External Type, then key.</summary>
    public IReadOnlyList<ExternalMemberRecord> External { get; }

    /// <summary>Sorted by key.</summary>
    public IReadOnlyList<NonExecutableMemberRecord> NonExecutable { get; }

    /// <summary>The <see cref="OccurrenceRecord.TargetMember"/> of <paramref name="occurrence"/>; <c>null</c> when it has none.</summary>
    public int? Of(OccurrenceFact occurrence) =>
        targets.GetValueOrDefault(occurrence) switch
        {
            Target.Executable executable => executable.Index,
            Target.External external => externalIndex[external.Key],
            Target.NonExecutable nonExecutable => OccurrenceRecord.NonExecutableTarget(nonExecutableIndex[nonExecutable.Key]),
            _ => null,
        };

    private abstract record Target
    {
        public sealed record Executable(int Index) : Target;

        public sealed record External((string Type, string Key) Key) : Target;

        public sealed record NonExecutable(string Key) : Target;
    }
}
