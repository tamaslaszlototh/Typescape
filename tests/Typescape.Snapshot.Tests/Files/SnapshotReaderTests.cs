using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Typescape.Snapshot.Files;
using Typescape.Snapshot.Json;

namespace Typescape.Snapshot.Tests.Files;

public class SnapshotReaderTests
{
    [Fact]
    public void Reads_what_the_writer_wrote()
    {
        var read = SnapshotReader.Read(new MemoryStream(Write(SnapshotRecordTests.Sample())));

        Assert.Equal(Json(SnapshotRecordTests.Sample()), Json(read));
    }

    [Fact]
    public async Task Reads_asynchronously_what_the_writer_wrote()
    {
        var read = await SnapshotReader.ReadAsync(new MemoryStream(Write(SnapshotRecordTests.Sample())), TestContext.Current.CancellationToken);

        Assert.Equal(Json(SnapshotRecordTests.Sample()), Json(read));
    }

    [Fact]
    public void Sniff_returns_the_written_schemaVersion()
    {
        Assert.Equal(SnapshotRecord.CurrentSchemaVersion, SnapshotReader.SniffSchemaVersion(new MemoryStream(Write(SnapshotRecordTests.Sample()))));
    }

    [Fact]
    public void Newer_minor_is_read_and_its_unknown_fields_are_ignored()
    {
        var file = Gzip(NewerMinor().ToJsonString());

        var read = SnapshotReader.Read(new MemoryStream(file));

        Assert.Equal("1.7", read.SchemaVersion);
        Assert.Equal(Json(SnapshotRecordTests.Sample() with { SchemaVersion = "1.7" }), Json(read));
    }

