using System;

namespace EmulationScreenServer.Platform.Linux
{
    public class LinuxScreenCaptureProvider : IScreenCaptureProvider
    {
        public string GetFfmpegVideoInputArgs(int monitorIndex, int fps)
        {
            var display = LinuxDisplayHelper.GetDisplay();
            var (x, y, width, height) = LinuxDisplayHelper.GetMonitorBounds(monitorIndex);

            Console.WriteLine($"[Stream] Linux Capture: monitor={monitorIndex} ({width}x{height} @ {x},{y}) fps={fps}");

            return $"-f x11grab -framerate {fps} -draw_mouse 1 " +
                   $"-video_size {width}x{height} -i {display}+{x},{y} ";
        }
    }
}
