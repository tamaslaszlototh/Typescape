using Xunit.Sdk;

namespace Typescape.Testing;

/// <summary>
/// Compares test output with a checked-in golden file, <c>&lt;name&gt;.verified.&lt;ext&gt;</c>, in the test project's
/// <c>Golden/</c> folder. On a mismatch it writes <c>&lt;name&gt;.received.&lt;ext&gt;</c> next to it and fails.
/// Set <c>TYPESCAPE_ACCEPT_GOLDEN=1</c> to overwrite the golden file with the received output instead.
/// Line endings are normalized to LF, so a golden file is identical on macOS and Windows.
/// </summary>
internal static class Golden
{
    public const string AcceptVariable = "TYPESCAPE_ACCEPT_GOLDEN";

    public static void Match(string actual, string name, string extension = "txt") =>
        Match(actual, DefaultDirectory(), name, extension, accept: AcceptRequested());

    internal static void Match(string actual, string directory, string name, string extension, bool accept)
    {
        var verifiedPath = Path.Combine(directory, $"{name}.verified.{extension}");
        var receivedPath = Path.Combine(directory, $"{name}.received.{extension}");
        var received = Normalize(actual);
        var verified = File.Exists(verifiedPath) ? Normalize(File.ReadAllText(verifiedPath)) : null;

        if (received == verified || accept)
        {
            if (received != verified)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(verifiedPath, received);
            }

            File.Delete(receivedPath);
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(receivedPath, received);

        var reason = verified is null
            ? $"No golden file at {verifiedPath}."
            : $"Output differs from {verifiedPath}{FirstDifference(verified, received)}.";
        throw new XunitException(
            $"{reason}\nReceived output written to {receivedPath}.\n" +
            $"Review it, then rename it to .verified or rerun with {AcceptVariable}=1.");
    }

    private static string Normalize(string text) => text.ReplaceLineEndings("\n");

    private static bool AcceptRequested() =>
        Environment.GetEnvironmentVariable(AcceptVariable) is "1" or "true";

    private static string DefaultDirectory() => Path.Combine(TestMetadata.ProjectDirectory, "Golden");

    private static string FirstDifference(string verified, string received)
    {
        var expectedLines = verified.Split('\n');
        var actualLines = received.Split('\n');
        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var expected = i < expectedLines.Length ? expectedLines[i] : "<end of file>";
            var actual = i < actualLines.Length ? actualLines[i] : "<end of file>";
            if (expected != actual)
            {
                return $" at line {i + 1}:\n  verified: {expected}\n  received: {actual}";
            }
        }

        return string.Empty;
    }
}
