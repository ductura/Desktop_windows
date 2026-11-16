using System.Diagnostics;
using Ductura.Core;

namespace Ductura.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);
        if (options.Smoke)
        {
            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            var line = $"Ductura OK {version}";
            if (options.SmokeOutput is { } path) File.WriteAllText(path, line);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(options));
        return 0;
    }
}

internal sealed class MainForm : Form
{
    public MainForm(LaunchOptions options)
    {
        Text = Packaging.ProductName;
        Width = 960; Height = 640;
        var link = new LinkLabel { Text = "Otvori Ducturu", Dock = DockStyle.Top, Padding = new Padding(16) };
        var url = Packaging.DocumentUrl(options.OpenDocumentId);
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        Controls.Add(link);
        // Sljedeći korak: ugraditi editor (WebView2) umjesto poveznice.
    }
}
