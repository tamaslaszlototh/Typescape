using System.Collections.Immutable;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Provenance;

internal sealed class ProvenanceRecorder
{
    private readonly ProvenanceRecord _start;
    private readonly IProvenanceSources _sources;
    private readonly Dictionary<AnalysisPhase, double> _seconds = [];

    private ProvenanceRecorder(ProvenanceRecord start, IProvenanceSources sources)
    {
        _start = start;
        _sources = sources;
    }

    /// <summary>Call it before the first phase, so the git state is the one the analysis saw.</summary>
    /// <exception cref="ArgumentException">
    /// Not a <c>.sln</c> or <c>.slnx</c> file, or two property names differ only in case (MSBuild ignores case).
    /// </exception>
    public static async Task<ProvenanceRecorder> StartAsync(
        AnalysisRequest request,
        IProvenanceSources sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sources);

        var now = sources.UtcNow.ToUniversalTime();
        var timestamp = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var solution = Solution(request.SolutionPath);
        var properties = Properties(request.Properties);

        var directory = Path.GetDirectoryName(Path.GetFullPath(request.SolutionPath))!;
        var sdkVersion = sources.GetSdkVersionAsync(directory, cancellationToken);
        var git = sources.GetGitAsync(directory, cancellationToken);
        await Task.WhenAll(sdkVersion, git);

        var start = new ProvenanceRecord(
            sources.Tool,
            timestamp,
            solution,
            await sdkVersion,
            sources.Os,
            await git,
            request.Framework,
            properties.FirstOrDefault(property => IsConfiguration(property.Key)).Value ?? GlobalProperties.DefaultConfiguration,
            properties,
            new PhaseSecondsRecord(null, 0, 0, 0, 0, 0),
            PeakMemoryBytes: 0);
        return new ProvenanceRecorder(start, sources);
    }

    public void Record(AnalysisPhase phase, TimeSpan duration)
    {
        lock (_seconds)
        {
            if (!_seconds.TryAdd(phase, Math.Round(duration.TotalSeconds, 3)))
            {
                throw new InvalidOperationException($"The {phase} phase has already been recorded.");
            }
        }
    }

    /// <summary>A phase not recorded yet counts 0 seconds, a skipped Restore <c>null</c>.</summary>
    public ProvenanceRecord Build()
    {
        lock (_seconds)
        {
            return _start with
            {
                PhaseSeconds = new PhaseSecondsRecord(
                    _seconds.TryGetValue(AnalysisPhase.Restore, out var restore) ? restore : null,
                    Seconds(AnalysisPhase.Load),
                    Seconds(AnalysisPhase.Compile),
                    Seconds(AnalysisPhase.Diagnostics),
                    Seconds(AnalysisPhase.Walk),
                    Seconds(AnalysisPhase.Assemble)),
                PeakMemoryBytes = _sources.GetPeakMemoryBytes(),
            };
        }
    }

    private double Seconds(AnalysisPhase phase) => _seconds.GetValueOrDefault(phase);

    private static SolutionProvenanceRecord Solution(string solutionPath)
    {
        var fileName = Path.GetFileName(solutionPath);
        var format = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".sln" => SolutionFormat.Sln,
            ".slnx" => SolutionFormat.Slnx,
            _ => throw new ArgumentException(
                $"The solution '{fileName}' is neither a .sln nor a .slnx file.",
                nameof(AnalysisRequest.SolutionPath)),
        };
        return new SolutionProvenanceRecord(fileName, format);
    }

    private static ImmutableSortedDictionary<string, string> Properties(IReadOnlyDictionary<string, string> properties)
    {
        var clash = properties.Keys
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(names => names.Count() > 1);
        if (clash is not null)
        {
            throw new ArgumentException(
                $"The MSBuild properties {string.Join(", ", clash.Order(StringComparer.Ordinal))} differ only in case.",
                nameof(AnalysisRequest.Properties));
        }

        return properties.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    private static bool IsConfiguration(string name) =>
        string.Equals(name, GlobalProperties.Configuration, StringComparison.OrdinalIgnoreCase);
}
