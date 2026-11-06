using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Api.IntegrationTests;

public sealed class BookingApiFactory(string connectionString, string environment = "Testing")
    : WebApplicationFactory<Program>
{
    public MutableTimeProvider Clock { get; } = new(
        new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:BookingDatabase", connectionString);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:BookingDatabase"] = connectionString
                });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
