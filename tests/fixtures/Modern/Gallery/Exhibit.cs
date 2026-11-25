using Shapes;

namespace Gallery;

public static class Exhibit
{
    public static IReadOnlyList<IShape> Build(int count)
    {
        var shapes = new List<IShape>();
        for (var i = 0; i < count; i++)
        {
            shapes.Add(i % 2 == 0 ? new Circle(i) : new Square(i));
        }

        return shapes;
    }

    public static double TotalArea(IEnumerable<IShape> shapes) => shapes.Sum(shape => shape.Area());
}
