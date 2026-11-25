namespace Measures;

public sealed class Ruler
{
#if NET10_0_OR_GREATER
    private readonly Lock gate = new();
#else
    private readonly object gate = new();
#endif
    private readonly List<Length> readings = [];

    public void Record(Length reading)
    {
        lock (gate)
        {
            readings.Add(reading);
        }
    }

    public Length Longest()
    {
        lock (gate)
        {
            return readings.Count == 0 ? default : readings.MaxBy(reading => reading.Metres);
        }
    }
}
