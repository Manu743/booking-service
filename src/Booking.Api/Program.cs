using Booking.Api.Data;
using Booking.Api.Data.Repositories;
using Booking.Api.Idempotency;
using Booking.Api.Models.Customers;
using Booking.Api.Models.Rooms;
using Booking.Domain.Models;
using Booking.Domain.Services;
using DotNetEnv;
using Booking.Api.Models.Bookings;
using System.Globalization;
using System.Text;
using System.Text.Json;

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
builder.Services.AddSingleton<CancellationPolicy>();
builder.Services.AddSingleton<RoomAvailabilityCalculator>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);


var app = builder.Build();


//Rooms
// GET /rooms
app.MapGet("/rooms", async (RoomRepository repository) =>
{
    var rooms = await repository.GetAllAsync();

    return Results.Ok(rooms);
});

// GET /rooms/{id}/availability?date=2026-03-02&minMinutes=60
app.MapGet("/rooms/{id:int}/availability", async (
    int id,
    string? date,
    int? minMinutes,
    RoomRepository roomRepository,
    BookingRepository bookingRepository,
    RoomAvailabilityCalculator availabilityCalculator) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new
        {
            error = "Room ID must be greater than zero."
        });
    }

    if (!DateOnly.TryParseExact(
            date,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedDate))
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["date"] = new[]
                {
                    "Date must use the yyyy-MM-dd format."
                }
            });
    }

    if (minMinutes is null or <= 0)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["minMinutes"] = new[]
                {
                    "minMinutes must be greater than zero."
                }
            });
    }

    if (!await roomRepository.ExistsAsync(id))
    {
        return Results.Problem(
            title: "Room not found",
            detail: $"No room was found with ID {id}.",
            statusCode: StatusCodes.Status404NotFound);
    }

    var dayStart = parsedDate.ToDateTime(TimeOnly.MinValue);
    var dayEnd = dayStart.AddDays(1);

    var bookings = await bookingRepository.GetActiveBookingsAsync(
        id,
        dayStart,
        dayEnd);

    var windows = availabilityCalculator.GetFreeWindows(
        dayStart,
        bookings,
        minMinutes.Value);

    return Results.Ok(
        windows.Select(window => new AvailabilityWindow(
            window.Start,
            window.End)));
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

    return Results.Ok(
        BuildBookingResponse(
            booking,
            pricingCalculator,
            booking.Status,
            booking.Fee
        )
    );
});

// POST /bookings/{id}/cancel
app.MapPost("/bookings/{id:int}/cancel", async (
    int id,
    BookingRepository repository,
    CancellationPolicy cancellationPolicy,
    PricingCalculator pricingCalculator,
    TimeProvider timeProvider) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new
        {
            error = "Booking ID must be greater than zero."
        });
    }

    var booking = await repository.GetDetailsByIdAsync(id);

    if (booking is null)
    {
        return Results.Problem(
            title: "Booking not found",
            detail: $"No booking was found with ID {id}.",
            statusCode: StatusCodes.Status404NotFound
        );
    }

    if (booking.Status == "C")
    {
        return Results.Problem(
            title: "Booking already cancelled",
            detail: "The booking has already been cancelled.",
            statusCode: StatusCodes.Status409Conflict
        );
    }

    var now = timeProvider.GetLocalNow().DateTime;

    CancellationResult cancellation;

    try
    {
        cancellation = cancellationPolicy.Calculate(
            booking.StartAt,
            booking.Total,
            now
        );
    }
    catch (InvalidOperationException)
    {
        return Results.Problem(
            title: "Booking already started",
            detail: "A booking cannot be cancelled at or after its start time.",
            statusCode: StatusCodes.Status409Conflict
        );
    }

    var cancelled = await repository.TryCancelAsync(
        id,
        cancellation.CancellationFee,
        now
    );

    if (!cancelled)
    {
        return Results.Problem(
            title: "Booking already cancelled",
            detail: "The booking was cancelled by another request.",
            statusCode: StatusCodes.Status409Conflict
        );
    }

    return Results.Ok(
        BuildBookingResponse(
            booking,
            pricingCalculator,
            status: "C",
            fee: cancellation.CancellationFee
        )
    );
});

