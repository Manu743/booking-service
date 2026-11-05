namespace Booking.Api.Models.Customers;

public class CustomerResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Tier { get; set; } = "None";
}