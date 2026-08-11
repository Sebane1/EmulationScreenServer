using System;
using System.IO;
using System.Text;
using Avalonia.Threading;

namespace EmulationScreenServer
{
    public class UiLogWriter : TextWriter
    {
        private readonly Action<string> _onLogMessage;

        public UiLogWriter(Action<string> onLogMessage)
        {
            _onLogMessage = onLogMessage;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            if (value == null) return;
            Dispatcher.UIThread.Post(() => _onLogMessage(value + Environment.NewLine));
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            Dispatcher.UIThread.Post(() => _onLogMessage(value));
        }
    }
}
