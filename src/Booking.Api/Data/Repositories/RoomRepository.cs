using Booking.Api.Models.Rooms;
using Dapper;
using System.Data;

namespace Booking.Api.Data.Repositories;

public class RoomRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RoomRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<RoomResponse>> GetAllAsync()
    {
        const string sql = """
            SELECT Id, Name, Capacity, HourlyRate
            FROM dbo.Rooms
            ORDER BY Id;
            """;

        await using var connection = _connectionFactory.Create();

        var rooms = await connection.QueryAsync<RoomResponse>(sql);

        return rooms.AsList();
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

        var parameters = new DynamicParameters();

        parameters.Add(
            "@Name",
            request.Name,
            DbType.String,
            size: 100);

        parameters.Add(
            "@Capacity",
            request.Capacity,
            DbType.Int32);

        parameters.Add(
            "@HourlyRate",
            request.HourlyRate,
            DbType.Decimal,
            precision: 18,
            scale: 2);

        await using var connection = _connectionFactory.Create();

        return await connection.QuerySingleAsync<RoomResponse>(
            sql,
            parameters);
    }
}
