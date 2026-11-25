using Measures;

namespace Shapes;

public interface IMeasurable
{
    Length Perimeter();
}

public interface IShape : IMeasurable
{
    double Area();
}

public abstract class Shape : IShape
{
    public Colour Fill { get; init; } = Palette.Default;

    public abstract double Area();

    public abstract Length Perimeter();
}

public sealed class Circle(Length radius) : Shape
{
    public Length Radius { get; } = radius;

    public override double Area() => Math.PI * Radius.Metres * Radius.Metres;

    public override Length Perimeter() => Radius * (2 * Math.PI);
}

public sealed class Square(Length side) : Shape
{
    public override double Area() => side.Metres * side.Metres;

    public override Length Perimeter() => side * 4;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ShapeInfoAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}
