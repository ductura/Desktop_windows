using System.Text.Json;

namespace Ductura.Core;

/// <summary>Exam mode in the desktop app. PROPOSAL P-4 (status PRIJEDLOG), built and tested on invented input only.
/// While an exam runs, a key that a program sent (SendInput, flag LLKHF_INJECTED in the low-level keyboard hook)
/// is dropped before it reaches the editor, so the page never sees it. Only while Ductura is the foreground window,
/// and never with an approved accommodation: the on-screen keyboard and dictation tools send keys the same way.
/// Keeps one number, how many keys were dropped; no key, no time, no program.</summary>
public sealed class ExamKeyGate
{
    /// <summary>KBDLLHOOKSTRUCT.flags LLKHF_INJECTED: the key came from SendInput or keybd_event.</summary>
    public const uint InjectedFlag = 0x10;

    // Set on the UI thread, read on the keyboard hook thread.
    private readonly object _lock = new();
    private bool _active, _accommodation;
    private int _blocked;

    public bool Active { get { lock (_lock) return _active; } }
    public bool Accommodation { get { lock (_lock) return _accommodation; } }
    public int BlockedKeys { get { lock (_lock) return _blocked; } }

    public void Set(bool on, bool accommodation)
    {
        lock (_lock) { _active = on; _accommodation = accommodation; }
    }

    /// <summary>Whether the hook drops this key. A release of a dropped key is dropped too, but counts once.</summary>
    public bool Block(uint hookFlags, bool released, bool foreground)
    {
        lock (_lock)
        {
            if (!_active || _accommodation || !foreground || (hookFlags & InjectedFlag) == 0) return false;
            if (!released) _blocked++;
            return true;
        }
    }

    /// <summary>The message the page receives, so exam mode can record the refusals.</summary>
    public string BlockedJson() => $"{{\"type\":\"ductura-keys-blocked\",\"count\":{BlockedKeys}}}";
}

/// <summary>The page turns exam mode on and off: {"type":"ductura-exam","on":bool,"accommodation":bool}.
/// Accepted only from Ductura's own web address (the configured URL as a prefix), never from another site.</summary>
public sealed record ExamMessage(bool On, bool Accommodation)
{
    public const string Type = "ductura-exam";

    public static ExamMessage? Parse(string json, string sourceUri, WebAddress web)
    {
        if (string.IsNullOrEmpty(sourceUri) || !sourceUri.StartsWith(web.Url, StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != Type) return null;
            if (!TryBool(root, "on", out var on) || !TryBool(root, "accommodation", out var accommodation)) return null;
            return new ExamMessage(on, accommodation);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryBool(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var e) || e.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = e.GetBoolean();
        return true;
    }
}

/// <summary>Environment variables through which another program can change how WebView2 starts, most of all
/// WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port, which lets it type into the page over the
/// DevTools protocol, past any keyboard hook. Removed from this process before WebView2 starts.</summary>
public static class WebViewHardening
{
    public static readonly IReadOnlyList<string> Variables =
    [
        "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
        "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER",
        "WEBVIEW2_USER_DATA_FOLDER",
        "WEBVIEW2_RELEASE_CHANNEL_PREFERENCE",
        "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER",
    ];

    /// <summary>Clears every variable that is set; returns the names it cleared (never their values).</summary>
    public static IReadOnlyList<string> Scrub(Func<string, string?> get, Action<string, string?> set)
    {
        var removed = new List<string>();
        foreach (var name in Variables)
        {
            if (string.IsNullOrEmpty(get(name))) continue;
            set(name, null);
            removed.Add(name);
        }
        return removed;
    }
}
