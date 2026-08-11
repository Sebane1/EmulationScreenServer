using System;
using System.Threading;
using System.Threading.Tasks;

using EmulationScreenServer.Platform;
using EmulationScreenServer.Platform.Windows;
using EmulationScreenServer.Platform.Linux;

namespace EmulationScreenServer
{
    public class EmulationServerController : IDisposable
    {
        private BinaryInputListener? _binaryListener;
        private IInputSimulator? _input;
        private StreamManager? _stream;
        private StreamApiServer? _apiServer;
        private MediaMTXProcess? _mtx;
        private SilentTone? _silentTone;
        
        public bool IsRunning { get; private set; }
        public string CurrentRtspUrl { get; private set; } = string.Empty;
        public string CurrentPublicRtspUrl { get; private set; } = string.Empty;

        public event Action<string>? PublicRtspUrlResolved;
        
        // Configuration Properties
        public int MonitorIndex { get; set; } = 0;
        public int TargetFps { get; set; } = 60;
        public bool EnableAudio { get; set; } = true;
        public bool EnableMouseForwarding { get; set; } = true;
        public bool EnableControllerForwarding { get; set; } = true;

        public event Action<string>? LogMessage;

        private void Log(string message)
        {
            Console.WriteLine(message);
            LogMessage?.Invoke(message);
        }

        private static string GetLanIPv4OrLocalhost() => NetworkAddressHelper.GetLanIPv4OrLocalhost();

        private async Task ResolvePublicRtspUrlAsync(string sessionId)
        {
            try
            {
                var publicIp = await NetworkAddressHelper.TryGetPublicIPv4Async().ConfigureAwait(false);
                if (publicIp != null)
                {
                    CurrentPublicRtspUrl = $"rtsp://{publicIp}:8554/live/screen_{sessionId}";
                    Log($"[Controller] Public RTSP URL: {CurrentPublicRtspUrl}");
                    Log("[Controller] Note: Public access requires port 8554 to be forwarded on your router.");
                    PublicRtspUrlResolved?.Invoke(CurrentPublicRtspUrl);
                }
                else
                {
                    Log("[Controller] Could not detect public IP address.");
                    PublicRtspUrlResolved?.Invoke(string.Empty);
                }
            }
            catch (Exception ex)
            {
                Log($"[Controller] Public IP lookup failed: {ex.Message}");
                PublicRtspUrlResolved?.Invoke(string.Empty);
            }
        }

        public async Task StartAsync()
        {
            if (IsRunning) return;

            Log("=== Emulation Screen Server Starting ===");

            string sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            Log($"[Controller] Session ID: {sessionId}");

            _silentTone = new SilentTone();
            _silentTone.Start();

            try
            {
                Log("[Controller] Initializing dependencies...");
                await DependencyManager.EnsureDependenciesAsync();
                Log("[Controller] Dependencies resolved.");

                // Environment variables used by StreamManager
                Environment.SetEnvironmentVariable("EMUSCREEN_MONITOR", MonitorIndex.ToString());
                Environment.SetEnvironmentVariable("EMUSCREEN_FPS", TargetFps.ToString());
                Environment.SetEnvironmentVariable("EMUSCREEN_AUDIO", EnableAudio ? "1" : "0");

                Log($"[Controller] Mouse forwarding: {(EnableMouseForwarding ? "enabled" : "disabled")}");
                Log($"[Controller] Controller forwarding: {(EnableControllerForwarding ? "enabled" : "disabled")}");

                if (EnableMouseForwarding || EnableControllerForwarding)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        _input = new WindowsInputSimulator(EnableMouseForwarding, EnableControllerForwarding);
                        Log("[Controller] WindowsInputSimulator created.");
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        _input = new LinuxInputSimulator(EnableMouseForwarding, EnableControllerForwarding);
                        Log("[Controller] LinuxInputSimulator created (/dev/uinput).");
                    }
                    else
                    {
                        Log("[Controller] Warning: Input simulation is not yet supported on this OS.");
                    }
                }
                else
                {
                    Log("[Controller] Input forwarding disabled — no virtual devices will be created.");
                }

                _binaryListener = new BinaryInputListener(50051, sessionId);
                Log("[Controller] BinaryInputListener created.");

                string pushUrl = $"rtmp://localhost/live/screen_{sessionId}";
                _stream = new StreamManager(pushUrl);
                Log("[Controller] StreamManager created.");

                _apiServer = new StreamApiServer(8080, sessionId);
                _apiServer.ServerIp = GetLanIPv4OrLocalhost();
                Log("[Controller] StreamApiServer created.");

                _mtx = new MediaMTXProcess();
                Log("[Controller] MediaMTXProcess created.");

                _binaryListener.OnPacketReceived += (playerIndex, payload) =>
                {
                    if (playerIndex == 4 && !EnableMouseForwarding) return;
                    if (playerIndex >= 0 && playerIndex < 4 && !EnableControllerForwarding) return;
                    _input?.ProcessPacket(playerIndex, payload);
                };

                _binaryListener.Start();
                _mtx.Start();
                _stream.Start();
                _apiServer.Start();

                CurrentRtspUrl = $"rtsp://{_apiServer.ServerIp}:8554/live/screen_{sessionId}";
                Log($"[Controller] Local RTSP URL: {CurrentRtspUrl}");
                
                IsRunning = true;

                _ = ResolvePublicRtspUrlAsync(sessionId);
            }
            catch (Exception ex)
            {
                Log($"[Controller] Fatal Error: {ex.Message}");
                Stop();
                throw;
            }
        }

        public void Stop()
        {
            if (!IsRunning) return;

            Log("[Controller] Shutting down...");
            _silentTone?.Stop();
            _binaryListener?.Dispose();
            _stream?.Stop();
            _mtx?.Dispose();
            _apiServer?.Dispose();
            _input?.Dispose();
            
            IsRunning = false;
            CurrentRtspUrl = string.Empty;
            CurrentPublicRtspUrl = string.Empty;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
