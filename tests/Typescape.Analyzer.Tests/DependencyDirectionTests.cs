using System.Reflection;

namespace Typescape.Analyzer.Tests;

public class DependencyDirectionTests
{
    [Fact]
    public void Analyzer_assembly_does_not_reference_the_tool()
    {
        var analyzer = Assembly.Load("Typescape.Analyzer");

        var names = analyzer.GetReferencedAssemblies().Select(reference => reference.Name);

        Assert.DoesNotContain("Typescape", names);
    }
}
