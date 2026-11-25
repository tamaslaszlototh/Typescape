namespace Typescape.Analyzer.Tests.Fixtures;

/// <summary>Each fixture solution's full Snapshot against its one golden file, shared by macOS and Windows.</summary>
/// <remarks>
/// One method per fixture, so a CI job can run a single fixture, e.g.
/// <c>--filter-method "*.FixtureGoldenTests.Legacy"</c>.
/// </remarks>
[Trait("Category", "Golden")]
public sealed class FixtureGoldenTests
{
    [Fact]
    public Task Modern() => MatchAsync("Modern");

    private static async Task MatchAsync(string fixture) =>
        FixtureSolution.Match(await FixtureSolution.AnalyzeAsync(fixture), fixture);
}
