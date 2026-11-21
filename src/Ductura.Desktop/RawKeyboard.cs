using System.Runtime.InteropServices;

namespace Ductura.Desktop;

/// <summary>Raw Input for the keyboard, only for this window and only while it is in the foreground
/// (no RIDEV_INPUTSINK, no global hook). PROPOSAL P-3: used only with --key-tally-out.</summary>
internal static class RawKeyboard
{
    public const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeKeyboard = 1;
    private const ushort RiKeyBreak = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboardData
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputKeyboard
    {
        public RawInputHeader Header;
        public RawKeyboardData Keyboard;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, out RawInputKeyboard data, ref uint size, uint headerSize);

    /// <summary>Generic desktop page, keyboard usage, delivered to <paramref name="window"/>.</summary>
    public static bool Register(IntPtr window) =>
        RegisterRawInputDevices(
            [new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = 0, Target = window }],
            1, (uint)Marshal.SizeOf<RawInputDevice>());

    /// <summary>The device handle (zero for a key a program sent) and whether it is a release.</summary>
    public static (IntPtr Device, bool Released)? Read(IntPtr lParam)
    {
        var size = (uint)Marshal.SizeOf<RawInputKeyboard>();
        var read = GetRawInputData(lParam, RidInput, out var data, ref size, (uint)Marshal.SizeOf<RawInputHeader>());
        if (read == uint.MaxValue || data.Header.Type != RimTypeKeyboard) return null;
        return (data.Header.Device, (data.Keyboard.Flags & RiKeyBreak) != 0);
    }
}
