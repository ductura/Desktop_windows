using Ductura.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Ductura.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);
        var web = WebAddress.FromAssembly(typeof(WebAddress).Assembly);
        if (options.Smoke)
        {
            // A package with an invalid web address must not pass the functional test.
            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var (exitCode, line) = Startup.Smoke(web, version);
            if (line is not null && options.SmokeOutput is { } path) File.WriteAllText(path, line);
            return exitCode;
        }

        ApplicationConfiguration.Initialize();
        var form = new MainForm(options, web);
        Application.Run(form);
        return form.ExitCode;
    }
}

internal sealed class MainForm : Form
{
    // --shell-smoke-out: the window reports what it showed and closes; no report within the limit is exit code 2.
    private static readonly TimeSpan SmokeLimit = TimeSpan.FromSeconds(90);
    private readonly string? _smokeOut;
    private readonly LaunchOptions _options;
    private bool _reported;
    private readonly KeyOriginTally? _keys;
    private readonly WebAddress? _web;
    // P-4 exam mode (PROPOSAL): the page turns it on; --exam-gate turns it on from the start.
    private readonly ExamKeyGate _exam = new();
    private LowLevelKeyboard? _hook;
    private WebView2? _webView;
    private int _blockedReportPending;
    // The window handle for the hook thread, which must not touch the form itself.
    private IntPtr _window;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _window = Handle;
        if (_keys is not null) RawKeyboard.Register(Handle);
        if (_options.ExamGate) SetExam(on: true, accommodation: false);
    }

    private void SetExam(bool on, bool accommodation)
    {
        _exam.Set(on, accommodation);
        var hook = on && !accommodation;
        if (hook && _hook is null)
        {
            // Windows does not call the hook while this process has Raw Input registered: the hook counts instead.
            if (_keys is not null) RawKeyboard.Unregister();
            _hook = new LowLevelKeyboard(DropKey);
        }
        else if (!hook && _hook is not null)
        {
            _hook.Dispose();
            _hook = null;
            if (_keys is not null) RawKeyboard.Register(Handle);
        }
    }

    // Runs on the hook thread.
    private bool DropKey(uint flags, bool released)
    {
        // The WebView2 window is a child of this one, so the foreground window is this form while the page has focus.
        var foreground = GetForegroundWindow() == _window;
        if (!_exam.Block(flags, released, foreground))
        {
            // Raw Input counts only keys for this window in the foreground; the hook sees every key, so the same.
            if (foreground) _keys?.RecordHookKey(flags, released);
            return false;
        }
        // Tell the page later, outside the hook: the hook must answer at once.
        if (Interlocked.Exchange(ref _blockedReportPending, 1) == 0) BeginInvoke(ReportBlocked);
        return true;
    }

    private void ReportBlocked()
    {
        Interlocked.Exchange(ref _blockedReportPending, 0);
        _webView?.CoreWebView2?.PostWebMessageAsJson(_exam.BlockedJson());
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hook?.Dispose();
        _hook = null;
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (_keys is not null && m.Msg == RawKeyboard.WmInput && RawKeyboard.Read(m.LParam) is { } key)
            _keys.Record(key.Device, key.Released);
        base.WndProc(ref m);
    }

    public int ExitCode { get; private set; }

    public MainForm(LaunchOptions options, WebAddress? web)
    {
        Text = Packaging.ProductName;
        Width = 960; Height = 640;
        _options = options;
        _web = web;
        _smokeOut = options.ShellSmokeOutput;
        if (options.KeyTallyOutput is { } tallyPath)
        {
            // P-3 red-team (PROPOSAL): two counts, written on close; nothing is kept in normal use.
            // With --exam-gate also the number of keys exam mode dropped before they reached the window.
            _keys = new KeyOriginTally();
            FormClosed += (_, _) => File.WriteAllText(tallyPath,
                options.ExamGate ? _keys.ToJson(_exam.BlockedKeys) : _keys.ToJson());
        }
        // Fail closed: never open an address that did not pass the check, and create no WebView2 for it.
        var view = Startup.Shell(options, web);
        if (view.WebViewUrl is not { } url)
        {
            ShowMessage(view.Message!);
            if (_smokeOut is not null) Shown += (_, _) => Report(ShellOutcome.Message, null);
            return;
        }
        if (_smokeOut is not null)
        {
            // A WinForms timer runs on this UI thread; it fires because WebView2 start-up is awaited, not blocking.
            var limit = new System.Windows.Forms.Timer { Interval = (int)SmokeLimit.TotalMilliseconds };
            limit.Tick += (_, _) => { limit.Stop(); Report(ShellOutcome.WebView, null); };
            limit.Start();
        }
        var webView = new WebView2 { Dock = DockStyle.Fill };
        _webView = webView;
        Controls.Add(webView);
        Load += async (_, _) => await StartWebView(webView, url);
    }

    private async Task StartWebView(WebView2 webView, string url)
    {
        try
        {
            var data = Startup.WebViewDataFolder(_options,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetTempPath());
            // Another program must not open the page for remote control (P-4): drop the WebView2 override variables.
            WebViewHardening.Scrub(Environment.GetEnvironmentVariable, Environment.SetEnvironmentVariable);
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            await webView.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // The runtime is not bundled and not downloaded here (spike DAN-956, README).
            Fail(webView, Startup.NoRuntimeText, ShellOutcome.NoRuntime);
            return;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            Fail(webView, Startup.WebViewFailedText, ShellOutcome.Failed);
            return;
        }
        // DevTools would let a script in the page turn exam mode off or type for the student.
        webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        webView.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            if (_web is null) return;
            string json;
            try { json = e.WebMessageAsJson; } catch (ArgumentException) { return; }
            if (ExamMessage.Parse(json, e.Source, _web) is { } exam && !_options.ExamGate)
                SetExam(exam.On, exam.Accommodation);
        };
        webView.CoreWebView2.NavigationStarting += (_, e) =>
        {
            // The smoke test proves which address WebView2 opens, without contacting the server.
            if (_smokeOut is not null) e.Cancel = true;
            Report(ShellOutcome.WebView, e.Uri);
        };
        webView.CoreWebView2.Navigate(url);
    }

    private void Fail(WebView2 webView, string text, ShellOutcome outcome)
    {
        Controls.Remove(webView);
        webView.Dispose();
        ShowMessage(text);
        Report(outcome, null);
    }

    private void ShowMessage(string text) =>
        // AutoSize: a code-created Label is 23 px high, less than its 32 px padding, so the text would not show.
        Controls.Add(new Label { Text = text, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(16) });

    // Only in --shell-smoke-out mode, and only once: the first result is written and the window closes.
    private void Report(ShellOutcome outcome, string? url)
    {
        if (_smokeOut is not { } path || _reported) return;
        _reported = true;
        var (exitCode, line) = Startup.ShellSmoke(outcome, url);
        if (line is not null) File.WriteAllText(path, line);
        ExitCode = exitCode;
        Close();
    }
}
