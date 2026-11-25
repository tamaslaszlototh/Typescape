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

    // Only Measures targets net8.0; the other projects stay on their one framework.
    [Fact]
    public async Task Modern_for_framework_net8_0() =>
        FixtureSolution.Match(await FixtureSolution.AnalyzeAsync("Modern", request => request with { Framework = "net8.0" }), "Modern.net8.0");

    private static async Task MatchAsync(string fixture) =>
        FixtureSolution.Match(await FixtureSolution.AnalyzeAsync(fixture), fixture);
}
