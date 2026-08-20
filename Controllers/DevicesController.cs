using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartHomeIoT.Api.Data;
using SmartHomeIoT.Api.DTOs.Device;
using SmartHomeIoT.Api.DTOs.EventLog;
using SmartHomeIoT.Api.DTOs.SensorData;
using SmartHomeIoT.Api.Models;
using SmartHomeIoT.Api.Services;

namespace SmartHomeIoT.Api.Controllers;

/// <summary>
/// Device management: list, detail, register/pair, update, remove, history, events and control
/// commands (HK-02, HK-03, HK-05, HK-11, HK-13, HK-14).
/// </summary>
[ApiController]
[Route("api/v1/devices")]
[Produces("application/json")]
public class DevicesController : ControllerBase
{
    private readonly SmartHomeDbContext _db;
    private readonly WifiDiscoveryService _wifiDiscoveryService;

    public DevicesController(SmartHomeDbContext db, WifiDiscoveryService wifiDiscoveryService)
    {
        _db = db;
        _wifiDiscoveryService = wifiDiscoveryService;
    }

    /// <summary>List all registered devices, optionally filtered by room or status.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DeviceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<DeviceDto>>> GetAll([FromQuery] int? roomId, [FromQuery] DeviceStatus? status)
    {
        var query = _db.Devices.AsNoTracking().Include(d => d.Room).AsQueryable();

        if (roomId is not null)
            query = query.Where(d => d.RoomId == roomId);

        if (status is not null)
            query = query.Where(d => d.Status == status);

        var devices = await query.OrderBy(d => d.Name).Select(d => d.ToDto()).ToListAsync();
        return Ok(devices);
    }

    /// <summary>Device detail: name, id, ip, type, room, registration date (HK-14).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(DeviceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceDetailDto>> GetById(int id)
    {
        var device = await _db.Devices.AsNoTracking().Include(d => d.Room).FirstOrDefaultAsync(d => d.DeviceId == id);
        if (device is null)
            return NotFound(new { statusCode = 404, message = $"Device {id} was not found." });

        return Ok(device.ToDetailDto());
    }

    /// <summary>
    /// Register/pair a new device (HK-03, HK-03.1). The normal path in production is the
    /// WiFi-provisioning + mDNS + MQTT-heartbeat flow (see the sequence diagrams); this endpoint
    /// lets an operator register (or manually correct) a device record directly through the API.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DeviceDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceDetailDto>> Create(DeviceCreateDto dto)
    {
        var roomExists = await _db.Rooms.AnyAsync(r => r.RoomId == dto.RoomId);
        if (!roomExists)
            return NotFound(new { statusCode = 404, message = $"Room {dto.RoomId} was not found." });

        var macTaken = await _db.Devices.AnyAsync(d => d.MacAddress == dto.MacAddress);
        if (macTaken)
        {
            return Conflict(new
            {
                statusCode = 409,
                message = $"A device with MAC address '{dto.MacAddress}' is already registered."
            });
        }

        var device = new Device
        {
            Name = dto.Name,
            Type = dto.Type,
            RoomId = dto.RoomId,
            MacAddress = dto.MacAddress,
            IPv4Address = dto.IPv4Address,
            Status = DeviceStatus.Online,
            RegistrationDate = DateTime.UtcNow,
            LastSeen = DateTime.UtcNow
        };

        _db.Devices.Add(device);
        _db.EventLogs.Add(new EventLog
        {
            Event = "DeviceRegistered",
            Description = $"Device '{dto.Name}' ({dto.MacAddress}) was registered and paired to room {dto.RoomId}.",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        await _db.Entry(device).Reference(d => d.Room).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = device.DeviceId }, device.ToDetailDto());
    }

    /// <summary>Update a device's name, type or room assignment.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(DeviceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceDetailDto>> Update(int id, DeviceUpdateDto dto)
    {
        var device = await _db.Devices.Include(d => d.Room).FirstOrDefaultAsync(d => d.DeviceId == id);
        if (device is null)
            return NotFound(new { statusCode = 404, message = $"Device {id} was not found." });

        if (device.RoomId != dto.RoomId)
        {
            var roomExists = await _db.Rooms.AnyAsync(r => r.RoomId == dto.RoomId);
            if (!roomExists)
                return NotFound(new { statusCode = 404, message = $"Room {dto.RoomId} was not found." });
        }

        device.Name = dto.Name;
        device.Type = dto.Type;
        device.RoomId = dto.RoomId;
        if (dto.IPv4Address is not null)
            device.IPv4Address = dto.IPv4Address;

        await _db.SaveChangesAsync();

        await _db.Entry(device).Reference(d => d.Room).LoadAsync();
        return Ok(device.ToDetailDto());
    }

