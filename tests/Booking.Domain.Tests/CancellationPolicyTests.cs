using Booking.Domain.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Booking.Domain.Tests;

public class CancellationPolicyTests
{
    private static readonly DateTime FixedNow =
        new(2026, 11, 4, 10, 0, 0, DateTimeKind.Unspecified);

    private static CancellationPolicy CreatePolicy()
    {
        var fakeTime = new FakeTimeProvider(
            new DateTimeOffset(FixedNow));

        return new CancellationPolicy(fakeTime);
    }

    [Fact]
    public void Calculate_AtLeast48Hours_ReturnsZeroFee()
    {
        var policy = CreatePolicy();
        var start = FixedNow.AddHours(48);

        var result = policy.Calculate(start, 100m);

        Assert.Equal(0m, result.FeePercentage);
        Assert.Equal(0m, result.CancellationFee);
    }

    [Fact]
    public void Calculate_Exactly48Hours_ReturnsZeroFee()
    {
        var policy = CreatePolicy();
        var start = FixedNow.AddHours(48);

        var result = policy.Calculate(start, 100m);

        Assert.Equal(0m, result.CancellationFee);
    }

    [Fact]
    public void Calculate_Between24And48Hours_Returns50PercentFee()
    {
        var policy = CreatePolicy();
        var start = FixedNow.AddHours(36);

        var result = policy.Calculate(start, 100m);

        Assert.Equal(0.50m, result.FeePercentage);
        Assert.Equal(50m, result.CancellationFee);
    }

    [Fact]
    public void Calculate_Exactly24Hours_Returns50PercentFee()
    {
        var policy = CreatePolicy();
        var start = FixedNow.AddHours(24);

        var result = policy.Calculate(start, 100m);

        Assert.Equal(50m, result.CancellationFee);
    }

    [Fact]
    public void Calculate_LessThan24Hours_ReturnsFullFee()
    {
        var policy = CreatePolicy();
        var start = FixedNow.AddHours(12);

        var result = policy.Calculate(start, 100m);

        Assert.Equal(1m, result.FeePercentage);
        Assert.Equal(100m, result.CancellationFee);
    }

    [Fact]
    public void Calculate_FractionalAmount_RoundsAwayFromZero()
    {
        var policy = CreatePolicy();
        var start = FixedNow.AddHours(12);

        var result = policy.Calculate(start, 10.125m);

        Assert.Equal(10.13m, result.CancellationFee);
    }

    [Fact]
    public void Calculate_AtStartTime_ThrowsException()
    {
        var policy = CreatePolicy();

        Assert.Throws<InvalidOperationException>(() =>
            policy.Calculate(FixedNow, 100m));
    }

    [Fact]
    public void Calculate_AfterStartTime_ThrowsException()
    {
        var policy = CreatePolicy();
        var start = FixedNow.AddHours(-1);

        Assert.Throws<InvalidOperationException>(() =>
            policy.Calculate(start, 100m));
    }

    [Fact]
    public void Calculate_NegativeTotal_ThrowsException()
    {
        var policy = CreatePolicy();
        var start = FixedNow.AddHours(12);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            policy.Calculate(start, -100m));
    }
}