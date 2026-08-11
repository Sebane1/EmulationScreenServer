using System;
using System.Diagnostics;
using System.IO;

namespace EmulationScreenServer
{
    public class MediaMTXProcess : IDisposable
    {
        private Process? _process;
        private bool _started;
        private readonly string _binPath;

        public MediaMTXProcess()
        {
            _binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_bin");
        }

        public void Start()
        {
            // If Start is called multiple times, stop the previous instance first.
            Stop();

            string exePath = Path.Combine(_binPath, "mediamtx.exe");
            if (!File.Exists(exePath))
            {
                Console.WriteLine("[MediaMTX] ERORR: Executable not found. Did DependencyManager fail?");
                return;
            }

            // If a previous run crashed, a stray mediamtx.exe can remain and keep ports bound.
            // Best-effort: kill any existing mediamtx processes.
            try
            {
                foreach (var p in Process.GetProcessesByName("mediamtx"))
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    try { p.Dispose(); } catch { }
                }
            }
            catch { }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = _binPath, // Ensures it picks up the mediamtx.yml in _bin
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            try 
            {
                _process = new Process { StartInfo = psi };
                _process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        Console.WriteLine($"[MediaMTX] {e.Data}");
                };

                _process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        Console.WriteLine($"[MediaMTX][ERR] {e.Data}");
                };

                _started = _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                Console.WriteLine("[MediaMTX] Server started in background.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaMTX] Failed to start: {ex.Message}");
                try { _process?.Dispose(); } catch { }
                _process = null;
                _started = false;
            }
        }

        public void Stop()
        {
            var p = _process;
            var started = _started;
            _process = null;
            _started = false;

            if (p == null) return;

            try
            {
                if (!started) return;
                if (p.HasExited) return;

                p.Kill(entireProcessTree: true);
                try { p.WaitForExit(2000); } catch { }
                Console.WriteLine("[MediaMTX] Server stopped.");
            }
            catch (InvalidOperationException)
            {
                // Not associated with a running OS process.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaMTX] Failed to stop: {ex.Message}");
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
