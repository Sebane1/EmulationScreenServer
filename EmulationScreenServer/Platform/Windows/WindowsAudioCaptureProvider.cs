using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;

namespace EmulationScreenServer.Platform.Windows
{
    public class WindowsAudioCaptureProvider : IAudioCaptureProvider
    {
        private WasapiLoopbackCapture? _loopback;
        private BufferedWaveProvider? _buffer;
        private Stream? _stdin;
        private int _lastLoggedSecond;

        public string GetFfmpegAudioInputArgs()
        {
            // Windows pushes 48kHz float32 stereo via stdin pipe
            return "-f f32le -ar 48000 -ac 2 -i pipe:0 -af aresample=48000 ";
        }

        public void StartCapture(Process ffmpegProcess)
        {
            try
            {
                _stdin = ffmpegProcess.StandardInput.BaseStream;

                _loopback = new WasapiLoopbackCapture()
                {
                    ShareMode = NAudio.CoreAudioApi.AudioClientShareMode.Shared
                };

                Console.WriteLine("[Stream] Created Audio Loopback");

                _buffer = new BufferedWaveProvider(_loopback.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(100),
                };

                Console.WriteLine("[Stream] Created Audio Buffer");

                _loopback.DataAvailable += (s, e) =>
                {
                    if (ffmpegProcess.HasExited)
                        return;

                    int bytesRecorded = e.BytesRecorded;
                    if (bytesRecorded != 0)
                    {
                        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    }
                    
                    int available = _buffer.BufferedBytes;
                    if (available > 0)
                    {
                        var temp = new byte[bytesRecorded];
                        int read = _buffer.Read(temp, 0, temp.Length);

                        try
                        {
                            if (_stdin != null && _stdin.CanWrite)
                            {
                                _stdin.Write(temp, 0, read);
                                if (DateTime.Now.Second % 5 == 0 && DateTime.Now.Second != _lastLoggedSecond)
                                {
                                    // Console.WriteLine($"Wrote {read} audio bytes.");
                                    _lastLoggedSecond = DateTime.Now.Second;
                                }
                            }
                        }
                        catch
                        {
                            Console.WriteLine("[Stream] ERROR: Audio Write Failure to FFmpeg stdin.");
                        }
                    }
                };

                _loopback.StartRecording();
                Console.WriteLine("[Stream] NAudio Loopback Started.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Stream] Audio loopback failed: {ex.Message}");
                StopCapture();
            }
        }

        public void StopCapture()
        {
            try { _loopback?.StopRecording(); } catch { }
            try { _loopback?.Dispose(); } catch { }
            _loopback = null;
        }

        public void Dispose()
        {
            StopCapture();
        }
    }
}
