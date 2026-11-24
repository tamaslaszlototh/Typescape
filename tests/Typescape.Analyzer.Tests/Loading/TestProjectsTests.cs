using Typescape.Analyzer.Loading;

namespace Typescape.Analyzer.Tests.Loading;

public class TestProjectsTests
{
    [Theory]
    [InlineData("/nuget/xunit.core/2.9.3/lib/netstandard1.1/xunit.core.dll", "framework:xunit")]
    [InlineData("/nuget/xunit.v3.core/3.0.0/lib/net8.0/xunit.v3.core.dll", "framework:xunit")]
    [InlineData("/nuget/nunit/4.3.2/lib/net8.0/nunit.framework.dll", "framework:nunit")]
    [InlineData("/nuget/mstest.testframework/3.9.0/lib/net8.0/Microsoft.VisualStudio.TestPlatform.TestFramework.dll", "framework:mstest")]
    [InlineData("/vs/PublicAssemblies/Microsoft.VisualStudio.QualityTools.UnitTestFramework.dll", "framework:mstest")]
    [InlineData("/nuget/tunit.core/0.25.0/lib/net8.0/TUnit.Core.dll", "framework:tunit")]
    [InlineData("/packages/Machine.Specifications.1.1.0/lib/net45/Machine.Specifications.dll", "framework:mspec")]
    [InlineData("/nuget/microsoft.testplatform.testhost/18.0.0/lib/net8.0/testhost.dll", "framework:microsoft.net.test.sdk")]
    [InlineData("/nuget/microsoft.codecoverage/18.0.0/lib/net462/Microsoft.VisualStudio.CodeCoverage.Shim.dll", "framework:microsoft.net.test.sdk")]
    public void A_reference_to_a_known_test_framework_makes_a_test_project(string reference, string reason)
    {
        Assert.Equal((true, reason), Classify("Domain", reference));
    }

    [Fact]
    public void The_framework_names_the_reason_over_the_test_SDK_it_comes_with()
    {
        Assert.Equal(
            (true, "framework:nunit"),
            Classify("Domain", "/nuget/testhost.dll", "/nuget/Microsoft.VisualStudio.CodeCoverage.Shim.dll", "/nuget/nunit.framework.dll"));
    }

    [Theory]
    [InlineData("Domain.Tests")]
    [InlineData("Domain.Test")]
    [InlineData("Domain.Specs")]
    [InlineData("Domain.tests")]
    public void A_test_project_name_makes_a_test_project(string name)
    {
        Assert.Equal((true, "name"), Classify(name));
    }

    [Theory]
    [InlineData("Domain")]
    [InlineData("DomainTests")]
    [InlineData("Domain.Testing")]
    [InlineData("Domain.Tests.Helpers")]
    public void Any_other_project_is_not_a_test_project_and_has_no_reason(string name)
    {
        Assert.Equal((false, null), Classify(name, "/nuget/xunit.assert.dll", "/nuget/Domain.Tests.dll"));
    }

    [Fact]
    public void A_framework_reference_wins_over_the_name()
    {
        Assert.Equal((true, "framework:xunit"), Classify("Domain.Tests", "/nuget/xunit.core.dll"));
    }

    [Fact]
    public void An_override_wins_over_both()
    {
        Assert.Equal((true, "override"), TestProjects.Classify("Domain.Fixtures", [], ["*.fixtures"], []));
        Assert.Equal((false, "override"), TestProjects.Classify("Domain.Tests", ["/nuget/xunit.core.dll"], [], ["Domain.Te?ts"]));
    }

    [Fact]
    public void An_override_matches_the_whole_name()
    {
        Assert.Equal((false, null), TestProjects.Classify("Domain.Fixtures", [], ["Fixtures"], []));
        Assert.Equal((true, "name"), TestProjects.Classify("Domain.Tests", [], [], ["Domain"]));
    }

    [Fact]
    public void Not_test_wins_when_both_overrides_match()
    {
        Assert.Equal((false, "override"), TestProjects.Classify("Domain.Tests.Helpers", [], ["Domain.*"], ["*.Helpers"]));
    }

    private static (bool, string?) Classify(string name, params string[] references) =>
        TestProjects.Classify(name, references, [], []);
}