    [Fact]
    public async Task Newer_minor_passes_the_sniff()
    {
        var file = Gzip(NewerMinor().ToJsonString());

        Assert.Equal("1.7", SnapshotReader.SniffSchemaVersion(new MemoryStream(file)));
        Assert.Equal("1.7", await SnapshotReader.SniffSchemaVersionAsync(new MemoryStream(file), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Newer_major_is_refused_with_both_versions_and_the_update_command()
    {
        // A newer major may change anything after schemaVersion: the refusal comes before the rest is parsed.
        var file = Gzip("""{"schemaVersion":"2.0","projects":"now a string"}""");

        foreach (var exception in await RefusalsOf(file))
        {
            Assert.Equal(SnapshotReadFailure.UnsupportedSchemaVersion, exception.Reason);
            Assert.Equal("2.0", exception.SchemaVersion);
            Assert.Equal(
                "The Snapshot has schemaVersion 2.0, but this Typescape reads schemaVersion 1.x: it was written by a newer Typescape. "
                + "Update it with `dotnet tool update -g Typescape`.",
                exception.Message);
        }
    }

    [Fact]
    public async Task Older_major_is_refused_with_both_versions_and_the_advice_to_analyze_again()
    {
        var file = Gzip("""{"schemaVersion":"0.9","projects":[]}""");

        foreach (var exception in await RefusalsOf(file))
        {
            Assert.Equal(SnapshotReadFailure.UnsupportedSchemaVersion, exception.Reason);
            Assert.Equal("0.9", exception.SchemaVersion);
            Assert.Equal(
                "The Snapshot has schemaVersion 0.9, but this Typescape reads schemaVersion 1.x: it was written by an older Typescape. "
                + "Analyze the solution again with this Typescape.",
                exception.Message);
        }
    }

    [Fact]
    public async Task Plain_json_is_refused_as_not_gzip()
    {
        var file = JsonSerializer.SerializeToUtf8Bytes(SnapshotRecordTests.Sample(), SnapshotJson.Options);

        foreach (var exception in await RefusalsOf(file))
        {
            Assert.Equal(SnapshotReadFailure.NotGzip, exception.Reason);
            Assert.Equal("The file is not a Snapshot: it is not gzip-compressed.", exception.Message);
        }
    }

    [Theory]
    [InlineData(new byte[0], "The file is not a Snapshot: it is empty.")]
    [InlineData(new byte[] { 0x1F }, "The file is not a Snapshot: it is not gzip-compressed.")]
    public async Task Empty_or_one_byte_file_is_refused_as_not_gzip(byte[] file, string message)
    {
        foreach (var exception in await RefusalsOf(file))
        {
            Assert.Equal(SnapshotReadFailure.NotGzip, exception.Reason);
            Assert.Equal(message, exception.Message);
        }
    }

    [Theory]
    [InlineData("hello", "it does not decompress to JSON.")]
    [InlineData("[1,2]", "its JSON is not an object.")]
    [InlineData("""{"name":"x","schemaVersion":"1.0"}""", "the first property of its JSON is not schemaVersion.")]
    [InlineData("""{"schemaVersion":1}""", "its schemaVersion is not a string.")]
    [InlineData("""{"schemaVersion":"1"}""", "its schemaVersion \"1\" is not major.minor.")]
    [InlineData("""{"schemaVersion":"1.0.0"}""", "its schemaVersion \"1.0.0\" is not major.minor.")]
    [InlineData("""{"schemaVersion":"v1.0"}""", "its schemaVersion \"v1.0\" is not major.minor.")]
    public async Task Gzip_that_is_not_a_Snapshot_is_refused_with_the_reason(string json, string reason)
    {
        foreach (var exception in await RefusalsOf(Gzip(json)))
        {
            Assert.Equal(SnapshotReadFailure.NotSnapshot, exception.Reason);
            Assert.Equal("The file is not a Snapshot: " + reason, exception.Message);
        }
    }

    [Fact]
    public async Task SchemaVersion_must_be_within_the_start_of_the_file()
    {
        var file = Gzip(new string(' ', 5000) + """{"schemaVersion":"1.0"}""");

        foreach (var exception in await RefusalsOf(file))
        {
            Assert.Equal(SnapshotReadFailure.NotSnapshot, exception.Reason);
            Assert.Equal("The file is not a Snapshot: its first 4096 bytes hold no schemaVersion.", exception.Message);
        }
    }

    [Fact]
    public async Task File_truncated_before_its_schemaVersion_is_refused_as_corrupt()
    {
        // The gzip header alone: GZipStream reports a cut-off stream as an early end, not as an error.
        var file = Write(SnapshotRecordTests.Sample())[..10];

        foreach (var exception in await RefusalsOf(file))
        {
            Assert.Equal(SnapshotReadFailure.Corrupt, exception.Reason);
            Assert.Equal("The Snapshot is truncated: it ends before its schemaVersion.", exception.Message);
        }
    }

    [Fact]
    public async Task File_truncated_after_its_schemaVersion_passes_the_sniff_but_is_refused_by_a_full_read()
    {
        var written = Write(SnapshotRecordTests.Sample());
        var file = written[..(written.Length / 2)];

        Assert.Equal(SnapshotRecord.CurrentSchemaVersion, SnapshotReader.SniffSchemaVersion(new MemoryStream(file)));
        var sync = Assert.Throws<SnapshotReadException>(() => SnapshotReader.Read(new MemoryStream(file)));
        var async = await Assert.ThrowsAsync<SnapshotReadException>(
            () => SnapshotReader.ReadAsync(new MemoryStream(file), TestContext.Current.CancellationToken));

        foreach (var exception in new[] { sync, async })
        {
            Assert.Equal(SnapshotReadFailure.Corrupt, exception.Reason);
            Assert.Equal(SnapshotRecord.CurrentSchemaVersion, exception.SchemaVersion);
            Assert.StartsWith("The Snapshot is corrupt or truncated: ", exception.Message);
            Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        }
    }

    [Fact]
    public async Task Gzip_with_a_broken_header_is_refused_as_corrupt()
    {
        var file = Write(SnapshotRecordTests.Sample());
        file[2] = 0x07; // compression method: only 8 (deflate) exists.

        foreach (var exception in await RefusalsOf(file))
        {
            Assert.Equal(SnapshotReadFailure.Corrupt, exception.Reason);
            Assert.StartsWith("The Snapshot is corrupt: its gzip data cannot be decompressed (", exception.Message);
            Assert.IsType<InvalidDataException>(exception.InnerException);
        }
    }

    [Fact]
    public async Task Sniff_reads_only_the_start_of_the_file()
    {
        var file = Gzip("""{"schemaVersion":"1.0",""" + new string('!', 100_000));

        Assert.Equal("1.0", SnapshotReader.SniffSchemaVersion(new MemoryStream(file)));
        Assert.Equal("1.0", await SnapshotReader.SniffSchemaVersionAsync(new MemoryStream(file), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Prefix_is_put_back_when_schemaVersion_arrives_in_one_byte_reads()
    {
        var read = SnapshotReader.Read(new OneByteAtATimeStream(Write(SnapshotRecordTests.Sample())));

        Assert.Equal(Json(SnapshotRecordTests.Sample()), Json(read));
    }

    [Fact]
    public void Source_is_left_open()
    {
        using var source = new MemoryStream(Write(SnapshotRecordTests.Sample()));

        SnapshotReader.Read(source);
        SnapshotReader.SniffSchemaVersion(new MemoryStream(source.ToArray()));

        Assert.True(source.CanRead);
    }

    /// <summary>The sample at schemaVersion 1.7, with fields this assembly does not know at the root and nested.</summary>
    private static JsonNode NewerMinor()
    {
        var root = JsonNode.Parse(Json(SnapshotRecordTests.Sample()))!.AsObject();
        root["schemaVersion"] = "1.7";
        root["futureTable"] = new JsonArray(new JsonObject { ["key"] = "x" }, 3);
        root["projects"]![0]!["futureField"] = new JsonObject { ["nested"] = new JsonArray(1, 2) };
        root["loadHealth"]!["futureFlag"] = true;
        root["provenance"]!["futureNumber"] = 1.5;
        return root;
    }

    /// <summary>The exceptions <see cref="SnapshotReader"/>'s four entry points refuse <paramref name="file"/> with.</summary>
    private static async Task<SnapshotReadException[]> RefusalsOf(byte[] file)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        return
        [
            Assert.Throws<SnapshotReadException>(() => SnapshotReader.Read(new MemoryStream(file))),
            await Assert.ThrowsAsync<SnapshotReadException>(() => SnapshotReader.ReadAsync(new MemoryStream(file), cancellationToken)),
            Assert.Throws<SnapshotReadException>(() => SnapshotReader.SniffSchemaVersion(new MemoryStream(file))),
            await Assert.ThrowsAsync<SnapshotReadException>(() => SnapshotReader.SniffSchemaVersionAsync(new MemoryStream(file), cancellationToken)),
        ];
    }

    private static string Json(SnapshotRecord snapshot) => JsonSerializer.Serialize(snapshot, SnapshotJson.Options);

    private static byte[] Write(SnapshotRecord snapshot)
    {
        using var destination = new MemoryStream();
        SnapshotWriter.Write(snapshot, destination);
        return destination.ToArray();
    }

    private static byte[] Gzip(string json)
    {
        using var destination = new MemoryStream();
        using (var gzip = new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }

        return destination.ToArray();
    }

    private sealed class OneByteAtATimeStream(byte[] content) : MemoryStream(content)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);

        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
    }
}
