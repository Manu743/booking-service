using Booking.Api.Models.Bookings;
using Dapper;
using Microsoft.Data.SqlClient;
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
    private const int DeadlockVictimErrorNumber = 1205;

    // Delays between deadlock retries. The array length also caps the number
    // of retries: three attempts in total.
    private static readonly TimeSpan[] DeadlockRetryDelays =
    [
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(50)
    ];

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
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ExecuteCreateAsync(
                    request,
                    subtotal,
                    discount,
                    total);
            }
            catch (SqlException exception)
                when (exception.Number == DeadlockVictimErrorNumber &&
                      attempt < DeadlockRetryDelays.Length)
            {
                // SQL Server rolled this transaction back when choosing it as
                // the deadlock victim, so no booking was persisted and a retry
                // is safe. Timeouts and other ambiguous failures are not
                // retried because the insert may have completed.
                await Task.Delay(DeadlockRetryDelays[attempt]);
            }
        }
    }

    private async Task<BookingCreationResult> ExecuteCreateAsync(
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

    // Cancels an active booking. The conditional update makes the transition
    // atomic: only one concurrent request can change an active booking.
    public async Task<bool> TryCancelAsync(
        int id,
        decimal fee,
        DateTime cancelledAt)
    {
        const string sql = """
            UPDATE dbo.Bookings
            SET Status = 'C',
                Fee = @Fee,
                CancelledAt = @CancelledAt
            WHERE Id = @Id
              AND Status = 'A';
            """;

        var parameters = new DynamicParameters();

        parameters.Add(
            "@Id",
            id,
            DbType.Int32);

        parameters.Add(
            "@Fee",
            fee,
            DbType.Decimal,
            precision: 18,
            scale: 2);

        parameters.Add(
            "@CancelledAt",
            cancelledAt,
            DbType.DateTime2,
            scale: 0);

        await using var connection = _connectionFactory.Create();

        var affected = await connection.ExecuteAsync(sql, parameters);

        return affected == 1;
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
            b.Subtotal,
            b.Discount,
            b.Total,
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
