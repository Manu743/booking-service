namespace Booking.Domain.Models;

public record PricingSegment(
    DateTime Start,
    DateTime End,
    decimal Multiplier,
    decimal Amount);
