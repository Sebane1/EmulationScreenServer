using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

public static class FfmpegWasapiEnumerator
{
    public static List<string> GetAudioDevices(string ffmpegPath)
    {
        var devices = new List<string>();

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = "-list_devices true -f dshow -i dummy",
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        string output = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // Collect quoted device names from lines that FFmpeg tags as "(audio)".
        // This is robust across FFmpeg log wording differences.
        foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine ?? string.Empty;
            if (!line.Contains("(audio)", StringComparison.OrdinalIgnoreCase)) continue;

            // Device lines look like: [dshow @ ...]  "Device Name"
            var match = Regex.Match(line, "\"([^\"]+)\"");
            if (match.Success && match.Groups.Count > 1)
            {
                var name = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !devices.Contains(name))
                    devices.Add(name);
            }
        }

        return devices;
    }
}