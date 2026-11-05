namespace Booking.Api.Data.Repositories;

public record BookingDetailsData(
    int Id,
    int RoomId,
    int CustomerId,
    DateTime StartAt,
    DateTime EndAt,
    string Status,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    decimal Fee,
    decimal HourlyRate,
    string CustomerTier
);