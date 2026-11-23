namespace Typescape.Snapshot.Files;

/// <summary>
/// A read-only stream that returns <c>prefix</c> and then the rest of <c>rest</c>: it puts back the bytes a reader
/// had to look at before it knew what to do with the stream. <c>rest</c> is not disposed with it.
/// </summary>
internal sealed class PrefixedStream(ReadOnlyMemory<byte> prefix, Stream rest) : Stream
{
    private ReadOnlyMemory<byte> _prefix = prefix;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_prefix.IsEmpty)
        {
            return rest.Read(buffer);
        }

        return TakePrefix(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_prefix.IsEmpty)
        {
            return rest.ReadAsync(buffer, cancellationToken);
        }

        return ValueTask.FromResult(TakePrefix(buffer.Span));
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int TakePrefix(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, _prefix.Length);
        _prefix.Span[..count].CopyTo(buffer);
        _prefix = _prefix[count..];
        return count;
    }
}
