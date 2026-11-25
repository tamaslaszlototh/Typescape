using Microsoft.Extensions.Logging;

namespace Gallery;

public sealed partial class Curator(ILogger<Curator> logger)
{
    public double Open(int count)
    {
        var shapes = Exhibit.Build(count);
        var area = Exhibit.TotalArea(shapes);
        LogOpened(shapes.Count, area);
        return area;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Opened with {Count} shapes covering {Area} square metres")]
    private partial void LogOpened(int count, double area);
}
