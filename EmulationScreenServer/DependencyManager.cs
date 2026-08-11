using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Nefarius.ViGEm.Client;

namespace EmulationScreenServer
{
    public static class DependencyManager
    {
        private static readonly string BinPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_bin");

        // Hardcoded URLs for POC reliability
        private const string MediaMtxUrl = "https://github.com/bluenviron/mediamtx/releases/download/v1.9.3/mediamtx_v1.9.3_windows_amd64.zip";
        private const string VigemUrl = "https://github.com/ViGEm/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";

        /// <summary>
        /// BtbN win64 <strong>gpl</strong> "full" build: gdigrab, WASAPI (-f wasapi -loopback), libx264, native AAC, NVENC, etc.
        /// Pinned to FFmpeg <strong>8.1</strong> release branch (not master) so behavior stays predictable.
        /// Override with <c>EMUSCREEN_FFMPEG_URL</c>. Force replace with <c>EMUSCREEN_FFMPEG_REDOWNLOAD=1</c>.
        /// </summary>
        private const string DefaultFfmpegUrl =
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-gpl-8.1.zip";

        private static string GetFfmpegDownloadUrl()
        {
            var u = Environment.GetEnvironmentVariable("EMUSCREEN_FFMPEG_URL")?.Trim();
            return string.IsNullOrEmpty(u) ? DefaultFfmpegUrl : u;
        }

        private static bool ShouldRedownloadFfmpeg()
        {
            var v = Environment.GetEnvironmentVariable("EMUSCREEN_FFMPEG_REDOWNLOAD")?.Trim();
            if (string.IsNullOrEmpty(v)) return false;
            return v.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<bool> EnsureDependenciesAsync()
        {
            if (!Directory.Exists(BinPath))
                Directory.CreateDirectory(BinPath);

            Console.WriteLine("[DependencyManager] Checking dependencies...");

            await EnsureMediaMtxAsync();
            Console.WriteLine("[DependencyManager] MediaMTX ready.");
            await EnsureFfmpegAsync();
            Console.WriteLine("[DependencyManager] FFmpeg ready.");
            await EnsureViGEmBusAsync();
            Console.WriteLine("[DependencyManager] ViGEmBus ready.");

            Console.WriteLine("[DependencyManager] All dependencies ready.");
            return true;
        }

        private static async Task EnsureMediaMtxAsync()
        {
            string mtxPath = Path.Combine(BinPath, "mediamtx.exe");
            if (File.Exists(mtxPath))
            {
                var fi = new FileInfo(mtxPath);
                if (fi.Length > 10 * 1024 * 1024) // Should be ~30MB, if it's less than 10MB it's corrupted
                {
                    return;
                }
                Console.WriteLine("[DependencyManager] mediamtx.exe is corrupted or incomplete. Redownloading...");
                File.Delete(mtxPath);
            }

            Console.WriteLine("[DependencyManager] Downloading MediaMTX...");
            string zipPath = Path.Combine(BinPath, "mediamtx.zip");
            await DownloadFileAsync(MediaMtxUrl, zipPath);

            Console.WriteLine("[DependencyManager] MediaMTX extraction complete.");
            ZipFile.ExtractToDirectory(zipPath, BinPath, true);
            File.Delete(zipPath);

            // Generate optimized mediamtx.yml if missing
            string ymlPath = Path.Combine(BinPath, "mediamtx.yml");
            string config = @"
logLevel: info
rtmp: yes
rtmpAddress: :1935
rtsp: yes
rtspAddress: :8554
# Balanced outgoing buffer. 512 is default. 32 was too small and dropped clients.
writeQueueSize: 512
readBufferCount: 512
writeTimeout: 5s
hls: yes
hlsAddress: :8888
hlsVariant: lowLatency
paths:
  all:
    allowPublishIPs: ['127.0.0.1', '::1']
";
            // Always overwrite the config to ensure correct settings
            File.WriteAllText(ymlPath, config);
        }

        private static async Task EnsureFfmpegAsync()
        {
            string ffmpegPath = Path.Combine(BinPath, "ffmpeg.exe");
            if (ShouldRedownloadFfmpeg())
            {
                try
                {
                    if (File.Exists(ffmpegPath))
                    {
                        File.Delete(ffmpegPath);
                        Console.WriteLine("[DependencyManager] EMUSCREEN_FFMPEG_REDOWNLOAD=1: removed existing ffmpeg.exe.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DependencyManager] Could not delete ffmpeg.exe for redownload: {ex.Message}");
                }
            }

            if (File.Exists(ffmpegPath)) return;

            string url = GetFfmpegDownloadUrl();
            Console.WriteLine($"[DependencyManager] Downloading FFmpeg (BtbN win64 gpl; WASAPI loopback + gdigrab + NVENC)…");
            Console.WriteLine($"[DependencyManager] URL: {url}");
            string zipPath = Path.Combine(BinPath, "ffmpeg.zip");
            await DownloadFileAsync(url, zipPath);

            Console.WriteLine("[DependencyManager] FFmpeg extraction complete.");
            // Only extract the single ffmpeg.exe entry — skip the hundreds of other files
            bool extracted = false;
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(ffmpegPath, true);
                        extracted = true;
                        Console.WriteLine("[DependencyManager] ffmpeg.exe extracted successfully.");
                        break;
                    }
                }
            }
            try { File.Delete(zipPath); } catch { /* best-effort */ }

