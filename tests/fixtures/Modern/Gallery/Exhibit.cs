using Measures;
using Shapes;

namespace Gallery;

[ShapeInfo("exhibit")]
public static class Exhibit
{
    public static IReadOnlyList<IShape> Build(int count)
    {
        var shapes = new List<IShape>();
        for (var i = 0; i < count; i++)
        {
            var size = Length.FromCentimetres(i + 1);
            shapes.Add(i % 2 == 0 ? new Circle(size) : new Square(size) { Fill = Palette.Parse("Red") });
        }

        return shapes;
    }

    public static double TotalArea(IEnumerable<IShape> shapes) => shapes.Sum(shape => shape.Area());

    public static string Describe(IShape shape) => shape switch
    {
        Circle circle => $"{nameof(Circle)} {circle.Radius}",
        Square => Strings.SquareLabel,
        _ => typeof(IShape).Name,
    };
}
