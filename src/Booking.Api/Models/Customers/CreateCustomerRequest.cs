namespace Booking.Api.Models.Customers;

public class CreateCustomerRequest
{
    public string Name { get; set; } = string.Empty;

    public string Tier { get; set; } = "None";
}