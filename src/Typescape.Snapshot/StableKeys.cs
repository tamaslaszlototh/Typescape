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

    private const char FileLocalSeparator = '@';

    /// <summary>
    /// The key of a type, own or External: <c>&lt;assembly&gt;|&lt;doc ID&gt;</c>, or
    /// <c>&lt;assembly&gt;|&lt;doc ID&gt;@&lt;document key&gt;</c> for a file-local type and the types nested in it.
    /// </summary>
    /// <param name="assembly">The defining assembly's name.</param>
    /// <param name="documentationCommentId">Roslyn's documentation comment ID of the open definition, e.g. <c>T:N.List`1</c>.</param>
    /// <param name="fileLocalDocument">
    /// The key of the document declaring the outermost file-local type, when there is one. Every document's
    /// <c>file class Helper</c> has the doc ID <c>T:Helper</c>, and the metadata name hashes the absolute path.
    /// </param>
    public static string Type(string assembly, string documentationCommentId, string? fileLocalDocument = null)
    {
        var key = Join(assembly, documentationCommentId);
        if (fileLocalDocument is null)
        {
            return key;
        }

        ArgumentException.ThrowIfNullOrEmpty(fileLocalDocument);
        return string.Concat(key, FileLocalSeparator.ToString(), fileLocalDocument);
    }

    /// <summary>
    /// The key of an own member, an Executable Member or a non-executable one: <c>&lt;type key&gt;|&lt;doc ID&gt;</c>.
    /// </summary>
    /// <param name="typeKey">The containing type's key.</param>
    /// <param name="documentationCommentId">Roslyn's documentation comment ID of the member, e.g. <c>M:N.C.Run(System.Int32)</c>.</param>
    public static string Member(string typeKey, string documentationCommentId) =>
        Join(typeKey, documentationCommentId);

    /// <summary>The documentation comment ID inside a key built by <see cref="Member"/>.</summary>
    /// <param name="memberKey">The member's key.</param>
    /// <param name="typeKey">The containing type's key, which <paramref name="memberKey"/> starts with.</param>
    public static string MemberDocumentationCommentId(string memberKey, string typeKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(memberKey);
        ArgumentException.ThrowIfNullOrEmpty(typeKey);

        if (memberKey.Length <= typeKey.Length + 1
            || !memberKey.StartsWith(typeKey, StringComparison.Ordinal)
            || memberKey[typeKey.Length] != Separator)
        {
            throw new ArgumentException($"'{memberKey}' is not a member key of type '{typeKey}'.", nameof(memberKey));
        }

        return memberKey[(typeKey.Length + 1)..];
    }

    private static string Join(string prefix, string documentationCommentId)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentException.ThrowIfNullOrEmpty(documentationCommentId);
        return string.Concat(prefix, Separator.ToString(), documentationCommentId);
    }
}
