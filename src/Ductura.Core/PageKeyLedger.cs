using System.Text.Json;

namespace Ductura.Core;

/// <summary>Exam mode, keys that reached the page without passing the keyboard hook. PROPOSAL P-4 (status PRIJEDLOG),
/// built and tested on invented input only. A program can post WM_KEYDOWN or WM_CHAR straight to the WebView2 window
/// (PostMessage): the low-level hook never sees such a key, so it cannot drop it. A script the app adds to every page
/// counts the keys the page received; every key the hook let through pays for one of them, and a page key nothing
/// paid for is unaccounted. Two numbers and the excess; no key, no time, no program.</summary>
public sealed class PageKeyLedger
{
    /// <summary>How many keys the hook let through may still be on their way to the page. Keys the browser keeps
    /// (shortcuts) never arrive, so without a limit they would pay for keys a program posts later.</summary>
    public const int MaxInFlight = 16;

    private readonly object _lock = new();
    private int _hook, _page, _credit, _unaccounted, _reported;

    public int HookKeys { get { lock (_lock) return _hook; } }
    public int PageKeyCount { get { lock (_lock) return _page; } }
    public int Unaccounted { get { lock (_lock) return _unaccounted; } }

    /// <summary>A key the exam hook let through while Ductura was in the foreground. Called on the hook thread.</summary>
    public void HookPassed(bool released)
    {
        if (released) return;
        lock (_lock)
        {
            _hook++;
            if (_credit < MaxInFlight) _credit++;
        }
    }

    /// <summary>Keys the page received, from the added script (<see cref="PageKeysMessage"/>).</summary>
    public void PageKeys(int count)
    {
        if (count <= 0) return;
        lock (_lock)
        {
            _page += count;
            var paid = Math.Min(count, _credit);
            _credit -= paid;
            _unaccounted += count - paid;
        }
    }

    /// <summary>The message for the page when the excess grew since the last one, so exam mode can record it.</summary>
    public bool TakeReport(out string json)
    {
        lock (_lock)
        {
            json = $"{{\"type\":\"ductura-keys-unaccounted\",\"count\":{_unaccounted}}}";
            if (_unaccounted <= _reported) return false;
            _reported = _unaccounted;
            return true;
        }
    }

    /// <summary>Added to every document before its own scripts run (AddScriptToExecuteOnDocumentCreatedAsync).
    /// Counts keys Windows delivered (isTrusted), in the capture phase on window, so the page cannot stop it first. A
    /// keydown is one key; a keypress (WM_CHAR) is the same key unless no keydown came before it since the last keyup,
    /// which is what a posted WM_CHAR looks like. Sends only counts, batched once per task.</summary>
    public const string Script = """
        (() => {
          if (window !== window.top || !window.chrome || !window.chrome.webview) return;
          const webview = window.chrome.webview;
          const post = webview.postMessage.bind(webview);
          let pending = false, count = 0, queued = false;
          const flush = () => { queued = false; if (count > 0) { const n = count; count = 0; post({ type: 'ductura-page-keys', count: n }); } };
          const add = () => { count++; if (!queued) { queued = true; setTimeout(flush, 0); } };
          window.addEventListener('keydown', e => { if (e.isTrusted) { pending = true; add(); } }, true);
          window.addEventListener('keypress', e => { if (!e.isTrusted) return; if (pending) pending = false; else add(); }, true);
          window.addEventListener('keyup', e => { if (e.isTrusted) pending = false; }, true);
        })();
        """;
}

/// <summary>{"type":"ductura-page-keys","count":n} from the added script, accepted only from Ductura's own web
/// address (the configured URL as a prefix), like <see cref="ExamMessage"/>.</summary>
public static class PageKeysMessage
{
    public const string Type = "ductura-page-keys";
    /// <summary>More than this in one batch is not typing.</summary>
    public const int MaxBatch = 100000;

    public static int? Parse(string json, string sourceUri, WebAddress web)
    {
        if (string.IsNullOrEmpty(sourceUri) || !sourceUri.StartsWith(web.Url, StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != Type) return null;
            if (!root.TryGetProperty("count", out var c) || c.ValueKind != JsonValueKind.Number || !c.TryGetInt32(out var n)) return null;
            return n is >= 1 and <= MaxBatch ? n : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
