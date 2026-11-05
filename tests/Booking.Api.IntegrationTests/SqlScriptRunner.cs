using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Booking.Api.IntegrationTests;

internal static class SqlScriptRunner
{
    public static async Task ExecuteAsync(
        string connectionString,
        string scriptPath)
    {
        var script = await File.ReadAllTextAsync(scriptPath);

        var batches = Regex.Split(
            script,
            @"^\s*GO\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await using var command = new SqlCommand(batch, connection)
            {
                CommandTimeout = 120
            };

            await command.ExecuteNonQueryAsync();
        }
    }
}
