using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Typescape.Analyzer.Tests.Fakes;

/// <summary>Real assemblies, compiled in memory, for MSBuild to resolve as references.</summary>
internal static class FakeAssemblies
{
    /// <summary>A library named <paramref name="name"/> with no types, against this runtime's core library.</summary>
    public static byte[] Empty(string name) => Compile(name, "", File.ReadAllBytes(typeof(object).Assembly.Location));

    /// <param name="references">The images of the assemblies <paramref name="source"/> compiles against.</param>
    public static byte[] Compile(string name, string source, params byte[][] references)
    {
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            [.. references.Select(reference => MetadataReference.CreateFromImage(reference))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, options: new EmitOptions(runtimeMetadataVersion: "v4.0.30319"));
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return stream.ToArray();
    }
}
