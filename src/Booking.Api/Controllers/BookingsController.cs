using System.Text.Json;
using Booking.Api.Data.Repositories;
using Booking.Api.Idempotency;
using Booking.Api.Models.Bookings;
using Booking.Domain.Models;
using Booking.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

[ApiController]
[Route("bookings")]
public sealed class BookingsController : ControllerBase
{
    private readonly BookingRepository _repository;
    private readonly BookingValidator _validator;
    private readonly PricingCalculator _pricingCalculator;
    private readonly DiscountCalculator _discountCalculator;
    private readonly CancellationPolicy _cancellationPolicy;
    private readonly TimeProvider _timeProvider;

    public BookingsController(
        BookingRepository repository,
        BookingValidator validator,
        PricingCalculator pricingCalculator,
        DiscountCalculator discountCalculator,
        CancellationPolicy cancellationPolicy,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _validator = validator;
        _pricingCalculator = pricingCalculator;
        _discountCalculator = discountCalculator;
        _cancellationPolicy = cancellationPolicy;
        _timeProvider = timeProvider;
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBooking(int id)
    {
        if (id <= 0)
        {
            return BadRequest(new
            {
                error = "Booking ID must be greater than zero."
            });
        }

        var booking = await _repository.GetDetailsByIdAsync(id);

        if (booking is null)
        {
            return Problem(
                title: "Booking not found",
                detail: $"No booking was found with ID {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(
            BuildBookingResponse(
                booking,
                _pricingCalculator,
                booking.Status,
                booking.Fee));
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelBooking(int id)
    {
        if (id <= 0)
        {
            return BadRequest(new
            {
                error = "Booking ID must be greater than zero."
            });
        }

        var booking = await _repository.GetDetailsByIdAsync(id);

        if (booking is null)
        {
            return Problem(
                title: "Booking not found",
                detail: $"No booking was found with ID {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        if (booking.Status == "C")
        {
            return Problem(
                title: "Booking already cancelled",
                detail: "The booking has already been cancelled.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var now = _timeProvider.GetLocalNow().DateTime;

        CancellationResult cancellation;

        try
        {
            cancellation = _cancellationPolicy.Calculate(
                booking.StartAt,
                booking.Total,
                now);
        }
        catch (InvalidOperationException)
        {
            return Problem(
                title: "Booking already started",
                detail: "A booking cannot be cancelled at or after its start time.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var cancelled = await _repository.TryCancelAsync(
            id,
            cancellation.CancellationFee,
            now);

        if (!cancelled)
        {
            return Problem(
                title: "Booking already cancelled",
                detail: "The booking was cancelled by another request.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return Ok(
            BuildBookingResponse(
                booking,
                _pricingCalculator,
                status: "C",
                fee: cancellation.CancellationFee));
    }

    [HttpPost]
    public async Task<IActionResult> CreateBooking(
        [FromBody] CreateBookingRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        // R9: The header is optional; absent means normal creation behavior.
        idempotencyKey ??= string.Empty;

        // Validate local date/time values.
        if (request.StartAt.Kind != DateTimeKind.Unspecified ||
            request.EndAt.Kind != DateTimeKind.Unspecified)
        {
            return ValidationError(
                "dates",
                "StartAt and EndAt must be local date/time values without timezone information.");
        }

        if (request.RoomId <= 0 || request.CustomerId <= 0)
        {
            return ValidationError(
                "ids",
                "RoomId and CustomerId must be greater than zero.");
        }

        // R1-R3: Validate booking rules before accessing the database.
        var validationErrors = _validator.Validate(
            request.StartAt,
            request.EndAt);

        if (validationErrors.Count > 0)
        {
            foreach (var error in validationErrors)
            {
                ModelState.AddModelError("booking", error);
            }

            return ValidationProblem(ModelState);
        }

        if (idempotencyKey.Length > 200)
        {
            return ValidationError(
                "idempotencyKey",
                "Idempotency-Key must contain at most 200 characters.");
        }

        // Retrieve room rate and customer tier.
        var pricingData = await _repository.GetPricingDataAsync(
            request.RoomId,
            request.CustomerId);

        if (pricingData.HourlyRate is null ||
            pricingData.CustomerTier is null)
        {
            return Problem(
                title: "Room or customer not found",
                detail: "The specified room or customer does not exist.",
                statusCode: StatusCodes.Status404NotFound);
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
        var pricing = _pricingCalculator.Calculate(
            request.StartAt,
            request.EndAt,
            pricingData.HourlyRate.Value);

        // R6: Apply customer discount and calculate final total.
        var discount = _discountCalculator.Calculate(
            pricing.Subtotal,
            discountPercentage);

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
            var creationResult = await _repository.CreateAsync(
                request,
                pricing.Subtotal,
                discount.DiscountAmount,
                discount.Total);

            if (creationResult.ResultCode == 1)
            {
                return Problem(
                    title: "Booking conflict",
                    detail: "The room is not available for the requested time.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (creationResult.ResultCode != 0 ||
                creationResult.BookingId is null)
            {
                throw new InvalidOperationException(
                    "The booking could not be created.");
            }

            // Build the response using the original calculated values.
            var response = CreateResponse(creationResult.BookingId.Value);

            return Created($"/bookings/{response.Id}", response);
        }

        // R9: Store the key and the response in the same transaction as the
        // booking. Same key and payload replay the stored response; a different
        // payload returns 422.
        var requestHash = IdempotencyRequestHasher.Compute(request);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var outcome = await _repository.CreateIdempotentAsync(
            idempotencyKey,
            requestHash,
            now,
            request,
            pricing.Subtotal,
            discount.DiscountAmount,
            discount.Total,
            bookingId => JsonSerializer.Serialize(
                CreateResponse(bookingId),
                JsonSerializerOptions.Web));

        switch (outcome.Outcome)
        {
            case IdempotentBookingOutcome.Created:
            {
                var bookingId = outcome.BookingId!.Value;

                Response.Headers.Location = $"/bookings/{bookingId}";

                return StoredResponse(
                    outcome.ResponseJson!,
                    outcome.StatusCode ?? StatusCodes.Status201Created);
            }

            case IdempotentBookingOutcome.Replayed:
            {
                var responseJson = outcome.ResponseJson!;
                var bookingId = ReadBookingId(responseJson);

                Response.Headers.Location = $"/bookings/{bookingId}";

                return StoredResponse(
                    responseJson,
                    outcome.StatusCode ?? StatusCodes.Status201Created);
            }

            case IdempotentBookingOutcome.Conflict:
                return Problem(
                    title: "Booking conflict",
                    detail: "The room is not available for the requested time.",
                    statusCode: StatusCodes.Status409Conflict);

            case IdempotentBookingOutcome.RequestMismatch:
                return Problem(
                    title: "Idempotency key conflict",
                    detail: "The Idempotency-Key was already used with a different request payload.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            default:
                throw new InvalidOperationException(
                    "Unexpected idempotent booking outcome.");
        }
    }

    private IActionResult ValidationError(string key, string message)
    {
        ModelState.AddModelError(key, message);

        return ValidationProblem(ModelState);
    }

    private static ContentResult StoredResponse(
        string responseJson,
        int statusCode) => new()
        {
            Content = responseJson,
            ContentType = "application/json; charset=utf-8",
            StatusCode = statusCode
        };

    private static BookingResponse BuildBookingResponse(
        BookingDetailsData booking,
        PricingCalculator pricingCalculator,
        string status,
        decimal fee)
    {
        // Reconstruct the pricing segments from the current room rate.
        var pricing = pricingCalculator.Calculate(
            booking.StartAt,
            booking.EndAt,
            booking.HourlyRate);

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

    private static int ReadBookingId(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);

        return document.RootElement.GetProperty("id").GetInt32();
    }
}
