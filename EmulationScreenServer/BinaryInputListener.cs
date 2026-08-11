using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace EmulationScreenServer
{
    public class BinaryInputListener : IDisposable
    {
        private readonly int _port;
        private UdpClient? _udpClient;
        private Thread? _thread;
        private bool _running;
        public bool IsRunning => _running;

        private readonly byte[] _sessionBytes;
        public Action<byte, byte[]> OnPacketReceived;

        public BinaryInputListener(int port = 50051, string sessionId = "00000000")
        {
            _port = port;
            try { _sessionBytes = Convert.FromHexString(sessionId); }
            catch { _sessionBytes = new byte[4]; }
        }

        public void Start()
        {
            if (_running) return;

            try
            {
                _udpClient = new UdpClient(_port);
                _running = true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                // Another instance (or another app) is already bound to this port.
                // Don't crash the whole server; just disable UDP input.
                _udpClient = null;
                _running = false;
                Console.WriteLine($"[BinaryInputListener] Port {_port} already in use; UDP input disabled.");
                return;
            }

            _thread = new Thread(ListenLoop);
            _thread.IsBackground = true;
            _thread.Start();

            Console.WriteLine($"[BinaryInputListener] Listening for UDP binary protocol on port {_port}...");
        }

        private void ListenLoop()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, _port);
            
            while (_running)
            {
                try 
                {
                    var client = _udpClient;
                    if (client == null) return;

                    byte[] data = client.Receive(ref remoteEndPoint);

                    if (data.Length >= 17 && 
                        data[0] == 'E' && data[1] == 'M' && data[2] == 'U' && data[3] == 'L')
                    {
                        // Verify Session ID (Bytes 4-7)
                        if (data[4] != _sessionBytes[0] || data[5] != _sessionBytes[1] || 
                            data[6] != _sessionBytes[2] || data[7] != _sessionBytes[3])
                        {
                            continue;
                        }

                        if (DateTime.Now.Second % 5 == 0) // Log occasionally
                            Console.WriteLine($"[BinaryInputListener] Recv EMUL (Secure) from {remoteEndPoint.Address} (Slot {data[8]})");
                        
                        byte playerIndex = data[8];
                        
                        // Extract payload (bytes 9-16)
                        byte[] payload = new byte[8];
                        Array.Copy(data, 9, payload, 0, 8);

                        OnPacketReceived?.Invoke(playerIndex, payload);
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted || ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    // Expected on close
                }
                catch (Exception ex)
                {
                    if (_running)
                        Console.WriteLine($"[BinaryInputListener] Receive error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            _udpClient?.Close();
            _thread?.Join(1000);
        }
    }
}
