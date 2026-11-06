using Microsoft.Data.SqlClient;
using Xunit;

namespace Booking.Api.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class SqlReportTests(DatabaseFixture fixture)
    : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task RoomUtilisation_ReturnsGoldenRows()
    {
        var rows = await QueryUtilisationAsync(
            1,
            Local(2026, 3, 2),
            Local(2026, 3, 8));

        Assert.Equal(7, rows.Count);

        AssertUtilisation(rows[0], new DateOnly(2026, 3, 2), 900, 360, 40.00m);
        AssertUtilisation(rows[1], new DateOnly(2026, 3, 3), 900, 0, 0.00m);
        AssertUtilisation(rows[2], new DateOnly(2026, 3, 4), 900, 0, 0.00m);
        AssertUtilisation(rows[3], new DateOnly(2026, 3, 5), 900, 0, 0.00m);
        AssertUtilisation(rows[4], new DateOnly(2026, 3, 6), 900, 0, 0.00m);
        AssertUtilisation(rows[5], new DateOnly(2026, 3, 7), 600, 0, 0.00m);
        AssertUtilisation(rows[6], new DateOnly(2026, 3, 8), 0, 0, null);
    }

    [Fact]
    public async Task RoomUtilisation_ExcludesCancelledBookings()
    {
        // Room 2 has a cancelled booking 09:00-12:00 (180 minutes) and an
        // active booking 10:00-11:00 (60 minutes). Only the active one counts.
        var rows = await QueryUtilisationAsync(
            2,
            Local(2026, 3, 2),
            Local(2026, 3, 2));

        var row = Assert.Single(rows);

        AssertUtilisation(row, new DateOnly(2026, 3, 2), 900, 60, 6.67m);
    }

    [Fact]
    public async Task RoomUtilisation_WithNoBookings_ReturnsZero()
    {
        var rows = await QueryUtilisationAsync(
            1,
            Local(2026, 3, 3),
            Local(2026, 3, 3));

        var row = Assert.Single(rows);

        AssertUtilisation(row, new DateOnly(2026, 3, 3), 900, 0, 0.00m);
    }

    [Fact]
    public async Task TopCustomers_ReturnsGoldenRows()
    {
        var rows = await QueryTopCustomersAsync();

        Assert.Equal(7, rows.Count);

        AssertCustomer(rows[0], "2026-03", "C2", 271.25m, 1);
        AssertCustomer(rows[1], "2026-03", "C1", 156.25m, 2);
        AssertCustomer(rows[2], "2026-03", "C3", 135.00m, 3);
        AssertCustomer(rows[3], "2026-03", "C4", 135.00m, 3);
        AssertCustomer(rows[4], "2026-04", "C4", 180.00m, 1);
        AssertCustomer(rows[5], "2026-04", "C1", 37.50m, 2);
        AssertCustomer(rows[6], "2026-04", "C3", 25.00m, 3);
    }

    [Fact]
    public async Task FreeWindows_ReturnsGoldenRows()
    {
        var rows = await QueryFreeWindowsAsync(
            1,
            Local(2026, 3, 2),
            60);

        Assert.Equal(2, rows.Count);

        AssertWindow(rows[0], 10, 15, 11, 15);
        AssertWindow(rows[1], 15, 45, 22, 0);
    }

    [Fact]
    public async Task FreeWindows_MergesOverlappingBookings()
    {
        var rows = await QueryFreeWindowsAsync(
            3,
            Local(2026, 3, 7),
            60);

        Assert.Equal(2, rows.Count);

        AssertWindow(rows[0], 8, 0, 9, 45);
        AssertWindow(rows[1], 12, 45, 18, 0);
    }

    [Fact]
    public async Task FreeWindows_IgnoresCancelledBookings()
    {
        var rows = await QueryFreeWindowsAsync(
            2,
            Local(2026, 3, 2),
            60);

        Assert.Equal(2, rows.Count);

        AssertWindow(rows[0], 7, 0, 9, 45);
        AssertWindow(rows[1], 11, 15, 22, 0);
    }

    [Fact]
    public async Task FreeWindows_OnSunday_ReturnsEmpty()
    {
        var rows = await QueryFreeWindowsAsync(
            1,
            Local(2026, 3, 8),
            60);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task FreeWindows_WithNoBookings_ReturnsWholeOpeningWindow()
    {
        var rows = await QueryFreeWindowsAsync(
            3,
            Local(2026, 12, 7),
            60);

        var row = Assert.Single(rows);

        AssertWindow(row, 7, 0, 22, 0);
    }

    [Fact]
    public async Task FreeWindows_FiltersByMinMinutes()
    {
        var atBoundary = await QueryFreeWindowsAsync(
            1,
            Local(2026, 3, 2),
            60);

        var aboveBoundary = await QueryFreeWindowsAsync(
            1,
            Local(2026, 3, 2),
            61);

        Assert.Equal(2, atBoundary.Count);

        var row = Assert.Single(aboveBoundary);

        AssertWindow(row, 15, 45, 22, 0);
    }

    [Fact]
    public async Task FreeWindows_EndsAtClosingTime()
    {
        await ExecuteAsync(
            """
            INSERT INTO dbo.Bookings
            (
                RoomId, CustomerId, StartAt, EndAt, Status,
                Subtotal, Discount, Total, Fee, CancelledAt
            )
            VALUES
            (
                3, 1, '2026-12-07T20:00:00', '2026-12-07T22:00:00', 'A',
                180.00, 0.00, 180.00, NULL, NULL
            );
            """);

        var rows = await QueryFreeWindowsAsync(
            3,
            Local(2026, 12, 7),
            60);

        var row = Assert.Single(rows);

        AssertWindow(row, 7, 0, 19, 45);
    }

    [Fact]
    public async Task ConflictingBookings_ReturnsGoldenPairs()
    {
        var rows = await QueryConflictsAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal(new ConflictRow(2, 3), rows[0]);
        Assert.Equal(new ConflictRow(6, 7), rows[1]);
        Assert.Equal(new ConflictRow(9, 10), rows[2]);
    }

    private async Task<List<UtilisationRow>> QueryUtilisationAsync(
        int roomId,
        DateTime fromDate,
        DateTime toDate)
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            EXEC dbo.usp_RoomUtilisation
                @RoomId = @RoomId,
                @FromDate = @FromDate,
                @ToDate = @ToDate;
            """,
            connection);

        command.Parameters.AddWithValue("@RoomId", roomId);
        command.Parameters.AddWithValue("@FromDate", fromDate);
        command.Parameters.AddWithValue("@ToDate", toDate);

        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<UtilisationRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new UtilisationRow(
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3)));
        }

        return rows;
    }

    private async Task<List<TopCustomerRow>> QueryTopCustomersAsync()
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command =
            new SqlCommand("EXEC dbo.usp_TopCustomersByMonth;", connection);

        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<TopCustomerRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new TopCustomerRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                Convert.ToInt32(reader.GetInt64(3))));
        }

        return rows;
    }

    private async Task<List<FreeWindowRow>> QueryFreeWindowsAsync(
        int roomId,
        DateTime date,
        int minMinutes)
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(
            """
            EXEC dbo.usp_FreeWindows
                @RoomId = @RoomId,
                @Date = @Date,
                @MinMinutes = @MinMinutes;
            """,
            connection);

        command.Parameters.AddWithValue("@RoomId", roomId);
        command.Parameters.AddWithValue("@Date", date);
        command.Parameters.AddWithValue("@MinMinutes", minMinutes);

        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<FreeWindowRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new FreeWindowRow(
                reader.GetTimeSpan(0),
                reader.GetTimeSpan(1)));
        }

        return rows;
    }

    private async Task<List<ConflictRow>> QueryConflictsAsync()
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command =
            new SqlCommand("EXEC dbo.usp_ConflictingBookings;", connection);

        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<ConflictRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new ConflictRow(
                reader.GetInt32(0),
                reader.GetInt32(1)));
        }

        return rows;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection =
            new SqlConnection(Fixture.ConnectionString);

        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync();
    }

    private static void AssertUtilisation(
        UtilisationRow row,
        DateOnly date,
        int openMinutes,
        int bookedMinutes,
        decimal? utilisation)
    {
        Assert.Equal(date, row.Date);
        Assert.Equal(openMinutes, row.OpenMinutes);
        Assert.Equal(bookedMinutes, row.BookedMinutes);
        Assert.Equal(utilisation, row.UtilisationPct);
    }

    private static void AssertCustomer(
        TopCustomerRow row,
        string month,
        string customer,
        decimal revenue,
        int rank)
    {
        Assert.Equal(month, row.Month);
        Assert.Equal(customer, row.Customer);
        Assert.Equal(revenue, row.Revenue);
        Assert.Equal(rank, row.Rank);
    }

    private static void AssertWindow(
        FreeWindowRow row,
        int fromHour,
        int fromMinute,
        int toHour,
        int toMinute)
    {
        Assert.Equal(
            new TimeSpan(fromHour, fromMinute, 0),
            row.From);

        Assert.Equal(
            new TimeSpan(toHour, toMinute, 0),
            row.To);
    }

    private static DateTime Local(
        int year,
        int month,
        int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);

    private sealed record UtilisationRow(
        DateOnly Date,
        int OpenMinutes,
        int BookedMinutes,
        decimal? UtilisationPct);

    private sealed record TopCustomerRow(
        string Month,
        string Customer,
        decimal Revenue,
        int Rank);

    private sealed record FreeWindowRow(
        TimeSpan From,
        TimeSpan To);

    private sealed record ConflictRow(
        int LowerId,
        int HigherId);
}
