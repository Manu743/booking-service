using System.Globalization;
using Booking.Api.Data.Repositories;
using Booking.Api.Models.Rooms;
using Booking.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

[ApiController]
[Route("rooms")]
public sealed class RoomsController : ControllerBase
{
    private readonly RoomRepository _roomRepository;
    private readonly BookingRepository _bookingRepository;
    private readonly RoomAvailabilityCalculator _availabilityCalculator;

    public RoomsController(
        RoomRepository roomRepository,
        BookingRepository bookingRepository,
        RoomAvailabilityCalculator availabilityCalculator)
    {
        _roomRepository = roomRepository;
        _bookingRepository = bookingRepository;
        _availabilityCalculator = availabilityCalculator;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoomResponse>>> GetRooms()
    {
        var rooms = await _roomRepository.GetAllAsync();

        return Ok(rooms);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRoom(
        [FromBody] CreateRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationError(
                "name",
                "Room name is required.");
        }

        if (request.Name.Length > 100)
        {
            return ValidationError(
                "name",
                "Room name cannot exceed 100 characters.");
        }

        if (request.Capacity <= 0)
        {
            return ValidationError(
                "capacity",
                "Room capacity must be greater than zero.");
        }

        if (request.HourlyRate <= 0)
        {
            return ValidationError(
                "hourlyRate",
                "Hourly rate must be greater than zero.");
        }

        var room = await _roomRepository.CreateAsync(request);

        return Created($"/rooms/{room.Id}", room);
    }

    [HttpGet("{id:int}/availability")]
    public async Task<IActionResult> GetAvailability(
        int id,
        [FromQuery] string? date,
        [FromQuery] int? minMinutes)
    {
        if (id <= 0)
        {
            return ValidationError(
                "id",
                "Room ID must be greater than zero.");
        }

        if (!DateOnly.TryParseExact(
                date,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            return ValidationError(
                "date",
                "Date must use the yyyy-MM-dd format.");
        }

        if (minMinutes is null or <= 0)
        {
            return ValidationError(
                "minMinutes",
                "minMinutes must be greater than zero.");
        }

        if (!await _roomRepository.ExistsAsync(id))
        {
            return Problem(
                title: "Room not found",
                detail: $"No room was found with ID {id}.",
                statusCode: StatusCodes.Status404NotFound);
        }

        var dayStart = parsedDate.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);

        var bookings = await _bookingRepository.GetActiveBookingsAsync(
            id,
            dayStart,
            dayEnd);

        var windows = _availabilityCalculator.GetFreeWindows(
            dayStart,
            bookings,
            minMinutes.Value);

        return Ok(
            windows
                .Select(window => new AvailabilityWindow(
                    window.Start,
                    window.End))
                .ToArray());
    }

    private IActionResult ValidationError(string key, string message)
    {
        ModelState.AddModelError(key, message);

        return ValidationProblem(ModelState);
    }
}
