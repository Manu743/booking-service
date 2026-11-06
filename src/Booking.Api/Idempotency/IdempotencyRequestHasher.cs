using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Booking.Api.Models.Bookings;

namespace Booking.Api.Idempotency;

// Hashes the parsed booking request, never the raw JSON body. Property order,
// whitespace, or number formatting must not change the resulting hash.
public static class IdempotencyRequestHasher
{
    public static string Compute(CreateBookingRequest request)
    {
        var canonical = string.Join(
            '|',
            request.RoomId.ToString(CultureInfo.InvariantCulture),
            request.CustomerId.ToString(CultureInfo.InvariantCulture),
            request.StartAt.ToString("O", CultureInfo.InvariantCulture),
            request.EndAt.ToString("O", CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical));

        return Convert.ToHexStringLower(hash);
    }
}
