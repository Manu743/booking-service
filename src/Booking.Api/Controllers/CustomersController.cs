using Booking.Api.Data.Repositories;
using Booking.Api.Models.Customers;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

[ApiController]
[Route("customers")]
public sealed class CustomersController : ControllerBase
{
    private static readonly string[] AllowedTiers =
    [
        "None",
        "Silver",
        "Gold"
    ];

    private readonly CustomerRepository _repository;

    public CustomersController(CustomerRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> GetCustomers()
    {
        var customers = await _repository.GetAllAsync();

        return Ok(customers);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCustomer(
        [FromBody] CreateCustomerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationError(
                "name",
                "Customer name is required.");
        }

        if (request.Name.Length > 100)
        {
            return ValidationError(
                "name",
                "Customer name cannot exceed 100 characters.");
        }

        if (!AllowedTiers.Contains(request.Tier))
        {
            return ValidationError(
                "tier",
                "Invalid customer tier.");
        }

        var customer = await _repository.CreateAsync(request);

        return Created($"/customers/{customer.Id}", customer);
    }

    private IActionResult ValidationError(string key, string message)
    {
        ModelState.AddModelError(key, message);

        return ValidationProblem(ModelState);
    }
}
