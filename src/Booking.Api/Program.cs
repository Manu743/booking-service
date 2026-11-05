using Booking.Api.Data;
using Booking.Api.Data.Repositories;
using Booking.Api.Models.Customers;
using Booking.Api.Models.Rooms;
using Booking.Domain.Services;
using DotNetEnv;
using Booking.Api.Models.Bookings;

var envPath = EnvFileFinder.Find(AppContext.BaseDirectory);

if (envPath is not null)
{
    Env.Load(envPath);
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

builder.Services.AddScoped<RoomRepository>();
builder.Services.AddScoped<CustomerRepository>();
builder.Services.AddScoped<BookingRepository>();


builder.Services.AddSingleton<BookingValidator>();
builder.Services.AddSingleton<PricingCalculator>();
builder.Services.AddSingleton<DiscountCalculator>();


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

// GET /bookings/{id}
app.MapGet("/bookings/{id:int}", async (
    int id,
    BookingRepository repository,
    PricingCalculator pricingCalculator) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new
        {
            error = "Booking ID must be greater than zero."
        });
    }

    // Retrieve persisted booking data.
    var booking = await repository.GetDetailsByIdAsync(id);

    if (booking is null)
    {
        return Results.Problem(
            title: "Booking not found",
            detail: $"No booking was found with ID {id}.",
            statusCode: StatusCodes.Status404NotFound
        );
    }

    // Reconstruct the pricing segments from the current room rate.
    var pricing = pricingCalculator.Calculate(
        booking.StartAt,
        booking.EndAt,
        booking.HourlyRate
    );

    var response = new BookingResponse
    {
        Id = booking.Id,
        RoomId = booking.RoomId,
        CustomerId = booking.CustomerId,
        StartAt = booking.StartAt,
        EndAt = booking.EndAt,
        Status = booking.Status,
        Segments = pricing.Segments,
        Subtotal = booking.Subtotal,
        Discount = booking.Discount,
        Total = booking.Total,
        Fee = booking.Fee
    };

    return Results.Ok(response);
});

// Bookings
// POST /bookings
app.MapPost("/bookings", async (
    CreateBookingRequest request,
    BookingValidator validator,
    PricingCalculator pricingCalculator,
    DiscountCalculator discountCalculator,
    BookingRepository repository) =>
{
    // Validate local date/time values.
    if (request.StartAt.Kind != DateTimeKind.Unspecified ||
        request.EndAt.Kind != DateTimeKind.Unspecified)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["dates"] = new[]
                {
                    "StartAt and EndAt must be local date/time values without timezone information."
                }
            });
    }

    if (request.RoomId <= 0 || request.CustomerId <= 0)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["ids"] = new[]
                {
                    "RoomId and CustomerId must be greater than zero."
                }
            });
    }

    // R1-R3: Validate booking rules before accessing the database.
    var validationErrors = validator.Validate(
        request.StartAt,
        request.EndAt
    );

    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["booking"] = validationErrors.ToArray()
            });
    }

    // Retrieve room rate and customer tier.
    var pricingData = await repository.GetPricingDataAsync(
        request.RoomId,
        request.CustomerId
    );

    if (pricingData.HourlyRate is null ||
        pricingData.CustomerTier is null)
    {
        return Results.Problem(
            title: "Room or customer not found",
            detail: "The specified room or customer does not exist.",
            statusCode: StatusCodes.Status404NotFound
        );
    }

    // Determine the customer discount percentage.
    var discountPercentage = pricingData.CustomerTier switch
    {
        "Silver" => 0.10m,
        "Gold" => 0.20m,
        "None" => 0m,
        _ => throw new InvalidOperationException(
            "Unsupported customer tier.")
    };

    // R5: Calculate pricing segments and subtotal.
    var pricing = pricingCalculator.Calculate(
        request.StartAt,
        request.EndAt,
        pricingData.HourlyRate.Value
    );

    // R6: Apply customer discount and calculate final total.
    var discount = discountCalculator.Calculate(
        pricing.Subtotal,
        discountPercentage
    );

    // R8: Create booking using the concurrency-safe SQL procedure.
    var creationResult = await repository.CreateAsync(
        request,
        pricing.Subtotal,
        discount.DiscountAmount,
        discount.Total
    );

    if (creationResult.ResultCode == 1)
    {
        return Results.Problem(
            title: "Booking conflict",
            detail: "The room is not available for the requested time.",
            statusCode: StatusCodes.Status409Conflict
        );
    }

    if (creationResult.ResultCode != 0 ||
        creationResult.BookingId is null)
    {
        throw new InvalidOperationException(
            "The booking could not be created.");
    }

    // Build the response using the original calculated values.
    var response = new BookingResponse
    {
        Id = creationResult.BookingId.Value,
        RoomId = request.RoomId,
        CustomerId = request.CustomerId,
        StartAt = request.StartAt,
        EndAt = request.EndAt,
        Status = "A",
        Segments = pricing.Segments,
        Subtotal = pricing.Subtotal,
        Discount = discount.DiscountAmount,
        Total = discount.Total,
        Fee = 0m
    };

    return Results.Created(
        $"/bookings/{response.Id}",
        response
    );
});

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
            statusCode: StatusCodes.Status503ServiceUnavailable
        );
    }
});



// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

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
