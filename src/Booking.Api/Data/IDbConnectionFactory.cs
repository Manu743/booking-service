using Microsoft.Data.SqlClient;

namespace Booking.Api.Data;

public interface IDbConnectionFactory
{
    SqlConnection Create();
}