// Bookings
// POST /bookings
app.MapPost("/bookings", async (
    CreateBookingRequest request,
    BookingValidator validator,
    PricingCalculator pricingCalculator,
    DiscountCalculator discountCalculator,
    BookingRepository repository,
    TimeProvider timeProvider,
    HttpContext httpContext) =>
{
    // R9: The header is optional; absent means normal creation behavior.
    var idempotencyKey =
        httpContext.Request.Headers["Idempotency-Key"].ToString();

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

    if (idempotencyKey.Length > 200)
    {
        return Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["idempotencyKey"] = new[]
                {
                    "Idempotency-Key must contain at most 200 characters."
                }
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

    BookingResponse CreateResponse(int bookingId) => new()
    {
        Id = bookingId,
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

    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
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
        var response = CreateResponse(creationResult.BookingId.Value);

        return Results.Created(
            $"/bookings/{response.Id}",
            response
        );
    }

    // R9: Store the key and the response in the same transaction as the
    // booking. Same key and payload replay the stored response; a different
    // payload returns 422.
    var requestHash = IdempotencyRequestHasher.Compute(request);
    var now = timeProvider.GetUtcNow().UtcDateTime;

    var outcome = await repository.CreateIdempotentAsync(
        idempotencyKey,
        requestHash,
        now,
        request,
        pricing.Subtotal,
        discount.DiscountAmount,
        discount.Total,
        bookingId => JsonSerializer.Serialize(
            CreateResponse(bookingId),
            JsonSerializerOptions.Web)
    );

    switch (outcome.Outcome)
    {
        case IdempotentBookingOutcome.Created:
        {
            var bookingId = outcome.BookingId!.Value;

            httpContext.Response.Headers.Location =
                $"/bookings/{bookingId}";

            return Results.Content(
                outcome.ResponseJson!,
                "application/json",
                Encoding.UTF8,
                outcome.StatusCode ?? StatusCodes.Status201Created);
        }

        case IdempotentBookingOutcome.Replayed:
        {
            var responseJson = outcome.ResponseJson!;
            var bookingId = ReadBookingId(responseJson);

            httpContext.Response.Headers.Location =
                $"/bookings/{bookingId}";

            return Results.Content(
                responseJson,
                "application/json",
                Encoding.UTF8,
                outcome.StatusCode ?? StatusCodes.Status201Created);
        }

        case IdempotentBookingOutcome.Conflict:
            return Results.Problem(
                title: "Booking conflict",
                detail: "The room is not available for the requested time.",
                statusCode: StatusCodes.Status409Conflict
            );

        case IdempotentBookingOutcome.RequestMismatch:
            return Results.Problem(
                title: "Idempotency key conflict",
                detail: "The Idempotency-Key was already used with a different request payload.",
                statusCode: StatusCodes.Status422UnprocessableEntity
            );

        default:
            throw new InvalidOperationException(
                "Unexpected idempotent booking outcome.");
    }
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

app.Run();

static BookingResponse BuildBookingResponse(
    BookingDetailsData booking,
    PricingCalculator pricingCalculator,
    string status,
    decimal fee)
{
    // Reconstruct the pricing segments from the current room rate.
    var pricing = pricingCalculator.Calculate(
        booking.StartAt,
        booking.EndAt,
        booking.HourlyRate
    );

    return new BookingResponse
    {
        Id = booking.Id,
        RoomId = booking.RoomId,
        CustomerId = booking.CustomerId,
        StartAt = booking.StartAt,
        EndAt = booking.EndAt,
        Status = status,
        Segments = pricing.Segments,
        Subtotal = booking.Subtotal,
        Discount = booking.Discount,
        Total = booking.Total,
        Fee = fee
    };
}

static int ReadBookingId(string responseJson)
{
    using var document = JsonDocument.Parse(responseJson);

    return document.RootElement.GetProperty("id").GetInt32();
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
