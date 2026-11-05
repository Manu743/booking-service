using Booking.Domain.Models;

namespace Booking.Api.Models.Bookings;

public class BookingResponse
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public int CustomerId { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public string Status { get; set; } = "A";

    public IReadOnlyList<PricingSegment> Segments { get; set; }
        = Array.Empty<PricingSegment>();

    public decimal Subtotal { get; set; }

    public decimal Discount { get; set; }

    public decimal Total { get; set; }

    public decimal Fee { get; set; }
}