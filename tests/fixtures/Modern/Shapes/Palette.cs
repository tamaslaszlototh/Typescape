namespace Shapes;

public enum Colour
{
    Black,
    Red,
    Green,
    Blue,
}

public static partial class Palette
{
    public static Colour Default => Colour.Black;

    public static bool IsPrimary(Colour colour) => Array.IndexOf(Primaries, colour) >= 0;
}
