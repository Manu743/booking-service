using Booking.Api.Data;
using Booking.Api.Data.Repositories;
using Booking.Domain.Services;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);

// Explicit configuration (including container/test settings) must not read a local .env.
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("BookingDatabase")))
{
    var envPath = EnvFileFinder.Find(AppContext.BaseDirectory);
    if (envPath is not null)
    {
        Env.NoClobber().Load(envPath);
        builder.Configuration.AddEnvironmentVariables();
    }
}

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

builder.Services.AddScoped<RoomRepository>();
builder.Services.AddScoped<CustomerRepository>();
builder.Services.AddScoped<BookingRepository>();

builder.Services.AddSingleton<BookingValidator>();
builder.Services.AddSingleton<PricingCalculator>();
builder.Services.AddSingleton<DiscountCalculator>();
builder.Services.AddSingleton<CancellationPolicy>();
builder.Services.AddSingleton<RoomAvailabilityCalculator>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

var app = builder.Build();

app.MapControllers();

app.MapGet("/health/database", async (IDbConnectionFactory connectionFactory) =>
{
    try
    {
        await using var connection = connectionFactory.Create();

        await connection.OpenAsync();

        return Results.Ok(new
        {
            status = "Connected",
            database = connection.Database
        });
    }
    catch
    {
        return Results.Problem(
            title: "Database connection failed",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "Booking API v1"));
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.Run();

static class EnvFileFinder
{
    public static string? Find(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".env");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

public partial class Program { }
