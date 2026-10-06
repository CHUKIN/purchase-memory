using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.IntegrationTests;

public sealed class BoundingBoxTests
{
    [Theory]
    [InlineData(0.05, 0.8, 0.62, 0.045)]
    [InlineData(0, 0, 1, 0.5)]
    [InlineData(0.5, 0.5, 0.5, 0.5)]
    public void AcceptsBoxesInsideTheImage(double x, double y, double width, double height)
    {
        var box = BoundingBox.TryCreate(x, y, width, height);

        Assert.NotNull(box);
        Assert.Equal(x, box.X, 6);
        Assert.Equal(y, box.Y, 6);
    }

    [Fact]
    public void ClampsCoordinatesThatOnlyOverflowByModelRounding()
    {
        var box = BoundingBox.TryCreate(-0.001, 0.2, 0.5, 0.803);

        Assert.NotNull(box);
        Assert.Equal(0, box.X);
        Assert.True(box.Y + box.Height <= 1);
    }

    [Theory]
    [InlineData(null, 0.2, 0.5, 0.1)]
    [InlineData(0.2, null, 0.5, 0.1)]
    [InlineData(0.2, 0.2, null, 0.1)]
    [InlineData(0.2, 0.2, 0.5, null)]
    public void RejectsAPartialBox(double? x, double? y, double? width, double? height)
    {
        Assert.Null(BoundingBox.TryCreate(x, y, width, height));
    }

    [Theory]
    // Outside the image on either axis.
    [InlineData(-0.2, 0.2, 0.5, 0.1)]
    [InlineData(0.2, -0.2, 0.5, 0.1)]
    [InlineData(0.9, 0.2, 0.5, 0.1)]
    [InlineData(0.2, 0.95, 0.5, 0.1)]
    // Degenerate or unreadably thin.
    [InlineData(0.2, 0.2, 0, 0.1)]
    [InlineData(0.2, 0.2, 0.5, 0)]
    [InlineData(0.2, 0.2, -0.5, 0.1)]
    [InlineData(0.2, 0.2, 0.005, 0.1)]
    [InlineData(0.2, 0.2, 0.5, 0.001)]
    // A frame around the whole receipt says nothing about where the value is.
    [InlineData(0, 0, 1, 1)]
    [InlineData(0.01, 0.01, 0.98, 0.95)]
    public void RejectsUnusableBoxes(double x, double y, double width, double height)
    {
        Assert.Null(BoundingBox.TryCreate(x, y, width, height));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsNonFiniteCoordinates(double value)
    {
        Assert.Null(BoundingBox.TryCreate(value, 0.2, 0.5, 0.1));
        Assert.Null(BoundingBox.TryCreate(0.2, value, 0.5, 0.1));
        Assert.Null(BoundingBox.TryCreate(0.2, 0.2, value, 0.1));
        Assert.Null(BoundingBox.TryCreate(0.2, 0.2, 0.5, value));
    }
}
