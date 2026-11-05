namespace Booking.Api.Models.Bookings
{
    public class CreateBookingRequest
    {
        public int RoomId { get; set; }
        public int CustomerId { get; set; }
        public DateTime StartAt { get; set;  }
        public DateTime EndAt { get; set; }

    }
}
