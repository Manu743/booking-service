using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class BookingTotalsTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task GetBookingById_ReturnsPersistedTotals()
    {
        await ExecuteAsync(
            """
            UPDATE dbo.Bookings
            SET Subtotal = 999.99,
                Discount = 1.00,
                Total = 998.99
            WHERE Id = 1;
            """);

        using var client = CreateClient();

        var response = await client.GetAsync("/bookings/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var booking = await response.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(booking);
        Assert.Equal(999.99m, booking.Subtotal);
        Assert.Equal(1.00m, booking.Discount);
        Assert.Equal(998.99m, booking.Total);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public async Task GetBookingById_ReturnsPersistedMonetaryValues(
        int bookingId)
    {
        var persisted = await ReadPersistedValuesAsync(bookingId);

        using var client = CreateClient();

        var response = await client.GetAsync($"/bookings/{bookingId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var booking = await response.Content
            .ReadFromJsonAsync<BookingResponseDto>(TestJson.Options);

        Assert.NotNull(booking);
        Assert.Equal(persisted.Subtotal, booking.Subtotal);
        Assert.Equal(persisted.Discount, booking.Discount);
        Assert.Equal(persisted.Total, booking.Total);
        Assert.Equal(persisted.Fee, booking.Fee);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<PersistedValues> ReadPersistedValuesAsync(
        int bookingId)
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            SELECT Subtotal, Discount, Total, ISNULL(Fee, 0)
            FROM dbo.Bookings
            WHERE Id = @Id;
            """,
            connection);

        command.Parameters.AddWithValue("@Id", bookingId);

        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());

        return new PersistedValues(
            reader.GetDecimal(0),
            reader.GetDecimal(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3));
    }

    private sealed record PersistedValues(
        decimal Subtotal,
        decimal Discount,
        decimal Total,
        decimal Fee);
}
