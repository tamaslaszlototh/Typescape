namespace Typescape.Analyze;

/// <summary><c>--fail-on</c>: the worst Load Health a run still succeeds with.</summary>
internal enum FailOn
{
    None,
    Degraded,
    Failed,
}
