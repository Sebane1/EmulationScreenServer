using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using WindowsInput;
using WindowsInput.Native;

using EmulationScreenServer.Platform;

namespace EmulationScreenServer.Platform.Windows
{
    public class WindowsInputSimulator : IInputSimulator
    {
        private readonly ViGEmClient? _client;
        private readonly IXbox360Controller[]? _controllers;
        private readonly InputSimulator? _inputSimulator;

        // Resolution for absolute mouse mapping
        private int _screenWidth;
        private int _screenHeight;
        private bool _isLmbDown = false;
        private bool _isRmbDown = false;

        public WindowsInputSimulator(bool enableMouse = true, bool enableController = true)
        {
            try 
            {
                if (enableController)
                {
                    _client = new ViGEmClient();
                    _controllers = new IXbox360Controller[4];

                    for (int i = 0; i < 4; i++)
                    {
                        _controllers[i] = _client.CreateXbox360Controller();
                        _controllers[i].Connect();
                    }

                    Console.WriteLine("[Input] 4x Virtual Xbox 360 Controllers connected.");
                }

                if (enableMouse)
                {
                    _inputSimulator = new InputSimulator();

                    // Basic desktop resolution capture (Assumes primary monitor)
                    var primary = System.Windows.Forms.Screen.PrimaryScreen;
                    _screenWidth = primary?.Bounds.Width ?? 1920;
                    _screenHeight = primary?.Bounds.Height ?? 1080;

                    Console.WriteLine($"[Input] Mouse forwarding enabled. Boundaries: {_screenWidth}x{_screenHeight}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Input] Failed to initialize input devices: {ex.Message}. Make sure ViGEmBus is installed if using controller forwarding!");
                throw;
            }
        }

        public void ProcessPacket(int playerIndex, byte[] data)
        {
            // PlayerIndex 4 is reserved for Mouse/KB
            if (playerIndex == 4)
            {
                ProcessMouseData(data);
                return;
            }

            if (playerIndex >= 0 && playerIndex < 4)
            {
                ProcessGamepadData((byte)playerIndex, data);
            }
        }

        private void ProcessMouseData(byte[] data)
        {
            if (_inputSimulator == null) return;

            // data array offset adjusted for the payload inside listener (0-7 instead of 5-12)
            // Original Packet: [5-6]: btnMask, [7]: LT, [8]: RT, [9]: LX/MouseX, [10]: LY/MouseY, [11]: RX/MouseBtnMask
            
            // Mouse X (0-255 mapped to 0-65535 absolute)
            double normX = data[4] / 255.0;
            double normY = data[5] / 255.0;

            // InputSimulator requires 0-65535 range mapping
            double absoluteX = normX * 65535.0;
            double absoluteY = normY * 65535.0;

            // Invert Y because Unity's UV/Local bounds might be bottom-left, but Windows is top-left
            absoluteY = 65535.0 - absoluteY;

            _inputSimulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absoluteX, absoluteY);

            // Mouse Buttons
            byte mouseMask = data[7];
            bool lmb = (mouseMask & 0x01) != 0;
            bool rmb = (mouseMask & 0x02) != 0;

            if (lmb && !_isLmbDown) { _inputSimulator.Mouse.LeftButtonDown(); _isLmbDown = true; }
            if (!lmb && _isLmbDown) { _inputSimulator.Mouse.LeftButtonUp(); _isLmbDown = false; }
            
            if (rmb && !_isRmbDown) { _inputSimulator.Mouse.RightButtonDown(); _isRmbDown = true; }
            if (!rmb && _isRmbDown) { _inputSimulator.Mouse.RightButtonUp(); _isRmbDown = false; }

            sbyte scroll = unchecked((sbyte)data[6]);
            if (scroll != 0)
            {
                _inputSimulator.Mouse.VerticalScroll(Math.Sign(scroll));
            }
        }

        private void ProcessGamepadData(byte index, byte[] data)
        {
            if (_controllers == null) return;

            var controller = _controllers[index];

            ushort buttons = BitConverter.ToUInt16(data, 0); // [5-6]
            
            controller.SetButtonState(Xbox360Button.A, (buttons & 0x0001) != 0);
            controller.SetButtonState(Xbox360Button.B, (buttons & 0x0002) != 0);
            controller.SetButtonState(Xbox360Button.X, (buttons & 0x0004) != 0);
            controller.SetButtonState(Xbox360Button.Y, (buttons & 0x0008) != 0);
            
            controller.SetButtonState(Xbox360Button.LeftShoulder,  (buttons & 0x0010) != 0);
            controller.SetButtonState(Xbox360Button.RightShoulder, (buttons & 0x0020) != 0);
            controller.SetButtonState(Xbox360Button.LeftThumb,     (buttons & 0x0040) != 0);
            controller.SetButtonState(Xbox360Button.RightThumb,    (buttons & 0x0080) != 0);
            
            controller.SetButtonState(Xbox360Button.Start, (buttons & 0x0100) != 0);
            controller.SetButtonState(Xbox360Button.Back,  (buttons & 0x0200) != 0);
            
            controller.SetButtonState(Xbox360Button.Up,    (buttons & 0x0400) != 0);
            controller.SetButtonState(Xbox360Button.Down,  (buttons & 0x0800) != 0);
            controller.SetButtonState(Xbox360Button.Left,  (buttons & 0x1000) != 0);
            controller.SetButtonState(Xbox360Button.Right, (buttons & 0x2000) != 0);

            controller.SetSliderValue(Xbox360Slider.LeftTrigger,  data[2]); // [7]
            controller.SetSliderValue(Xbox360Slider.RightTrigger, data[3]); // [8]

            // Sticks (-128 to 127 mapping to short -32768 to 32767)
            sbyte lx = unchecked((sbyte)data[4]); // [9]
            sbyte ly = unchecked((sbyte)data[5]); // [10]
            sbyte rx = unchecked((sbyte)data[6]); // [11]
            sbyte ry = unchecked((sbyte)data[7]); // [12]

            controller.SetAxisValue(Xbox360Axis.LeftThumbX,  (short)(lx * 256));
            controller.SetAxisValue(Xbox360Axis.LeftThumbY,  (short)(ly * 256));
            controller.SetAxisValue(Xbox360Axis.RightThumbX, (short)(rx * 256));
            controller.SetAxisValue(Xbox360Axis.RightThumbY, (short)(ry * 256));
        }

        public void Dispose()
        {
            if (_controllers != null)
            {
                foreach (var c in _controllers)
                {
                    c?.Disconnect();
                }
            }
            _client?.Dispose();
        }
    }
}
