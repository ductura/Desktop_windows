using System.Runtime.InteropServices;

namespace Ductura.Desktop;

/// <summary>Low-level keyboard hook (WH_KEYBOARD_LL), installed only while exam mode is on (PROPOSAL P-4).
/// Windows calls it before any window receives the key; returning 1 drops the key. It runs on its own thread
/// with its own message loop: Windows silently removes a hook that does not answer within LowLevelHooksTimeout,
/// and the UI thread is busy while WebView2 starts. Windows does not call it while this process has Raw Input
/// registered, so Raw Input is off while it runs (KeyOriginTally.RecordHookKey).</summary>
internal sealed class LowLevelKeyboard : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        // MSG ends with lPrivate on Windows 10 and later; without it GetMessage writes past the struct.
        public uint Private;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    /// <summary>(flags, released) → true to drop the key. Called on the hook thread.</summary>
    private readonly Func<uint, bool, bool> _drop;
    // Kept in a field: the delegate must live as long as the hook, or the garbage collector frees it.
    private readonly HookProc _proc;
    private readonly Thread _thread;
    private IntPtr _hook;
    private uint _threadId;

    public LowLevelKeyboard(Func<uint, bool, bool> drop)
    {
        _drop = drop;
        _proc = Callback;
        var ready = new ManualResetEventSlim();
        var error = 0;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) error = Marshal.GetLastWin32Error();
            ready.Set();
            if (_hook == IntPtr.Zero) return;
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(_hook);
        }) { IsBackground = true, Name = "Ductura exam keyboard hook" };
        _thread.Start();
        ready.Wait();
        if (_hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(error);
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var released = (int)wParam is WmKeyUp or WmSysKeyUp;
            if (_drop(key.Flags, released)) return 1;
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId == 0) return;
        PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _threadId = 0;
    }
}
