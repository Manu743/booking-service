namespace Booking.Api.Models.Rooms;

public class RoomResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Capacity { get; set; }

    public decimal HourlyRate { get; set; }
}