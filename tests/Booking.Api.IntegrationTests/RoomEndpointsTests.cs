using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class RoomEndpointsTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task GetRooms_ReturnsSeededRooms()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/rooms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rooms = await response.Content
            .ReadFromJsonAsync<List<RoomResponseDto>>(TestJson.Options);

        Assert.NotNull(rooms);
        Assert.Equal(3, rooms.Count);
        Assert.Equal(new[] { "R1", "R2", "R3" }, rooms.Select(room => room.Name));
        Assert.Equal(new[] { 40.00m, 25.00m, 60.00m }, rooms.Select(room => room.HourlyRate));
    }

    [Fact]
    public async Task CreateRoom_ReturnsCreatedRoom()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/rooms",
            new
            {
                Name = "R4",
                Capacity = 4,
                HourlyRate = 20.50m
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var room = await response.Content
            .ReadFromJsonAsync<RoomResponseDto>(TestJson.Options);

        Assert.NotNull(room);
        Assert.True(room.Id > 0);
        Assert.Equal("R4", room.Name);
        Assert.Equal(4, room.Capacity);
        Assert.Equal(20.50m, room.HourlyRate);
        Assert.Equal($"/rooms/{room.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateRoom_WithInvalidHourlyRate_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/rooms",
            new
            {
                Name = "Invalid",
                Capacity = 4,
                HourlyRate = 0m
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertProblemDetailsAsync(response, "hourlyRate");
    }

    [Fact]
    public async Task CreateRoom_WithInvalidCapacity_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/rooms",
            new
            {
                Name = "Invalid",
                Capacity = 0,
                HourlyRate = 20.00m
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertProblemDetailsAsync(response, "capacity");
    }

    [Fact]
    public async Task CreateRoom_WithEmptyName_ReturnsProblemDetails()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/rooms",
            new
            {
                Name = "",
                Capacity = 4,
                HourlyRate = 20.00m
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await AssertProblemDetailsAsync(response, "name");
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        string errorKey)
    {
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.True(
            document.RootElement.TryGetProperty("errors", out var errors),
            "The response does not contain a ProblemDetails errors object.");

        Assert.True(
            errors.TryGetProperty(errorKey, out _),
            $"The errors object does not contain the key '{errorKey}'.");
    }
}
