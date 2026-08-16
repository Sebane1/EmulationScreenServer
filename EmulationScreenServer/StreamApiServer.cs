using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmulationScreenServer
{
    public class StreamApiServer : IDisposable
    {
        private HttpListener? _listener;
        private Thread _thread;
        private bool _running;
        private int _port;
        private readonly string _sessionId;
        private int _stopRequested;

        // In a real scenario, this IP should be resolved dynamically (e.g. public IP)
        // For testing, we'll return a localhost equivalent if accessed locally, or the target IP.
        public string ServerIp { get; set; } = "127.0.0.1";

        public StreamApiServer(int port = 8080, string sessionId = "00000000")
        {
            _port = port;
            _sessionId = sessionId;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{_port}/streaminfo/");
        }

        public void Start()
        {
            try
            {
                _listener?.Start();
                _running = true;
                _thread = new Thread(ListenLoop);
                _thread.IsBackground = true;
                _thread.Start();

                Console.WriteLine($"[API] Stream Info Server listening on http://*:{_port}/streaminfo/");
                Console.WriteLine($"[API] VRChat: use JSON field vrchatVideoUrl (RTSP) in the video player usually lower latency than HLS.");
            }
            catch (HttpListenerException ex)
            {
                // Windows often requires admin rights to listen on wildcards. 
                // Fallback to localhost if permission denied.
                if (ex.ErrorCode == 5) // Access Denied
                {
                    Console.WriteLine("[API] Access denied to listen on all interfaces. Try running as Admin.");
                    Console.WriteLine("[API] Falling back to localhost only...");

                    // Recreate the listener: on some Windows builds, a failed Start() can
                    // leave the instance in a bad state for immediate reuse.
                    try { _listener?.Close(); } catch { }
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{_port}/streaminfo/");
                    _listener.Start();
                    
                    _running = true;
                    _thread = new Thread(ListenLoop);
                    _thread.IsBackground = true;
                    _thread.Start();

                    Console.WriteLine($"[API] Stream Info Server listening on http://localhost:{_port}/streaminfo/");
                    Console.WriteLine($"[API] VRChat: use JSON field vrchatVideoUrl (RTSP) in the video player usually lower latency than HLS.");
                }
                else
                {
                    throw;
                }
            }
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var listener = _listener;
                    if (listener == null) break;

                    var context = listener.GetContext();
                    ProcessRequest(context);
                }
                catch (HttpListenerException)
                {
                    // Listener stopped
                    if (!_running) break;
                }
                catch (ObjectDisposedException)
                {
                    // Listener was closed/disposed during shutdown
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                        Console.WriteLine($"[API] Error processing request: {ex.Message}");
                }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // Simple CORS support for potential web clients
            response.AddHeader("Access-Control-Allow-Origin", "*");

            if (request.HttpMethod == "GET")
            {
                // Dynamic IP resolution check (if requested via external IP, use that IP)
                string host = request.UserHostName ?? ServerIp;
                string baseIp = host.Contains(":") ? host.Split(':')[0] : host;

                // VRChat's video player can use RTSP; it is usually lower latency than HLS (segmented).
                string rtsp = $"rtsp://{baseIp}:8554/live/screen_{_sessionId}";
                var data = new
                {
                    rtspUrl = rtsp,
                    // Same stream as rtspUrl use this URL in VRChat's video player when RTSP is supported.
                    vrchatVideoUrl = rtsp,
                    hlsUrl = $"http://{baseIp}:8888/live/screen_{_sessionId}/index.m3u8",
                    // FFmpeg publishes here; viewers generally use rtspUrl / vrchatVideoUrl or hlsUrl instead.
                    rtmpPublishUrl = $"rtmp://{baseIp}:1935/live/screen_{_sessionId}"
                };

                string jsonResponse = JsonSerializer.Serialize(data);
                byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);

                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                
                try 
                {
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                catch (Exception) { /* Ignored if client disconnects early */ }
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            }

            response.Close();
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopRequested, 1) == 1)
                return;

            _running = false;

            try
            {
                // Stop will unblock GetContext(). If already disposed, this can throw.
                if (_listener != null && _listener.IsListening)
                    _listener.Stop();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }

            try
            {
                // Avoid deadlocking if Stop is called on the listener thread itself.
                if (_thread != null && _thread.IsAlive && Thread.CurrentThread.ManagedThreadId != _thread.ManagedThreadId)
                    _thread.Join(1000);
            }
            catch (Exception) { }
        }

        public void Dispose()
        {
            Stop();
            try { _listener?.Close(); } catch { }
        }
    }
}
