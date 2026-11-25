namespace Typescape.Analyzer.Tests.Fixtures;

/// <summary>Each fixture solution's full Snapshot against its one golden file, shared by macOS and Windows.</summary>
[Trait("Category", "Golden")]
public sealed class FixtureGoldenTests
{
    [Theory]
    [InlineData("Modern")]
    public async Task A_fixture_s_Snapshot_matches_its_golden(string fixture) =>
        FixtureSolution.Match(await FixtureSolution.AnalyzeAsync(fixture), fixture);
}
