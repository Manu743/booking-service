using Booking.Domain.Models;

namespace Booking.Domain.Services{}

public class PricingCalculator
{
    private const decimal OffPeakMultiplier = 1.00m;
    private const decimal PeakMultiplier = 1.50m;
    private const decimal SaturdayMultiplier = 1.25m;

    public PricingResult Calculate(
        DateTime start,
        DateTime end,
        decimal hourlyRate)
    {
        if (end <= start)
            throw new ArgumentException(
                "End must be greater than Start.");

        if (start.Date != end.Date)
            throw new ArgumentException(
                "Pricing requires a single-day booking.");

        var boundaries = new List<DateTime> { start };

        //peak-window
        var peakBoundaries = new[]
        {
            start.Date.AddHours(9),
            start.Date.AddHours(12),
            start.Date.AddHours(14),
            start.Date.AddHours(17)
        };

        //boundary within the reserve
        boundaries.AddRange(
            peakBoundaries.Where(x => x > start && x < end));

        boundaries.Add(end);

        var segments = new List<PricingSegment>();

        for (int i = 0; i < boundaries.Count - 1; i++)
        {
            var segmentStart = boundaries[i];
            var segmentEnd = boundaries[i + 1];

            var multiplier = GetMultiplier(
                segmentStart.DayOfWeek,
                segmentStart.TimeOfDay);

            var minutes = (decimal)(segmentEnd - segmentStart).TotalMinutes;

            var amount =
                hourlyRate * multiplier * minutes / 60m;

            segments.Add(new PricingSegment(
                segmentStart,
                segmentEnd,
                multiplier,
                amount));
        }

        var subtotal = segments.Sum(x => x.Amount);

        return new PricingResult(segments, subtotal);
    }
    private static decimal GetMultiplier(
        DayOfWeek day,
        TimeSpan time)
    {
    if (day == DayOfWeek.Saturday)
        return SaturdayMultiplier;

    bool isWeekday =
        day >= DayOfWeek.Monday &&
        day <= DayOfWeek.Friday;

    if (!isWeekday)
        return OffPeakMultiplier;

    bool isPeak =
        (time >= TimeSpan.FromHours(9) &&
            time < TimeSpan.FromHours(12) ||
            time >= TimeSpan.FromHours(14) &&
            time < TimeSpan.FromHours(17));

        return isPeak
                ? PeakMultiplier
                : OffPeakMultiplier;
    }
}
