using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Typescape.Analyzer.Extraction;
using Typescape.Analyzer.Loading;
using Typescape.Analyzer.Pipeline;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Tests.Fakes;

/// <summary>
/// Stages over real Roslyn objects that do no real work: they log every call and measure how many calls of a phase run
/// at once. Each project gets a fresh <see cref="CSharpCompilation"/> that nothing but the pipeline holds.
/// </summary>
internal sealed class FakeStages(params int[] documentsPerProject) : IAnalysisStages
{
    private readonly ConcurrentDictionary<string, InFlight> _inFlight = new();
    private readonly ConcurrentDictionary<string, Gate> _gates = new();

    /// <summary>Every stage call, as <c>"&lt;phase&gt; start"</c> and <c>"&lt;phase&gt; end"</c>, in the order they happened.</summary>
    public ConcurrentQueue<string> Log { get; } = new();

    /// <summary>
    /// Where each walk result came from: the project key and the document, <c>null</c> for the declarations. Keyed by
    /// reference, because the results are records.
    /// </summary>
    public ConcurrentDictionary<ProjectFacts, (string Project, SyntaxTree? Document)> FactSources { get; } =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>The compilation Compile produced per project key.</summary>
    public ConcurrentDictionary<string, WeakReference<Compilation>> Compilations { get; } = new();

    /// <summary>The compilations Diagnostics and Walk were handed, per phase.</summary>
    public ConcurrentBag<(string Phase, string Project, Compilation Compilation)> Received { get; } = new();

    /// <summary>When set, the phase's calls wait until this many of them run at once (or time out and fail).</summary>
    public Dictionary<string, int> Gates { get; } = [];

    /// <summary>Runs inside Assemble, before it returns its Snapshot.</summary>
    public Action<AssembleInput>? OnAssemble { get; init; }

    public SnapshotRecord Snapshot { get; init; } = Snapshots.WithStatuses();

    public Action<string>? OnStart { get; init; }

    public IReadOnlyList<string> RestoreWarnings { get; init; } = [];

    public IReadOnlyList<ReferenceAssemblyFailure> ReferenceAssemblyFailures { get; init; } = [];

    public bool ObservesCancellation { get; init; }

    public string? FailingPhase { get; init; }

    public TrackingWorkspace? Workspace { get; private set; }

    public int MaxInFlight(string phase) => _inFlight.TryGetValue(phase, out var inFlight) ? inFlight.Max : 0;

    public Task<IReadOnlyList<string>> RestoreAsync(AnalysisRequest request, CancellationToken cancellationToken) =>
        Track("restore", cancellationToken, () => Task.FromResult(RestoreWarnings));

    public Task<LoadedSolution> LoadAsync(AnalysisRequest request, PhaseCounter loaded, CancellationToken cancellationToken) =>
        Track("load", cancellationToken, () =>
        {
            var workspace = Workspace = new TrackingWorkspace();
            loaded.Start(documentsPerProject.Length);
            for (var i = 0; i < documentsPerProject.Length; i++)
            {
                workspace.AddProject($"P{i}", LanguageNames.CSharp);
                loaded.Increment();
            }

            var solution = workspace.CurrentSolution;
            var projects = solution.Projects
                .OrderBy(project => project.Name, StringComparer.Ordinal)
                .Select(project => new LoadedProject($"{project.Name}/{project.Name}.csproj", project, "net10.0", ["net10.0"]))
                .ToArray();
            return Task.FromResult(new LoadedSolution(workspace, solution, "/repo", projects, [], [], [], ReferenceAssemblyFailures));
        });

    public async Task<CompiledProject> CompileAsync(LoadedSolution solution, LoadedProject project, CancellationToken cancellationToken)
    {
        Begin("compile", cancellationToken);
        using var _ = Enter("compile");
        await WaitAtGateAsync("compile");
        await Task.Yield();
        Thread.Sleep(2);

        var count = documentsPerProject[int.Parse(project.Project.Name[1..])];
        var trees = Enumerable.Range(0, count)
            .Select(d => CSharpSyntaxTree.ParseText($"class C{d} {{ }}", path: $"/repo/{project.Project.Name}/C{d}.cs"));
        var compilation = CSharpCompilation.Create(project.Project.Name, trees);
        Compilations[project.Key] = new WeakReference<Compilation>(compilation);

        Log.Enqueue("compile end");
        return new CompiledProject(project, compilation, new ProjectContext(project.Key, solution.Directory, false, new HashSet<string>()));
    }

    public ProjectDiagnostics Diagnose(CompiledProject project, CancellationToken cancellationToken) =>
        Track("diagnostics", cancellationToken, () =>
        {
            Received.Add(("diagnostics", project.Project.Key, project.Compilation));
            return new ProjectDiagnostics(new CompileErrorsRecord(0, []), ObjectResolves: true);
        });

    public ProjectFacts WalkDeclarations(CompiledProject project, CancellationToken cancellationToken) =>
        Walk(project, document: null, cancellationToken);

