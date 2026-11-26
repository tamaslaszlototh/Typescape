namespace Typescape.Analyze;

/// <summary><c>-v|--verbosity</c>: how much progress and Load Health goes to stderr.</summary>
internal enum Verbosity
{
    Quiet = -1,

    // 0, so that it is the value without the flag.
    Normal = 0,
    Detailed = 1,
}