            if (!extracted)
            {
                try { if (File.Exists(ffmpegPath)) File.Delete(ffmpegPath); } catch { }
                throw new InvalidOperationException(
                    "ffmpeg.exe was not found inside the downloaded zip. Check EMUSCREEN_FFMPEG_URL or the BtbN release layout.");
            }

            TryLogFfmpegVersion(ffmpegPath);
        }

        /// <summary>
        /// Prints ffmpeg -version (first line) so logs confirm which binary is in use.
        /// </summary>
        private static void TryLogFfmpegVersion(string ffmpegPath)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-hide_banner -version",
                    WorkingDirectory = Path.GetDirectoryName(ffmpegPath) ?? BinPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (p == null) return;
                var stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    var first = stdout.AsSpan().Trim().ToString();
                    var nl = first.IndexOfAny(new[] { '\r', '\n' });
                    if (nl >= 0) first = first[..nl].Trim();
                    Console.WriteLine($"[DependencyManager] FFmpeg: {first}");
                }
            }
            catch
            {
                /* ignore */
            }
        }

        private static async Task<bool> EnsureViGEmBusAsync()
        {
            Console.WriteLine("[DependencyManager] Checking ViGEmBus driver...");
            try
            {
                // Test if driver exists by instantiating client
                var testClient = new ViGEmClient();
                Console.WriteLine("[DependencyManager] ViGEmBus driver found.");
                return true;
            }
            catch (Exception)
            {
                Console.WriteLine("[DependencyManager] ViGEmBus Driver not found! Initiating installation...");
                await InstallViGEmBusAsync();
                return false;
            }
        }

        private static async Task InstallViGEmBusAsync()
        {
            string installerPath = Path.Combine(BinPath, "vigembus_installer.exe");

            Console.WriteLine("[DependencyManager] Downloading ViGEmBus installer...");
            await DownloadFileAsync(VigemUrl, installerPath);

            Console.WriteLine("[DependencyManager] Launching Installer to install Virtual Xbox Controller Driver.");
            Console.WriteLine("[!!!] PLEASE ACCEPT THE WINDOWS UAC PROMPT IF IT APPEARS [!!!]");

            Process installer = new Process();
            installer.StartInfo.FileName = installerPath;
            // Silent install flag for standard installers, though UAC will still pop
            installer.StartInfo.Arguments = "/q";
            installer.StartInfo.UseShellExecute = true; // Needed for UAC elevation
            installer.StartInfo.Verb = "runas";

            try
            {
                installer.Start();
                installer.WaitForExit();

                // Quick validation
                using (var testClient = new ViGEmClient()) { }
                Console.WriteLine("[DependencyManager] ViGEmBus installed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DependencyManager] CRITICAL: Failed to install ViGEmBus. You must install it manually. Error: {ex.Message}");
            }
            finally
            {
                if (File.Exists(installerPath)) File.Delete(installerPath);
            }
        }

        private static async Task DownloadFileAsync(string url, string outputPath)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "EmulationScreen-Server/1.0");

                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[8192];
                        long totalReadBytes = 0;
                        int readBytes;
                        int lastProgress = -1;

                        while ((readBytes = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, readBytes);
                            totalReadBytes += readBytes;

                            if (totalBytes.HasValue)
                            {
                                int progress = (int)((double)totalReadBytes / totalBytes.Value * 100);
                                if (progress % 5 == 0 && progress != lastProgress) // Print every 5%
                                {
                                    Console.Write($"\r[DependencyManager] Downloading... {progress}% ({totalReadBytes / 1024 / 1024} MB / {totalBytes.Value / 1024 / 1024} MB)");
                                    lastProgress = progress;
                                }
                            }
                        }
                        Console.WriteLine("\n[DependencyManager] Download Complete!");
                    }
                }
            }
        }
    }
}
