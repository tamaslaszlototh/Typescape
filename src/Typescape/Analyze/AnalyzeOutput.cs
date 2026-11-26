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

    // What the running phase shows on a terminal, and how much of a live line is on screen now.
    private string _liveText = "";
    private int _drawnLength;
    private long _lastDraw;

    public void Report(AnalysisProgress value)
    {
        switch (value)
        {
            case AnalysisProgress.PhaseStarted started:
                Start(Name(started.Phase));
                break;
            case AnalysisProgress.PhaseProgressed progressed:
                DrawCount(progressed.Done, progressed.Total, Unit(progressed.Phase));
                break;
            case AnalysisProgress.PhaseFinished finished:
                Finish(finished.Duration);
                break;
            case AnalysisProgress.Warning warning:
                lock (_lock)
                {
                    WriteLine(warning.Message);
                    if (_phase is not null)
                    {
                        Draw(_liveText, force: true);
                    }
                }

                break;
            case AnalysisProgress.LoadHealthReported:
                // Printed by WriteLoadHealth from the result instead, which holds each project's evidence.
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
            _liveText = phase;
            Draw(_liveText, force: true);
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
                line += $"  {Counted(total, unit)}";
            }

            _phase = null;
            WriteLine(line);
        }
    }

    /// <summary>
    /// The summary line, then one line per non-ok project with its main reason; <c>detailed</c> lists every project with
    /// its workspace diagnostics, then the solution's. <c>quiet</c> prints nothing when every project is ok.
    /// </summary>
    public void WriteLoadHealth(AnalysisResult result)
    {
        var (snapshot, summary) = (result.Snapshot, result.LoadHealth);
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
                $"{Counted(summary.Projects, "project")}: {summary.Ok} ok · {summary.Degraded} degraded · {summary.Failed} failed"));

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

    /// <summary>Why no Snapshot was written: the message, with the stack trace under <c>detailed</c>.</summary>
    public void WriteFailure(Exception exception)
    {
        Interrupt();
        lock (_lock)
        {
            WriteLine(verbosity == Verbosity.Detailed ? exception.ToString() : exception.Message);
        }
    }

    public void WriteNoProjectLoaded()
    {
        lock (_lock)
        {
            WriteLine("No project loaded: the Snapshot holds nothing to analyze.");
        }
    }

    /// <param name="threshold">The <c>--fail-on</c> value.</param>
    /// <param name="projects">How many projects are that or worse.</param>
    public void WriteFailOnCrossed(FailOn threshold, int projects)
    {
        var worse = threshold == FailOn.Degraded ? "degraded or failed" : "failed";
        lock (_lock)
        {
            WriteLine($"--fail-on {threshold.ToString().ToLowerInvariant()}: {Counted(projects, "project")} {(projects == 1 ? "is" : "are")} {worse}.");
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

        // Before the unresolved System.Object, because its compile error CS0518 also says how much of the code it hit.
        if (health.CompileErrors is { Top: [var top, ..] } errors)
        {
            var reason = string.Create(CultureInfo.InvariantCulture, $"{top.Code} ×{top.Count}");
            return top.Count == errors.Count ? reason : $"{reason} ({Counted(errors.Count, "compile error")})";
        }

        if (!health.ObjectResolves)
        {
            return "System.Object does not resolve";
        }

        return health.AnalyzerErrors.Count > 0 ? Counted(health.AnalyzerErrors.Count, "analyzer error") : "";
    }

    private static string FirstLine(string text) => text.ReplaceLineEndings("\n").Split('\n', 2)[0];

    /// <summary>Ends the live counter's line, so that an error printed next starts on a line of its own.</summary>
    public void Interrupt()
    {
        lock (_lock)
        {
            if (_drawnLength > 0)
            {
                error.WriteLine();
                _drawnLength = 0;
            }
        }
    }

    private void DrawCount(int done, int total, string unit)
    {
        lock (_lock)
        {
            if (_phase is null)
            {
                return;
            }

            _count = (total, unit);
            _liveText = $"{_phase,-NameWidth}{done}/{total} {Plural(total, unit)}";
            Draw(_liveText, force: done == 0 || done == total);
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
        error.Write($"\r{text.PadRight(_drawnLength)}");
        _drawnLength = text.Length;
    }

    /// <summary>Writes a line of its own over the live counter, if one is drawn; text of several lines clears it first.</summary>
    private void WriteLine(string line)
    {
        if (_drawnLength > 0)
        {
            error.Write(line.Contains('\n') ? $"\r{new string(' ', _drawnLength)}\r{line}" : $"\r{line.PadRight(_drawnLength)}");
            _drawnLength = 0;
        }
        else
        {
            error.Write(line);
        }

        error.WriteLine();
    }

    private static string Name(AnalysisPhase phase) => phase.ToString().ToLowerInvariant();

    private static string Unit(AnalysisPhase phase) => phase == AnalysisPhase.Walk ? "document" : "project";

    private static string Counted(int count, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {Plural(count, unit)}");

    private static string Plural(int count, string unit) => count == 1 ? unit : unit + "s";

    private static string Duration(TimeSpan duration) =>
        duration < TimeSpan.FromSeconds(1) ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMilliseconds}ms")
        : duration < TimeSpan.FromMinutes(1) ? string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.0}s")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s");
}
