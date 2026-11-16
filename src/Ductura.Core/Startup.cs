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
}
