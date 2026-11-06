namespace Booking.Domain.Services;
public class BookingValidator
{
    public IReadOnlyList<string> Validate(DateTime start, DateTime end)
    {
        var errors = new List<string>();

        //R1 Start and End
        if (end <= start)
        {
            errors.Add("INVALID_INTERVAL");
            return errors;
        }
        //R1 aling to 15 minute intervals.
        if (!IsAligned(start) || !IsAligned(end))
        {
            errors.Add("INVALID_ALIGNMENT");
        }

        //R2 Duration must be between 30 and 480 minutes
        var duration = end - start;

        if (duration < TimeSpan.FromMinutes(30) ||
            duration > TimeSpan.FromMinutes(480))
        {
            errors.Add("INVALID_DURATION");
        }

        //R3 start and end on the same day
        if (start.Date != end.Date)
        {
            errors.Add("OUTSIDE_OPENING_HOURS");
            return errors;
        }

        //R3 Sunday if closed
        var opening = OpeningHours.For(start.DayOfWeek);

        if (opening is null)
        {
            errors.Add("OUTSIDE_OPENING_HOURS");
            return errors;
        }

        //R3 Entire booking must fit within opening hours.
        if (start.TimeOfDay < opening.Value.Open ||
            end.TimeOfDay > opening.Value.Close)
        {
            errors.Add("OUTSIDE_OPENING_HOURS");
        }
        return errors;
    }

    private static bool IsAligned(DateTime dateTime)
    {
        return dateTime.Minute %15 == 0 &&
            dateTime.TimeOfDay.Ticks % 
                TimeSpan.TicksPerMinute == 0;
    }
}

