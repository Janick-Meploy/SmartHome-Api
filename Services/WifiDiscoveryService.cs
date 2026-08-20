using SmartHomeIoT.Api.DTOs.Device;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SmartHomeIoT.Api.Services;

public class WifiDiscoveryService
{
    public async Task<List<DiscoveredDeviceDto>> ScanAsync()
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sudo",
                Arguments = "iw dev wlan0 scan",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"WiFi scan failed: {error}");
        }

        return ParseScanResult(output);
    }

    private List<DiscoveredDeviceDto> ParseScanResult(string output)
    {
        var devices = new List<DiscoveredDeviceDto>();

        string? currentMac = null;
        double? currentSignal = null;

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();

            // Example:
            // BSS aa:bb:cc:dd:ee:ff(on wlan0)
            if (trimmed.StartsWith("BSS "))
            {
                var match = Regex.Match(
                    trimmed,
                    @"BSS\s+([0-9a-fA-F:]{17})"
                );

                if (match.Success)
                {
                    currentMac = match.Groups[1].Value;
                    currentSignal = null;
                }
            }

            // Example:
            // signal: -48.00 dBm
            else if (trimmed.StartsWith("signal:"))
            {
                var match = Regex.Match(
                    trimmed,
                    @"signal:\s*(-?\d+(?:\.\d+)?)"
                );

                if (match.Success &&
                    double.TryParse(
                        match.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var signal))
                {
                    currentSignal = signal;
                }
            }

            // Example:
            // SSID: SmartHome-ABC123
            else if (trimmed.StartsWith("SSID:"))
            {
                var ssid = trimmed["SSID:".Length..].Trim();

                if (!string.IsNullOrWhiteSpace(ssid) &&
                    currentMac != null &&
                    currentSignal.HasValue)
                {
                    devices.Add(new DiscoveredDeviceDto
                    {
                        Ssid = ssid,
                        MacAddress = currentMac,
                        SignalStrength = currentSignal.Value
                    });
                }
            }
        }

        return devices;
    }
}