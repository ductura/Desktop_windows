namespace Ductura.Core;

/// <summary>Što aplikacija radi s adresom iz paketa (DAN-883). Program.cs samo primjenjuje ove rezultate.</summary>
public static class Startup
{
    /// <summary>Tekst umjesto poveznice kad adresa iz paketa ne prolazi provjeru.</summary>
    public const string InvalidAddressText = "Adresa Ducture u ovom paketu nije ispravna.";

    /// <summary>Ishod --smoke: paket s neispravnom adresom vraća 1 i ne piše potvrdu.</summary>
    public static (int ExitCode, string? Line) Smoke(WebAddress? web, string version) =>
        web is null ? (1, null) : (0, $"Ductura OK {version}");

    /// <summary>Adresa za poveznicu ili null (fail closed): neprovjerena adresa se nikad ne otvara.</summary>
    public static string? LinkUrl(LaunchOptions options, WebAddress? web) =>
        web is null ? null : Packaging.DocumentUrl(web, options.OpenDocumentId);

    /// <summary>Tekst kad na računalu nema WebView2 runtimea (DAN-956); aplikacija ga ne preuzima sama.</summary>
    public const string NoRuntimeText = "Za prikaz Ducture treba Microsoft Edge WebView2 Runtime.";

    /// <summary>Tekst kad se WebView2 ne može pokrenuti iz drugog razloga (npr. podatkovna mapa nije zapisiva).</summary>
    public const string WebViewFailedText = "Prikaz Ducture nije se mogao pokrenuti.";

    /// <summary>Sadržaj prozora (DAN-956): WebView2 samo s provjerenom adresom, inače samo poruka.</summary>
    public static ShellView Shell(LaunchOptions options, WebAddress? web) =>
        LinkUrl(options, web) is { } url ? new ShellView(url, null) : new ShellView(null, InvalidAddressText);

    /// <summary>
    /// Ishod --shell-smoke-out: 0 i adresa koju WebView2 otvara, 1 za poruku, 3 bez runtimea, 4 za drugu grešku pokretanja WebView2.
    /// WebView2 bez adrese nije dokaz, pa vraća 2 bez retka (isto kao istek roka).
    /// </summary>
    public static (int ExitCode, string? Line) ShellSmoke(ShellOutcome outcome, string? url) => outcome switch
    {
        ShellOutcome.WebView when !string.IsNullOrEmpty(url) => (0, $"Ductura WebView2 {url}"),
        ShellOutcome.Message => (1, "Ductura poruka"),
        ShellOutcome.NoRuntime => (3, "Ductura bez WebView2"),
        ShellOutcome.Failed => (4, "Ductura greška WebView2"),
        _ => (2, null),
    };
}

/// <summary>Točno jedno od dvoga: adresa za WebView2 ili poruka.</summary>
public sealed record ShellView(string? WebViewUrl, string? Message);

public enum ShellOutcome { WebView, Message, NoRuntime, Failed }
