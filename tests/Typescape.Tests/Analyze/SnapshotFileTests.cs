using Typescape.Analyze;
using Typescape.Snapshot;
using Typescape.Snapshot.Files;

namespace Typescape.Tests.Analyze;

public sealed class SnapshotFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-write-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void The_default_path_is_the_solution_name_in_the_directory()
    {
        Assert.Equal(
            Path.Combine(_directory, "App.typescape.json.gz"),
            SnapshotFile.DefaultPath(Path.Combine("/repo", "App.slnx"), _directory));
    }

    [Fact]
    public async Task The_written_file_reads_back_and_no_temp_file_is_left()
    {
        var path = Path.Combine(_directory, "App.typescape.json.gz");

        await SnapshotFile.WriteAsync(Snapshots.Empty(), path, TestContext.Current.CancellationToken);

        Assert.Equal(Snapshots.Empty().Provenance.Configuration, Read(path).Provenance.Configuration);
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task An_existing_file_is_overwritten()
    {
        var path = Path.Combine(_directory, "App.typescape.json.gz");
        await SnapshotFile.WriteAsync(Snapshots.Empty("Debug"), path, TestContext.Current.CancellationToken);

        await SnapshotFile.WriteAsync(Snapshots.Empty("Release"), path, TestContext.Current.CancellationToken);

        Assert.Equal("Release", Read(path).Provenance.Configuration);
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Cancelling_deletes_the_temp_file_and_keeps_the_existing_file()
    {
        var path = Path.Combine(_directory, "App.typescape.json.gz");
        await SnapshotFile.WriteAsync(Snapshots.Empty("Debug"), path, TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SnapshotFile.WriteAsync(Snapshots.Empty("Release"), path, new CancellationToken(canceled: true)));

        Assert.Equal("Debug", Read(path).Provenance.Configuration);
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_missing_directory_fails_and_writes_nothing()
    {
        var path = Path.Combine(_directory, "missing", "App.typescape.json.gz");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => SnapshotFile.WriteAsync(Snapshots.Empty(), path, TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    private static SnapshotRecord Read(string path)
    {
        using var stream = File.OpenRead(path);
        return SnapshotReader.Read(stream);
    }
}
