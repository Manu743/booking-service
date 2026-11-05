using Booking.Api.Models.Rooms;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Booking.Api.Data.Repositories;

public class RoomRepository
{
    private readonly SqlConnection _connection;

    public RoomRepository(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task<List<RoomResponse>> GetAllAsync()
    {
        var rooms = new List<RoomResponse>();

        const string sql = """
            SELECT Id, Name, Capacity, HourlyRate
            FROM dbo.Rooms
            ORDER BY Id;
            """;

        await _connection.OpenAsync();

        using var command = new SqlCommand(sql, _connection);

        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rooms.Add(new RoomResponse
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Capacity = reader.GetInt32(2),
                HourlyRate = reader.GetDecimal(3)
            });
        }

        await reader.CloseAsync();
        await _connection.CloseAsync();

        return rooms;
    }

    public async Task<RoomResponse> CreateAsync(CreateRoomRequest request)
    {
        const string sql = """
            INSERT INTO dbo.Rooms (Name, Capacity, HourlyRate)
            OUTPUT INSERTED.Id,
                   INSERTED.Name,
                   INSERTED.Capacity,
                   INSERTED.HourlyRate
            VALUES (@Name, @Capacity, @HourlyRate);
            """;

        await _connection.OpenAsync();

        using var command = new SqlCommand(sql, _connection);

        command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value =
            request.Name;

        command.Parameters.Add("@Capacity", SqlDbType.Int).Value =
            request.Capacity;

        var hourlyRateParameter = command.Parameters.Add(
            "@HourlyRate",
            SqlDbType.Decimal
        );

        hourlyRateParameter.Precision = 18;
        hourlyRateParameter.Scale = 2;
        hourlyRateParameter.Value = request.HourlyRate;

        using var reader = await command.ExecuteReaderAsync();

        await reader.ReadAsync();

        var room = new RoomResponse
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Capacity = reader.GetInt32(2),
            HourlyRate = reader.GetDecimal(3)
        };

        await reader.CloseAsync();
        await _connection.CloseAsync();

        return room;
    }
}