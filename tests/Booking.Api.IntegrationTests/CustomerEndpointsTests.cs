using System.Net;
using System.Net.Http.Json;
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
