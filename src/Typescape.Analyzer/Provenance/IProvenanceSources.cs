using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Provenance;

/// <summary>Nothing here may return an absolute path or the machine name.</summary>
internal interface IProvenanceSources
{
    DateTimeOffset UtcNow { get; }

    ToolProvenanceRecord Tool { get; }

    string Os { get; }

    Task<string> GetSdkVersionAsync(string solutionDirectory, CancellationToken cancellationToken);

    /// <returns><c>null</c> outside a git working tree.</returns>
    Task<GitProvenanceRecord?> GetGitAsync(string solutionDirectory, CancellationToken cancellationToken);

    long GetPeakMemoryBytes();
}
