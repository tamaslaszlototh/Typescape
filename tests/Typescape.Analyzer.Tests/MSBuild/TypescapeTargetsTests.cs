using System.Reflection;
using Typescape.Analyzer.Infrastructure;
using Typescape.Analyzer.MSBuild;

namespace Typescape.Analyzer.Tests.MSBuild;

/// <summary>
/// Evaluates throwaway projects with the real <c>dotnet msbuild</c> (.NET SDK MSBuild) under <see cref="GlobalProperties"/>,
/// and asks whether the .NET SDK's <c>Microsoft.WinFX.targets</c> came in: only it sets <c>_PresentationBuildTasksAssembly</c>.
/// </summary>
public sealed class TypescapeTargetsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-targets-").FullName;

    // The repository's global.json, so dotnet msbuild picks the SDK the build and the BuildHost use.
    public TypescapeTargetsTests() => File.Copy(Path.Combine(RepositoryRoot(), "global.json"), Path.Combine(_directory, "global.json"));

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task A_classic_project_gets_the_WPF_markup_compiler()
    {
        Assert.EndsWith("PresentationBuildTasks.dll", await WinFXTasksAsync(Classic("v4.8")));
    }

    [Fact]
    public async Task A_classic_NET_Framework_2_project_is_left_alone_as_Visual_Studio_MSBuild_leaves_it()
    {
        Assert.Equal("", await WinFXTasksAsync(Classic("v2.0")));
    }

    [Fact]
    public async Task An_SDK_style_project_is_left_alone()
    {
        var project = Write("Modern.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);

        Assert.Equal("", await WinFXTasksAsync(project));
    }

    private string Classic(string frameworkVersion) => Write("Classic.csproj", $"""
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <OutputType>Library</OutputType>
            <TargetFrameworkVersion>{frameworkVersion}</TargetFrameworkVersion>
          </PropertyGroup>
          <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
        </Project>
        """);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(
            typeof(TypescapeTargetsTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "Typescape.ProjectDirectory")
                .Value!,
            "..",
            ".."));

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<string> WinFXTasksAsync(string project)
    {
        var properties = GlobalProperties.For(new AnalysisRequest { SolutionPath = project });
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            ["msbuild", project, "-getProperty:_PresentationBuildTasksAssembly", .. properties.Select(p => $"-p:{p.Key}={p.Value}")],
            _directory,
            ProcessRunner.QuietEnvironment,
            TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        return result.Output.Trim();
    }
}
