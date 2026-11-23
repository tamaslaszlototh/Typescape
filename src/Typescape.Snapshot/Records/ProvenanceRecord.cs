namespace Typescape.Snapshot.Records;

/// <summary>
/// How the Snapshot was produced (tool, environment, source revision, run measurements), rather than facts about the
/// code. The only part of a Snapshot that may differ between two analyses of the same code: everything that depends on
/// the clock, the machine or the run lives here and nowhere else.
/// </summary>
/// <param name="Tool">The Typescape build that wrote the Snapshot.</param>
/// <param name="Timestamp">When the analysis started, in UTC, to the whole second.</param>
/// <param name="Solution">The analyzed solution file.</param>
/// <param name="SdkVersion">The .NET SDK <c>dotnet --version</c> picks in the solution directory, e.g. <c>10.0.100</c>.</param>
/// <param name="Os">The OS and architecture, e.g. <c>macOS 27.2.1 Arm64</c>; never the machine name.</param>
/// <param name="Git">The analyzed solution's git state; <c>null</c> when the solution is not in a git repository.</param>
/// <param name="Framework">The requested <c>--framework</c>; <c>null</c> when none was passed.</param>
/// <param name="Configuration">The MSBuild configuration, <c>Debug</c> unless <c>-p:Configuration=…</c> says otherwise.</param>
/// <param name="Properties">
/// Every MSBuild global property passed with <c>-p</c>, by name (a repeated name keeps its last value), in ordinal name
/// order. <paramref name="Configuration"/> holds the effective configuration whether or not it was passed.
/// </param>
/// <param name="PhaseSeconds">The wall-clock duration of each pipeline phase, rounded to milliseconds.</param>
/// <param name="PeakMemoryBytes">
/// The tool process's peak working set (<c>Process.PeakWorkingSet64</c>, <c>getrusage</c> on macOS), in bytes; the
/// BuildHost is not measured.
/// </param>
/// <remarks>
/// The root's <see cref="SnapshotRecord.SchemaVersion"/> and <see cref="SnapshotRecord.RulesVersion"/> are Provenance
/// too. They stay on the root, not repeated here, because <c>schemaVersion</c> has to be the root's first property and
/// two copies could disagree.
/// </remarks>
public sealed record ProvenanceRecord(
    ToolProvenanceRecord Tool,
    DateTimeOffset Timestamp,
    SolutionProvenanceRecord Solution,
    string SdkVersion,
    string Os,
    GitProvenanceRecord? Git,
    string? Framework,
    string Configuration,
    IReadOnlyDictionary<string, string> Properties,
    PhaseSecondsRecord PhaseSeconds,
    long PeakMemoryBytes);

/// <summary>The Typescape build that wrote a Snapshot.</summary>
/// <param name="Version">The tool version (SemVer 2, from the git tag), e.g. <c>0.1.0</c>.</param>
/// <param name="Commit">The full commit SHA the tool was built from; <c>null</c> when it was built outside a git checkout.</param>
public sealed record ToolProvenanceRecord(string Version, string? Commit);

/// <summary>The analyzed solution file.</summary>
/// <param name="FileName">The file name only, e.g. <c>Jellyfin.sln</c>; never its directory.</param>
/// <param name="Format">The solution file format.</param>
public sealed record SolutionProvenanceRecord(string FileName, SolutionFormat Format);

public enum SolutionFormat
{
    Sln,
    Slnx,
}

/// <summary>The git state of the analyzed solution's working tree.</summary>
/// <param name="Commit">The full SHA of <c>HEAD</c>.</param>
/// <param name="Branch">The checked-out branch; <c>null</c> on a detached <c>HEAD</c>.</param>
/// <param name="Dirty">Whether the working tree has uncommitted changes, untracked files included.</param>
public sealed record GitProvenanceRecord(string Commit, string? Branch, bool Dirty);

/// <summary>
/// The wall-clock seconds of each pipeline phase. The final write is missing because it cannot be recorded inside the
/// file being written; it appears only on stderr.
/// </summary>
/// <param name="Restore">The restore phase; <c>null</c> when it was skipped with <c>--no-restore</c>.</param>
/// <param name="Load">The load phase.</param>
/// <param name="Compile">The compile phase.</param>
/// <param name="Diagnostics">The compiler diagnostics phase.</param>
/// <param name="Walk">The walk phase.</param>
/// <param name="Assemble">The assemble phase.</param>
public sealed record PhaseSecondsRecord(
    double? Restore,
    double Load,
    double Compile,
    double Diagnostics,
    double Walk,
    double Assemble);
