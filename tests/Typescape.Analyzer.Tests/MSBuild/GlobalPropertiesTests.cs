using Typescape.Analyzer.MSBuild;
using Typescape.Analyzer.Packages;

namespace Typescape.Analyzer.Tests.MSBuild;

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
                ["TargetFrameworkRootPath"] = ReferenceAssemblies.Default.Root,
                ["ResolveAssemblyReferencesSilent"] = "false",
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
    public void The_reference_assembly_root_cannot_be_replaced_in_any_casing()
    {
        var request = new AnalysisRequest
        {
            SolutionPath = "/repo/Fake.slnx",
            Properties = new Dictionary<string, string> { ["targetframeworkrootpath"] = "/developer-pack/" },
        };
        var cache = new ReferenceAssemblies("/caches/typescape");

        var properties = GlobalProperties.For(request, cache);

        Assert.Equal(cache.Root, properties["TargetFrameworkRootPath"]);
        Assert.DoesNotContain("/developer-pack/", properties.Values);
    }

    [Fact]
    public void Reference_resolution_cannot_be_silenced_in_any_casing()
    {
        var request = new AnalysisRequest
        {
            SolutionPath = "/repo/Fake.slnx",
            Properties = new Dictionary<string, string> { ["resolveassemblyreferencessilent"] = "true" },
        };

        Assert.Equal("false", GlobalProperties.For(request)["ResolveAssemblyReferencesSilent"]);
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
                ["TargetFrameworkRootPath"] = ReferenceAssemblies.Default.Root,
                ["ResolveAssemblyReferencesSilent"] = "false",
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
        Assert.Equal(4, GlobalProperties.For(request).Count);
    }

    [Fact]
    public void The_solution_properties_are_added_unless_the_request_sets_them()
    {
        var solutionPath = Path.Combine(Path.GetTempPath(), "repo", "Fake.slnx");
        var properties = new Dictionary<string, string> { ["Configuration"] = "Debug", ["solutionname"] = "Mine" };

        var withSolution = GlobalProperties.WithSolution(properties, solutionPath);

        Assert.Equal(Path.Combine(Path.GetTempPath(), "repo") + Path.DirectorySeparatorChar, withSolution["SolutionDir"]);
        Assert.Equal(solutionPath, withSolution["SolutionPath"]);
        Assert.Equal("Mine", withSolution["SolutionName"]);
        Assert.Equal("Fake.slnx", withSolution["SolutionFileName"]);
        Assert.Equal(".slnx", withSolution["SolutionExt"]);
        Assert.Equal("Debug", withSolution["Configuration"]);
    }

    [Fact]
    public void The_targets_ship_next_to_the_Analyzer()
    {
        Assert.True(File.Exists(GlobalProperties.TargetsPath), GlobalProperties.TargetsPath);
        var legacy = Path.Combine(Path.GetDirectoryName(GlobalProperties.TargetsPath)!, "Typescape.Legacy.targets");
        Assert.True(File.Exists(legacy), legacy);
    }
}
