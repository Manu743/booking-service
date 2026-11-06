using Booking.Domain.Models;

namespace Booking.Domain.Services;

public class RoomAvailabilityCalculator
{
    private static readonly TimeSpan Buffer =
        TimeSpan.FromMinutes(15);

    // Returns the free windows of the opening day, already merged and
    // filtered by the minimum duration. Free windows end at closing time;
    // the buffer of a new booking may extend beyond closing (R4 allows it).
    public IReadOnlyList<TimeWindow> GetFreeWindows(
        DateTime date,
        IEnumerable<BookingInterval> bookings,
        int minMinutes)
    {
        if (minMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minMinutes),
                "The minimum duration must be greater than zero.");
        }

        var opening = OpeningHours.For(date.DayOfWeek);

        if (opening is null)
        {
            return Array.Empty<TimeWindow>();
        }

        var day = date.Date;

        var openingStart = day.Add(opening.Value.Open);
        var openingEnd = day.Add(opening.Value.Close);

        var blocked = bookings
            .Where(booking => !booking.IsCancelled)
            .Select(booking => new TimeWindow(
                booking.Start.Add(-Buffer),
                booking.End.Add(Buffer)))
            .OrderBy(window => window.Start)
            .ToList();

        var merged = new List<TimeWindow>();

        foreach (var window in blocked)
        {
            if (merged.Count == 0 ||
                window.Start > merged[^1].End)
            {
                merged.Add(window);
                continue;
            }

            var last = merged[^1];

            if (window.End > last.End)
            {
                merged[^1] = new TimeWindow(last.Start, window.End);
            }
        }

        var free = new List<TimeWindow>();
        var cursor = openingStart;

        foreach (var window in merged)
        {
            var start = window.Start < openingStart
                ? openingStart
                : window.Start;

            var end = window.End > openingEnd
                ? openingEnd
                : window.End;

            if (end <= start)
            {
                continue;
            }

            if (start > cursor)
            {
                free.Add(new TimeWindow(cursor, start));
            }

            if (end > cursor)
            {
                cursor = end;
            }
        }

        if (cursor < openingEnd)
        {
            free.Add(new TimeWindow(cursor, openingEnd));
        }

        var minimum = TimeSpan.FromMinutes(minMinutes);

        return free
            .Where(window => window.End - window.Start >= minimum)
            .ToList();
    }
}
