using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using EmulationScreenServer.Platform;
using EmulationScreenServer.Platform.Windows;
using EmulationScreenServer.Platform.Linux;

namespace EmulationScreenServer
{
    public class StreamManager : IDisposable
    {
        private Process? _ffmpegProcess;
        private readonly string _streamUrl;
        private readonly int _monitorIndex;
        private readonly int _fps;
        private readonly bool _enableAudioDefault;
        private bool _audioFallbackAttempted;
        private bool _ffmpegStarted;
        private bool _fallbackAttempted;
        private DateTime _lastFfmpegActivity = DateTime.UtcNow;
        private readonly object _restartLock = new object();
        private readonly bool _verboseFfmpegLogs =
            string.Equals(Environment.GetEnvironmentVariable("EMUSCREEN_FFMPEG_VERBOSE"), "1", StringComparison.OrdinalIgnoreCase);

        private IScreenCaptureProvider _screenProvider;
        private IAudioCaptureProvider? _audioProvider;

        public StreamManager(string streamUrl = "rtmp://localhost/live/screen", int monitorIndex = 0, int fps = 60)
        {
            _streamUrl = streamUrl;
            _monitorIndex = TryGetIntFromEnv("EMUSCREEN_MONITOR", monitorIndex);
            _fps = Math.Clamp(TryGetIntFromEnv("EMUSCREEN_FPS", fps), 1, 240);
            _enableAudioDefault = !string.Equals(Environment.GetEnvironmentVariable("EMUSCREEN_AUDIO"), "0", StringComparison.OrdinalIgnoreCase);

            if (OperatingSystem.IsWindows())
            {
                _screenProvider = new WindowsScreenCaptureProvider();
            }
            else
            {
                _screenProvider = new LinuxScreenCaptureProvider();
            }
        }

        public void Start()
        {
            _fallbackAttempted = false;
            _audioFallbackAttempted = false;
            StartInternal(preferNvenc: true, enableAudio: _enableAudioDefault);
            Console.WriteLine(
                "[Stream] Latency: VLC buffers RTMP heavily by default. Try " +
                $"ffplay -fflags nobuffer -flags low_delay {_streamUrl.Replace("localhost", "127.0.0.1")} to compare.");
        }

        private static int TryGetIntFromEnv(string key, int fallback)
        {
            var v = Environment.GetEnvironmentVariable(key);
            return int.TryParse(v, out var parsed) ? parsed : fallback;
        }

        private void StartInternal(bool preferNvenc, bool enableAudio)
        {
            Stop();

            Console.WriteLine(preferNvenc
                ? "[Stream] Starting FFmpeg (NVENC)..."
                : "[Stream] Starting FFmpeg (libx264 fallback)...");

            string videoCodecArgs;
            if (preferNvenc)
            {
                videoCodecArgs = "-c:v h264_nvenc -preset p1 -tune ull -rc cbr -b:v 6M -maxrate 6M -bufsize 6M -delay 0";
            }
            else
            {
                videoCodecArgs =
                    "-c:v libx264 -preset ultrafast -tune zerolatency -pix_fmt yuv420p " +
                    "-x264-params sync-lookahead=0:rc-lookahead=0:sliced_threads=0";
            }

            var argsParts = new List<string>
            {
                $"-hide_banner -loglevel {(_verboseFfmpegLogs ? "info" : "warning")} -stats " +
                "-fflags nobuffer -flags low_delay -analyzeduration 0 -probesize 32 "
            };

            // 1. Get Platform Video Args
            argsParts.Add(_screenProvider.GetFfmpegVideoInputArgs(_monitorIndex, _fps));
            argsParts.Add("-thread_queue_size 512 ");

            // 2. Get Platform Audio Args
            if (enableAudio)
            {
                if (OperatingSystem.IsWindows())
                {
                    _audioProvider = new WindowsAudioCaptureProvider();
                }
                else
                {
                    _audioProvider = new LinuxAudioCaptureProvider();
                }
                argsParts.Add(_audioProvider.GetFfmpegAudioInputArgs());
            }

            argsParts.Add($"{videoCodecArgs} -bf 0 -g {_fps} -keyint_min {_fps} -sc_threshold 0 ");

            if (enableAudio)
            {
                argsParts.Add("-map 0:v:0 -map 1:a:0 ");
                argsParts.Add("-c:a aac -b:a 160k -ar 48000 -ac 2 ");
                argsParts.Add("-af aresample=async=1:first_pts=0 ");
            }
            else
            {
                argsParts.Add("-map 0:v:0 ");
            }

            argsParts.Add("-flvflags no_duration_filesize -muxdelay 0 -muxpreload 0 -rtmp_live live ");
            argsParts.Add($"-f flv \"{_streamUrl}\"");

            string args = string.Concat(argsParts);
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_bin", "ffmpeg.exe");

            // Temporary Linux executable extension handling
            if (!OperatingSystem.IsWindows() && ffmpegPath.EndsWith(".exe"))
            {
                ffmpegPath = ffmpegPath.Substring(0, ffmpegPath.Length - 4);
            }

            Console.WriteLine($"[Stream] FFmpeg Command: {ffmpegPath} {args}");

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };

