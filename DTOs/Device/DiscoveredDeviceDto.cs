namespace SmartHomeIoT.Api.DTOs.Device
{
    public class DiscoveredDeviceDto
    {
        public string Ssid { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public double SignalStrength { get; set; }
    }
}
