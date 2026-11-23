namespace Typescape.Snapshot.Serialization;

/// <summary>
/// A write-only pass-through to <see cref="Inner"/> that sets the OS byte of the gzip header to 255 ("unknown").
/// <see cref="System.IO.Compression.GZipStream"/> writes the platform it runs on there (3 on Linux, 19 on macOS,
/// 10 on Windows), so without this the same records would give different files on different platforms.
/// </summary>
/// <remarks>Every other header byte is already fixed: no file name, no comment, and a modification time of 0.</remarks>
internal sealed class GzipHeaderNormalizingStream(Stream inner) : Stream
{
    /// <summary>The offset of the OS byte in the gzip header (RFC 1952, section 2.3).</summary>
    private const int OsOffset = 9;

    private const byte UnknownOs = 255;

    private long written;

    public Stream Inner { get; } = inner;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (!CoversOsByte(buffer.Length))
        {
            Inner.Write(buffer);
            written += buffer.Length;
            return;
        }

        var normalized = buffer.ToArray();
        Normalize(normalized);
        Inner.Write(normalized);
        written += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (CoversOsByte(buffer.Length))
        {
            var normalized = buffer.ToArray();
            Normalize(normalized);
            buffer = normalized;
        }

        await Inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        written += buffer.Length;
    }

    public override void Flush() => Inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => Inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    private bool CoversOsByte(int count) => written <= OsOffset && written + count > OsOffset;

    private void Normalize(byte[] chunk) => chunk[OsOffset - written] = UnknownOs;
}
