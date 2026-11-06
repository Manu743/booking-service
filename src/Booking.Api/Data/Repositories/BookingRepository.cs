using Booking.Api.Models.Bookings;
using Booking.Domain.Models;
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

public enum IdempotentBookingOutcome
{
    Created,
    Replayed,
    Conflict,
    RequestMismatch
}

public record IdempotentBookingResult(
    IdempotentBookingOutcome Outcome,
    int? BookingId,
    int? StatusCode,
    string? ResponseJson);

internal sealed class StoredIdempotencyEntry
{
    public string RequestHash { get; set; } = string.Empty;

    public string? ResponseJson { get; set; }

    public int? StatusCode { get; set; }

    public DateTime CreatedAt { get; set; }
}

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
                await using var connection = _connectionFactory.Create();

                await connection.OpenAsync();

                return await ExecuteCreateAsync(
                    connection,
                    transaction: null,
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

    // Creates a booking and stores the response under the idempotency key in
    // one transaction. Concurrent requests with the same key serialize on the
    // key row, and a deadlock restarts the entire transaction.
    public async Task<IdempotentBookingResult> CreateIdempotentAsync(
        string idempotencyKey,
        string requestHash,
        DateTime now,
        CreateBookingRequest request,
        decimal subtotal,
        decimal discount,
        decimal total,
        Func<int, string> buildResponseJson)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ExecuteCreateIdempotentAsync(
                    idempotencyKey,
                    requestHash,
                    now,
                    request,
                    subtotal,
                    discount,
                    total,
                    buildResponseJson);
            }
            catch (SqlException exception)
                when (exception.Number == DeadlockVictimErrorNumber &&
                      attempt < DeadlockRetryDelays.Length)
            {
                await Task.Delay(DeadlockRetryDelays[attempt]);
            }
        }
    }

    private async Task<IdempotentBookingResult> ExecuteCreateIdempotentAsync(
        string idempotencyKey,
        string requestHash,
        DateTime now,
        CreateBookingRequest request,
        decimal subtotal,
        decimal discount,
        decimal total,
        Func<int, string> buildResponseJson)
    {
        const string selectSql = """
            SELECT RequestHash, ResponseJson, StatusCode, CreatedAt
            FROM dbo.IdempotencyKeys WITH (UPDLOCK, HOLDLOCK)
            WHERE [Key] = @Key;
            """;

        const string deleteSql = """
            DELETE FROM dbo.IdempotencyKeys
            WHERE [Key] = @Key;
            """;

        const string insertSql = """
            INSERT INTO dbo.IdempotencyKeys
                ([Key], RequestHash, ResponseJson, StatusCode, CreatedAt)
            VALUES
                (@Key, @RequestHash, @ResponseJson, @StatusCode, @CreatedAt);
            """;

        await using var connection = _connectionFactory.Create();

        await connection.OpenAsync();

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync();

        var stored =
            await connection.QuerySingleOrDefaultAsync<StoredIdempotencyEntry>(
                selectSql,
                new { Key = idempotencyKey },
                transaction);

        if (stored is not null)
        {
            var isFresh = stored.CreatedAt >= now.AddHours(-24);

            if (isFresh &&
                stored.ResponseJson is not null &&
                stored.StatusCode is not null)
            {
                if (!string.Equals(
                        stored.RequestHash,
                        requestHash,
                        StringComparison.Ordinal))
                {
                    await transaction.RollbackAsync();

                    return new IdempotentBookingResult(
                        IdempotentBookingOutcome.RequestMismatch,
                        BookingId: null,
                        StatusCode: null,
                        ResponseJson: null);
                }

                await transaction.RollbackAsync();

                return new IdempotentBookingResult(
                    IdempotentBookingOutcome.Replayed,
                    BookingId: null,
                    stored.StatusCode,
                    stored.ResponseJson);
            }

            // Expired keys and incomplete rows are replaced by this request.
            await connection.ExecuteAsync(
                deleteSql,
                new { Key = idempotencyKey },
                transaction);
        }

        var creationResult = await ExecuteCreateAsync(
            connection,
            transaction,
            request,
            subtotal,
            discount,
            total);

        if (creationResult.ResultCode != 0 ||
            creationResult.BookingId is null)
        {
            await transaction.RollbackAsync();

            return new IdempotentBookingResult(
                IdempotentBookingOutcome.Conflict,
                BookingId: null,
                StatusCode: null,
                ResponseJson: null);
        }

        var responseJson = buildResponseJson(creationResult.BookingId.Value);

        await connection.ExecuteAsync(
            insertSql,
            new
            {
                Key = idempotencyKey,
                RequestHash = requestHash,
                ResponseJson = responseJson,
                StatusCode = 201,
                CreatedAt = now
            },
            transaction);

        await transaction.CommitAsync();

        return new IdempotentBookingResult(
            IdempotentBookingOutcome.Created,
            creationResult.BookingId,
            StatusCode: 201,
            ResponseJson: responseJson);
    }

    private static async Task<BookingCreationResult> ExecuteCreateAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CreateBookingRequest request,
        decimal subtotal,
        decimal discount,
        decimal total)
    {
        var parameters = BuildCreateParameters(
            request,
            subtotal,
            discount,
            total);

        await connection.ExecuteAsync(
            "dbo.usp_CreateBooking",
            parameters,
            transaction,
            commandType: CommandType.StoredProcedure);

        return new BookingCreationResult(
            parameters.Get<int?>("@BookingId"),
            parameters.Get<int>("@ResultCode"));
    }

    private static DynamicParameters BuildCreateParameters(
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

        return parameters;
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

    // Active bookings for a room that overlap the requested day. Cancelled
    // bookings never block availability.
    public async Task<IReadOnlyList<BookingInterval>> GetActiveBookingsAsync(
        int roomId,
        DateTime dayStart,
        DateTime dayEnd)
    {
        const string sql = """
            SELECT StartAt AS [Start], EndAt AS [End]
            FROM dbo.Bookings
            WHERE RoomId = @RoomId
              AND Status = 'A'
              AND StartAt < @DayEnd
              AND EndAt > @DayStart;
            """;

        await using var connection = _connectionFactory.Create();

        var bookings = await connection.QueryAsync<BookingInterval>(
            sql,
            new
            {
                RoomId = roomId,
                DayStart = dayStart,
                DayEnd = dayEnd
            });

        return bookings.AsList();
    }

    // Retrieves a persisted booking by its ID.
    // Retrieves booking details together with the room rate and customer tier.
    public async Task<BookingDetailsData?> GetDetailsByIdAsync(int id)    {
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
