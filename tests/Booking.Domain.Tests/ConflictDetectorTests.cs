using Booking.Domain.Models;
using Booking.Domain.Services;
using Xunit;

namespace Booking.Domain.Tests;

public class ConflictDetectorTests
{
    private readonly ConflictDetector _detector = new();

    [Fact]
    public void HasConflict_OverlappingBookings_ReturnsTrue()
    {
        var existing = new[]
        {
            new BookingInterval
            {
                Start = new DateTime(2026, 11, 2, 10, 0, 0),
                End = new DateTime(2026, 11, 2, 11, 0, 0)
            }
        };

        var result = _detector.HasConflict(
            new DateTime(2026, 11, 2, 10, 30, 0),
            new DateTime(2026, 11, 2, 11, 30, 0),
            existing);

        Assert.True(result);
    }

    [Fact]
    public void HasConflict_Only14MinutesAfter_ReturnsTrue()
    {
        var existing = new[]
        {
            new BookingInterval
            {
                Start = new DateTime(2026, 11, 2, 10, 0, 0),
                End = new DateTime(2026, 11, 2, 11, 0, 0)
            }
        };

        var result = _detector.HasConflict(
            new DateTime(2026, 11, 2, 11, 14, 0),
            new DateTime(2026, 11, 2, 12, 0, 0),
            existing);

        Assert.True(result);
    }

    [Fact]
    public void HasConflict_Exactly15MinutesAfter_ReturnsFalse()
    {
        var existing = new[]
        {
            new BookingInterval
            {
                Start = new DateTime(2026, 11, 2, 10, 0, 0),
                End = new DateTime(2026, 11, 2, 11, 0, 0)
            }
        };

        var result = _detector.HasConflict(
            new DateTime(2026, 11, 2, 11, 15, 0),
            new DateTime(2026, 11, 2, 12, 0, 0),
            existing);

        Assert.False(result);
    }

    [Fact]
    public void HasConflict_BookingBeforeExistingWithInsufficientBuffer_ReturnsTrue()
    {
        var existing = new[]
        {
            new BookingInterval
            {
                Start = new DateTime(2026, 11, 2, 10, 0, 0),
                End = new DateTime(2026, 11, 2, 11, 0, 0)
            }
        };

        var result = _detector.HasConflict(
            new DateTime(2026, 11, 2, 8, 0, 0),
            new DateTime(2026, 11, 2, 9, 46, 0),
            existing);

        Assert.True(result);
    }

    [Fact]
    public void HasConflict_CancelledBooking_ReturnsFalse()
    {
        var existing = new[]
        {
            new BookingInterval
            {
                Start = new DateTime(2026, 11, 2, 10, 0, 0),
                End = new DateTime(2026, 11, 2, 11, 0, 0),
                IsCancelled = true
            }
        };

        var result = _detector.HasConflict(
            new DateTime(2026, 11, 2, 10, 30, 0),
            new DateTime(2026, 11, 2, 11, 30, 0),
            existing);

        Assert.False(result);
    }

    [Fact]
    public void HasConflict_NoExistingBookings_ReturnsFalse()
    {
        var result = _detector.HasConflict(
            new DateTime(2026, 11, 2, 10, 0, 0),
            new DateTime(2026, 11, 2, 11, 0, 0),
            Array.Empty<BookingInterval>());

        Assert.False(result);
    }
}