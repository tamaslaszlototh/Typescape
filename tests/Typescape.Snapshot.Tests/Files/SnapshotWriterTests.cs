using System.IO.Compression;
using System.Text.Json;
using Typescape.Snapshot.Files;
using Typescape.Snapshot.Json;

namespace Typescape.Snapshot.Tests.Files;

public class SnapshotWriterTests
{
    [Fact]
    public void Output_is_gzip_of_the_compact_Snapshot_json()
    {
        var written = Write(SnapshotRecordTests.Sample());

        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(SnapshotRecordTests.Sample(), SnapshotJson.Options), Decompress(written));
    }

    [Fact]
    public void SchemaVersion_is_the_first_property_of_the_written_file()
    {
        var reader = new Utf8JsonReader(Decompress(Write(SnapshotRecordTests.Sample())));

        Assert.True(reader.Read());
        Assert.Equal(JsonTokenType.StartObject, reader.TokenType);
        Assert.True(reader.Read());
        Assert.Equal(JsonTokenType.PropertyName, reader.TokenType);
        Assert.Equal("schemaVersion", reader.GetString());
        Assert.True(reader.Read());
        Assert.Equal(SnapshotRecord.CurrentSchemaVersion, reader.GetString());
    }

    [Fact]
    public void Same_records_give_identical_bytes()
    {
        var first = Write(SnapshotRecordTests.Sample());
        var second = Write(SnapshotRecordTests.Sample());

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Async_writing_gives_the_same_bytes_as_sync_writing()
    {
        using var destination = new MemoryStream();

        await SnapshotWriter.WriteAsync(SnapshotRecordTests.Sample(), destination, TestContext.Current.CancellationToken);

        Assert.Equal(Write(SnapshotRecordTests.Sample()), destination.ToArray());
    }

    [Fact]
    public void Gzip_header_names_no_file_time_or_os()
    {
        var header = Write(SnapshotRecordTests.Sample())[..10];

        // ID1 ID2, deflate, no flags, MTIME 0, XFL 0, OS 255 (unknown): nothing that depends on when or where it was written.
        Assert.Equal([0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF], header);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(64)]
    public async Task Os_byte_is_normalized_however_the_header_is_split_across_writes(int chunk)
    {
        byte[] gzip = [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x13, 0xAA, 0xBB, 0x13];
        byte[] expected = [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xAA, 0xBB, 0x13];
        using var sync = new MemoryStream();
        using var async = new MemoryStream();
        var syncNormalizer = new GzipHeaderNormalizingStream(sync);
        var asyncNormalizer = new GzipHeaderNormalizingStream(async);

        foreach (var part in gzip.Chunk(chunk))
        {
            syncNormalizer.Write(part);
            await asyncNormalizer.WriteAsync(part, TestContext.Current.CancellationToken);
        }

        Assert.Equal(expected, sync.ToArray());
        Assert.Equal(expected, async.ToArray());
    }

    [Fact]
    public void Records_round_trip_through_the_written_file()
    {
        var original = SnapshotRecordTests.Sample();

        var read = SnapshotReader.Read(new MemoryStream(Write(original)));

        Assert.Equal(
            JsonSerializer.Serialize(original, SnapshotJson.Options),
            JsonSerializer.Serialize(read, SnapshotJson.Options));
    }

    [Fact]
    public void Destination_is_left_open()
    {
        using var destination = new MemoryStream();

        SnapshotWriter.Write(SnapshotRecordTests.Sample(), destination);

        Assert.True(destination.CanWrite);
    }

    private static byte[] Write(SnapshotRecord snapshot)
    {
        using var destination = new MemoryStream();
        SnapshotWriter.Write(snapshot, destination);
        return destination.ToArray();
    }

    private static byte[] Decompress(byte[] gzip)
    {
        using var source = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress);
        using var json = new MemoryStream();
        source.CopyTo(json);
        return json.ToArray();
    }
}
