using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class CustomerEndpointsTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task GetCustomers_ReturnsSeededCustomers()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/customers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var customers = await response.Content
            .ReadFromJsonAsync<List<CustomerResponseDto>>(TestJson.Options);

        Assert.NotNull(customers);
        Assert.Equal(4, customers.Count);
        Assert.Equal(new[] { "C1", "C2", "C3", "C4" }, customers.Select(customer => customer.Name));
        Assert.All(customers, customer => Assert.Equal("None", customer.Tier));
    }

    [Fact]
    public async Task CreateCustomer_WithInvalidTier_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/customers",
            new
            {
                Name = "C5",
                Tier = "Platinum"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.True(
            document.RootElement.TryGetProperty("errors", out var errors),
            "The response does not contain a ProblemDetails errors object.");

        Assert.True(
            errors.TryGetProperty("tier", out _),
            "The errors object does not contain the key 'tier'.");
    }

    [Fact]
    public async Task CreateCustomer_ReturnsCreatedCustomer()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/customers",
            new
            {
                Name = "C5",
                Tier = "Gold"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var customer = await response.Content
            .ReadFromJsonAsync<CustomerResponseDto>(TestJson.Options);

        Assert.NotNull(customer);
        Assert.True(customer.Id > 0);
        Assert.Equal("C5", customer.Name);
        Assert.Equal("Gold", customer.Tier);
        Assert.Equal($"/customers/{customer.Id}", response.Headers.Location?.OriginalString);
    }
}
