using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class BookingCancellationTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task CancelBooking_AtLeast48HoursBeforeStart_ReturnsOkWithoutFee()
    {
        SetNow(2026, 2, 28, 8, 0);
        using var client = CreateClient();

        var response = await client.PostAsync("/bookings/1/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var booking = await response.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(booking);
        Assert.Equal("C", booking.Status);
        Assert.Equal(0.00m, booking.Fee);

        var persisted = await ReadCancellationAsync(1);

        Assert.Equal("C", persisted.Status);
        Assert.Equal(0.00m, persisted.Fee!.Value);
        Assert.Equal(
            new DateTime(2026, 2, 28, 8, 0, 0),
            persisted.CancelledAt);
    }

    [Fact]
    public async Task CancelBooking_Exactly24HoursBeforeStart_ChargesHalf()
    {
        SetNow(2026, 3, 1, 8, 0);
        using var client = CreateClient();

        var response = await client.PostAsync("/bookings/1/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var booking = await response.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(booking);
        Assert.Equal("C", booking.Status);
        Assert.Equal(50.00m, booking.Fee);

        var persisted = await ReadCancellationAsync(1);

        Assert.Equal("C", persisted.Status);
        Assert.Equal(50.00m, persisted.Fee!.Value);
        Assert.Equal(
            new DateTime(2026, 3, 1, 8, 0, 0),
            persisted.CancelledAt);
    }

    [Fact]
    public async Task CancelBooking_LessThan24HoursBeforeStart_ChargesFullTotal()
    {
        SetNow(2026, 3, 2, 0, 0);
        using var client = CreateClient();

        var response = await client.PostAsync("/bookings/1/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var booking = await response.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(booking);
        Assert.Equal("C", booking.Status);
        Assert.Equal(100.00m, booking.Fee);

        var persisted = await ReadCancellationAsync(1);

        Assert.Equal("C", persisted.Status);
        Assert.Equal(100.00m, persisted.Fee!.Value);
        Assert.Equal(
            new DateTime(2026, 3, 2, 0, 0, 0),
            persisted.CancelledAt);
    }

    [Fact]
    public async Task CancelBooking_AtStartTime_ReturnsConflict()
    {
        SetNow(2026, 3, 2, 8, 0);
        using var client = CreateClient();

        var response = await client.PostAsync("/bookings/1/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var persisted = await ReadCancellationAsync(1);

        Assert.Equal("A", persisted.Status);
        Assert.Null(persisted.Fee);
        Assert.Null(persisted.CancelledAt);
    }

    [Fact]
    public async Task CancelBooking_AlreadyCancelled_ReturnsConflictAndKeepsFee()
    {
        SetNow(2026, 2, 28, 8, 0);
        using var client = CreateClient();

        var response = await client.PostAsync("/bookings/4/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var persisted = await ReadCancellationAsync(4);

        Assert.Equal("C", persisted.Status);
        Assert.Equal(56.25m, persisted.Fee!.Value);
        Assert.Equal(
            new DateTime(2026, 3, 2, 8, 0, 0),
            persisted.CancelledAt);
    }

    [Fact]
    public async Task CancelBooking_NotFound_ReturnsNotFound()
    {
        SetNow(2026, 2, 28, 8, 0);
        using var client = CreateClient();

        var response = await client.PostAsync("/bookings/999/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelBooking_WithNonPositiveId_ReturnsBadRequest()
    {
        using var client = CreateClient();

        var response = await client.PostAsync("/bookings/0/cancel", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CancelBooking_Twice_DoesNotChargeAgain()
    {
        SetNow(2026, 3, 1, 8, 0);
        using var client = CreateClient();

        var first = await client.PostAsync("/bookings/1/cancel", null);
        var second = await client.PostAsync("/bookings/1/cancel", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var persisted = await ReadCancellationAsync(1);

        Assert.Equal("C", persisted.Status);
        Assert.Equal(50.00m, persisted.Fee!.Value);
        Assert.Equal(
            new DateTime(2026, 3, 1, 8, 0, 0),
            persisted.CancelledAt);
    }

    [Fact]
    public async Task CancelBooking_WhenRequestsRace_OnlyOneSucceeds()
    {
        SetNow(2026, 3, 1, 8, 0);
        using var client = CreateClient();

        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var pendingResponses = Enumerable
            .Range(0, 2)
            .Select(async _ =>
            {
                await startGate.Task;

                return await client.PostAsync("/bookings/1/cancel", null);
            })
            .ToArray();

        startGate.SetResult();

        var responses = await Task.WhenAll(pendingResponses);

        Assert.Equal(
            1,
            responses.Count(
                response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(
            1,
            responses.Count(
                response => response.StatusCode == HttpStatusCode.Conflict));

        var persisted = await ReadCancellationAsync(1);

        Assert.Equal("C", persisted.Status);
        Assert.Equal(50.00m, persisted.Fee!.Value);
        Assert.Equal(
            new DateTime(2026, 3, 1, 8, 0, 0),
            persisted.CancelledAt);
    }

    [Fact]
    public async Task CancelledBooking_DoesNotBlockNewBooking()
    {
        SetNow(2026, 3, 1, 8, 0);
        using var client = CreateClient();

        var cancelResponse = await client.PostAsync("/bookings/1/cancel", null);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync(
            "/bookings",
            new
            {
                RoomId = 1,
                CustomerId = 1,
                StartAt = Local(2026, 3, 2, 8, 0),
                EndAt = Local(2026, 3, 2, 9, 0)
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    }

    private void SetNow(
        int year,
        int month,
        int day,
        int hour,
        int minute)
    {
        Fixture.Factory.Clock.SetUtcNow(
            new DateTimeOffset(
                year,
                month,
                day,
                hour,
                minute,
                0,
                TimeSpan.Zero));
    }

    private async Task<PersistedCancellation> ReadCancellationAsync(int id)
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT Status, Fee, CancelledAt
            FROM dbo.Bookings
            WHERE Id = @Id;
            """,
            connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        return new PersistedCancellation(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetDecimal(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2));
    }

    private static DateTime Local(
        int year,
        int month,
        int day,
        int hour,
        int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private sealed record PersistedCancellation(
        string Status,
        decimal? Fee,
        DateTime? CancelledAt);
}
