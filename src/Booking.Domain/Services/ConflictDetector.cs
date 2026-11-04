using Booking.Domain.Models;

namespace Booking.Domain.Services;

public class ConflictDetector
{
    //15-minute interval
    private static readonly TimeSpan Buffer =
        TimeSpan.FromMinutes(15);

    public bool HasConflict(
        DateTime newStart,
        DateTime newEnd,
        IEnumerable<BookingInterval> existingBookings)
    {
        foreach (var booking in existingBookings)
        {
            //bookings cancelled
            if (booking.IsCancelled)
                continue;

            bool overlaps =
                newStart < booking.End.Add(Buffer) &&
                booking.Start < newEnd.Add(Buffer);

            if (overlaps)
                return true;
        }

        return false;
    }
}