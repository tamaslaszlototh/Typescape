namespace Measures;

public readonly record struct Length(double Metres)
{
    public static Length FromCentimetres(double centimetres) => new(centimetres / 100);

    public static Length operator *(Length length, double factor) => new(length.Metres * factor);

    public override string ToString() => $"{Metres} m";
}
