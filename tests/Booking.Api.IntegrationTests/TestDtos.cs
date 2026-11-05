namespace Booking.Api.IntegrationTests;

internal sealed class RoomResponseDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Capacity { get; set; }

    public decimal HourlyRate { get; set; }
}

internal sealed class CustomerResponseDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Tier { get; set; } = string.Empty;
}

internal sealed class BookingResponseDto
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public int CustomerId { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public List<PricingSegmentDto> Segments { get; set; } = [];

    public decimal Subtotal { get; set; }

    public decimal Discount { get; set; }

    public decimal Total { get; set; }

    public decimal Fee { get; set; }
}

internal sealed class PricingSegmentDto
{
    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    public decimal Multiplier { get; set; }

    public decimal Amount { get; set; }
}
