namespace Typescape.Analyzer.Tests;

public class GlobalPropertiesTests
{
    [Fact]
    public void The_request_s_properties_are_kept_and_the_Typescape_targets_are_hooked_in()
    {
        var request = new AnalysisRequest
        {
            SolutionPath = "/repo/Fake.slnx",
            Properties = new Dictionary<string, string> { ["Configuration"] = "Release" },
        };

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Configuration"] = "Release",
                ["CustomAfterMicrosoftCommonTargets"] = GlobalProperties.TargetsPath,
            },
            GlobalProperties.For(request));
    }

    [Fact]
    public void The_hook_cannot_be_replaced_in_any_casing()
    {
        var request = new AnalysisRequest
        {
            SolutionPath = "/repo/Fake.slnx",
            Properties = new Dictionary<string, string> { ["customaftermicrosoftcommontargets"] = "/elsewhere.targets" },
        };

        var properties = GlobalProperties.For(request);

        Assert.Equal(GlobalProperties.TargetsPath, properties["CustomAfterMicrosoftCommonTargets"]);
        Assert.DoesNotContain("/elsewhere.targets", properties.Values);
    }

    [Fact]
    public void The_configuration_defaults_to_Debug()
    {
        var request = new AnalysisRequest
        {
            SolutionPath = "/repo/Fake.slnx",
            Properties = new Dictionary<string, string> { ["Flavor"] = "Blue" },
        };

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Flavor"] = "Blue",
                ["Configuration"] = "Debug",
                ["CustomAfterMicrosoftCommonTargets"] = GlobalProperties.TargetsPath,
            },
            GlobalProperties.For(request));
    }

    [Fact]
    public void A_configuration_in_any_casing_replaces_the_default()
    {
        var request = new AnalysisRequest
        {
            SolutionPath = "/repo/Fake.slnx",
            Properties = new Dictionary<string, string> { ["configuration"] = "Release" },
        };

        Assert.Equal("Release", GlobalProperties.For(request)["Configuration"]);
        Assert.Equal(2, GlobalProperties.For(request).Count);
    }

    [Fact]
    public void The_targets_ship_next_to_the_Analyzer()
    {
        Assert.True(File.Exists(GlobalProperties.TargetsPath), GlobalProperties.TargetsPath);
    }
}
