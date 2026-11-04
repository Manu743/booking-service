namespace Booking.Domain.Models;

public class BookingInterval
{
    public DateTime Start { get; init; }

    public DateTime End { get; init; }

    public bool IsCancelled { get; init; }
}