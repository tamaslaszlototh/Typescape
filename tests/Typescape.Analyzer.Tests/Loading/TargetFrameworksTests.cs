using Typescape.Analyzer.Loading;

namespace Typescape.Analyzer.Tests.Loading;

public class TargetFrameworksTests
{
    [Theory]
    [InlineData("net10.0", "net8.0")]
    [InlineData("net5.0", "netcoreapp3.1", "netstandard2.1")]
    [InlineData("net8.0", "netstandard2.0", "net48")]
    [InlineData("net8.0-windows", "net8.0")]
    [InlineData("net8.0-windows10.0.19041", "net8.0-windows")]
    [InlineData("netstandard2.0", "net472")]
    [InlineData("net481", "net48", "net472")]
    public void The_highest_target_framework_is_chosen(string highest, params string[] others)
    {
        Assert.Equal(highest, TargetFrameworks.Choose([.. others, highest], requested: null));
        Assert.Equal(highest, TargetFrameworks.Choose([highest, .. others], requested: null));
    }

    [Fact]
    public void The_requested_framework_wins_when_the_project_targets_it()
    {
        Assert.Equal("net8.0", TargetFrameworks.Choose(["net8.0", "net10.0"], requested: "net8.0"));
    }

    [Fact]
    public void The_requested_framework_matches_any_spelling_of_the_same_framework()
    {
        Assert.Equal("net8.0", TargetFrameworks.Choose(["net8.0", "net10.0"], requested: "NET80"));
    }

    [Fact]
    public void A_project_without_the_requested_framework_falls_back_to_its_highest()
    {
        Assert.Equal("net10.0", TargetFrameworks.Choose(["net8.0", "net10.0"], requested: "net472"));
    }

    [Fact]
    public void No_target_framework_means_none_is_chosen()
    {
        Assert.Null(TargetFrameworks.Choose([], requested: null));
        Assert.Null(TargetFrameworks.Choose([], requested: "net8.0"));
    }

    [Fact]
    public void Unparseable_target_frameworks_still_give_one_deterministic_choice()
    {
        Assert.Equal("net8.0", TargetFrameworks.Choose(["foo", "net8.0", "bar-"], requested: null));
        Assert.Equal("foo", TargetFrameworks.Choose(["foo", "bar-"], requested: null));
        Assert.Equal("bar-", TargetFrameworks.Choose(["foo", "bar-"], requested: "BAR-"));
    }

    [Theory]
    [InlineData("net472", "v4.7.2", true)]
    [InlineData("net48", "v4.8", true)]
    [InlineData("net4.8", "v4.8", true)]
    [InlineData("net40-client", "v4.0", true)]
    [InlineData("net48", "v4.7.2", false)]
    [InlineData("net8.0", "v4.8", false)]
    [InlineData("foo", "v4.8", false)]
    public void A_target_framework_is_matched_to_its_NET_Framework_version(string tfm, string version, bool expected)
    {
        Assert.Equal(expected, TargetFrameworks.IsNetFramework(tfm, version));
    }

    [Theory]
    [InlineData("net472", ".NETFramework", "4.7.2", null)]
    [InlineData("net10.0-windows", ".NETCoreApp", "10.0", "windows")]
    [InlineData("netstandard2.0", ".NETStandard", "2.0", null)]
    [InlineData("foo", null, null, null)]
    public void A_target_framework_gives_MSBuild_s_identifier_version_and_platform(string tfm, string? identifier, string? version, string? platform)
    {
        Assert.Equal(identifier, TargetFrameworks.FrameworkIdentifier(tfm));
        Assert.Equal(version, TargetFrameworks.FrameworkVersion(tfm));
        Assert.Equal(platform, TargetFrameworks.PlatformIdentifier(tfm));
    }

    [Theory]
    [InlineData("net10.0", "net10.0", "net8.0", "net10.0")]
    [InlineData("net9.0", "net8.0", "net10.0", "net8.0")]
    [InlineData("net48", "netstandard2.0", "net10.0", "netstandard2.0")]
    [InlineData("net10.0", "net48", null, null)]
    [InlineData("foo", "net10.0", null, null)]
    public void A_reference_gets_the_target_framework_NuGet_picks_as_nearest(string target, string first, string? second, string? expected)
    {
        Assert.Equal(expected, TargetFrameworks.Nearest(second is null ? [first] : [first, second], target));
    }
}
