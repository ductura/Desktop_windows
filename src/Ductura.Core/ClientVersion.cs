using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Ductura.Core;

/// <summary>
/// Provjera verzije pri pokretanju (DAN-944): <c>health.minClientVersion</c> servera prema vlastitoj verziji klijenta.
/// Čista funkcija bez mreže i sata; poziv <c>health</c> i prozor su zaseban korak (944-2).
/// <see cref="Packaging.IsNewer"/> ostaje zaseban: uspoređuje ponuđeni instalacijski paket, ne ugovor.
/// </summary>
public static partial class ClientVersion
{
    /// <summary>Poruka umjesto weba kad je klijent stariji od najstarije verzije koju server poslužuje.</summary>
    public const string UpdateRequiredText = "Ova verzija Ducture više nije podržana. Preuzmite i instalirajte noviju verziju.";

    /// <summary>
    /// Pravila redom: vlastita verzija koja nije strogi major.minor.patch traži nadogradnju (fail closed);
    /// server koji nije odgovorio (mreža, rok, ne-200, tijelo izvan ugovora) je nedostupan;
    /// odgovor bez <c>minClientVersion</c> (null ili izostalo; polje je u ugovoru neobavezno) ne traži ništa;
    /// <c>minClientVersion</c> izvan uzorka ugovora je nečitljiv odgovor, dakle nedostupan server;
    /// inače se dijelovi uspoređuju kao cijeli brojevi bez gornje granice.
    /// </summary>
    public static VersionVerdict Decide(bool serverAnswered, string? minClientVersion, string? clientVersion)
    {
        if (!TryParse(clientVersion, out var own)) return VersionVerdict.UpdateRequired;
        if (!serverAnswered) return VersionVerdict.ServerUnavailable;
        if (minClientVersion is null) return VersionVerdict.Current;
        if (!TryParse(minClientVersion, out var min)) return VersionVerdict.ServerUnavailable;
        for (var i = 0; i < 3; i++)
        {
            if (own[i] != min[i]) return own[i] < min[i] ? VersionVerdict.UpdateRequired : VersionVerdict.Current;
        }
        return VersionVerdict.Current;
    }

    /// <summary>
    /// Web se otvara osim kad je klijent zastario. Nedostupan server ne blokira: web i preloader (D-300) prikazuju
    /// stanje servera. minClientVersion je savjet za nadogradnju, ne sigurnosna granica (blokiranjem healtha se zaobilazi).
    /// </summary>
    public static bool OpensWeb(VersionVerdict verdict) => verdict != VersionVerdict.UpdateRequired;

    // Pattern of the contract; [0-9] instead of \d (which in .NET also matches non-ASCII digits),
    // \z instead of $ (which also matches before a final \n).
    [GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    private static bool TryParse(string? text, out BigInteger[] parts)
    {
        parts = [];
        if (text is null || Pattern().Match(text) is not { Success: true } m) return false;
        parts = [.. m.Groups.Values.Skip(1).Select(g => BigInteger.Parse(g.Value, NumberStyles.None, CultureInfo.InvariantCulture))];
        return true;
    }
}

public enum VersionVerdict { Current, UpdateRequired, ServerUnavailable }
