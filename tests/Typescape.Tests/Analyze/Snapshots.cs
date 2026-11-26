using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Tests.Analyze;

internal static class Snapshots
{
    /// <summary>A Snapshot of a solution without projects; <paramref name="configuration"/> tells two apart.</summary>
    public static SnapshotRecord Empty(string configuration = "Debug") =>
        new(
            SnapshotRecord.CurrentSchemaVersion,
            RulesVersion: 1,
            Projects: [],
            Namespaces: [],
            Documents: [],
            Types: [],
            ExternalTypes: [],
            ExecutableMembers: [],
            ExternalMembers: [],
            NonExecutableMembers: [],
            Dependencies: [],
            new LoadHealthRecord([], []),
            new ProvenanceRecord(
                new ToolProvenanceRecord("0.0.0", null),
                DateTimeOffset.UnixEpoch,
                new SolutionProvenanceRecord("App.sln", SolutionFormat.Sln),
                SdkVersion: "10.0.100",
                Os: "test",
                Git: null,
                Framework: null,
                configuration,
                new Dictionary<string, string>(),
                new PhaseSecondsRecord(null, 0, 0, 0, 0, 0),
                PeakMemoryBytes: 0),
            Occurrences: []);
}
