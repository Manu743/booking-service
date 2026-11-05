namespace Booking.Api.Models.Rooms;

public class CreateRoomRequest
{
    public string Name { get; set; } = string.Empty;

    public int Capacity { get; set; }

    public decimal HourlyRate { get; set; }
}