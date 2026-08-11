using System;
using System.Diagnostics;

namespace EmulationScreenServer.Platform
{
    public interface IAudioCaptureProvider : IDisposable
    {
        string GetFfmpegAudioInputArgs();
        void StartCapture(Process ffmpegProcess);
        void StopCapture();
    }
}
