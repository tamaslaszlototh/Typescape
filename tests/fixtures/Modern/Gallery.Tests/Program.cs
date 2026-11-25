namespace Gallery.Tests;

internal static class Program
{
    public static int Main(string[] args) =>
        args.Any(arg => arg is "--server" or "--internal-msbuild-node")
            ? Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions).GetAwaiter().GetResult()
            : Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();
}
