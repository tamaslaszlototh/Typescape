namespace Typescape.Snapshot;

/// <summary>
/// Builds the composite stable keys, so their format is defined in one place. A diff joins two Snapshots on these.
/// </summary>
/// <remarks>
/// Projects and documents use their solution-relative path as the key; namespaces use the pair (project key, name);
/// External members use their documentation comment ID. Those need no builder.
/// </remarks>
public static class StableKeys
{
    public const char Separator = '|';

    /// <summary>The key of a type, own or External: <c>&lt;assembly&gt;|&lt;doc ID&gt;</c>.</summary>
    /// <param name="assembly">The defining assembly's name.</param>
    /// <param name="documentationCommentId">Roslyn's documentation comment ID of the open definition, e.g. <c>T:N.List`1</c>.</param>
    public static string Type(string assembly, string documentationCommentId) =>
        Join(assembly, documentationCommentId);

    /// <summary>The key of an Executable Member: <c>&lt;type key&gt;|&lt;doc ID&gt;</c>.</summary>
    /// <param name="typeKey">The containing type's key.</param>
    /// <param name="documentationCommentId">Roslyn's documentation comment ID of the member, e.g. <c>M:N.C.Run(System.Int32)</c>.</param>
    public static string ExecutableMember(string typeKey, string documentationCommentId) =>
        Join(typeKey, documentationCommentId);

    private static string Join(string prefix, string documentationCommentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentException.ThrowIfNullOrEmpty(documentationCommentId);
        return string.Concat(prefix, Separator.ToString(), documentationCommentId);
    }
}
