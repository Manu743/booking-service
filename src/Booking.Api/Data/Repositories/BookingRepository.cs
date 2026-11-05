using Booking.Api.Models.Bookings;
using Dapper;
using System.Data;

namespace Booking.Api.Data.Repositories;

public record BookingPricingData(
    decimal? HourlyRate,
    string? CustomerTier);

public record BookingCreationResult(
    int? BookingId,
    int ResultCode);

public class BookingRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public BookingRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // Retrieves the room rate and customer membership tier.
    public async Task<BookingPricingData> GetPricingDataAsync(
        int roomId,
        int customerId)
    {
        const string sql = """
            SELECT
                (SELECT HourlyRate
                 FROM dbo.Rooms
                 WHERE Id = @RoomId) AS HourlyRate,

                (SELECT Tier
                 FROM dbo.Customers
                 WHERE Id = @CustomerId) AS CustomerTier;
            """;

        await using var connection = _connectionFactory.Create();

        return await connection.QuerySingleAsync<BookingPricingData>(
            sql,
            new
            {
                RoomId = roomId,
                CustomerId = customerId
            });
    }

    // Creates a booking through the concurrency-safe stored procedure.
    public async Task<BookingCreationResult> CreateAsync(
        CreateBookingRequest request,
        decimal subtotal,
        decimal discount,
        decimal total)
    {
        var parameters = new DynamicParameters();

        parameters.Add(
            "@RoomId",
            request.RoomId,
            DbType.Int32);

        parameters.Add(
            "@CustomerId",
            request.CustomerId,
            DbType.Int32);

        parameters.Add(
            "@StartAt",
            request.StartAt,
            DbType.DateTime2,
            scale: 0);

        parameters.Add(
            "@EndAt",
            request.EndAt,
            DbType.DateTime2,
            scale: 0);

        parameters.Add(
            "@Subtotal",
            subtotal,
            DbType.Decimal,
            precision: 18,
            scale: 2);

        parameters.Add(
            "@Discount",
            discount,
            DbType.Decimal,
            precision: 18,
            scale: 2);

        parameters.Add(
            "@Total",
            total,
            DbType.Decimal,
            precision: 18,
            scale: 2);

        parameters.Add(
            "@BookingId",
            dbType: DbType.Int32,
            direction: ParameterDirection.Output);

        parameters.Add(
            "@ResultCode",
            dbType: DbType.Int32,
            direction: ParameterDirection.Output);

        await using var connection = _connectionFactory.Create();

        await connection.ExecuteAsync(
            "dbo.usp_CreateBooking",
            parameters,
            commandType: CommandType.StoredProcedure);

        return new BookingCreationResult(
            parameters.Get<int?>("@BookingId"),
            parameters.Get<int>("@ResultCode"));
    }

    // Retrieves a persisted booking by its ID.
    // Retrieves booking details together with the room rate and customer tier.
    public async Task<BookingDetailsData?> GetDetailsByIdAsync(int id)
    {
        const string sql = """
        SELECT
            b.Id,
            b.RoomId,
            b.CustomerId,
            b.StartAt,
            b.EndAt,
            b.Status,
            ISNULL(b.Fee, 0) AS Fee,
            r.HourlyRate,
            c.Tier AS CustomerTier
        FROM dbo.Bookings b
        INNER JOIN dbo.Rooms r
            ON r.Id = b.RoomId
        INNER JOIN dbo.Customers c
            ON c.Id = b.CustomerId
        WHERE b.Id = @Id;
        """;

        await using var connection = _connectionFactory.Create();

        return await connection.QuerySingleOrDefaultAsync<BookingDetailsData>(
            sql,
            new { Id = id });
    }
}
