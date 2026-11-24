using Typescape.Analyzer.Loading;

namespace Typescape.Analyzer.Tests.Loading;

public class MSBuildConditionTests
{
    [Theory]
    [InlineData("'$(Configuration)' == 'debug'", true)]
    [InlineData(" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' ", true)]
    [InlineData("'$(Undefined)' == ''", true)]
    [InlineData("'$(Configuration)' != 'Debug' Or '$(Count)' >= '09'", true)]
    [InlineData("!('$(Count)' == '9') AND '$(Count)' < 9.5", false)]
    [InlineData("'$(Version)' > '4.7.2'", true)]
    [InlineData("$(Flag)", true)]
    [InlineData("!$(Flag)", false)]
    [InlineData("'$(Flag)' == 'true' and HasTrailingSlash('$(Root)')", true)]
    [InlineData("$(Configuration.StartsWith('Deb'))", true)]
    [InlineData("Exists('$(MSBuildThisFileDirectory)App.csproj')", false)]
    [InlineData("Exists('')", false)]
    [InlineData("'$(MSBuildExtensionsPath)' != ''", null)]
    [InlineData("'@(Compile)' != ''", null)]
    [InlineData("'$([System.DateTime]::Now)' != ''", null)]
    [InlineData("'$(Unknown)' == 'x' and false", false)]
    [InlineData("'$(MSBuildExtensionsPath)' == 'x' or true", true)]
    [InlineData("'$(Configuration)' = 'Debug'", null)]
    [InlineData("'unterminated", null)]
    [InlineData("'a' == 'a' 'b'", null)]
    public void Conditions_evaluate_in_three_valued_logic(string condition, bool? expected)
    {
        var expressions = MSBuildExpressionsTests.Expressions();

        Assert.Equal(expected, MSBuildCondition.Evaluate(condition, expressions, Path.GetDirectoryName(MSBuildExpressionsTests.Project)!));
    }
}
