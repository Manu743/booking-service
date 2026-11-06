namespace Booking.Domain.Services;

// Opening hours shared by validation and availability so both agree on the
// day boundaries. Sunday is closed.
public static class OpeningHours
{
    public static (TimeSpan Open, TimeSpan Close)? For(DayOfWeek day) =>
        day switch
        {
            DayOfWeek.Sunday => null,
            DayOfWeek.Saturday => (
                TimeSpan.FromHours(8),
                TimeSpan.FromHours(18)),
            _ => (
                TimeSpan.FromHours(7),
                TimeSpan.FromHours(22))
        };
}
