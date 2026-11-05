using Booking.Domain.Services;
using Xunit;

namespace Booking.Domain.Tests;

public class DiscountCalculatorTests
{
    private readonly DiscountCalculator _calculator = new();

    [Fact]
    public void Calculate_WithoutDiscount_ReturnsOriginalSubtotal()
    {
        var result = _calculator.Calculate(100m, 0m);

        Assert.Equal(100m, result.Subtotal);
        Assert.Equal(0m, result.DiscountAmount);
        Assert.Equal(100m, result.Total);
    }

    [Fact]
    public void Calculate_10PercentDiscount_Returns90()
    {
        var result = _calculator.Calculate(100m, 0.10m);

        Assert.Equal(10m, result.DiscountAmount);
        Assert.Equal(90m, result.Total);
    }

    [Fact]
    public void Calculate_DiscountBelowMaximum_AppliesFullDiscount()
    {
        var result = _calculator.Calculate(100m, 0.20m);

        Assert.Equal(20m, result.DiscountAmount);
        Assert.Equal(80m, result.Total);
    }

    [Fact]
    public void Calculate_DiscountAboveMaximum_CapsAt50()
    {
        var result = _calculator.Calculate(200m, 0.30m);

        Assert.Equal(50m, result.DiscountAmount);
        Assert.Equal(150m, result.Total);
    }

    [Fact]
    public void Calculate_DiscountExactly50_Applies50()
    {
        var result = _calculator.Calculate(100m, 0.50m);

        Assert.Equal(50m, result.DiscountAmount);
        Assert.Equal(50m, result.Total);
    }

    [Fact]
    public void Calculate_RoundsAwayFromZero()
    {
        var result = _calculator.Calculate(10.125m, 0m);

        Assert.Equal(10.13m, result.Total);
    }


    [Fact]
    public void Calculate_RoundsNegativeMidpointAwayFromZero()
    {
        var result = _calculator.Calculate(10.125m, 0m);

        Assert.Equal(10.13m, result.Total);
    }

    [Fact]
    public void Calculate_NegativeSubtotal_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Calculate(-100m, 0.10m));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Calculate_InvalidDiscountPercentage_ThrowsException(
        double percentage)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Calculate(100m, (decimal)percentage));
    }
}