using System.IO.Compression;
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
/// repository's <c>global.json</c> goes along, unless the fixture has its own, so the copy builds with the SDK the
/// repository pins.
/// </remarks>
internal static class FixtureSolution
{
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
            CopyDirectory(Path.Combine(TestMetadata.RepositoryDirectory, "tests", "fixtures", fixture), copy);
            var globalJson = Path.Combine(copy, "global.json");
            if (!File.Exists(globalJson))
            {
                File.Copy(Path.Combine(TestMetadata.RepositoryDirectory, "global.json"), globalJson);
            }

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
    /// with <c>Golden/Fixture.&lt;goldenName&gt;.verified.json</c>.
    /// </summary>
    /// <param name="goldenName">The fixture name, plus a suffix for a variant run, e.g. <c>Modern.net8.0</c>.</param>
    public static void Match(SnapshotRecord snapshot, string goldenName) =>
        Golden.Match(Render(Write(snapshot)), $"Fixture.{goldenName}", "json");

    /// <summary>
    /// <paramref name="snapshot"/> without the Load Health evidence of which MSBuild loaded each project: the one
    /// documented platform exception (spec §10.1). Classic projects go to Visual Studio MSBuild on Windows with Visual
    /// Studio and fall back to .NET SDK MSBuild elsewhere; that choice alone sets
    /// <see cref="ProjectHealthRecord.SdkFallback"/>, words the fallback warning, and decides which project references
    /// the workspace reports as kept (<see cref="WorkspaceDiagnosticKind.Info"/>), among them an SDK-style project's
    /// failed .NET SDK evaluation of a classic reference that Visual Studio MSBuild loaded. It also picks the markup
    /// compiler: Visual Studio's runs on .NET Framework, whose code generator writes one more header line into each
    /// <c>*.g.cs</c> (<c>Runtime Version:4.0.30319.42000</c>), so the lines in those documents are moved back to the .NET
    /// SDK layout.
    /// </summary>
    public static SnapshotRecord WithoutMSBuildChoice(SnapshotRecord snapshot) =>
        WithoutFrameworkMarkupHeader(snapshot) with
        {
            LoadHealth = snapshot.LoadHealth with
            {
                Projects =
                [
                    .. snapshot.LoadHealth.Projects.Select(project => project with
                    {
                        SdkFallback = false,
                        WorkspaceDiagnostics =
                        [
                            .. project.WorkspaceDiagnostics.Where(diagnostic =>
                                diagnostic.Kind != WorkspaceDiagnosticKind.Info
                                && !diagnostic.Message.Contains("will be loaded with the .NET Core SDK", StringComparison.Ordinal)),
                        ],
                    }),
                ],
            },
        };

    private static SnapshotRecord WithoutFrameworkMarkupHeader(SnapshotRecord snapshot)
    {
        // Classic projects that did not fall back: Visual Studio MSBuild loaded them.
        var visualStudioLoaded = snapshot.LoadHealth.Projects
            .Where(health => !health.SdkFallback && snapshot.Projects[health.Project].Style == ProjectStyle.Legacy)
            .Select(health => health.Project)
            .ToHashSet();
        var shifted = snapshot.Documents
            .Select((document, index) => (document, index))
            .Where(entry => visualStudioLoaded.Contains(entry.document.Project)
                && entry.document.Path.Contains("/obj/", StringComparison.Ordinal)
                && entry.document.Path.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(entry => entry.index)
            .ToHashSet();
        if (shifted.Count == 0)
        {
            return snapshot;
        }

        LineSpan Up(LineSpan lines) => new(lines.Start - 1, lines.End - 1);
        return snapshot with
        {
            Types =
            [
                .. snapshot.Types.Select(type => type with
                {
                    Declarations =
                    [
                        .. type.Declarations.Select(declaration =>
                            shifted.Contains(declaration.Document) ? declaration with { Lines = Up(declaration.Lines) } : declaration),
                    ],
                }),
            ],
            ExecutableMembers =
            [
                .. snapshot.ExecutableMembers.Select(member =>
                    shifted.Contains(member.Document) ? member with { Lines = Up(member.Lines) } : member),
            ],
            Occurrences = snapshot.Occurrences is null
                ? null
                :
                [
                    .. snapshot.Occurrences.Select(occurrence =>
                        shifted.Contains(occurrence.Document) ? occurrence with { Line = occurrence.Line - 1 } : occurrence),
                ],
        };
    }

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
