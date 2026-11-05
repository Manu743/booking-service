namespace Booking.Domain.Models;

public record PricingSummary(
    decimal Subtotal,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal Total);
