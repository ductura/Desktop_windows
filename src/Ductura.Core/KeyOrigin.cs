namespace Ductura.Core;

/// <summary>Where a key came from, as Windows Raw Input tells it. PROPOSAL P-3 (docs/ductus/USTAV-DOPUNA-P3.md in
/// ductura/.github, status PRIJEDLOG): built and tested on invented input only, not shown to anyone.</summary>
public enum KeyOrigin
{
    /// <summary>A keyboard device: a real keyboard, or anything that presents itself as one over USB.</summary>
    Device,
    /// <summary>Sent by a program (SendInput): an automation tool, but also the on-screen keyboard and some
    /// assistive tools, which Windows does not tell apart.</summary>
    Program,
}

/// <summary>Counts of key presses in the Ductura window, and nothing else: no key, no time, no program.</summary>
public sealed class KeyOriginTally
{
    // Raw Input records on the UI thread, the exam hook on its own thread.
    private int _device, _program;
    public int DeviceKeys => Volatile.Read(ref _device);
    public int ProgramKeys => Volatile.Read(ref _program);

    /// <summary>A key from Raw Input. <paramref name="device"/> is RAWINPUTHEADER.hDevice: zero for a key that a
    /// program sent. Only presses count; a release (RI_KEY_BREAK) is the same key again.</summary>
    public void Record(IntPtr device, bool released)
    {
        if (released) return;
        Count(Classify(device));
    }

    /// <summary>A key the exam hook let through (WH_KEYBOARD_LL). While the hook is on, Raw Input is off: Windows does
    /// not call a low-level hook in a process that also has Raw Input registered (found in the red-team run), so the
    /// hook counts instead. LLKHF_INJECTED is the same fact as a zero Raw Input device: a program sent the key.</summary>
    public void RecordHookKey(uint hookFlags, bool released)
    {
        if (released) return;
        Count((hookFlags & ExamKeyGate.InjectedFlag) != 0 ? KeyOrigin.Program : KeyOrigin.Device);
    }

    private void Count(KeyOrigin origin)
    {
        if (origin == KeyOrigin.Device) Interlocked.Increment(ref _device);
        else Interlocked.Increment(ref _program);
    }

    public static KeyOrigin Classify(IntPtr device) => device == IntPtr.Zero ? KeyOrigin.Program : KeyOrigin.Device;

    /// <summary>The two numbers as one JSON line, for the red-team test only.</summary>
    public string ToJson() => $"{{\"deviceKeys\":{DeviceKeys},\"programKeys\":{ProgramKeys}}}";

    /// <summary>The same, with the number of keys exam mode dropped before they reached the window (--exam-gate).</summary>
    public string ToJson(int blockedKeys) =>
        $"{{\"deviceKeys\":{DeviceKeys},\"programKeys\":{ProgramKeys},\"blockedKeys\":{blockedKeys}}}";
}

/// <summary>Rule R-5 of P-3: keys sent by a program. The thresholds are PLACEHOLDERS until samples (P-2 §5).</summary>
public static class KeysFromProgramRule
{
    /// <summary>PLACEHOLDER: at least this many keys from a program ...</summary>
    public const int MinProgramKeys = 50;
    /// <summary>PLACEHOLDER: ... and at least this share of all keys.</summary>
    public const double MinProgramShare = 0.2;

    /// <summary>Whether R-5 fires for a writing session. Never with an approved accommodation or a reported
    /// assistive tool (P-3 §4), never outside a task the teacher set for the desktop app.</summary>
    public static bool Fires(KeyOriginTally tally, bool desktopTask, bool assistiveToolOrAccommodation)
    {
        if (!desktopTask || assistiveToolOrAccommodation) return false;
        var total = tally.DeviceKeys + tally.ProgramKeys;
        return tally.ProgramKeys >= MinProgramKeys && total > 0 && (double)tally.ProgramKeys / total >= MinProgramShare;
    }
}
