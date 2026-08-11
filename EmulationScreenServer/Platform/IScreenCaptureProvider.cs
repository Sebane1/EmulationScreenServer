namespace EmulationScreenServer.Platform
{
    public interface IScreenCaptureProvider
    {
        string GetFfmpegVideoInputArgs(int monitorIndex, int fps);
    }
}
