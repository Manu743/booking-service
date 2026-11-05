using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class HealthEndpointTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task GetDatabaseHealth_ReturnsConnected()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health/database");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<JsonElement>(TestJson.Options);

        Assert.Equal(
            "Connected",
            payload.GetProperty("status").GetString());

        Assert.Equal(
            "BookingServiceDb",
            payload.GetProperty("database").GetString());
    }
}
