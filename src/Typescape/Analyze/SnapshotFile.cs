using Typescape.Snapshot;
using Typescape.Snapshot.Files;

namespace Typescape.Analyze;

/// <summary>Writes the Snapshot file so that a reader never sees it half written.</summary>
internal static class SnapshotFile
{
    public const string Extension = ".typescape.json.gz";

    /// <summary>The default output, <c>&lt;Solution&gt;.typescape.json.gz</c> in <paramref name="directory"/>.</summary>
    public static string DefaultPath(string solution, string directory) =>
        Path.Combine(directory, Path.GetFileNameWithoutExtension(solution) + Extension);

    /// <summary>
    /// Writes <paramref name="snapshot"/> to a temp file next to <paramref name="path"/>, then renames it over
    /// <paramref name="path"/>, replacing an existing file. On failure or cancellation the temp file is deleted and
    /// <paramref name="path"/> is left as it was.
    /// </summary>
    public static async Task WriteAsync(SnapshotRecord snapshot, string path, CancellationToken cancellationToken)
    {
        // Same directory, so the rename stays on one volume and is atomic.
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                await SnapshotWriter.WriteAsync(snapshot, stream, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    }
}
