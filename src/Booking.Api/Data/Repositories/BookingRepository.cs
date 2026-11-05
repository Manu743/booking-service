
using Booking.Api.Models.Bookings;
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
    private readonly SqlConnection _connection;

    public BookingRepository(SqlConnection connection)
    {
        _connection = connection;
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

        await _connection.OpenAsync();

        try
        {
            await using var command = new SqlCommand(sql, _connection);

            command.Parameters.Add("@RoomId", SqlDbType.Int).Value = roomId;
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId;

            await using var reader = await command.ExecuteReaderAsync();

            await reader.ReadAsync();

            decimal? hourlyRate = reader.IsDBNull(0)
                ? null
                : reader.GetDecimal(0);

            string? customerTier = reader.IsDBNull(1)
                ? null
                : reader.GetString(1);

            return new BookingPricingData(hourlyRate, customerTier);
        }
        finally
        {
            await _connection.CloseAsync();
        }
    }

    // Creates a booking through the concurrency-safe stored procedure.
    public async Task<BookingCreationResult> CreateAsync(
        CreateBookingRequest request,
        decimal subtotal,
        decimal discount,
        decimal total)
    {
        await _connection.OpenAsync();

        try
        {
            await using var command = new SqlCommand(
                "dbo.usp_CreateBooking",
                _connection)
            {
                CommandType = CommandType.StoredProcedure
            };

            command.Parameters.Add("@RoomId", SqlDbType.Int).Value =
                request.RoomId;

            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value =
                request.CustomerId;

            var startParameter = command.Parameters.Add(
                "@StartAt", SqlDbType.DateTime2);
            startParameter.Scale = 0;
            startParameter.Value = request.StartAt;

            var endParameter = command.Parameters.Add(
                "@EndAt", SqlDbType.DateTime2);
            endParameter.Scale = 0;
            endParameter.Value = request.EndAt;

            AddDecimalParameter(command, "@Subtotal", subtotal);
            AddDecimalParameter(command, "@Discount", discount);
            AddDecimalParameter(command, "@Total", total);

            var bookingIdParameter = command.Parameters.Add(
                "@BookingId", SqlDbType.Int);
            bookingIdParameter.Direction = ParameterDirection.Output;

            var resultCodeParameter = command.Parameters.Add(
                "@ResultCode", SqlDbType.Int);
            resultCodeParameter.Direction = ParameterDirection.Output;

            await command.ExecuteNonQueryAsync();

            int resultCode = (int)resultCodeParameter.Value;

            int? bookingId = bookingIdParameter.Value == DBNull.Value
                ? null
                : (int)bookingIdParameter.Value;

            return new BookingCreationResult(bookingId, resultCode);
        }
        finally
        {
            await _connection.CloseAsync();
        }
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
            b.Fee,
            r.HourlyRate,
            c.Tier
        FROM dbo.Bookings b
        INNER JOIN dbo.Rooms r
            ON r.Id = b.RoomId
        INNER JOIN dbo.Customers c
            ON c.Id = b.CustomerId
        WHERE b.Id = @Id;
        """;

        await _connection.OpenAsync();

        try
        {
            await using var command = new SqlCommand(sql, _connection);

            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new BookingDetailsData(
                Id: reader.GetInt32(0),
                RoomId: reader.GetInt32(1),
                CustomerId: reader.GetInt32(2),
                StartAt: reader.GetDateTime(3),
                EndAt: reader.GetDateTime(4),
                Status: reader.GetString(5),
                Fee: reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
                HourlyRate: reader.GetDecimal(7),
                CustomerTier: reader.GetString(8)
            );
        }
        finally
        {
            await _connection.CloseAsync();
        }
    }

    private static void AddDecimalParameter(
        SqlCommand command,
        string name,
        decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);

        parameter.Precision = 18;
        parameter.Scale = 2;
        parameter.Value = value;
    }
}
