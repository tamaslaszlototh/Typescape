using Typescape.Snapshot;

namespace Typescape.Analyzer.Assembling;

/// <summary>Where each stable key sits in its Snapshot table.</summary>
internal sealed record RowIndexes(
    IReadOnlyDictionary<string, int> Documents,
    IReadOnlyDictionary<string, int> Types,
    IReadOnlyDictionary<string, int> ExternalTypes,
    IReadOnlyDictionary<string, int> ExecutableMembers)
{
    public static Dictionary<TKey, int> Of<T, TKey>(IReadOnlyList<T> rows, Func<T, TKey> key)
        where TKey : notnull
    {
        var index = new Dictionary<TKey, int>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            index.Add(key(rows[i]), i);
        }

        return index;
    }

    /// <summary>The own type or External Type with <paramref name="key"/>; <c>null</c> when neither table has it.</summary>
    public TypeReference? Type(string key) =>
        Types.TryGetValue(key, out var own) ? new TypeReference(own, External: false)
        : ExternalTypes.TryGetValue(key, out var external) ? new TypeReference(external, External: true)
        : null;
}

/// <summary>A row of <see cref="SnapshotRecord.ExternalTypes"/> when <paramref name="External"/>, otherwise of <see cref="SnapshotRecord.Types"/>.</summary>
internal readonly record struct TypeReference(int Index, bool External);
