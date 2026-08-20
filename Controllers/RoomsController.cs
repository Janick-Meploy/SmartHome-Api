using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartHomeIoT.Api.Data;
using SmartHomeIoT.Api.DTOs.Device;
using SmartHomeIoT.Api.DTOs.Room;
using SmartHomeIoT.Api.Models;

namespace SmartHomeIoT.Api.Controllers;

/// <summary>Room management: create, rename, delete, view rooms and the devices in each room (HK-06..HK-10).</summary>
[ApiController]
[Route("api/v1/rooms")]
[Produces("application/json")]
public class RoomsController : ControllerBase
{
    private readonly SmartHomeDbContext _db;

    public RoomsController(SmartHomeDbContext db) => _db = db;

    /// <summary>List all rooms with their device count.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RoomDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RoomDto>>> GetAll()
    {
        var rooms = await _db.Rooms
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoomDto(
                r.RoomId,
                r.Name,
                r.Devices.Count
            ))
            .ToListAsync();

        return Ok(rooms);
    }

    /// <summary>Get a single room with the devices assigned to it (HK-10).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RoomDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomDetailDto>> GetById(int id)
    {
        var room = await _db.Rooms
            .AsNoTracking()
            .Include(r => r.Devices).ThenInclude(d => d.Room)
            .FirstOrDefaultAsync(r => r.RoomId == id);

        if (room is null)
            return NotFound(new { statusCode = 404, message = $"Room {id} was not found." });

        var dto = new RoomDetailDto(room.RoomId, room.Name, room.Devices.Select(d => d.ToDto()).ToList());
        return Ok(dto);
    }

    /// <summary>Devices assigned to a room (equivalent to GET /rooms/{id} but device-list only).</summary>
    [HttpGet("{id:int}/devices")]
    [ProducesResponseType(typeof(IEnumerable<DeviceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<DeviceDto>>> GetDevices(int id)
    {
        var exists = await _db.Rooms.AnyAsync(r => r.RoomId == id);
        if (!exists)
            return NotFound(new { statusCode = 404, message = $"Room {id} was not found." });

        var devices = await _db.Devices
            .AsNoTracking()
            .Include(d => d.Room)
            .Where(d => d.RoomId == id)
            .Select(d => d.ToDto())
            .ToListAsync();

        return Ok(devices);
    }

    /// <summary>Create a new room (HK-06).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(RoomDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoomDto>> Create(RoomCreateDto dto)
    {
        var room = new Room { Name = dto.Name };
        _db.Rooms.Add(room);

        _db.EventLogs.Add(new EventLog
        {
            Event = "RoomCreated",
            Description = $"Room '{dto.Name}' was created.",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var result = new RoomDto(room.RoomId, room.Name, 0);
        return CreatedAtAction(nameof(GetById), new { id = room.RoomId }, result);
    }

    /// <summary>Rename a room (HK-07).</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(RoomDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomDto>> Update(int id, RoomUpdateDto dto)
    {
        var room = await _db.Rooms.Include(r => r.Devices).FirstOrDefaultAsync(r => r.RoomId == id);
        if (room is null)
            return NotFound(new { statusCode = 404, message = $"Room {id} was not found." });

        var oldName = room.Name;
        room.Name = dto.Name;

        _db.EventLogs.Add(new EventLog
        {
            Event = "RoomRenamed",
            Description = $"Room '{oldName}' renamed to '{dto.Name}'.",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return Ok(new RoomDto(room.RoomId, room.Name, room.Devices.Count));
    }

    /// <summary>
    /// Delete a room (HK-08). Blocked (409) while devices are still assigned to it, since a
    /// device must always belong to a room - reassign or remove the devices first.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id)
    {
        var room = await _db.Rooms.Include(r => r.Devices).FirstOrDefaultAsync(r => r.RoomId == id);
        if (room is null)
            return NotFound(new { statusCode = 404, message = $"Room {id} was not found." });

        if (room.Devices.Count > 0)
        {
            return Conflict(new
            {
                statusCode = 409,
                message = $"Room '{room.Name}' still has {room.Devices.Count} device(s) assigned. " +
                           "Move or remove them before deleting the room."
            });
        }

        _db.Rooms.Remove(room);
        _db.EventLogs.Add(new EventLog
        {
            Event = "RoomDeleted",
            Description = $"Room '{room.Name}' was deleted.",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return NoContent();
    }
}
