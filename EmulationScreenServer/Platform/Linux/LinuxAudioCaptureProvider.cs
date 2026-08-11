using System;
using System.Diagnostics;

namespace EmulationScreenServer.Platform.Linux
{
    public class LinuxAudioCaptureProvider : IAudioCaptureProvider
    {
        private Process? _ffmpegProcess;
        private bool _isCapturing;
        private bool _disposed;

        public string GetFfmpegAudioInputArgs()
        {
            var device = Environment.GetEnvironmentVariable("EMUSCREEN_PULSE_DEVICE")?.Trim();
            if (string.IsNullOrEmpty(device))
                device = "default";

            return $"-f pulse -i \"{device}\" ";
        }

        public void StartCapture(Process ffmpegProcess)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_isCapturing)
                StopCapture();

            if (ffmpegProcess.HasExited)
                throw new InvalidOperationException("Cannot start Linux audio capture: FFmpeg process has already exited.");

            _ffmpegProcess = ffmpegProcess;
            _isCapturing = true;

            var device = Environment.GetEnvironmentVariable("EMUSCREEN_PULSE_DEVICE")?.Trim();
            if (string.IsNullOrEmpty(device))
                device = "default";

            Console.WriteLine($"[Stream] Linux Audio: FFmpeg capturing PulseAudio device \"{device}\".");
        }

        public void StopCapture()
        {
            if (!_isCapturing)
                return;

            _isCapturing = false;
            _ffmpegProcess = null;
            Console.WriteLine("[Stream] Linux Audio: Capture stopped.");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            StopCapture();
        }
    }
}
