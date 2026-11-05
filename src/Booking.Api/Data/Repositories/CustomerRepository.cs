using Booking.Api.Models.Customers;
using Dapper;
using System.Data;

namespace Booking.Api.Data.Repositories;

public class CustomerRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CustomerRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<List<CustomerResponse>> GetAllAsync()
    {
        const string sql = """
            SELECT Id, Name, Tier
            FROM dbo.Customers
            ORDER BY Id;
            """;

        await using var connection = _connectionFactory.Create();

        var customers = await connection.QueryAsync<CustomerResponse>(sql);

        return customers.AsList();
    }

    public async Task<CustomerResponse> CreateAsync(
        CreateCustomerRequest request)
    {
        const string sql = """
            INSERT INTO dbo.Customers (Name, Tier)
            OUTPUT INSERTED.Id, INSERTED.Name, INSERTED.Tier
            VALUES (@Name, @Tier);
            """;

        var parameters = new DynamicParameters();

        parameters.Add(
            "@Name",
            request.Name,
            DbType.String,
            size: 100);

        parameters.Add(
            "@Tier",
            request.Tier,
            DbType.AnsiString,
            size: 10);

        await using var connection = _connectionFactory.Create();

        return await connection.QuerySingleAsync<CustomerResponse>(
            sql,
            parameters);
    }
}
