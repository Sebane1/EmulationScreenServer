using System;

namespace EmulationScreenServer.Platform
{
    public interface IInputSimulator : IDisposable
    {
        void ProcessPacket(int playerIndex, byte[] payload);
    }
}
