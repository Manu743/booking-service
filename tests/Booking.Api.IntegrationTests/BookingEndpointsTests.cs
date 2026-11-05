using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class BookingEndpointsTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task GetBookingById_ReturnsExpectedShape()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/bookings/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var booking = await response.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(booking);
        Assert.Equal(1, booking.Id);
        Assert.Equal(1, booking.RoomId);
        Assert.Equal(1, booking.CustomerId);
        Assert.Equal(Local(2026, 3, 2, 8, 0), booking.StartAt);
        Assert.Equal(Local(2026, 3, 2, 10, 0), booking.EndAt);
        Assert.Equal("A", booking.Status);
        Assert.Equal(100.00m, booking.Subtotal);
        Assert.Equal(0.00m, booking.Discount);
        Assert.Equal(100.00m, booking.Total);
        Assert.Equal(0.00m, booking.Fee);
        Assert.Equal(2, booking.Segments.Count);
        Assert.Equal(40.00m, booking.Segments[0].Amount);
        Assert.Equal(60.00m, booking.Segments[1].Amount);
    }

    [Fact]
    public async Task GetBookingById_ReturnsNotFound()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/bookings/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_ReturnsCreatedBooking()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/bookings",
            new
            {
                RoomId = 1,
                CustomerId = 1,
                StartAt = Local(2026, 11, 9, 8, 0),
                EndAt = Local(2026, 11, 9, 9, 0)
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var booking = await response.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(booking);
        Assert.True(booking.Id > 11);
        Assert.Equal(1, booking.RoomId);
        Assert.Equal(1, booking.CustomerId);
        Assert.Equal("A", booking.Status);
        Assert.Equal(40.00m, booking.Subtotal);
        Assert.Equal(0.00m, booking.Discount);
        Assert.Equal(40.00m, booking.Total);
        Assert.Equal(0.00m, booking.Fee);
        Assert.Single(booking.Segments);
        Assert.Equal($"/bookings/{booking.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateBooking_WithOverlap_ReturnsConflict()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/bookings",
            new
            {
                RoomId = 1,
                CustomerId = 2,
                StartAt = Local(2026, 3, 2, 8, 30),
                EndAt = Local(2026, 3, 2, 9, 30)
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_OnCancelledBookingInterval_ReturnsCreated()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/bookings",
            new
            {
                RoomId = 2,
                CustomerId = 1,
                StartAt = Local(2026, 3, 2, 9, 0),
                EndAt = Local(2026, 3, 2, 9, 30)
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var booking = await response.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(booking);
        Assert.Equal(18.75m, booking.Subtotal);
        Assert.Equal(18.75m, booking.Total);
    }

    [Fact]
    public async Task CreateBooking_WithInvalidAlignment_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/bookings",
            new
            {
                RoomId = 1,
                CustomerId = 1,
                StartAt = Local(2026, 11, 9, 8, 10),
                EndAt = Local(2026, 11, 9, 9, 10)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_WithTooShortDuration_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/bookings",
            new
            {
                RoomId = 1,
                CustomerId = 1,
                StartAt = Local(2026, 11, 9, 8, 0),
                EndAt = Local(2026, 11, 9, 8, 20)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_OutsideOpeningHours_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/bookings",
            new
            {
                RoomId = 1,
                CustomerId = 1,
                StartAt = Local(2026, 11, 9, 6, 0),
                EndAt = Local(2026, 11, 9, 7, 0)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBooking_WithUnknownRoom_ReturnsNotFound()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/bookings",
            new
            {
                RoomId = 999,
                CustomerId = 1,
                StartAt = Local(2026, 11, 9, 8, 0),
                EndAt = Local(2026, 11, 9, 9, 0)
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static DateTime Local(
        int year,
        int month,
        int day,
        int hour,
        int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
}
