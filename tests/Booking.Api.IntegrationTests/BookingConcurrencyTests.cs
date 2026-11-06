using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class BookingConcurrencyTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    private const int ConcurrentRequests = 20;

    [Fact]
    public async Task CreateBooking_WhenRequestsRace_CreatesSingleBooking()
    {
        using var client = CreateClient();

        // Release every request at the same instant instead of relying on
        // Task.WhenAll to schedule them close enough together.
        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var pendingResponses = Enumerable
            .Range(0, ConcurrentRequests)
            .Select(async _ =>
            {
                await startGate.Task;

                return await client.PostAsJsonAsync(
                    "/bookings",
                    new
                    {
                        RoomId = 3,
                        CustomerId = 1,
                        StartAt = Local(2026, 12, 7, 10, 0),
                        EndAt = Local(2026, 12, 7, 11, 0)
                    });
            })
            .ToArray();

        startGate.SetResult();

        var responses = await Task.WhenAll(pendingResponses);

        Assert.Equal(ConcurrentRequests, responses.Length);
        Assert.Equal(
            1,
            responses.Count(
                response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(
            ConcurrentRequests - 1,
            responses.Count(
                response => response.StatusCode == HttpStatusCode.Conflict));

        var persistedBookings = await CountPersistedBookingsAsync();

        Assert.Equal(1, persistedBookings);
    }

    private async Task<int> CountPersistedBookingsAsync()
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM dbo.Bookings
            WHERE RoomId = 3
              AND Status = 'A'
              AND StartAt = '2026-12-07T10:00:00';
            """,
            connection);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static DateTime Local(
        int year,
        int month,
        int day,
        int hour,
        int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
}
