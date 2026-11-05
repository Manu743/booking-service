using Microsoft.Data.SqlClient;

namespace Booking.Api.Data;

public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        var value = configuration.GetConnectionString("BookingDatabase");

        _connectionString = string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                "Database connection string 'BookingDatabase' is not configured.")
            : value;
    }

    public SqlConnection Create() => new(_connectionString);
}
