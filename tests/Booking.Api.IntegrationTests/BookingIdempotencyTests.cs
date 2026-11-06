using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class BookingIdempotencyTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    private const int ConcurrentRequests = 10;

    [Fact]
    public async Task CreateBooking_WithSameKeyAndPayload_ReplaysStoredResponse()
    {
        SetNow(2026, 11, 5, 10, 0);
        using var client = CreateClient();

        var first = await PostAsync(
            client,
            "replay-key",
            Request(roomId: 3, startHour: 10, endHour: 11));

        var second = await PostAsync(
            client,
            "replay-key",
            Request(roomId: 3, startHour: 10, endHour: 11));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(
            first.Headers.Location?.OriginalString,
            second.Headers.Location?.OriginalString);

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();

        Assert.Equal(firstBody, secondBody);

        var booking = await first.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(booking);
        Assert.Equal($"/bookings/{booking.Id}", first.Headers.Location?.OriginalString);
        Assert.Equal(
            1,
            await CountBookingsAsync(3, Local(2026, 12, 7, 10, 0)));
        Assert.Equal(1, await CountKeysAsync("replay-key"));
    }

    [Fact]
    public async Task CreateBooking_WithSameKeyAndDifferentPayload_ReturnsUnprocessableEntity()
    {
        SetNow(2026, 11, 5, 10, 0);
        using var client = CreateClient();

        var first = await PostAsync(
            client,
            "mismatch-key",
            Request(roomId: 3, startHour: 10, endHour: 11));

        var second = await PostAsync(
            client,
            "mismatch-key",
            Request(roomId: 3, startHour: 12, endHour: 13));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            second.StatusCode);
        Assert.Equal(
            0,
            await CountBookingsAsync(3, Local(2026, 12, 7, 12, 0)));
    }

    [Fact]
    public async Task CreateBooking_WithEquivalentJsonFormatting_ReplaysInsteadOfReturning422()
    {
        SetNow(2026, 11, 5, 10, 0);
        using var client = CreateClient();

        var first = await PostAsync(
            client,
            "canonical-key",
            Request(roomId: 3, startHour: 10, endHour: 11));

        // Same parsed values, different property order and whitespace. The
        // hash must be computed over the DTO, not the raw body.
        var rawBody = """
            {
                "startAt": "2026-12-07T10:00:00",
                "endAt":   "2026-12-07T11:00:00",
                "roomId":  3,
                "customerId": 1
            }
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, "/bookings")
        {
            Content = new StringContent(
                rawBody,
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Add("Idempotency-Key", "canonical-key");

        var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var firstBooking = await first.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);
        var secondBooking = await second.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(firstBooking);
        Assert.NotNull(secondBooking);
        Assert.Equal(firstBooking.Id, secondBooking.Id);
        Assert.Equal(
            1,
            await CountBookingsAsync(3, Local(2026, 12, 7, 10, 0)));
    }

    [Fact]
    public async Task CreateBooking_WhenSameKeyRequestsRace_CreatesSingleBooking()
    {
        SetNow(2026, 11, 5, 10, 0);
        using var client = CreateClient();

        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var pendingResponses = Enumerable
            .Range(0, ConcurrentRequests)
            .Select(async _ =>
            {
                await startGate.Task;

                return await PostAsync(
                    client,
                    "race-key",
                    Request(roomId: 3, startHour: 10, endHour: 11));
            })
            .ToArray();

        startGate.SetResult();

        var responses = await Task.WhenAll(pendingResponses);

        Assert.All(
            responses,
            response => Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode));

        var bookings = new List<BookingResponseDto>();

        foreach (var response in responses)
        {
            var booking = await response.Content
                .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

            Assert.NotNull(booking);
            bookings.Add(booking);
        }

        Assert.Single(bookings.Select(booking => booking.Id).Distinct());
        Assert.Equal(
            1,
            await CountBookingsAsync(3, Local(2026, 12, 7, 10, 0)));
        Assert.Equal(1, await CountKeysAsync("race-key"));
    }

    [Fact]
    public async Task CreateBooking_WithExpiredKey_TreatsRequestAsNew()
    {
        SetNow(2026, 11, 5, 10, 0);
        using var client = CreateClient();

        var first = await PostAsync(
            client,
            "expiring-key",
            Request(roomId: 3, startHour: 10, endHour: 11));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Exactly the guide's window: older than 24 hours is a new request.
        SetNow(2026, 11, 6, 11, 0);

        var second = await PostAsync(
            client,
            "expiring-key",
            Request(roomId: 3, startHour: 12, endHour: 13));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var firstBooking = await first.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);
        var secondBooking = await second.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(firstBooking);
        Assert.NotNull(secondBooking);
        Assert.NotEqual(firstBooking.Id, secondBooking.Id);
        Assert.Equal(
            1,
            await CountBookingsAsync(3, Local(2026, 12, 7, 10, 0)));
        Assert.Equal(
            1,
            await CountBookingsAsync(3, Local(2026, 12, 7, 12, 0)));
        Assert.Equal(1, await CountKeysAsync("expiring-key"));

        // The key now belongs to the new payload and replays it.
        var replay = await PostAsync(
            client,
            "expiring-key",
            Request(roomId: 3, startHour: 12, endHour: 13));

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);

        var replayed = await replay.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(replayed);
        Assert.Equal(secondBooking.Id, replayed.Id);
    }

    [Fact]
    public async Task CreateBooking_WithoutKey_KeepsExistingBehavior()
    {
        SetNow(2026, 11, 5, 10, 0);
        using var client = CreateClient();

        var first = await PostAsync(
            client,
            idempotencyKey: null,
            Request(roomId: 3, startHour: 10, endHour: 11));

        var second = await PostAsync(
            client,
            idempotencyKey: null,
            Request(roomId: 3, startHour: 10, endHour: 11));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(
            1,
            await CountBookingsAsync(3, Local(2026, 12, 7, 10, 0)));
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string? idempotencyKey,
        object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bookings")
        {
            Content = JsonContent.Create(payload)
        };

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private static object Request(
        int roomId,
        int startHour,
        int endHour) =>
        new
        {
            RoomId = roomId,
            CustomerId = 1,
            StartAt = Local(2026, 12, 7, startHour, 0),
            EndAt = Local(2026, 12, 7, endHour, 0)
        };

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

    private async Task<int> CountBookingsAsync(
        int roomId,
        DateTime startAt)
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM dbo.Bookings
            WHERE RoomId = @RoomId
              AND StartAt = @StartAt;
            """,
            connection);

        command.Parameters.AddWithValue("@RoomId", roomId);
        command.Parameters.AddWithValue("@StartAt", startAt);

        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private async Task<int> CountKeysAsync(string idempotencyKey)
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT COUNT(*)
            FROM dbo.IdempotencyKeys
            WHERE [Key] = @Key;
            """,
            connection);

        command.Parameters.AddWithValue("@Key", idempotencyKey);

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