    public ProjectFacts WalkDocument(CompiledProject project, SyntaxTree document, CancellationToken cancellationToken) =>
        Walk(project, document, cancellationToken);

    public SnapshotRecord Assemble(AssembleInput input, CancellationToken cancellationToken) =>
        Track("assemble", cancellationToken, () =>
        {
            OnAssemble?.Invoke(input);
            return Snapshot;
        });

    private ProjectFacts Walk(CompiledProject project, SyntaxTree? document, CancellationToken cancellationToken) =>
        Track("walk", cancellationToken, () =>
        {
            Received.Add(("walk", project.Project.Key, project.Compilation));
            var facts = new ProjectFacts();
            FactSources[facts] = (project.Project.Key, document);
            return facts;
        });

    private T Track<T>(string phase, CancellationToken cancellationToken, Func<T> body)
    {
        Begin(phase, cancellationToken);
        T result;
        using (Enter(phase))
        {
            WaitAtGateAsync(phase).GetAwaiter().GetResult();
            Thread.Sleep(2);
            result = body();
        }

        Log.Enqueue($"{phase} end");
        return result;
    }

    private void Begin(string phase, CancellationToken cancellationToken)
    {
        Log.Enqueue($"{phase} start");
        OnStart?.Invoke(phase);
        if (ObservesCancellation)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (phase == FailingPhase)
        {
            throw new InvalidOperationException($"The {phase} stage failed.");
        }
    }

    private InFlight.Scope Enter(string phase) => _inFlight.GetOrAdd(phase, _ => new InFlight()).Enter();

    private Task WaitAtGateAsync(string phase)
    {
        if (!Gates.TryGetValue(phase, out var size))
        {
            return Task.CompletedTask;
        }

        var gate = _gates.GetOrAdd(phase, _ => new Gate(size));
        return gate.ArriveAsync(phase);
    }

    /// <summary>Lets its callers through only once <c>size</c> of them have arrived and are waiting together.</summary>
    private sealed class Gate(int size)
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task ArriveAsync(string phase)
        {
            if (Interlocked.Increment(ref _arrived) >= size)
            {
                _open.TrySetResult();
            }

            try
            {
                await _open.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"Only {Volatile.Read(ref _arrived)} {phase} calls ran at once; expected {size}.");
            }
        }
    }

    private sealed class InFlight
    {
        private int _current;
        private int _max;

        public int Max => Volatile.Read(ref _max);

        public Scope Enter()
        {
            var current = Interlocked.Increment(ref _current);
            int max;
            while (current > (max = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, current, max) != max)
            {
            }

            return new Scope(this);
        }

        public readonly struct Scope(InFlight owner) : IDisposable
        {
            public void Dispose() => Interlocked.Decrement(ref owner._current);
        }
    }
}

// AdhocWorkspace is sealed, so disposal is tracked on a Workspace of our own.
internal sealed class TrackingWorkspace() : Workspace(MefHostServices.DefaultHost, "Tracking")
{
    public bool Disposed { get; private set; }

    public void AddProject(string name, string language) =>
        OnProjectAdded(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), name, name, language));

    protected override void Dispose(bool finalize)
    {
        Disposed = true;
        base.Dispose(finalize);
    }
}

internal sealed class RecordingProgress : IProgress<AnalysisProgress>
{
    private readonly ConcurrentQueue<AnalysisProgress> _events = new();

    public IReadOnlyList<AnalysisProgress> Events => [.. _events];

    public void Report(AnalysisProgress value) => _events.Enqueue(value);
}

internal static class Snapshots
{
    /// <summary>A Snapshot whose only content is one project per status, with that Load Health verdict.</summary>
    public static SnapshotRecord WithStatuses(params LoadStatus[] statuses) =>
        new(
            SchemaVersion: SnapshotRecord.CurrentSchemaVersion,
            RulesVersion: 1,
            Projects: [.. statuses.Select((_, i) => new ProjectRecord($"P{i}/P{i}.csproj", $"P{i}", $"P{i}", ProjectStyle.Sdk, "net10.0", false, null, []))],
            Namespaces: [],
            Documents: [],
            Types: [],
            ExternalTypes: [],
            ExecutableMembers: [],
            ExternalMembers: [],
            NonExecutableMembers: [],
            Dependencies: [],
            LoadHealth: new LoadHealthRecord(
                [.. statuses.Select((status, i) => new ProjectHealthRecord(i, status, ["net10.0"], 200, true,
                    new CompileErrorsRecord(0, []), new UnresolvedTypesRecord(0, []), [], false, false, []))],
                []),
            Provenance: new ProvenanceRecord(
                new ToolProvenanceRecord("0.0.0", null),
                new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero),
                new SolutionProvenanceRecord("Fake.slnx", SolutionFormat.Slnx),
                "10.0.100",
                "Test OS",
                Git: null,
                Framework: null,
                Configuration: "Debug",
                Properties: new Dictionary<string, string>(),
                new PhaseSecondsRecord(null, 0, 0, 0, 0, 0),
                PeakMemoryBytes: 0),
            Occurrences: null);
}
