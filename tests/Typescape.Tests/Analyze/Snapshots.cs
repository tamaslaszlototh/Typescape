using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Tests.Analyze;

internal static class Snapshots
{
    /// <summary>A Snapshot of a solution with these projects, in this order, and no solution diagnostics.</summary>
    public static SnapshotRecord WithProjects(params (string Name, ProjectHealthRecord Health)[] projects) =>
        WithProjects([], projects);

    public static SnapshotRecord WithProjects(
        IReadOnlyList<WorkspaceDiagnosticRecord> solutionDiagnostics,
        params (string Name, ProjectHealthRecord Health)[] projects) =>
        Empty() with
        {
            Projects = [.. projects.Select(project => new ProjectRecord($"{project.Name}/{project.Name}.csproj", project.Name, project.Name, ProjectStyle.Sdk, "net10.0", false, null, []))],
            LoadHealth = new LoadHealthRecord(
                [.. projects.Select((project, i) => project.Health with { Project = i })],
                solutionDiagnostics),
        };

    /// <summary>A Snapshot with one project per status, named P0, P1, …</summary>
    public static SnapshotRecord WithStatuses(params LoadStatus[] statuses) =>
        WithProjects([.. statuses.Select((status, i) => ($"P{i}", Health(status)))]);

    /// <summary>The Load Health of a project that loaded fully, unless the arguments say otherwise.</summary>
    public static ProjectHealthRecord Health(
        LoadStatus status = LoadStatus.Ok,
        int metadataReferences = 150,
        bool objectResolves = true,
        CompileErrorsRecord? compileErrors = null,
        IReadOnlyList<WorkspaceDiagnosticRecord>? workspaceDiagnostics = null,
        int analyzerErrors = 0) =>
        new(
            Project: 0,
            status,
            ["net10.0"],
            metadataReferences,
            objectResolves,
            compileErrors ?? new CompileErrorsRecord(0, []),
            new UnresolvedTypesRecord(0, []),
            workspaceDiagnostics ?? [],
            SdkFallback: false,
            ProjectReferenceMismatch: false,
            [.. Enumerable.Repeat(new AnalyzerErrorRecord(0, "System.InvalidCastException", "Walker.Visit()"), analyzerErrors)]);

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
