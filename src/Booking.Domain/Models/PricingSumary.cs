
namespace Booking.Domain.Models { }
public record PricingSumary(
    decimal Subtotal,
    decimal DiscountPercentage,
    decimal DiscountAmount,
    decimal Total);