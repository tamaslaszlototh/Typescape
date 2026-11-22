using Typescape.Testing;
using Xunit.Sdk;

namespace Typescape.Snapshot.Tests;

public sealed class GoldenTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "typescape-golden-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string VerifiedPath => Path.Combine(directory, "sample.verified.json");

    private string ReceivedPath => Path.Combine(directory, "sample.received.json");

    [Fact]
    public void Matching_output_passes_and_removes_a_stale_received_file()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(VerifiedPath, "{\n  \"a\": 1\n}\n");
        File.WriteAllText(ReceivedPath, "stale");

        Golden.Match("{\r\n  \"a\": 1\r\n}\r\n", directory, "sample", "json", accept: false);

        Assert.False(File.Exists(ReceivedPath));
    }

    [Fact]
    public void Missing_golden_file_fails_and_writes_the_received_output()
    {
        var error = Assert.Throws<XunitException>(() => Golden.Match("{}", directory, "sample", "json", accept: false));

        Assert.Contains("No golden file", error.Message, StringComparison.Ordinal);
        Assert.Equal("{}", File.ReadAllText(ReceivedPath));
        Assert.False(File.Exists(VerifiedPath));
    }

    [Fact]
    public void Different_output_fails_and_names_the_first_differing_line()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(VerifiedPath, "one\ntwo\n");

        var error = Assert.Throws<XunitException>(() => Golden.Match("one\nTWO\n", directory, "sample", "json", accept: false));

        Assert.Contains("line 2", error.Message, StringComparison.Ordinal);
        Assert.Contains("received: TWO", error.Message, StringComparison.Ordinal);
        Assert.Equal("one\nTWO\n", File.ReadAllText(ReceivedPath));
    }

    [Fact]
    public void Accept_mode_overwrites_the_golden_file_with_LF_line_endings()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(VerifiedPath, "old\n");
        File.WriteAllText(ReceivedPath, "old received");

        Golden.Match("new\r\n", directory, "sample", "json", accept: true);

        Assert.Equal("new\n", File.ReadAllText(VerifiedPath));
        Assert.False(File.Exists(ReceivedPath));
    }
}
