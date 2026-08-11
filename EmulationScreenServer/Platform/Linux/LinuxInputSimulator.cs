using System;
using System.Runtime.InteropServices;
using static EmulationScreenServer.Platform.Linux.LinuxUinputInterop;

namespace EmulationScreenServer.Platform.Linux
{
    public class LinuxInputSimulator : IInputSimulator
    {
        private int[] _gamepadFds = new int[4];
        private int _mouseFd = -1;

        private readonly ushort[] _lastButtons = new ushort[4];
        private bool _disposed;

        public LinuxInputSimulator(bool enableMouse = true, bool enableController = true)
        {
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    _gamepadFds[i] = -1;
                }

                if (enableController)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        _gamepadFds[i] = CreateGamepad($"EmulationScreen Virtual Xbox Pad {i}");
                    }
                    Console.WriteLine("[Input] 4x Linux uinput virtual gamepads created.");
                }

                if (enableMouse)
                {
                    _mouseFd = CreateMouse("EmulationScreen Virtual Mouse");
                    Console.WriteLine("[Input] Linux uinput virtual mouse created.");
                }
            }
            catch (Exception ex)
            {
                Dispose();
                Console.WriteLine($"[Input] Failed to initialize uinput: {ex.Message}. Make sure you run as root or have udev permissions for /dev/uinput!");
                throw;
            }
        }

        private int CreateGamepad(string name)
        {
            int fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
            if (fd < 0) throw new InvalidOperationException($"Cannot open /dev/uinput for gamepad. Error code: {Marshal.GetLastWin32Error()}");

            // Enable Buttons
            ioctl(fd, UI_SET_EVBIT, EV_KEY);
            ioctl(fd, UI_SET_KEYBIT, BTN_A);
            ioctl(fd, UI_SET_KEYBIT, BTN_B);
            ioctl(fd, UI_SET_KEYBIT, BTN_X);
            ioctl(fd, UI_SET_KEYBIT, BTN_Y);
            ioctl(fd, UI_SET_KEYBIT, BTN_TL);
            ioctl(fd, UI_SET_KEYBIT, BTN_TR);
            ioctl(fd, UI_SET_KEYBIT, BTN_SELECT);
            ioctl(fd, UI_SET_KEYBIT, BTN_START);
            ioctl(fd, UI_SET_KEYBIT, BTN_THUMBL);
            ioctl(fd, UI_SET_KEYBIT, BTN_THUMBR);
            
            // Enable D-Pad (as buttons, or as axes. Linux usually uses axes for D-PAD)
            ioctl(fd, UI_SET_EVBIT, EV_ABS);
            ioctl(fd, UI_SET_ABSBIT, ABS_HAT0X);
            ioctl(fd, UI_SET_ABSBIT, ABS_HAT0Y);

            // Enable Analog Sticks and Triggers
            ioctl(fd, UI_SET_ABSBIT, ABS_X);
            ioctl(fd, UI_SET_ABSBIT, ABS_Y);
            ioctl(fd, UI_SET_ABSBIT, ABS_Z); // LT
            ioctl(fd, UI_SET_ABSBIT, ABS_RX);
            ioctl(fd, UI_SET_ABSBIT, ABS_RY);
            ioctl(fd, UI_SET_ABSBIT, ABS_RZ); // RT

            uinput_user_dev dev = new uinput_user_dev();
            dev.name = name;
            dev.id_bustype = 3; // BUS_USB
            dev.id_vendor = 0x045E; // Microsoft
            dev.id_product = 0x028E; // Xbox 360 Controller
            dev.id_version = 1;

            dev.absmin = new int[64];
            dev.absmax = new int[64];
            dev.absfuzz = new int[64];
            dev.absflat = new int[64];

            // Thumbsticks (-32768 to 32767)
            dev.absmin[ABS_X] = -32768; dev.absmax[ABS_X] = 32767; dev.absflat[ABS_X] = 128; dev.absfuzz[ABS_X] = 16;
            dev.absmin[ABS_Y] = -32768; dev.absmax[ABS_Y] = 32767; dev.absflat[ABS_Y] = 128; dev.absfuzz[ABS_Y] = 16;
            dev.absmin[ABS_RX] = -32768; dev.absmax[ABS_RX] = 32767; dev.absflat[ABS_RX] = 128; dev.absfuzz[ABS_RX] = 16;
            dev.absmin[ABS_RY] = -32768; dev.absmax[ABS_RY] = 32767; dev.absflat[ABS_RY] = 128; dev.absfuzz[ABS_RY] = 16;

            // Triggers (0 to 255)
            dev.absmin[ABS_Z] = 0; dev.absmax[ABS_Z] = 255;
            dev.absmin[ABS_RZ] = 0; dev.absmax[ABS_RZ] = 255;

            // D-Pad (-1 to 1)
            dev.absmin[ABS_HAT0X] = -1; dev.absmax[ABS_HAT0X] = 1;
            dev.absmin[ABS_HAT0Y] = -1; dev.absmax[ABS_HAT0Y] = 1;

            WriteUinputDev(fd, ref dev);
            ioctl(fd, UI_DEV_CREATE, 0);

            return fd;
        }

        private int CreateMouse(string name)
        {
            int fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
            if (fd < 0) throw new InvalidOperationException($"Cannot open /dev/uinput for mouse. Error code: {Marshal.GetLastWin32Error()}");

            ioctl(fd, UI_SET_EVBIT, EV_KEY);
            ioctl(fd, UI_SET_KEYBIT, BTN_LEFT);
            ioctl(fd, UI_SET_KEYBIT, BTN_RIGHT);

            ioctl(fd, UI_SET_EVBIT, EV_REL);
            ioctl(fd, UI_SET_RELBIT, REL_WHEEL);

            ioctl(fd, UI_SET_EVBIT, EV_ABS); // Using ABS for direct position mapping like Windows InputSimulator
            ioctl(fd, UI_SET_ABSBIT, ABS_X);
            ioctl(fd, UI_SET_ABSBIT, ABS_Y);

            uinput_user_dev dev = new uinput_user_dev();
            dev.name = name;
            dev.id_bustype = 3;
            dev.id_vendor = 0x1234;
            dev.id_product = 0x5678;
            dev.id_version = 1;

            dev.absmin = new int[64];
            dev.absmax = new int[64];
            dev.absfuzz = new int[64];
            dev.absflat = new int[64];

            // Map absolute screen coords to 65535 like Windows InputSimulator
            dev.absmin[ABS_X] = 0; dev.absmax[ABS_X] = 65535;
            dev.absmin[ABS_Y] = 0; dev.absmax[ABS_Y] = 65535;

            WriteUinputDev(fd, ref dev);
            ioctl(fd, UI_DEV_CREATE, 0);

            return fd;
        }

        private unsafe void WriteUinputDev(int fd, ref uinput_user_dev dev)
        {
            int size = Marshal.SizeOf(dev);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(dev, ptr, false);
                write(fd, ptr.ToPointer(), size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public void ProcessPacket(int playerIndex, byte[] data)
        {
            if (_disposed) return;

            if (playerIndex == 4)
            {
                ProcessMouseData(data);
                return;
            }

            if (playerIndex >= 0 && playerIndex < 4)
            {
                ProcessGamepadData(playerIndex, data);
            }
        }

        private void ProcessMouseData(byte[] data)
        {
            if (_mouseFd < 0) return;

            double normX = data[4] / 255.0;
            double normY = data[5] / 255.0;
            int absoluteX = (int)(normX * 65535.0);
            int absoluteY = (int)((1.0 - normY) * 65535.0);

            EmitEvent(_mouseFd, EV_ABS, ABS_X, absoluteX);
            EmitEvent(_mouseFd, EV_ABS, ABS_Y, absoluteY);

            byte mouseMask = data[7];
            bool lmb = (mouseMask & 0x01) != 0;
            bool rmb = (mouseMask & 0x02) != 0;

            EmitEvent(_mouseFd, EV_KEY, BTN_LEFT, lmb ? 1 : 0);
            EmitEvent(_mouseFd, EV_KEY, BTN_RIGHT, rmb ? 1 : 0);

            sbyte scroll = unchecked((sbyte)data[6]);
            if (scroll != 0)
            {
                EmitEvent(_mouseFd, EV_REL, REL_WHEEL, Math.Sign(scroll));
            }

            EmitEvent(_mouseFd, EV_SYN, SYN_REPORT, 0);
        }

        private void ProcessGamepadData(int index, byte[] data)
        {
            int fd = _gamepadFds[index];
            if (fd < 0) return;

            ushort buttons = BitConverter.ToUInt16(data, 0);

            EmitButtonChange(index, fd, BTN_A, buttons, 0x0001);
            EmitButtonChange(index, fd, BTN_B, buttons, 0x0002);
            EmitButtonChange(index, fd, BTN_X, buttons, 0x0004);
            EmitButtonChange(index, fd, BTN_Y, buttons, 0x0008);
            
            EmitButtonChange(index, fd, BTN_TL, buttons, 0x0010);
            EmitButtonChange(index, fd, BTN_TR, buttons, 0x0020);
            EmitButtonChange(index, fd, BTN_THUMBL, buttons, 0x0040);
            EmitButtonChange(index, fd, BTN_THUMBR, buttons, 0x0080);
            
            EmitButtonChange(index, fd, BTN_START, buttons, 0x0100);
            EmitButtonChange(index, fd, BTN_SELECT, buttons, 0x0200);

            // D-Pad to axes mapping
            int hatX = ((buttons & 0x2000) != 0 ? 1 : 0) - ((buttons & 0x1000) != 0 ? 1 : 0);
            int hatY = ((buttons & 0x0800) != 0 ? 1 : 0) - ((buttons & 0x0400) != 0 ? 1 : 0);
            EmitEvent(fd, EV_ABS, ABS_HAT0X, hatX);
            EmitEvent(fd, EV_ABS, ABS_HAT0Y, hatY);

            // Triggers
            EmitEvent(fd, EV_ABS, ABS_Z, data[2]);
            EmitEvent(fd, EV_ABS, ABS_RZ, data[3]);

            // Sticks
            sbyte lx = unchecked((sbyte)data[4]);
            sbyte ly = unchecked((sbyte)data[5]);
            sbyte rx = unchecked((sbyte)data[6]);
            sbyte ry = unchecked((sbyte)data[7]);

            EmitEvent(fd, EV_ABS, ABS_X, lx * 256);
            EmitEvent(fd, EV_ABS, ABS_Y, ly * 256);
            EmitEvent(fd, EV_ABS, ABS_RX, rx * 256);
            EmitEvent(fd, EV_ABS, ABS_RY, ry * 256);

            _lastButtons[index] = buttons;
            EmitEvent(fd, EV_SYN, SYN_REPORT, 0);
        }

        private void EmitButtonChange(int index, int fd, ushort btnCode, ushort currentButtons, ushort mask)
        {
            bool wasDown = (_lastButtons[index] & mask) != 0;
            bool isDown = (currentButtons & mask) != 0;

            if (wasDown != isDown)
            {
                EmitEvent(fd, EV_KEY, btnCode, isDown ? 1 : 0);
            }
        }

        private unsafe void EmitEvent(int fd, ushort type, ushort code, int value)
        {
            var ev = new input_event
            {
                type = type,
                code = code,
                value = value
            };

            int size = Marshal.SizeOf(ev);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(ev, ptr, false);
                write(fd, ptr.ToPointer(), size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = 0; i < 4; i++)
            {
                if (_gamepadFds[i] >= 0)
                {
                    try { ioctl(_gamepadFds[i], UI_DEV_DESTROY, 0); } catch { }
                    try { close(_gamepadFds[i]); } catch { }
                    _gamepadFds[i] = -1;
                }
            }

            if (_mouseFd >= 0)
            {
                try { ioctl(_mouseFd, UI_DEV_DESTROY, 0); } catch { }
                try { close(_mouseFd); } catch { }
                _mouseFd = -1;
            }
        }
    }
}
