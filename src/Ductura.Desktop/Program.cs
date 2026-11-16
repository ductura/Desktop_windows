using System.Diagnostics;
using Ductura.Core;

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
        Application.Run(new MainForm(options, web));
        return 0;
    }
}

internal sealed class MainForm : Form
{
    public MainForm(LaunchOptions options, WebAddress? web)
    {
        Text = Packaging.ProductName;
        Width = 960; Height = 640;
        // Fail closed: never open an address that did not pass the check.
        if (Startup.LinkUrl(options, web) is not { } url)
        {
            // AutoSize: a code-created Label is 23 px high, less than its 32 px padding, so the text would not show.
            Controls.Add(new Label { Text = Startup.InvalidAddressText, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(16) });
            return;
        }
        var link = new LinkLabel { Text = "Otvori Ducturu", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(16) };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        Controls.Add(link);
        // Sljedeći korak: ugraditi editor (WebView2) umjesto poveznice.
    }
}
