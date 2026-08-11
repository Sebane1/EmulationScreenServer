using System;
using System.Runtime.InteropServices;

namespace EmulationScreenServer.Platform.Linux
{
    public static class LinuxUinputInterop
    {
        // --- libc P/Invoke ---
        [DllImport("libc", SetLastError = true)]
        public static extern int open(string pathname, int flags);

        [DllImport("libc", SetLastError = true)]
        public static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        public static extern int ioctl(int fd, ulong request, int arg);

        [DllImport("libc", SetLastError = true)]
        public static extern unsafe nint write(int fd, void* buf, nint count);

        // --- File Flags ---
        public const int O_WRONLY = 0x01;
        public const int O_NONBLOCK = 0x0800;

        // --- Event Types ---
        public const ushort EV_SYN = 0x00;
        public const ushort EV_KEY = 0x01;
        public const ushort EV_REL = 0x02;
        public const ushort EV_ABS = 0x03;

        public const ushort SYN_REPORT = 0;

        // --- Controller Buttons (Xbox 360 mapping) ---
        public const ushort BTN_A = 0x130;
        public const ushort BTN_B = 0x131;
        public const ushort BTN_X = 0x133;
        public const ushort BTN_Y = 0x134;
        public const ushort BTN_TL = 0x136;
        public const ushort BTN_TR = 0x137;
        public const ushort BTN_SELECT = 0x13a;
        public const ushort BTN_START = 0x13b;
        public const ushort BTN_THUMBL = 0x13d;
        public const ushort BTN_THUMBR = 0x13e;
        
        // D-PAD is often mapped as axes on Linux Gamepads
        public const ushort ABS_HAT0X = 0x10;
        public const ushort ABS_HAT0Y = 0x11;

        // --- Controller Axes ---
        public const ushort ABS_X = 0x00; // Left Thumb X
        public const ushort ABS_Y = 0x01; // Left Thumb Y
        public const ushort ABS_Z = 0x02; // Left Trigger
        public const ushort ABS_RX = 0x03; // Right Thumb X
        public const ushort ABS_RY = 0x04; // Right Thumb Y
        public const ushort ABS_RZ = 0x05; // Right Trigger

        // --- Mouse / Keys ---
        public const ushort BTN_LEFT = 0x110;
        public const ushort BTN_RIGHT = 0x111;
        public const ushort REL_X = 0x00;
        public const ushort REL_Y = 0x01;
        public const ushort REL_WHEEL = 0x08;

        // --- Uinput IOCTL Macros ---
        public static class Ioctl
        {
            public const uint _IOC_NRBITS = 8;
            public const uint _IOC_TYPEBITS = 8;
            public const uint _IOC_SIZEBITS = 14;
            public const uint _IOC_DIRBITS = 2;

            public const uint _IOC_NRSHIFT = 0;
            public const uint _IOC_TYPESHIFT = _IOC_NRSHIFT + _IOC_NRBITS;
            public const uint _IOC_SIZESHIFT = _IOC_TYPESHIFT + _IOC_TYPEBITS;
            public const uint _IOC_DIRSHIFT = _IOC_SIZESHIFT + _IOC_SIZEBITS;

            public const uint _IOC_NONE = 0U;
            public const uint _IOC_WRITE = 1U;

            public static ulong _IOC(uint dir, uint type, uint nr, uint size)
            {
                return (dir << (int)_IOC_DIRSHIFT) |
                       (type << (int)_IOC_TYPESHIFT) |
                       (nr << (int)_IOC_NRSHIFT) |
                       (size << (int)_IOC_SIZESHIFT);
            }

            public static ulong _IO(uint type, uint nr) { return _IOC(_IOC_NONE, type, nr, 0); }
            public static ulong _IOW(uint type, uint nr, uint size) { return _IOC(_IOC_WRITE, type, nr, size); }
        }

        // --- Uinput Specific IOCTLs ---
        public static ulong UI_SET_EVBIT   => Ioctl._IOW('U', 100, 4);
        public static ulong UI_SET_KEYBIT  => Ioctl._IOW('U', 101, 4);
        public static ulong UI_SET_RELBIT  => Ioctl._IOW('U', 102, 4);
        public static ulong UI_SET_ABSBIT  => Ioctl._IOW('U', 103, 4);
        public static ulong UI_DEV_CREATE  => Ioctl._IO('U', 1);
        public static ulong UI_DEV_DESTROY => Ioctl._IO('U', 2);

        // --- Structs ---
        [StructLayout(LayoutKind.Sequential)]
        public struct input_event
        {
            public long time_sec;  // 8 bytes on 64-bit
            public long time_usec; // 8 bytes on 64-bit
            public ushort type;    // 2 bytes
            public ushort code;    // 2 bytes
            public int value;      // 4 bytes
        }

        // UINPUT_MAX_NAME_SIZE is 80
        // ABS_CNT is 64
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct uinput_user_dev
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string name;
            
            public ushort id_bustype; // BUS_USB = 3
            public ushort id_vendor;
            public ushort id_product;
            public ushort id_version;

            public int ff_effects_max;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public int[] absmax;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public int[] absmin;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public int[] absfuzz;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public int[] absflat;
        }
    }
}