    /// <summary>
    /// Remove a paired device (HK-05 / EK-04). Sensor history is removed along with the device;
    /// EventLog entries are preserved with DeviceId set to null (see README "Design assumptions").
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == id);
        if (device is null)
            return NotFound(new { statusCode = 404, message = $"Device {id} was not found." });

        _db.EventLogs.Add(new EventLog
        {
            DeviceId = null, // written with no FK so it survives the device row being deleted below
            Event = "DeviceRemoved",
            Description = $"Device '{device.Name}' ({device.MacAddress}) was removed from the system.",
            Timestamp = DateTime.UtcNow
        });

        _db.Devices.Remove(device);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Historical sensor data for a device over 24h / 7d / 30d (or a custom range).</summary>
    [HttpGet("{id:int}/history")]
    [ProducesResponseType(typeof(IEnumerable<SensorDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<SensorDataDto>>> GetHistory(
        int id,
        [FromQuery] string range = "24h",
        [FromQuery] string? sensorType = null)
    {
        var exists = await _db.Devices.AnyAsync(d => d.DeviceId == id);
        if (!exists)
            return NotFound(new { statusCode = 404, message = $"Device {id} was not found." });

        var since = range.ToLowerInvariant() switch
        {
            "24h" => DateTime.UtcNow.AddHours(-24),
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            _ => (DateTime?)null
        };

        if (since is null)
        {
            return BadRequest(new
            {
                statusCode = 400,
                message = $"Unsupported range '{range}'. Use one of: 24h, 7d, 30d."
            });
        }

        var query = _db.SensorData.AsNoTracking().Where(s => s.DeviceId == id && s.Timestamp >= since);

        if (!string.IsNullOrWhiteSpace(sensorType))
            query = query.Where(s => s.SensorType == sensorType);

        var data = await query.OrderBy(s => s.Timestamp).Select(s => s.ToDto()).ToListAsync();
        return Ok(data);
    }

    /// <summary>Event log entries for a single device.</summary>
    [HttpGet("{id:int}/events")]
    [ProducesResponseType(typeof(IEnumerable<EventLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<EventLogDto>>> GetEvents(int id)
    {
        var exists = await _db.Devices.AnyAsync(d => d.DeviceId == id);
        if (!exists)
            return NotFound(new { statusCode = 404, message = $"Device {id} was not found." });

        var events = await _db.EventLogs
            .AsNoTracking()
            .Where(e => e.DeviceId == id)
            .OrderByDescending(e => e.Timestamp)
            .Select(e => e.ToDto())
            .ToListAsync();

        return Ok(events);
    }

    /// <summary>
    /// Send a control command to a device (HK-11), e.g. lamp ON/OFF.
    /// NOTE: this endpoint records the command and logs the event; actual dispatch onto the
    /// device's MQTT command topic ("home/{deviceId}/cmd") requires wiring in a real MQTT client
    /// (e.g. MQTTnet against the Mosquitto broker) - see README "Not included in this API".
    /// </summary>
    [HttpPost("{id:int}/command")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendCommand(int id, DeviceCommandDto dto)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.DeviceId == id);
        if (device is null)
            return NotFound(new { statusCode = 404, message = $"Device {id} was not found." });

        _db.EventLogs.Add(new EventLog
        {
            DeviceId = device.DeviceId,
            Event = "CommandIssued",
            Description = $"Command '{dto.Command}' issued to device '{device.Name}'.",
            Timestamp = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        return Accepted(new
        {
            message = $"Command '{dto.Command}' recorded for device {id}. " +
                       "Dispatch over MQTT topic home/{deviceId}/cmd requires the MQTT publisher integration."
        });
    }
    [HttpGet("discovered")]
    [ProducesResponseType(typeof(IEnumerable<DiscoveredDeviceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<DiscoveredDeviceDto>>> GetDiscoveredDevices()
    {
        var devices = await _wifiDiscoveryService.ScanAsync(); // test
        devices = devices.Where(ssid => ssid.Ssid.StartsWith("SmartHome")).ToList();
        return Ok(devices);
    }
}
