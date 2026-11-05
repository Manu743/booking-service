using Booking.Api.Data.Repositories;
using Booking.Api.Models.Rooms;
using Booking.Api.Models.Customers;
using DotNetEnv;
using Microsoft.Data.SqlClient;

var envPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "../../../../../.env")
);

if (!File.Exists(envPath))
{
    throw new FileNotFoundException(
        "The .env file was not found.",
        envPath
    );
}

Env.Load(envPath);
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddScoped<SqlConnection>(serviceProvicer =>
{
    var configuration = serviceProvicer.GetRequiredService<IConfiguration>();

    var connectionString = configuration.GetConnectionString("BookingDatabase")
    ?? throw new InvalidOperationException(
        "Database connection string not configured");

    return new SqlConnection(connectionString);

});

builder.Services.AddScoped<RoomRepository>();
builder.Services.AddScoped<CustomerRepository>();

var app = builder.Build();


//Rooms
// GET /rooms
app.MapGet("/rooms", async (RoomRepository repository) =>
{
    var rooms = await repository.GetAllAsync();

    return Results.Ok(rooms);
});

// POST /rooms
app.MapPost("/rooms", async (
    CreateRoomRequest request,
    RoomRepository repository) =>
{

    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new
        {
            error = "Room name is required."
        });
    }

    if (request.Name.Length > 100)
    {
        return Results.BadRequest(new
        {
            error = "Room name cannot exceed 100 characters."
        });
    }

    if (request.Capacity <= 0)
    {
        return Results.BadRequest(new
        {
            error = "Room capacity must be greater than zero."
        });
    }

    if (request.HourlyRate <= 0)
    {
        return Results.BadRequest(new
        {
            error = "Hourly rate must be greater than zero."
        });
    }

    var room = await repository.CreateAsync(request);

    return Results.Created($"/rooms/{room.Id}", room);
});

//Customers
// GET /customers
app.MapGet("/customers", async (CustomerRepository repository) =>
{
    var customers = await repository.GetAllAsync();

    return Results.Ok(customers);
});

// POST /customers
app.MapPost("/customers", async (
    CreateCustomerRequest request,
    CustomerRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new
        {
            error = "Customer name is required."
        });
    }

    if (request.Name.Length > 100)
    {
        return Results.BadRequest(new
        {
            error = "Customer name cannot exceed 100 characters."
        });
    }

    var allowedTiers = new[] { "None", "Silver", "Gold" };

    if (!allowedTiers.Contains(request.Tier))
    {
        return Results.BadRequest(new
        {
            error = "Invalid customer tier."
        });
    }

    var customer = await repository.CreateAsync(request);

    return Results.Created(
        $"/customers/{customer.Id}",
        customer
    );
});

app.MapGet("/health/database", async (SqlConnection connection) =>
{
    try
    {
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
            statusCode: StatusCodes.Status503ServiceUnavailable
        );
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