            try
            {
                _ffmpegProcess = new Process { StartInfo = psi };
                _ffmpegProcess.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        if (_verboseFfmpegLogs) Console.WriteLine($"[FFmpeg] {e.Data}");
                        else if (e.Data.Contains("Error") || e.Data.Contains("fail"))
                        {
                            Console.WriteLine($"[FFmpeg] {e.Data}");
                        }

                        _lastFfmpegActivity = DateTime.UtcNow;

                        if (preferNvenc &&
                            !_fallbackAttempted &&
                            e.Data.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase) &&
                            e.Data.Contains("Error while opening encoder", StringComparison.OrdinalIgnoreCase))
                        {
                            lock (_restartLock)
                            {
                                if (_fallbackAttempted) return;
                                _fallbackAttempted = true;
                            }

                            Console.WriteLine("[FFmpeg] NVENC failed. Retrying with libx264...");
                            _ = Task.Run(() =>
                            {
                                try { Stop(); } catch { }
                                StartInternal(preferNvenc: false, enableAudio: enableAudio);
                            });
                        }

                        if (enableAudio &&
                            !_audioFallbackAttempted &&
                            IsAudioCaptureFailure(e.Data))
                        {
                            lock (_restartLock)
                            {
                                if (_audioFallbackAttempted) return;
                                _audioFallbackAttempted = true;
                            }

                            Console.WriteLine($"[FFmpeg] {e.Data}");
                            Console.WriteLine("[FFmpeg] Audio capture failed. Retrying without audio...");
                            _ = Task.Run(() =>
                            {
                                try { Stop(); } catch { }
                                StartInternal(preferNvenc: preferNvenc, enableAudio: false);
                            });
                        }
                    }
                };
                
                while (!IsPortOpen("127.0.0.1", 1935))
                {
                    Task.Delay(100).Wait();
                }

                _ffmpegStarted = _ffmpegProcess.Start();
                _ffmpegProcess.BeginErrorReadLine();
                Console.WriteLine($"[Stream] Streaming to {_streamUrl}...");

                // 3. Start Audio Capture (e.g. NAudio Loopback pipe)
                if (enableAudio && _audioProvider != null)
                {
                    _audioProvider.StartCapture(_ffmpegProcess);
                }

                StartWatchdog();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Stream] Failed to start FFmpeg: {ex.Message}");
                try { _ffmpegProcess?.Dispose(); } catch { }
                _ffmpegProcess = null;
                _ffmpegStarted = false;
            }
        }

        private void StartWatchdog()
        {
            _ = Task.Run(async () =>
            {
                while (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                {
                    await Task.Delay(2000);

                    var idleTime = DateTime.UtcNow - _lastFfmpegActivity;

                    if (idleTime.TotalSeconds > 5)
                    {
                        Console.WriteLine("[Watchdog] FFmpeg appears stalled. Restarting...");

                        try
                        {
                            Stop();
                        }
                        catch { }

                        _ = Task.Run(() =>
                        {
                            try
                            {
                                Start();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Watchdog] Restart failed: {ex.Message}");
                            }
                        });

                        break;
                    }
                }
            });
        }

        private static bool IsPortOpen(string host, int port)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var result = client.BeginConnect(host, port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(2000);
                return success && client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAudioCaptureFailure(string ffmpegLine)
        {
            bool mentionsAudioBackend =
                ffmpegLine.Contains("dshow", StringComparison.OrdinalIgnoreCase) ||
                ffmpegLine.Contains("DirectShow", StringComparison.OrdinalIgnoreCase) ||
                ffmpegLine.Contains("pulse", StringComparison.OrdinalIgnoreCase) ||
                ffmpegLine.Contains("PipeWire", StringComparison.OrdinalIgnoreCase) ||
                ffmpegLine.Contains("audio", StringComparison.OrdinalIgnoreCase);

            bool looksLikeOpenFailure =
                ffmpegLine.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
                ffmpegLine.Contains("I/O error", StringComparison.OrdinalIgnoreCase) ||
                ffmpegLine.Contains("Error opening input", StringComparison.OrdinalIgnoreCase) ||
                ffmpegLine.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
                ffmpegLine.Contains("No such process", StringComparison.OrdinalIgnoreCase);

            return mentionsAudioBackend && looksLikeOpenFailure;
        }

        public void Stop()
        {
            try { _audioProvider?.StopCapture(); } catch { }
            try { _audioProvider?.Dispose(); } catch { }
            _audioProvider = null;

            var p = _ffmpegProcess;
            var started = _ffmpegStarted;
            _ffmpegProcess = null;
            _ffmpegStarted = false;

            if (p == null) return;

            try
            {
                if (!started) return;
                if (p.HasExited) return;

                p.Kill(entireProcessTree: true);
                try { p.WaitForExit(2000); } catch { }
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Stream] Failed to stop FFmpeg: {ex.Message}");
            }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
