using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class RoomAvailabilityTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task GetAvailability_OnGoldenMonday_ReturnsExpectedWindows()
    {
        using var client = CreateClient();

        var windows = await GetWindowsAsync(
            client,
            "/rooms/1/availability?date=2026-03-02&minMinutes=60");

        Assert.Equal(2, windows.Count);
        Assert.Equal(Local(2026, 3, 2, 10, 15), windows[0].From);
        Assert.Equal(Local(2026, 3, 2, 11, 15), windows[0].To);
        Assert.Equal(Local(2026, 3, 2, 15, 45), windows[1].From);
        Assert.Equal(Local(2026, 3, 2, 22, 0), windows[1].To);
    }

    [Fact]
    public async Task GetAvailability_MergesOverlappingBookings()
    {
        using var client = CreateClient();

        var windows = await GetWindowsAsync(
            client,
            "/rooms/3/availability?date=2026-03-07&minMinutes=60");

        Assert.Equal(2, windows.Count);
        Assert.Equal(Local(2026, 3, 7, 8, 0), windows[0].From);
        Assert.Equal(Local(2026, 3, 7, 9, 45), windows[0].To);
        Assert.Equal(Local(2026, 3, 7, 12, 45), windows[1].From);
        Assert.Equal(Local(2026, 3, 7, 18, 0), windows[1].To);
    }

    [Fact]
    public async Task GetAvailability_IgnoresCancelledBookings()
    {
        using var client = CreateClient();

        // Room 2 has a cancelled booking 09:00-12:00 and an active one
        // 10:00-11:00. Only the active one may block the slot.
        var windows = await GetWindowsAsync(
            client,
            "/rooms/2/availability?date=2026-03-02&minMinutes=60");

        Assert.Equal(2, windows.Count);
        Assert.Equal(Local(2026, 3, 2, 7, 0), windows[0].From);
        Assert.Equal(Local(2026, 3, 2, 9, 45), windows[0].To);
        Assert.Equal(Local(2026, 3, 2, 11, 15), windows[1].From);
        Assert.Equal(Local(2026, 3, 2, 22, 0), windows[1].To);
    }

    [Fact]
    public async Task GetAvailability_OnSunday_ReturnsEmpty()
    {
        using var client = CreateClient();

        var windows = await GetWindowsAsync(
            client,
            "/rooms/1/availability?date=2026-03-08&minMinutes=60");

        Assert.Empty(windows);
    }

    [Fact]
    public async Task GetAvailability_WithNoBookings_ReturnsWholeOpeningWindow()
    {
        using var client = CreateClient();

        var windows = await GetWindowsAsync(
            client,
            "/rooms/3/availability?date=2026-12-07&minMinutes=60");

        var window = Assert.Single(windows);

        Assert.Equal(Local(2026, 12, 7, 7, 0), window.From);
        Assert.Equal(Local(2026, 12, 7, 22, 0), window.To);
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("03-02-2026")]
    [InlineData("not-a-date")]
    public async Task GetAvailability_WithInvalidDate_ReturnsBadRequest(string date)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            $"/rooms/1/availability?date={date}&minMinutes=60");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailability_WithoutDate_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/rooms/1/availability?minMinutes=60");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetAvailability_WithNonPositiveMinMinutes_ReturnsBadRequest(
        int minMinutes)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            $"/rooms/1/availability?date=2026-03-02&minMinutes={minMinutes}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailability_WithoutMinMinutes_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/rooms/1/availability?date=2026-03-02");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailability_ForUnknownRoom_ReturnsNotFound()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/rooms/999/availability?date=2026-03-02&minMinutes=60");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAvailability_WithNonPositiveRoomId_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.GetAsync(
            "/rooms/0/availability?date=2026-03-02&minMinutes=60");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<List<AvailabilityWindowDto>> GetWindowsAsync(
        HttpClient client,
        string url)
    {
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var windows = await response.Content
            .ReadFromJsonAsync<List<AvailabilityWindowDto>>(TestJson.Options);

        Assert.NotNull(windows);

        return windows;
    }

    private static DateTime Local(
        int year,
        int month,
        int day,
        int hour,
        int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
}
