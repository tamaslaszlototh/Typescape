// Written by the palette tool from palette.json; edits are overwritten.

namespace Shapes;

public static partial class Palette
{
    public static readonly Colour[] Primaries = [Colour.Red, Colour.Green, Colour.Blue];

    public static Colour Parse(string name) => name switch
    {
        "Red" => Colour.Red,
        "Green" => Colour.Green,
        "Blue" => Colour.Blue,
        _ => Colour.Black,
    };
}
