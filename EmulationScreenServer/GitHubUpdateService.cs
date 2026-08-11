using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EmulationScreenServer
{
    public sealed class UpdateInfo
    {
        public required string Version { get; init; }
        public required string ReleaseNotes { get; init; }
        public required string ReleasePageUrl { get; init; }
        public required string DownloadUrl { get; init; }
        public required string AssetName { get; init; }
    }

    public static class GitHubUpdateService
    {
        private const string RepoLatestReleaseApi =
            "https://api.github.com/repos/Sebane1/EmulationScreenServer/releases/latest";

        public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
        {
            using var http = CreateClient();
            using var response = await http.GetAsync(RepoLatestReleaseApi, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Update] GitHub release check failed: HTTP {(int)response.StatusCode}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                return null;

            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrWhiteSpace(tagName) || !AppVersion.IsNewerThanCurrent(tagName))
                return null;

            var assetSuffix = OperatingSystem.IsWindows() ? "win-x64.zip" : "linux-x64.tar.gz";
            if (!root.TryGetProperty("assets", out var assets))
                return null;

            JsonElement? matchedAsset = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name != null && name.Contains(assetSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    matchedAsset = asset;
                    break;
                }
            }

            if (matchedAsset == null)
            {
                Console.WriteLine($"[Update] Latest release has no asset matching '*{assetSuffix}'.");
                return null;
            }

            var downloadUrl = matchedAsset.Value.GetProperty("browser_download_url").GetString();
            var assetName = matchedAsset.Value.GetProperty("name").GetString();
            if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(assetName))
                return null;

            var releasePage = root.GetProperty("html_url").GetString() ?? "https://github.com/Sebane1/EmulationScreenServer/releases/latest";
            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;

            Console.WriteLine($"[Update] New version available: {tagName} (current: {AppVersion.Current})");

            return new UpdateInfo
            {
                Version = tagName,
                ReleaseNotes = notes,
                ReleasePageUrl = releasePage,
                DownloadUrl = downloadUrl,
                AssetName = assetName
            };
        }

        public static async Task ApplyUpdateAsync(UpdateInfo update, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var workDir = Path.Combine(Path.GetTempPath(), "EmulationScreenServer-update");
            var downloadPath = Path.Combine(workDir, update.AssetName);
            var stagingDir = Path.Combine(workDir, "staging");

            if (Directory.Exists(workDir))
                Directory.Delete(workDir, true);
            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(stagingDir);

            progress?.Report("Downloading update...");
            Console.WriteLine($"[Update] Downloading {update.DownloadUrl}");

            using (var http = CreateClient())
            await using (var downloadStream = await http.GetStreamAsync(update.DownloadUrl, cancellationToken).ConfigureAwait(false))
            await using (var fileStream = File.Create(downloadPath))
            {
                await downloadStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("Extracting update...");
            Console.WriteLine($"[Update] Extracting to {stagingDir}");

            if (OperatingSystem.IsWindows())
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(downloadPath, stagingDir, true);
            }
            else
            {
                RunShell($"tar -xzf \"{downloadPath}\" -C \"{stagingDir}\"");
            }

            var executablePath = FindExecutable(stagingDir);
            if (executablePath == null)
                throw new InvalidOperationException("Update package did not contain the application executable.");

            progress?.Report("Preparing installer...");
            var launcherPath = WriteUpdateLauncher(installDir, stagingDir, executablePath);

            progress?.Report("Restarting to apply update...");
            Console.WriteLine($"[Update] Launching updater: {launcherPath}");

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = launcherPath,
                UseShellExecute = true,
                WorkingDirectory = installDir
            };

            if (System.Diagnostics.Process.Start(startInfo) == null)
                throw new InvalidOperationException("Failed to launch update helper process.");

            Environment.Exit(0);
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EmulationScreenServer-Updater/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        private static string? FindExecutable(string root)
        {
            var name = OperatingSystem.IsWindows() ? "EmulationScreenServer.exe" : "EmulationScreenServer";
            return Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static string WriteUpdateLauncher(string installDir, string stagingDir, string executablePath)
        {
            var exeName = Path.GetFileName(executablePath);
            var targetExe = Path.Combine(installDir, exeName);

            if (OperatingSystem.IsWindows())
            {
                var batPath = Path.Combine(installDir, "_apply_update.bat");
                var script = $"""
                    @echo off
                    timeout /t 2 /nobreak >nul
                    xcopy /E /Y /I "{stagingDir}\*" "{installDir}\"
                    start "" "{targetExe}"
                    del "%~f0"
                    """;
                File.WriteAllText(batPath, script);
                return batPath;
            }

            var shPath = Path.Combine(installDir, "_apply_update.sh");
            var shellScript = $$"""
                #!/bin/sh
                sleep 2
                cp -a "{{stagingDir}}"/. "{{installDir}}"/
                chmod +x "{{targetExe}}"
                rm -f "$0"
                exec "{{targetExe}}"
                """;
            File.WriteAllText(shPath, shellScript);
            RunShell($"chmod +x \"{shPath}\"");
            return shPath;
        }

        private static void RunShell(string command)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to run: {command}");

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var err = process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"Command failed ({process.ExitCode}): {command}\n{err}");
            }
        }
    }
}
