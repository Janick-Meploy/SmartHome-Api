using SmartHomeIoT.Api.Data;
using SmartHomeIoT.Api.Models;

namespace SmartHomeIoT.Api.Services;
public class SensorDataService
{
    private readonly SmartHomeDbContext _context;

    public SensorDataService(SmartHomeDbContext context)
    {
        _context = context;
    }

    public async Task SaveSensorDataAsync(
        int deviceId,
        string sensorType,
        decimal value,
        string unit,
        DateTime timestamp)
    {
        var sensorData = new SensorData
        {
            DeviceId = deviceId,
            SensorType = sensorType,
            Value = value,
            Unit = unit,
            Timestamp = timestamp
        };

        _context.SensorData.Add(sensorData);
        Console.WriteLine("Saving sensor data in the database...");
        await _context.SaveChangesAsync();
    }
}