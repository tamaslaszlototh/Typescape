using System.Diagnostics;
using System.Globalization;
using Typescape.Analyzer;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyze;

/// <summary>
/// Everything <c>typescape analyze</c> prints on stderr: one line per phase with its duration, the warnings, and the
/// Load Health.
/// </summary>
/// <param name="error">stderr.</param>
/// <param name="verbosity">How much to print; warnings and non-ok projects are printed at every verbosity.</param>
/// <param name="interactive">
/// Whether stderr is a terminal: then a running phase shows a live counter, redrawn in place with a carriage return.
/// </param>
internal sealed class AnalyzeOutput(TextWriter error, Verbosity verbosity, bool interactive) : IProgress<AnalysisProgress>
{
    private const int NameWidth = 12;
    private const int DurationWidth = 7;
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(100);

    // Parallel workers report counts; one lock keeps lines whole and in order.
    private readonly Lock _lock = new();
    private string? _phase;
    private (int Total, string Unit)? _count;
    private int _liveLength;
    private long _lastDraw;

    public void Report(AnalysisProgress value)
    {
        switch (value)
        {
            case AnalysisProgress.PhaseStarted started:
                Start(Name(started.Phase));
                break;
            case AnalysisProgress.PhaseProgressed progressed:
                Count(progressed.Done, progressed.Total, Unit(progressed.Phase));
                break;
            case AnalysisProgress.PhaseFinished finished:
                Finish(finished.Duration);
                break;
            case AnalysisProgress.Warning warning:
                lock (_lock)
                {
                    WriteLine(warning.Message);
                }

                break;
        }
    }

    /// <summary>Starts a phase of the tool's own, such as <c>write</c>; it ends with <see cref="Finish"/>.</summary>
    public void Start(string phase)
    {
        lock (_lock)
        {
            _phase = phase;
            _count = null;
            Draw(phase, force: true);
        }
    }

    public void Finish(TimeSpan duration)
    {
        lock (_lock)
        {
            if (_phase is null || verbosity == Verbosity.Quiet)
            {
                _phase = null;
                return;
            }

            var line = $"{_phase,-NameWidth}{Duration(duration),DurationWidth}";
            if (_count is var (total, unit))
            {
                line += $"  {Plural(total, unit)}";
            }

            _phase = null;
            WriteLine(line);
        }
    }

    /// <summary>
    /// The summary line, then one line per non-ok project with its main reason; <c>detailed</c> lists every project with
    /// its workspace diagnostics, then the solution's. <c>quiet</c> prints nothing when every project is ok.
    /// </summary>
    public void LoadHealth(LoadHealthSummary summary, SnapshotRecord snapshot)
    {
        var detailed = verbosity == Verbosity.Detailed;
        var rows = snapshot.LoadHealth.Projects
            .Where(health => detailed || health.Status != LoadStatus.Ok)
            .Select(health => (snapshot.Projects[health.Project].Name, Health: health))
            .ToList();

        if (verbosity == Verbosity.Quiet && rows.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{Plural(summary.Projects, "projects")}: {summary.Ok} ok · {summary.Degraded} degraded · {summary.Failed} failed"));

            var nameWidth = rows.Count == 0 ? 0 : rows.Max(row => row.Name.Length);
            var statusWidth = rows.Count == 0 ? 0 : rows.Max(row => Status(row.Health.Status).Length);
            foreach (var (name, health) in rows)
            {
                WriteLine($"  {name.PadRight(nameWidth)}  {Status(health.Status).PadRight(statusWidth)}  {MainReason(health)}".TrimEnd());
                if (detailed)
                {
                    WriteDiagnostics(health.WorkspaceDiagnostics);
                }
            }

            if (detailed && snapshot.LoadHealth.SolutionDiagnostics.Count > 0)
            {
                WriteLine("  solution");
                WriteDiagnostics(snapshot.LoadHealth.SolutionDiagnostics);
            }
        }
    }

    private void WriteDiagnostics(IReadOnlyList<WorkspaceDiagnosticRecord> diagnostics)
    {
        const string indent = "    ";
        const int kindWidth = 7;
        foreach (var diagnostic in diagnostics)
        {
            var lines = diagnostic.Message.ReplaceLineEndings("\n").Split('\n');
            WriteLine($"{indent}{diagnostic.Kind.ToString().ToLowerInvariant(),-kindWidth}  {lines[0]}");
            foreach (var line in lines.Skip(1))
            {
                WriteLine($"{indent}{new string(' ', kindWidth)}  {line}");
            }
        }
    }

    private static string Status(LoadStatus status) => status.ToString().ToLowerInvariant();

    /// <summary>The first that holds, root causes before their symptoms, in the terms of the verdict.</summary>
    private static string MainReason(ProjectHealthRecord health)
    {
        if (health.Status == LoadStatus.Ok)
        {
            return "";
        }

        if (health.Status == LoadStatus.Failed)
        {
            var diagnostic = health.WorkspaceDiagnostics.FirstOrDefault(d => d.Kind == WorkspaceDiagnosticKind.Failure)
                ?? health.WorkspaceDiagnostics.FirstOrDefault(d => d.Kind == WorkspaceDiagnosticKind.Warning);
            return diagnostic is null ? "not loaded" : FirstLine(diagnostic.Message);
        }

        if (health.MetadataReferences == 0)
        {
            return "0 metadata references";
        }

        if (!health.ObjectResolves)
        {
            return "System.Object does not resolve";
        }

        if (health.CompileErrors is { Top: [var top, ..] } errors)
        {
            var reason = string.Create(CultureInfo.InvariantCulture, $"{top.Code} ×{top.Count}");
            return top.Count == errors.Count ? reason : $"{reason} ({Plural(errors.Count, "compile errors")})";
        }

        return health.AnalyzerErrors.Count > 0 ? Plural(health.AnalyzerErrors.Count, "analyzer errors") : "";
    }

    private static string FirstLine(string text) => text.ReplaceLineEndings("\n").Split('\n', 2)[0];

    private void Count(int done, int total, string unit)
    {
        lock (_lock)
        {
            if (_phase is null)
            {
                return;
            }

            _count = (total, unit);
            Draw($"{_phase,-NameWidth}{done}/{total} {unit}", force: done == 0 || done == total);
        }
    }

    private void Draw(string text, bool force)
    {
        if (!interactive || verbosity == Verbosity.Quiet)
        {
            return;
        }

        if (!force && Stopwatch.GetElapsedTime(_lastDraw) < RedrawInterval)
        {
            return;
        }

        _lastDraw = Stopwatch.GetTimestamp();
        error.Write($"\r{text.PadRight(_liveLength)}");
        _liveLength = text.Length;
    }

    /// <summary>Writes a line of its own, over the live counter if one is drawn.</summary>
    private void WriteLine(string line)
    {
        if (_liveLength > 0)
        {
            error.Write($"\r{line.PadRight(_liveLength)}");
            _liveLength = 0;
            error.WriteLine();
        }
        else
        {
            error.WriteLine(line);
        }
    }

    private static string Name(AnalysisPhase phase) => phase.ToString().ToLowerInvariant();

    private static string Unit(AnalysisPhase phase) => phase == AnalysisPhase.Walk ? "documents" : "projects";

    private static string Plural(int count, string plural) =>
        count == 1 ? $"1 {plural[..^1]}" : string.Create(CultureInfo.InvariantCulture, $"{count} {plural}");

    private static string Duration(TimeSpan duration) =>
        duration < TimeSpan.FromSeconds(1) ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMilliseconds}ms")
        : duration < TimeSpan.FromMinutes(1) ? string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.0}s")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s");
}
