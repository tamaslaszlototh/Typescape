using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Typescape.Snapshot.Json;

namespace Typescape.Snapshot.Files;

/// <summary>
/// Reads the Snapshot file <see cref="SnapshotWriter"/> writes, and checks its <c>schemaVersion</c> before anything
/// else.
/// </summary>
/// <remarks>
/// Only a major version equal to that of <see cref="SnapshotRecord.CurrentSchemaVersion"/> is read. A newer minor is
/// accepted and its unknown fields are ignored, because a minor version only adds things. Any other major, newer or
/// older, is refused: there is no migration. Every refusal is a <see cref="SnapshotReadException"/>. The source stream
/// is left open.
/// </remarks>
public static class SnapshotReader
{
    private const string SchemaVersionProperty = "schemaVersion";

    /// <summary>How far into the decompressed JSON <c>schemaVersion</c> has to be found.</summary>
    private const int PrefixLimit = 4096;

    private static readonly int SupportedMajor = ParseMajor(SnapshotRecord.CurrentSchemaVersion)
        ?? throw new InvalidOperationException($"{nameof(SnapshotRecord.CurrentSchemaVersion)} is not major.minor.");

    /// <summary>Reads the whole Snapshot in <paramref name="source"/>.</summary>
    /// <exception cref="SnapshotReadException">The file is not a Snapshot this assembly can read.</exception>
    public static SnapshotRecord Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var gzip = OpenGzip(source, ReadMagic(source));
        var (version, prefix) = Sniff(gzip);
        return Deserialize(version, () => JsonSerializer.Deserialize<SnapshotRecord>(new PrefixedStream(prefix, gzip), SnapshotJson.Options));
    }

    /// <inheritdoc cref="Read(Stream)"/>
    public static async Task<SnapshotRecord> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var gzip = OpenGzip(source, await ReadMagicAsync(source, cancellationToken).ConfigureAwait(false));
        var (version, prefix) = await SniffAsync(gzip, cancellationToken).ConfigureAwait(false);
        return await DeserializeAsync(
            version,
            () => JsonSerializer.DeserializeAsync<SnapshotRecord>(new PrefixedStream(prefix, gzip), SnapshotJson.Options, cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads only the <c>schemaVersion</c> from the start of the Snapshot in <paramref name="source"/> and checks that
    /// this assembly can read the file, without parsing the rest.
    /// </summary>
    /// <returns>The file's <c>schemaVersion</c>.</returns>
    /// <exception cref="SnapshotReadException">The file is not a Snapshot, or has a major version this assembly does not read.</exception>
    public static string SniffSchemaVersion(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var gzip = OpenGzip(source, ReadMagic(source));
        return Sniff(gzip).Version;
    }

    /// <inheritdoc cref="SniffSchemaVersion(Stream)"/>
    public static async Task<string> SniffSchemaVersionAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var gzip = OpenGzip(source, await ReadMagicAsync(source, cancellationToken).ConfigureAwait(false));
        return (await SniffAsync(gzip, cancellationToken).ConfigureAwait(false)).Version;
    }

    private static byte[] ReadMagic(Stream source)
    {
        var magic = new byte[2];
        return CheckMagic(magic, source.ReadAtLeast(magic, magic.Length, throwOnEndOfStream: false));
    }

    private static async Task<byte[]> ReadMagicAsync(Stream source, CancellationToken cancellationToken)
    {
        var magic = new byte[2];
        return CheckMagic(magic, await source.ReadAtLeastAsync(magic, magic.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false));
    }

    // GZipStream reports a file that is not gzip as "an unsupported compression method", so the magic bytes are
    // checked here, and then put back in front of the stream.
    private static byte[] CheckMagic(byte[] magic, int read)
    {
        if (read == 0)
        {
            throw new SnapshotReadException(SnapshotReadFailure.NotGzip, "The file is not a Snapshot: it is empty.");
        }

        if (read < magic.Length || magic[0] != 0x1F || magic[1] != 0x8B)
        {
            throw new SnapshotReadException(SnapshotReadFailure.NotGzip, "The file is not a Snapshot: it is not gzip-compressed.");
        }

        return magic;
    }

    private static GZipStream OpenGzip(Stream source, byte[] magic) =>
        new(new PrefixedStream(magic, source), CompressionMode.Decompress);

    private static (string Version, ReadOnlyMemory<byte> Prefix) Sniff(Stream decompressed)
    {
        var buffer = new byte[PrefixLimit];
        var filled = 0;
        while (true)
        {
            if (TryReadSchemaVersion(buffer.AsSpan(0, filled), isFinalBlock: false) is { } version)
            {
                return (Check(version), buffer.AsMemory(0, filled));
            }

            if (filled == buffer.Length)
            {
                throw NotSnapshot($"its first {PrefixLimit} bytes hold no {SchemaVersionProperty}.");
            }

            var read = Decompress(() => decompressed.Read(buffer, filled, buffer.Length - filled));
            if (read == 0)
            {
                return (Check(ReadSchemaVersionAtEnd(buffer.AsSpan(0, filled))), buffer.AsMemory(0, filled));
            }

            filled += read;
        }
    }

    private static async Task<(string Version, ReadOnlyMemory<byte> Prefix)> SniffAsync(Stream decompressed, CancellationToken cancellationToken)
    {
        var buffer = new byte[PrefixLimit];
        var filled = 0;
        while (true)
        {
            if (TryReadSchemaVersion(buffer.AsSpan(0, filled), isFinalBlock: false) is { } version)
            {
                return (Check(version), buffer.AsMemory(0, filled));
            }

            if (filled == buffer.Length)
            {
                throw NotSnapshot($"its first {PrefixLimit} bytes hold no {SchemaVersionProperty}.");
            }

            var read = await DecompressAsync(() => decompressed.ReadAsync(buffer.AsMemory(filled), cancellationToken)).ConfigureAwait(false);
            if (read == 0)
            {
                return (Check(ReadSchemaVersionAtEnd(buffer.AsSpan(0, filled))), buffer.AsMemory(0, filled));
            }

            filled += read;
        }
    }

    private static string ReadSchemaVersionAtEnd(ReadOnlySpan<byte> prefix) =>
        TryReadSchemaVersion(prefix, isFinalBlock: true)
        ?? throw new SnapshotReadException(SnapshotReadFailure.Corrupt, "The Snapshot is truncated: it ends before its schemaVersion.");

    /// <summary>
    /// Reads <c>{"schemaVersion": "…"</c> from the start of <paramref name="prefix"/>; <c>null</c> when the prefix ends
    /// before it.
    /// </summary>
    private static string? TryReadSchemaVersion(ReadOnlySpan<byte> prefix, bool isFinalBlock)
    {
        var reader = new Utf8JsonReader(prefix, isFinalBlock, state: default);
        try
        {
            if (!reader.Read())
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw NotSnapshot("its JSON is not an object.");
            }

            if (!reader.Read())
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals(SchemaVersionProperty))
            {
                throw NotSnapshot($"the first property of its JSON is not {SchemaVersionProperty}.");
            }

            if (!reader.Read())
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw NotSnapshot($"its {SchemaVersionProperty} is not a string.");
            }

            return reader.GetString();
        }
        catch (JsonException) when (isFinalBlock)
        {
            // Every byte was already read without error as a partial block: the JSON only ends too early.
            return null;
        }
        catch (JsonException exception)
        {
            throw NotSnapshot("it does not decompress to JSON.", exception);
        }
    }

    private static string Check(string version)
    {
        if (ParseMajor(version) is not { } major)
        {
            throw NotSnapshot($"its {SchemaVersionProperty} \"{version}\" is not major.minor.");
        }

        if (major == SupportedMajor)
        {
            return version;
        }

        var (writer, remedy) = major > SupportedMajor
            ? ("a newer", "Update it with `dotnet tool update -g Typescape`.")
            : ("an older", "Analyze the solution again with this Typescape.");
        throw new SnapshotReadException(
            SnapshotReadFailure.UnsupportedSchemaVersion,
            $"The Snapshot has schemaVersion {version}, but this Typescape reads schemaVersion {SupportedMajor}.x: it was written by {writer} Typescape. {remedy}")
        {
            SchemaVersion = version,
        };
    }

    private static int? ParseMajor(string version)
    {
        var dot = version.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0 || !IsNumber(version.AsSpan(0, dot)) || !IsNumber(version.AsSpan(dot + 1)))
        {
            return null;
        }

        return int.TryParse(version.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : null;
    }

    private static bool IsNumber(ReadOnlySpan<char> text) => !text.IsEmpty && !text.ContainsAnyExceptInRange('0', '9');

    private static SnapshotRecord Deserialize(string version, Func<SnapshotRecord?> deserialize)
    {
        try
        {
            return Decompress(deserialize) ?? throw Corrupt(version, "its JSON is null.");
        }
        catch (JsonException exception)
        {
            throw Corrupt(version, exception.Message, exception);
        }
    }

    private static async Task<SnapshotRecord> DeserializeAsync(string version, Func<ValueTask<SnapshotRecord?>> deserialize)
    {
        try
        {
            return await DecompressAsync(deserialize).ConfigureAwait(false) ?? throw Corrupt(version, "its JSON is null.");
        }
        catch (JsonException exception)
        {
            throw Corrupt(version, exception.Message, exception);
        }
    }

    private static T Decompress<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (InvalidDataException exception)
        {
            throw CorruptGzip(exception);
        }
    }

    private static async ValueTask<T> DecompressAsync<T>(Func<ValueTask<T>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw CorruptGzip(exception);
        }
    }

    private static SnapshotReadException NotSnapshot(string reason, Exception? innerException = null) =>
        new(SnapshotReadFailure.NotSnapshot, $"The file is not a Snapshot: {reason}", innerException);

    private static SnapshotReadException CorruptGzip(InvalidDataException exception) =>
        new(SnapshotReadFailure.Corrupt, $"The Snapshot is corrupt: its gzip data cannot be decompressed ({exception.Message})", exception);

    private static SnapshotReadException Corrupt(string version, string reason, Exception? innerException = null) =>
        new(SnapshotReadFailure.Corrupt, $"The Snapshot is corrupt or truncated: {reason}", innerException)
        {
            SchemaVersion = version,
        };
}
