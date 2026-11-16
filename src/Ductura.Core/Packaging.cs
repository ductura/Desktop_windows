namespace Ductura.Core;

public static class Packaging
{
    public const string ProductName = "Ductura";
    public const string WebUrl = "https://ductura.org";

    /// <summary>Adresa rada u web aplikaciji. ID ulazi samo ako prolazi pravilo ugovora, i to escapiran; inače početna adresa.</summary>
    public static string DocumentUrl(string? workId) =>
        WorkId.IsValid(workId) ? $"{WebUrl}/app/web/#/doc/{Uri.EscapeDataString(workId!)}" : WebUrl;

    /// <summary>Naziv instalacijskog paketa koji nudi preloader (npr. Ductura-Setup-x64.exe).</summary>
    public static string InstallerFileName(string arch = "x64") =>
        arch is "x64" or "arm64" ? $"{ProductName}-Setup-{arch}.exe"
            : throw new ArgumentOutOfRangeException(nameof(arch), arch, "Podržani su x64 i arm64.");

    /// <summary>Putanja prečaca u izborniku Start, ispod mape Programs (sve korisnike ili trenutnog korisnika).</summary>
    public static string StartMenuShortcut(string programsFolder) =>
        Path.Combine(programsFolder, ProductName, $"{ProductName}.lnk");

    /// <summary>Je li ponuđena verzija novija od instalirane.</summary>
    public static bool IsNewer(string offered, string installed) =>
        Version.TryParse(offered, out var o) && Version.TryParse(installed, out var i) && o > i;
}
