using System.Text.Json;

namespace Booking.Api.IntegrationTests;

internal static class TestJson
{
    public static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web);
}
