using System.IO.Compression;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Typescape.Snapshot;
using Typescape.Snapshot.Files;
using Typescape.Snapshot.Records;
using Typescape.Testing;

namespace Typescape.Analyzer.Tests.Fixtures;

/// <summary>
/// Runs the full Analyzer (MSBuildWorkspace, restore included) on a fixture solution in <c>tests/fixtures/</c> and
/// compares its Snapshot with a golden file.
/// </summary>
/// <remarks>
/// Each run analyzes a fresh copy outside the repository: restore output never lands in the source tree, two runs
/// cannot race on one <c>obj/</c>, and the repository's <c>Directory.Build.props</c> do not leak into the fixture. The
/// repository's <c>global.json</c> goes along, so the copy builds with the SDK the repository pins.
/// </remarks>
internal static class FixtureSolution
{
    private static readonly string RepositoryDirectory = typeof(FixtureSolution).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "Typescape.RepositoryDirectory")
        .Value!;

    private static readonly JsonSerializerOptions Pretty = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    /// <param name="fixture">The folder name under <c>tests/fixtures/</c>; it holds exactly one <c>.sln</c> or <c>.slnx</c>.</param>
    /// <param name="configure">Changes the request, e.g. its framework or max parallelism.</param>
    public static async Task<SnapshotRecord> AnalyzeAsync(string fixture, Func<AnalysisRequest, AnalysisRequest>? configure = null)
    {
        var copy = Directory.CreateTempSubdirectory($"typescape-fixture-{fixture}-").FullName;
        try
        {
            CopyDirectory(Path.Combine(RepositoryDirectory, "tests", "fixtures", fixture), copy);
            File.Copy(Path.Combine(RepositoryDirectory, "global.json"), Path.Combine(copy, "global.json"));

            var request = new AnalysisRequest { SolutionPath = SolutionIn(copy) };
            var result = await Analyzer.AnalyzeAsync(configure?.Invoke(request) ?? request, progress: null, TestContext.Current.CancellationToken);
            return result.Snapshot;
        }
        finally
        {
            DeleteQuietly(copy);
        }
    }

    /// <summary>
    /// Compares <paramref name="snapshot"/>, decompressed from its file and pretty-printed with Provenance scrubbed,
    /// with <c>Golden/Fixture.&lt;golden&gt;.verified.json</c>.
    /// </summary>
    public static void Match(SnapshotRecord snapshot, string golden) =>
        Golden.Match(Render(Write(snapshot)), $"Fixture.{golden}", "json");

    /// <summary>The Snapshot file's bytes, with the Provenance that depends on the clock, machine or run scrubbed.</summary>
    public static byte[] Write(SnapshotRecord snapshot)
    {
        using var file = new MemoryStream();
        SnapshotWriter.Write(Scrub(snapshot), file);
        return file.ToArray();
    }

    /// <summary>A Snapshot file decompressed and pretty-printed.</summary>
    public static string Render(byte[] file)
    {
        using var gzip = new GZipStream(new MemoryStream(file), CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        return JsonSerializer.Serialize(json.RootElement, Pretty) + "\n";
    }

    // The scrubbed values still satisfy the schema, so a golden file stays a loadable Snapshot. What the request
    // decides (solution file name, framework, configuration, properties, whether restore ran) is kept.
    private static SnapshotRecord Scrub(SnapshotRecord snapshot)
    {
        var provenance = snapshot.Provenance;
        return snapshot with
        {
            Provenance = provenance with
            {
                Tool = new ToolProvenanceRecord("0.0.0", Commit: null),
                Timestamp = DateTimeOffset.UnixEpoch,
                SdkVersion = "0.0.0",
                Os = "scrubbed",
                Git = null,
                PhaseSeconds = new PhaseSecondsRecord(provenance.PhaseSeconds.Restore is null ? null : 0, 0, 0, 0, 0, 0),
                PeakMemoryBytes = 0,
            },
        };
    }

    private static string SolutionIn(string directory)
    {
        var solutions = Directory.EnumerateFiles(directory, "*.sln")
            .Concat(Directory.EnumerateFiles(directory, "*.slnx"))
            .ToList();
        return solutions.Count == 1
            ? solutions[0]
            : throw new InvalidOperationException($"Expected one .sln or .slnx in {directory}, found {solutions.Count}.");
    }

    // bin/ and obj/ are skipped in case someone built the fixture in place.
    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"No fixture at {source}.");
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is not ("bin" or "obj"))
            {
                var target = Path.Combine(destination, name);
                Directory.CreateDirectory(target);
                CopyDirectory(directory, target);
            }
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    // A BuildHost or MSBuild node that outlives the analysis can still hold a file open on Windows; a leftover temp
    // folder must not fail the test.
    private static void DeleteQuietly(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
