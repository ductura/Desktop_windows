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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_keys is not null) RawKeyboard.Register(Handle);
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
        _smokeOut = options.ShellSmokeOutput;
        if (options.KeyTallyOutput is { } tallyPath)
        {
            // P-3 red-team (PROPOSAL): two counts, written on close; nothing is kept in normal use.
            _keys = new KeyOriginTally();
            FormClosed += (_, _) => File.WriteAllText(tallyPath, _keys.ToJson());
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
        Controls.Add(webView);
        Load += async (_, _) => await StartWebView(webView, url);
    }

    private async Task StartWebView(WebView2 webView, string url)
    {
        try
        {
            var data = Startup.WebViewDataFolder(_options,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetTempPath());
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
