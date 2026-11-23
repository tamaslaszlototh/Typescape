namespace Typescape.Snapshot;

/// <summary>Why <see cref="SnapshotReader"/> refused a file.</summary>
public enum SnapshotReadFailure
{
    /// <summary>The file is empty or does not start with the gzip magic bytes.</summary>
    NotGzip,

    /// <summary>The file decompresses, but not to a JSON object whose first property is a <c>major.minor</c> <c>schemaVersion</c>.</summary>
    NotSnapshot,

    /// <summary>The <c>schemaVersion</c> has a major version this assembly does not read, newer or older.</summary>
    UnsupportedSchemaVersion,

    /// <summary>The gzip data cannot be decompressed, or the JSON ends early or does not match the records.</summary>
    Corrupt,
}

/// <summary>
/// A file <see cref="SnapshotReader"/> cannot read as a Snapshot. The message is a complete sentence for the user,
/// without the file's path.
/// </summary>
public sealed class SnapshotReadException : Exception
{
    public SnapshotReadException(SnapshotReadFailure reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public SnapshotReadFailure Reason { get; }

    /// <summary>The file's <c>schemaVersion</c>, once it has been read; <c>null</c> before that.</summary>
    public string? SchemaVersion { get; init; }
}
