
using Xunit;
using Booking.Domain.Services;


namespace Booking.Domain.Tests;

public class BookingValidatorTests
{
    private readonly BookingValidator _validator = new();

    [Fact]
    public void Validate_WeekdayValidBooking_ReturnsNoErrors()
    {
        var start = new DateTime(2026, 11, 2, 8, 0, 0);
        var end = new DateTime(2026, 11, 2, 9, 0, 0);

        var result = _validator.Validate(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public void Validate_StartNotAligned_ReturnsAlignmentError()
    {
        var start = new DateTime(2026, 11, 2, 8, 10, 0);
        var end = new DateTime(2026, 11, 2, 9, 0, 0);

        var result = _validator.Validate(start, end);

        Assert.Contains("INVALID_ALIGNMENT", result);
    }

    [Fact]
    public void Validate_DurationLessThan30Minutes_ReturnsDurationError()
    {
        var start = new DateTime(2026, 11, 2, 8, 0, 0);
        var end = new DateTime(2026, 11, 2, 8, 15, 0);

        var result = _validator.Validate(start, end);

        Assert.Contains("INVALID_DURATION", result);
    }

    [Fact]
    public void Validate_DurationGreaterThan480Minutes_ReturnsDurationError()
    {
        var start = new DateTime(2026, 11, 2, 7, 0, 0);
        var end = new DateTime(2026, 11, 2, 15, 15, 0);

        var result = _validator.Validate(start, end);

        Assert.Contains("INVALID_DURATION", result);
    }

    [Fact]
    public void Validate_SaturdayValidBooking_ReturnsNoErrors()
    {
        var start = new DateTime(2026, 11, 7, 8, 0, 0);
        var end = new DateTime(2026, 11, 7, 9, 0, 0);

        var result = _validator.Validate(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public void Validate_SundayBooking_ReturnsOpeningHoursError()
    {
        var start = new DateTime(2026, 11, 8, 8, 0, 0);
        var end = new DateTime(2026, 11, 8, 9, 0, 0);

        var result = _validator.Validate(start, end);

        Assert.Contains("OUTSIDE_OPENING_HOURS", result);
    }

    [Fact]
    public void Validate_EndExactlyAtClosingTime_ReturnsNoErrors()
    {
        var start = new DateTime(2026, 11, 2, 21, 0, 0);
        var end = new DateTime(2026, 11, 2, 22, 0, 0);

        var result = _validator.Validate(start, end);

        Assert.Empty(result);
    }

    [Fact]
    public void Validate_EndAfterClosingTime_ReturnsOpeningHoursError()
    {
        var start = new DateTime(2026, 11, 2, 21, 15, 0);
        var end = new DateTime(2026, 11, 2, 22, 15, 0);

        var result = _validator.Validate(start, end);

        Assert.Contains("OUTSIDE_OPENING_HOURS", result);
    }

    [Fact]
    public void Validate_EndBeforeStart_ReturnsInvalidInterval()
    {
        var start = new DateTime(2026, 11, 2, 10, 0, 0);
        var end = new DateTime(2026, 11, 2, 9, 0, 0);

        var result = _validator.Validate(start, end);

        Assert.Contains("INVALID_INTERVAL", result);
    }

    [Fact]
    public void Validate_BookingAcrossTwoDays_ReturnsOpeningHoursError()
    {
        var start = new DateTime(2026, 11, 2, 21, 0, 0);
        var end = new DateTime(2026, 11, 3, 1, 0, 0);

        var result = _validator.Validate(start, end);

        Assert.Contains("OUTSIDE_OPENING_HOURS", result);
    }
}
