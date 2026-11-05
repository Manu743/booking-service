namespace Booking.Domain.Models;

public record CancellationResult(
    decimal Total,
    decimal FeePercentage,
    decimal CancellationFee);
