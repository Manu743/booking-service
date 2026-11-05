using Booking.Api.Models.Customers;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Booking.Api.Data.Repositories;

public class CustomerRepository
{
    private readonly SqlConnection _connection;

    public CustomerRepository(SqlConnection connection)
    {
        _connection = connection;
    }

    public async Task<List<CustomerResponse>> GetAllAsync()
    {
        var customers = new List<CustomerResponse>();

        const string sql = """
            SELECT Id, Name, Tier
            FROM dbo.Customers
            ORDER BY Id;
            """;

        await _connection.OpenAsync();

        using var command = new SqlCommand(sql, _connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            customers.Add(new CustomerResponse
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Tier = reader.GetString(2)
            });
        }

        await reader.CloseAsync();
        await _connection.CloseAsync();

        return customers;
    }

    public async Task<CustomerResponse> CreateAsync(
        CreateCustomerRequest request)
    {
        const string sql = """
            INSERT INTO dbo.Customers (Name, Tier)
            OUTPUT INSERTED.Id, INSERTED.Name, INSERTED.Tier
            VALUES (@Name, @Tier);
            """;

        await _connection.OpenAsync();

        using var command = new SqlCommand(sql, _connection);

        command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value =
            request.Name;

        command.Parameters.Add("@Tier", SqlDbType.VarChar, 10).Value =
            request.Tier;

        using var reader = await command.ExecuteReaderAsync();

        await reader.ReadAsync();

        var customer = new CustomerResponse
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Tier = reader.GetString(2)
        };

        await reader.CloseAsync();
        await _connection.CloseAsync();

        return customer;
    }
}