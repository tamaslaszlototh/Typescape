namespace Shapes;

public interface IShape
{
    double Area();
}

public sealed class Circle(double radius) : IShape
{
    public double Radius { get; } = radius;

    public double Area() => Math.PI * Radius * Radius;
}

public sealed class Square(double side) : IShape
{
    public double Area() => side * side;
}
