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
    private readonly IReadOnlyList<string> _hosts;
    private bool _reported;

    public int ExitCode { get; private set; }

    public MainForm(LaunchOptions options, WebAddress? web)
    {
        Text = Packaging.ProductName;
        Width = 960; Height = 640;
        _options = options;
        _hosts = web?.Hosts ?? [];
        _smokeOut = options.ShellSmokeOutput;
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
        // DAN-1547: every navigation of the view goes through Navigation.Handle; only Load is ever loaded.
        var core = webView.CoreWebView2;
        core.NavigationStarting += (_, e) =>
        {
            var action = Navigation.Handle(NavigationKind.MainFrame, e.Uri, _hosts, e.IsUserInitiated);
            if (action != NavigationAction.Load) e.Cancel = true;
            if (_smokeOut is not null)
            {
                // The smoke test proves which address WebView2 opens, without contacting the server;
                // a refused address is never reported as opened.
                e.Cancel = true;
                if (action == NavigationAction.Load) Report(ShellOutcome.WebView, e.Uri);
                return;
            }
            if (action == NavigationAction.OpenInBrowser) OpenInBrowser(e.Uri);
        };
        core.FrameNavigationStarting += (_, e) =>
        {
            if (Navigation.Handle(NavigationKind.Frame, e.Uri, _hosts, e.IsUserInitiated) != NavigationAction.Load)
                e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) =>
        {
            // Never a second WebView2 window: it would have none of these handlers.
            e.Handled = true;
            switch (Navigation.Handle(NavigationKind.NewWindow, e.Uri, _hosts, e.IsUserInitiated))
            {
                // Same view; NavigationStarting decides again.
                case NavigationAction.Load: core.Navigate(e.Uri); break;
                case NavigationAction.OpenInBrowser when _smokeOut is null: OpenInBrowser(e.Uri); break;
            }
        };
        // Other programs (mailto:, ductura:, ...) are never started from the page.
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.Navigate(url);
    }

    // Only for an address Navigation.Handle sent to the browser: https, printable ASCII, no space or '\'.
    private static void OpenInBrowser(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
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
