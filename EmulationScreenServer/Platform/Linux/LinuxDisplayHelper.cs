using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EmulationScreenServer.Platform.Linux
{
    internal static class LinuxDisplayHelper
    {
        private static readonly Regex GeometryRegex = new(
            @"(\d+)x(\d+)\+(-?\d+)\+(-?\d+)",
            RegexOptions.Compiled);

        public static (int X, int Y, int Width, int Height) GetMonitorBounds(int monitorIndex)
        {
            var monitors = TryGetMonitorBoundsFromXrandr();
            if (monitors.Count == 0)
            {
                Console.WriteLine("[Stream] Linux Display: xrandr unavailable, capturing full :0.0.");
                return (0, 0, 1920, 1080);
            }

            if (monitorIndex < 0 || monitorIndex >= monitors.Count)
            {
                Console.WriteLine($"[Stream] Linux Display: monitor index {monitorIndex} out of range, using primary.");
                return monitors[0];
            }

            return monitors[monitorIndex];
        }

        public static string GetDisplay()
        {
            var display = Environment.GetEnvironmentVariable("DISPLAY")?.Trim();
            return string.IsNullOrEmpty(display) ? ":0.0" : display;
        }

        private static List<(int X, int Y, int Width, int Height)> TryGetMonitorBoundsFromXrandr()
        {
            var monitors = new List<(int X, int Y, int Width, int Height)>();

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "xrandr",
                    Arguments = "--query",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null) return monitors;

                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(3000) || process.ExitCode != 0)
                    return monitors;

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!line.Contains(" connected", StringComparison.Ordinal))
                        continue;

                    var match = GeometryRegex.Match(line);
                    if (!match.Success)
                        continue;

                    monitors.Add((
                        int.Parse(match.Groups[3].Value),
                        int.Parse(match.Groups[4].Value),
                        int.Parse(match.Groups[1].Value),
                        int.Parse(match.Groups[2].Value)));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Stream] Linux Display: failed to query xrandr ({ex.Message}).");
            }

            return monitors;
        }
    }
}
