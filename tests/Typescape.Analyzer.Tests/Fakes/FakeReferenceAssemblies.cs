using NuGet.Packaging;
using Typescape.Analyzer.Packages;

namespace Typescape.Analyzer.Tests.Fakes;

/// <summary>
/// Stand-ins for the <c>Microsoft.NETFramework.ReferenceAssemblies</c> packages, small enough to build in a test: a
/// valid <c>mscorlib</c> with a handful of types and the redist list MSBuild reads, in the real package's layout.
/// </summary>
internal static class FakeReferenceAssemblies
{
    private static readonly Lazy<byte[]> Mscorlib = new(CompileMscorlib);

    /// <summary>
    /// A <c>nuget.config</c> in <paramref name="directory"/> whose sources are <paramref name="sources"/> (<c>feed</c>
    /// when none), named <c>test</c>, <c>test2</c>, …, and whose global packages folder is <c>global</c>, so a test never
    /// touches the network or the user's caches.
    /// </summary>
    public static void NuGetConfig(string directory, params string[] sources)
    {
        Directory.CreateDirectory(Path.Combine(directory, "feed"));
        var added = (sources.Length == 0 ? ["feed"] : sources)
            .Select((source, i) => $"""<add key="test{(i == 0 ? "" : $"{i + 1}")}" value="{source}" />""");
        File.WriteAllText(Path.Combine(directory, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                {string.Join(Environment.NewLine, added)}
              </packageSources>
              <config>
                <add key="globalPackagesFolder" value="global" />
              </config>
            </configuration>
            """);
    }

    /// <summary>Puts the package for <paramref name="version"/> (e.g. <c>v4.8</c>) into the <c>feed</c> folder.</summary>
    public static void Publish(string directory, string version) =>
        File.WriteAllBytes(
            Path.Combine(directory, "feed", $"{ReferenceAssemblies.PackageId(version)}.{ReferenceAssemblies.PackageVersion}.nupkg"),
            Package(version));

    /// <summary>Puts the package for <paramref name="version"/> into the <c>global</c> packages folder, as restore would.</summary>
    public static void CacheGlobally(string directory, string version)
    {
        var id = ReferenceAssemblies.PackageId(version).ToLowerInvariant();
        var folder = Directory.CreateDirectory(Path.Combine(directory, "global", id, $"{ReferenceAssemblies.PackageVersion}")).FullName;
        File.WriteAllBytes(Path.Combine(folder, $"{id}.{ReferenceAssemblies.PackageVersion}.nupkg"), Package(version));
    }

    private static byte[] Package(string version)
    {
        var id = ReferenceAssemblies.PackageId(version);
        var builder = new PackageBuilder { Id = id, Version = ReferenceAssemblies.PackageVersion, Description = id };
        builder.Authors.Add("Typescape");
        builder.Files.Add(File($"build/.NETFramework/{version}/mscorlib.dll", Mscorlib.Value));
        builder.Files.Add(File($"build/.NETFramework/{version}/RedistList/FrameworkList.xml", System.Text.Encoding.UTF8.GetBytes($"""
            <?xml version="1.0" encoding="utf-8"?>
            <FileList Redist="Microsoft-Windows-CLRCoreComp" Name=".NET Framework {version[1..]}">
              <File AssemblyName="mscorlib" Version="4.0.0.0" PublicKeyToken="b77a5c561934e089" Culture="neutral" ProcessorArchitecture="MSIL" InGac="true" />
            </FileList>
            """)));
        builder.Files.Add(File($"build/{id}.targets", "<Project />"u8.ToArray()));

        using var stream = new MemoryStream();
        builder.Save(stream);
        return stream.ToArray();

        static PhysicalPackageFile File(string path, byte[] content) => new(new MemoryStream(content)) { TargetPath = path };
    }

    private static byte[] CompileMscorlib() => FakeAssemblies.Compile("mscorlib", """
            [assembly: System.Reflection.AssemblyVersion("4.0.0.0")]
            namespace System
            {
                public class Object { }
                public abstract class ValueType { }
                public abstract class Enum : ValueType { }
                public struct Void { }
                public struct Boolean { }
                public struct Int32 { }
                public sealed class String { }
                public abstract class Attribute { }
                public enum AttributeTargets { All = 32767 }
                public sealed class AttributeUsageAttribute : Attribute
                {
                    public AttributeUsageAttribute(AttributeTargets validOn) { }
                    public bool AllowMultiple { get; set; }
                    public bool Inherited { get; set; }
                }
            }
            namespace System.Reflection
            {
                public sealed class AssemblyVersionAttribute : Attribute { public AssemblyVersionAttribute(string version) { } }
            }
            namespace System.Runtime.Versioning
            {
                public sealed class TargetFrameworkAttribute : Attribute
                {
                    public TargetFrameworkAttribute(string frameworkName) { }
                    public string FrameworkDisplayName { get; set; }
                }
            }
            """);
}
