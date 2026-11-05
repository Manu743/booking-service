using Booking.Domain.Services;
using Xunit;

namespace Booking.Domain.Tests;

public class PricingCalculatorTests
{
    private readonly PricingCalculator _calculator = new();

    [Fact]
    public void Calculate_Monday0800To1000_Returns100()
    {
        var result = _calculator.Calculate(
            new DateTime(2026, 11, 2, 8, 0, 0),
            new DateTime(2026, 11, 2, 10, 0, 0),
            40m);

        Assert.Equal(100.00m, result.Subtotal);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(40.00m, result.Segments[0].Amount);
        Assert.Equal(60.00m, result.Segments[1].Amount);
    }

    [Fact]
    public void Calculate_Monday1130To1430_Returns140()
    {
        var result = _calculator.Calculate(
            new DateTime(2026, 11, 2, 11, 30, 0),
            new DateTime(2026, 11, 2, 14, 30, 0),
            40m);

        Assert.Equal(140.00m, result.Subtotal);
        Assert.Equal(3, result.Segments.Count);
        Assert.Equal(30.00m, result.Segments[0].Amount);
        Assert.Equal(80.00m, result.Segments[1].Amount);
        Assert.Equal(30.00m, result.Segments[2].Amount);
    }

    [Fact]
    public void Calculate_Saturday1000To1145_Returns87_50()
    {
        var result = _calculator.Calculate(
            new DateTime(2026, 11, 7, 10, 0, 0),
            new DateTime(2026, 11, 7, 11, 45, 0),
            40m);

        Assert.Equal(87.50m, result.Subtotal);
        Assert.Single(result.Segments);
        Assert.Equal(1.25m, result.Segments[0].Multiplier);
    }

    [Fact]
    public void Calculate_EndingAt0900_UsesOffPeakRate()
    {
        var result = _calculator.Calculate(
            new DateTime(2026, 11, 2, 8, 0, 0),
            new DateTime(2026, 11, 2, 9, 0, 0),
            40m);

        Assert.Equal(40.00m, result.Subtotal);
        Assert.Equal(1.00m, result.Segments[0].Multiplier);
    }

    [Fact]
    public void Calculate_StartingAt0900_UsesPeakRate()
    {
        var result = _calculator.Calculate(
            new DateTime(2026, 11, 2, 9, 0, 0),
            new DateTime(2026, 11, 2, 10, 0, 0),
            40m);

        Assert.Equal(60.00m, result.Subtotal);
        Assert.Equal(1.50m, result.Segments[0].Multiplier);
    }

    [Fact]
    public void Calculate_InvalidInterval_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(
                new DateTime(2026, 11, 2, 10, 0, 0),
                new DateTime(2026, 11, 2, 9, 0, 0),
                40m));
    }

    [Fact]
    public void Calculate_CrossDayInterval_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _calculator.Calculate(
                new DateTime(2026, 11, 2, 21, 0, 0),
                new DateTime(2026, 11, 3, 8, 0, 0),
                40m));
    }
}