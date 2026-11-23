using System.Collections.Concurrent;
using Typescape.Analyzer.Provenance;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Tests.Fakes;

internal sealed class FakeProvenanceSources : IProvenanceSources
{
    private long _peakReads;

    public DateTimeOffset UtcNow { get; init; } = new(2026, 11, 23, 12, 30, 15, TimeSpan.Zero);

    public ToolProvenanceRecord Tool { get; init; } = new("9.9.9", "0123456789abcdef0123456789abcdef01234567");

    public string Os { get; init; } = "Fake OS 1.0 Arm64";

    public string SdkVersion { get; init; } = "10.0.404";

    public GitProvenanceRecord? Git { get; init; } = new("89abcdef0123456789abcdef0123456789abcdef", "main", Dirty: false);

    /// <summary>Receives <c>"sdk"</c> and <c>"git"</c>.</summary>
    public ConcurrentQueue<string>? Log { get; init; }

    public ConcurrentBag<string> Directories { get; } = [];

    /// <summary>Read <c>n</c> returns <c>1000 × n</c>.</summary>
    public long PeakReads => Interlocked.Read(ref _peakReads);

    public Task<string> GetSdkVersionAsync(string solutionDirectory, CancellationToken cancellationToken)
    {
        Directories.Add(solutionDirectory);
        Log?.Enqueue("sdk");
        return Task.FromResult(SdkVersion);
    }

    public Task<GitProvenanceRecord?> GetGitAsync(string solutionDirectory, CancellationToken cancellationToken)
    {
        Directories.Add(solutionDirectory);
        Log?.Enqueue("git");
        return Task.FromResult(Git);
    }

    public long GetPeakMemoryBytes() => 1000 * Interlocked.Increment(ref _peakReads);
}
