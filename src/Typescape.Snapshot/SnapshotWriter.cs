using System.IO.Compression;
using System.Text.Json;
using Typescape.Snapshot.Serialization;

namespace Typescape.Snapshot;

/// <summary>
/// Writes a <see cref="SnapshotRecord"/> as the Snapshot file: compact JSON in the <see cref="SnapshotJson"/> format,
/// gzip-compressed.
/// </summary>
/// <remarks>
/// The output is deterministic: the same records give the same bytes, on every platform. The JSON keeps the records'
/// property and element order (<c>schemaVersion</c> first, every table in the order the caller sorted it), and the gzip
/// header carries no file name, no modification time and no OS. The writer does not sort anything itself: the tables
/// arrive sorted by stable key.
/// </remarks>
public static class SnapshotWriter
{
    private const CompressionLevel Compression = CompressionLevel.Optimal;

    /// <summary>Writes <paramref name="snapshot"/> to <paramref name="destination"/>, which is left open.</summary>
    public static void Write(SnapshotRecord snapshot, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(destination);

        using var header = new GzipHeaderNormalizingStream(destination);
        using (var gzip = new GZipStream(header, Compression, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, snapshot, SnapshotJson.Options);
        }

        destination.Flush();
    }

    /// <inheritdoc cref="Write(SnapshotRecord, Stream)"/>
    public static async Task WriteAsync(SnapshotRecord snapshot, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(destination);

        await using var header = new GzipHeaderNormalizingStream(destination);
        await using (var gzip = new GZipStream(header, Compression, leaveOpen: true))
        {
            await JsonSerializer.SerializeAsync(gzip, snapshot, SnapshotJson.Options, cancellationToken).ConfigureAwait(false);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
