namespace Typescape;

internal static class ExitCodes
{
    public const int Success = 0;

    /// <summary>No Snapshot: the solution cannot be opened, no .NET SDK, the BuildHost fails, an I/O error.</summary>
    public const int RuntimeFailure = 1;

    /// <summary>A bad flag, or a missing or ambiguous input.</summary>
    public const int Usage = 2;

    /// <summary>The Snapshot is written, but no project loaded or <c>--fail-on</c> was crossed.</summary>
    public const int LoadHealthFailure = 3;

    /// <summary>Ctrl+C during analysis, as a shell reports a process that SIGINT ended.</summary>
    public const int Cancelled = 130;
}
