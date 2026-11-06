using Booking.Domain.Models;
using Booking.Domain.Services;
using Xunit;

namespace Booking.Domain.Tests;

public class RoomAvailabilityCalculatorTests
{
    private readonly RoomAvailabilityCalculator _calculator = new();

    [Fact]
    public void GetFreeWindows_OnGoldenMonday_ReturnsExpectedWindows()
    {
        var date = Local(2026, 3, 2);

        var bookings = new[]
        {
            Booking(date, 8, 0, 10, 0),
            Booking(date, 11, 30, 14, 30),
            Booking(date, 14, 30, 15, 30)
        };

        var windows = _calculator.GetFreeWindows(date, bookings, 60);

        Assert.Equal(2, windows.Count);
        Assert.Equal(Local(2026, 3, 2, 10, 15), windows[0].Start);
        Assert.Equal(Local(2026, 3, 2, 11, 15), windows[0].End);
        Assert.Equal(Local(2026, 3, 2, 15, 45), windows[1].Start);
        Assert.Equal(Local(2026, 3, 2, 22, 0), windows[1].End);
    }

    [Fact]
    public void GetFreeWindows_MergesOverlappingBlockedIntervals()
    {
        // Saturday opening hours are 08:00-18:00. The blocked intervals
        // 09:45-12:00 and 11:15-12:45 overlap and must merge.
        var date = Local(2026, 3, 7);

        var bookings = new[]
        {
            Booking(date, 10, 0, 11, 45),
            Booking(date, 11, 30, 12, 30)
        };

        var windows = _calculator.GetFreeWindows(date, bookings, 60);

        Assert.Equal(2, windows.Count);
        Assert.Equal(Local(2026, 3, 7, 8, 0), windows[0].Start);
        Assert.Equal(Local(2026, 3, 7, 9, 45), windows[0].End);
        Assert.Equal(Local(2026, 3, 7, 12, 45), windows[1].Start);
        Assert.Equal(Local(2026, 3, 7, 18, 0), windows[1].End);
    }

    [Fact]
    public void GetFreeWindows_IgnoresCancelledBookings()
    {
        var date = Local(2026, 3, 2);

        var bookings = new[]
        {
            Booking(date, 10, 0, 11, 0, isCancelled: true)
        };

        var windows = _calculator.GetFreeWindows(date, bookings, 60);

        var window = Assert.Single(windows);

        Assert.Equal(Local(2026, 3, 2, 7, 0), window.Start);
        Assert.Equal(Local(2026, 3, 2, 22, 0), window.End);
    }

    [Fact]
    public void GetFreeWindows_OnSunday_ReturnsEmpty()
    {
        var windows = _calculator.GetFreeWindows(
            Local(2026, 3, 8),
            Array.Empty<BookingInterval>(),
            60);

        Assert.Empty(windows);
    }

    [Fact]
    public void GetFreeWindows_FiltersWindowsShorterThanMinimum()
    {
        var date = Local(2026, 3, 2);
        var noBookings = Array.Empty<BookingInterval>();

        var exactly900 = _calculator.GetFreeWindows(
            date,
            noBookings,
            900);

        var above900 = _calculator.GetFreeWindows(
            date,
            noBookings,
            901);

        Assert.Single(exactly900);
        Assert.Empty(above900);
    }

    [Fact]
    public void GetFreeWindows_EndsAtClosingTimeNotClosingMinusBuffer()
    {
        // 20:00-21:00 blocks 19:45-21:15. The last free window must run to
        // 22:00, not stop at 21:45.
        var date = Local(2026, 3, 2);

        var bookings = new[]
        {
            Booking(date, 20, 0, 21, 0)
        };

        var windows = _calculator.GetFreeWindows(date, bookings, 45);

        Assert.Equal(2, windows.Count);
        Assert.Equal(Local(2026, 3, 2, 21, 15), windows[1].Start);
        Assert.Equal(Local(2026, 3, 2, 22, 0), windows[1].End);
    }

    [Fact]
    public void GetFreeWindows_BookingEndingAtClosing_LeavesNoWindowAfterIt()
    {
        var date = Local(2026, 3, 2);

        var bookings = new[]
        {
            Booking(date, 20, 0, 22, 0)
        };

        var windows = _calculator.GetFreeWindows(date, bookings, 60);

        var window = Assert.Single(windows);

        Assert.Equal(Local(2026, 3, 2, 7, 0), window.Start);
        Assert.Equal(Local(2026, 3, 2, 19, 45), window.End);
    }

    [Fact]
    public void GetFreeWindows_BufferCrossesOpeningTime()
    {
        var date = Local(2026, 3, 2);

        var bookings = new[]
        {
            Booking(date, 7, 0, 8, 0)
        };

        var windows = _calculator.GetFreeWindows(date, bookings, 60);

        var window = Assert.Single(windows);

        Assert.Equal(Local(2026, 3, 2, 8, 15), window.Start);
        Assert.Equal(Local(2026, 3, 2, 22, 0), window.End);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetFreeWindows_WithNonPositiveMinimum_Throws(int minMinutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _calculator.GetFreeWindows(
                Local(2026, 3, 2),
                Array.Empty<BookingInterval>(),
                minMinutes));
    }

    private static BookingInterval Booking(
        DateTime date,
        int startHour,
        int startMinute,
        int endHour,
        int endMinute,
        bool isCancelled = false) =>
        new()
        {
            Start = date.AddHours(startHour).AddMinutes(startMinute),
            End = date.AddHours(endHour).AddMinutes(endMinute),
            IsCancelled = isCancelled
        };

    private static DateTime Local(
        int year,
        int month,
        int day,
        int hour = 0,
        int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
}
