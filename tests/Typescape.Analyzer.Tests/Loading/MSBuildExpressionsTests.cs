using Typescape.Analyzer.Loading;

namespace Typescape.Analyzer.Tests.Loading;

public class MSBuildExpressionsTests
{
    internal static readonly string Project = Path.Combine(Path.GetTempPath(), "repo", "App", "App.csproj");

    [Theory]
    [InlineData("$(Configuration)-$(Platform)", "Debug-AnyCPU")]
    [InlineData("$(MSBuildThisFileName)$(MSBuildThisFileExtension)", "App.csproj")]
    [InlineData("$([MSBuild]::ValueOrDefault('$(Undefined)', 'fallback'))", "fallback")]
    [InlineData("$([MSBuild]::GetTargetFrameworkIdentifier('net472'))", ".NETFramework")]
    [InlineData("$([MSBuild]::GetTargetFrameworkVersion('net472'))", "4.7.2")]
    [InlineData("$([MSBuild]::GetTargetFrameworkVersion('net10.0-windows'))", "10.0")]
    [InlineData("$([MSBuild]::IsTargetFrameworkCompatible('net10.0', 'netstandard2.0'))", "True")]
    [InlineData("$(Configuration.Replace('Deb', 'X'))", "Xug")]
    [InlineData("$(Configuration.Replace('Deb', '').StartsWith('ug'))", "True")]
    [InlineData("$([MSBuild]::ValueOrDefault('a,(b)', 'c'))", "a,(b)")]
    [InlineData("@(Compile)", null)]
    [InlineData("%(Identity)", null)]
    [InlineData("$(MSBuildToolsPath)\\Microsoft.CSharp.targets", null)]
    [InlineData("$(Configuration.Substring(1))", null)]
    [InlineData("$([System.DateTime]::Now)", null)]
    [InlineData("$(Configuration", null)]
    public void Properties_and_the_common_functions_expand(string text, string? expected)
    {
        Assert.Equal(expected, Expressions().Expand(text));
    }

    [Fact]
    public void A_global_property_cannot_be_redefined()
    {
        var expressions = Expressions();

        expressions.Set("Configuration", "Release");

        Assert.Equal("Debug", expressions.Value("Configuration"));
    }

    internal static MSBuildExpressions Expressions()
    {
        var expressions = new MSBuildExpressions(Project);
        expressions.SetGlobal("Configuration", "Debug");
        expressions.Set("Platform", "AnyCPU");
        expressions.Set("Count", "9");
        expressions.Set("Version", "4.8");
        expressions.Set("Flag", "true");
        expressions.Set("Root", "/repo/");
        expressions.Set("MSBuildExtensionsPath", null);
        expressions.Set("MSBuildToolsPath", null);
        return expressions;
    }
}
