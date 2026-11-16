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
            if (web is null) return 1;
            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var line = $"Ductura OK {version}";
            if (options.SmokeOutput is { } path) File.WriteAllText(path, line);
            return 0;
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
        if (web is null)
        {
            // Fail closed: never open an address that did not pass the check.
            Controls.Add(new Label { Text = "Adresa Ducture u ovom paketu nije ispravna.", Dock = DockStyle.Top, Padding = new Padding(16) });
            return;
        }
        var link = new LinkLabel { Text = "Otvori Ducturu", Dock = DockStyle.Top, Padding = new Padding(16) };
        var url = Packaging.DocumentUrl(web, options.OpenDocumentId);
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        Controls.Add(link);
        // Sljedeći korak: ugraditi editor (WebView2) umjesto poveznice.
    }
}
