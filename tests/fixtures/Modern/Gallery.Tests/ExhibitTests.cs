using Shapes;

namespace Gallery.Tests;

public sealed class ExhibitTests
{
    [Fact]
    public void Build_alternates_circles_and_squares()
    {
        var shapes = Exhibit.Build(3);

        Assert.IsType<Circle>(shapes[0]);
        Assert.IsType<Square>(shapes[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Total_area_is_positive(int count) => Assert.True(Exhibit.TotalArea(Exhibit.Build(count)) > 0);
}
