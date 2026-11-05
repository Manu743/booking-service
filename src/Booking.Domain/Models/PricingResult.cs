namespace Booking.Domain.Models;

public record PricingResult(
    IReadOnlyList<PricingSegment> Segments,
    decimal Subtotal);
