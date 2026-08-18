namespace SmartHomeIoT.Api.Models
{
    public class SensorDataMessage
    {
        public decimal Value { get; set; }
        public string Unit { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}