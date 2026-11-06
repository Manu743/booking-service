namespace Booking.Api.Models.Rooms;

public record AvailabilityWindow(
    DateTime From,
    DateTime To);
