using System.Text.Json;
using MQTTnet;
using SmartHomeIoT.Api.Models;

namespace SmartHomeIoT.Api.Services
{
    public class MqttService : BackgroundService
    {
        private readonly MqttClientFactory _factory;
        private readonly IMqttClient _client;
        private readonly IServiceScopeFactory _scopeFactory;

        public MqttService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;

            _factory = new MqttClientFactory();
            _client = _factory.CreateMqttClient();

            _client.ApplicationMessageReceivedAsync += HandleMessageAsync;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            await ConnectAsync(stoppingToken);
        }

        private async Task ConnectAsync(CancellationToken stoppingToken)
        {
            var options = new MqttClientOptionsBuilder()
                .WithClientId("SmartHomeApi")
                .WithTcpServer("localhost", 1883)
                .Build();

            await _client.ConnectAsync(options, stoppingToken);

            await SubscribeAsync();
        }

        private async Task SubscribeAsync()
        {
            await _client.SubscribeAsync(
                new MqttTopicFilterBuilder()
                    .WithTopic("smarthome/device/+/sensor/#")
                    .Build());
        }

        private async Task HandleMessageAsync(
            MqttApplicationMessageReceivedEventArgs e)
        {
            var topic = e.ApplicationMessage.Topic;

            var payload = e.ApplicationMessage
                .ConvertPayloadToString();

            Console.WriteLine($"Topic: {topic}");
            Console.WriteLine($"Payload: {payload}");

            var topicParts = topic.Split('/');

            if (topicParts.Length != 5)
            {
                Console.WriteLine("Invalid MQTT topic.");
                return;
            }

            if (!int.TryParse(topicParts[2], out var deviceId))
            {
                Console.WriteLine("Invalid device ID in MQTT topic.");
                return;
            }
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine(deviceId);
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine();
            var sensorType = topicParts[4];

            SensorDataMessage? sensorMessage;

            try
            {
                sensorMessage =
                    JsonSerializer.Deserialize<SensorDataMessage>(payload);
            }
            catch (JsonException)
            {
                Console.WriteLine("Invalid sensor data JSON.");
                return;
            }

            if (sensorMessage == null)
            {
                Console.WriteLine("Sensor data payload was empty.");
                return;
            }

            Console.WriteLine(
                $"Parsed sensor data: " +
                $"Value={sensorMessage.Value}, " +
                $"Unit={sensorMessage.Unit}, " +
                $"Timestamp={sensorMessage.Timestamp:o}");

            using var scope = _scopeFactory.CreateScope();

            var sensorDataService =
                scope.ServiceProvider
                    .GetRequiredService<SensorDataService>();

            await sensorDataService.SaveSensorDataAsync(
                deviceId,
                sensorType,
                sensorMessage.Value,
                sensorMessage.Unit,
                sensorMessage.Timestamp);
        }
    }
}