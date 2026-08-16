using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EmulationScreenServer
{
    public static partial class DependencyManager
    {
        private static readonly string BinPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_bin");

        private const string MediaMtxUrlWindows =
            "https://github.com/bluenviron/mediamtx/releases/download/v1.9.3/mediamtx_v1.9.3_windows_amd64.zip";

        private const string MediaMtxUrlLinux =
            "https://github.com/bluenviron/mediamtx/releases/download/v1.9.3/mediamtx_v1.9.3_linux_amd64.tar.gz";

        private const string DefaultFfmpegUrlWindows =
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-gpl-8.1.zip";

        private const string DefaultFfmpegUrlLinux =
            "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-linux64-gpl-8.1.tar.xz";

        private static string MediaMtxBinaryName =>
            OperatingSystem.IsWindows() ? "mediamtx.exe" : "mediamtx";

        private static string FfmpegBinaryName =>
            OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        private static string GetFfmpegDownloadUrl()
        {
            var u = Environment.GetEnvironmentVariable("EMUSCREEN_FFMPEG_URL")?.Trim();
            if (!string.IsNullOrEmpty(u)) return u;

            return OperatingSystem.IsWindows() ? DefaultFfmpegUrlWindows : DefaultFfmpegUrlLinux;
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

#if WINDOWS
            await EnsureViGEmBusAsync();
            Console.WriteLine("[DependencyManager] ViGEmBus ready.");
#endif

            Console.WriteLine("[DependencyManager] All dependencies ready.");
            return true;
        }

        private static async Task EnsureMediaMtxAsync()
        {
            string mtxPath = Path.Combine(BinPath, MediaMtxBinaryName);
            if (File.Exists(mtxPath))
            {
                var fi = new FileInfo(mtxPath);
                if (fi.Length > 10 * 1024 * 1024)
                {
                    WriteMediaMtxConfig();
                    return;
                }

                Console.WriteLine($"[DependencyManager] {MediaMtxBinaryName} is corrupted or incomplete. Redownloading...");
                File.Delete(mtxPath);
            }

            Console.WriteLine("[DependencyManager] Downloading MediaMTX...");
            if (OperatingSystem.IsWindows())
            {
                string zipPath = Path.Combine(BinPath, "mediamtx.zip");
                await DownloadFileAsync(MediaMtxUrlWindows, zipPath);
                ZipFile.ExtractToDirectory(zipPath, BinPath, true);
                File.Delete(zipPath);
            }
            else
            {
                string archivePath = Path.Combine(BinPath, "mediamtx.tar.gz");
                await DownloadFileAsync(MediaMtxUrlLinux, archivePath);
                ExtractTarGz(archivePath, BinPath);
                File.Delete(archivePath);
                TryMakeExecutable(mtxPath);
            }

            Console.WriteLine("[DependencyManager] MediaMTX extraction complete.");
            WriteMediaMtxConfig();
        }

        private static void WriteMediaMtxConfig()
        {
            string ymlPath = Path.Combine(BinPath, "mediamtx.yml");
            const string config = """
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
                authInternalUsers:
                  - user: any
                    pass: ""
                    ips: ['127.0.0.1', '::1']
                    permissions:
                      - action: publish
                  - user: any
                    pass: ""
                    ips: []
                    permissions:
                      - action: read
                      - action: playback
                paths:
                  all:
                """;

            File.WriteAllText(ymlPath, config);
        }

        private static async Task EnsureFfmpegAsync()
        {
            string ffmpegPath = Path.Combine(BinPath, FfmpegBinaryName);
            if (ShouldRedownloadFfmpeg())
            {
                try
                {
                    if (File.Exists(ffmpegPath))
                    {
                        File.Delete(ffmpegPath);
                        Console.WriteLine($"[DependencyManager] EMUSCREEN_FFMPEG_REDOWNLOAD=1: removed existing {FfmpegBinaryName}.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DependencyManager] Could not delete {FfmpegBinaryName} for redownload: {ex.Message}");
                }
            }

            if (File.Exists(ffmpegPath)) return;

            string url = GetFfmpegDownloadUrl();
            Console.WriteLine($"[DependencyManager] Downloading FFmpeg…");
            Console.WriteLine($"[DependencyManager] URL: {url}");

            if (OperatingSystem.IsWindows())
            {
                string zipPath = Path.Combine(BinPath, "ffmpeg.zip");
                await DownloadFileAsync(url, zipPath);

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

                try { File.Delete(zipPath); } catch { }

                if (!extracted)
                {
                    try { if (File.Exists(ffmpegPath)) File.Delete(ffmpegPath); } catch { }
                    throw new InvalidOperationException(
                        "ffmpeg.exe was not found inside the downloaded zip. Check EMUSCREEN_FFMPEG_URL or the BtbN release layout.");
                }
            }
            else
            {
                string archivePath = Path.Combine(BinPath, "ffmpeg.tar.xz");
                await DownloadFileAsync(url, archivePath);

                string extractDir = Path.Combine(BinPath, "ffmpeg_extract");
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);

                RunTar($"-xJf \"{archivePath}\" -C \"{extractDir}\"");
                File.Delete(archivePath);

                string? discovered = FindFileRecursive(extractDir, "ffmpeg");
                if (discovered == null || !File.Exists(discovered))
                {
                    try { Directory.Delete(extractDir, true); } catch { }
                    throw new InvalidOperationException(
                        "ffmpeg was not found inside the downloaded archive. Check EMUSCREEN_FFMPEG_URL or the BtbN release layout.");
                }

                File.Copy(discovered, ffmpegPath, true);
                TryMakeExecutable(ffmpegPath);
                try { Directory.Delete(extractDir, true); } catch { }
                Console.WriteLine("[DependencyManager] ffmpeg extracted successfully.");
            }

            TryLogFfmpegVersion(ffmpegPath);
        }

        private static string? FindFileRecursive(string root, string fileName)
        {
            foreach (var file in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
                return file;
            return null;
        }

        private static void ExtractTarGz(string archivePath, string destination)
        {
            RunTar($"-xzf \"{archivePath}\" -C \"{destination}\"");
        }

        private static void RunTar(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start tar for archive extraction.");

            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"tar extraction failed: {stderr}");
        }

        private static void TryMakeExecutable(string path)
        {
            if (!OperatingSystem.IsLinux()) return;

            try
            {
                if (chmod(path, 0x755) != 0)
                    Console.WriteLine($"[DependencyManager] Warning: chmod failed for {path}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DependencyManager] Warning: could not mark {path} executable: {ex.Message}");
            }
        }

        [DllImport("libc", SetLastError = true, EntryPoint = "chmod")]
        private static extern int chmod(string pathname, int mode);

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

        private static async Task DownloadFileAsync(string url, string outputPath)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "EmulationScreen-Server/1.0");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            long totalReadBytes = 0;
            int readBytes;
            int lastProgress = -1;

            while ((readBytes = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, readBytes));
                totalReadBytes += readBytes;

                if (totalBytes.HasValue)
                {
                    int progress = (int)((double)totalReadBytes / totalBytes.Value * 100);
                    if (progress % 5 == 0 && progress != lastProgress)
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
