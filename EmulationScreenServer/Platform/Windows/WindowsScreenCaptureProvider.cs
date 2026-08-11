using System;
using System.Windows.Forms;

namespace EmulationScreenServer.Platform.Windows
{
    public class WindowsScreenCaptureProvider : IScreenCaptureProvider
    {
        public string GetFfmpegVideoInputArgs(int monitorIndex, int fps)
        {
            var screen = GetCaptureScreen(monitorIndex);
            var b = screen.Bounds;
            Console.WriteLine($"[Stream] Capture: monitor={monitorIndex} ({b.Width}x{b.Height} @ {b.X},{b.Y}) fps={fps}");

            return $"-f gdigrab -framerate {fps} -draw_mouse 1 " +
                   $"-offset_x {b.X} -offset_y {b.Y} -video_size {b.Width}x{b.Height} -i desktop ";
        }

        private static Screen GetCaptureScreen(int monitorIndex)
        {
            var screens = Screen.AllScreens;
            if (screens.Length == 0) return Screen.PrimaryScreen ?? throw new InvalidOperationException("No screens detected.");

            if (monitorIndex < 0 || monitorIndex >= screens.Length)
                return Screen.PrimaryScreen ?? screens[0];

            return screens[monitorIndex];
        }
    }
}
