using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

/// <summary>
/// Update offer (DAN-1072-1) against manifests in the shape of <c>downloads.manifest.get</c>
/// (ductura/server@6f7a594edc8dabf969e6aaada5cbaa7c41c685a1:openapi/v1.json, 200 body). Every negative case changes
/// one place of <see cref="Manifest"/>, which without the change offers windows/x64 1.0.0 to 0.9.0.
/// </summary>
public class UpdateOfferTests
{
    private const string U = "https://preuzimanje.example.invalid/ductura/1.0.0/ductura-windows-x64.msi";
    private const string ArmUrl = "/preuzimanje/ductura/1.0.0/ductura-windows-arm64.msi";
    private static readonly string D = new('d', 64), C = new('c', 64);

    private static readonly string Manifest = $$"""
        {"packages":[
        {"system":"macos","architecture":"arm64","version":"1.0.0","url":"https://preuzimanje.example.invalid/m.dmg","sha256":"{{new string('a', 64)}}"},
        {"system":"macos","architecture":"x64","version":"1.0.0","url":"/preuzimanje/m.dmg","sha256":"{{new string('b', 64)}}"},
        {"system":"windows","architecture":"arm64","version":"1.0.0","url":"{{ArmUrl}}","sha256":"{{C}}"},
        {"system":"windows","architecture":"x64","version":"1.0.0","url":"{{U}}","sha256":"{{D}}"}]}
        """;

    private static string Expand(string s) => s.Replace("{D63}", D[..63]).Replace("{D}", D).Replace("{U}", U);

    // Applies one change to Manifest; a change that does not apply would pass vacuously.
    private static PackageOffer? Changed(string find, string replace, string arch = "x64")
    {
        Assert.Contains(Expand(find), Manifest);
        return UpdateOffer.Decide(200, Manifest.Replace(Expand(find), Expand(replace)), "0.9.0", arch);
    }

    [Fact]
    public void Valjan_manifest_nudi_redak_windows_za_arhitekturu()
    {
        Assert.Equal(new PackageOffer("1.0.0", U, D), UpdateOffer.Decide(200, Manifest, "0.9.0", "x64"));
        Assert.Equal(new PackageOffer("1.0.0", ArmUrl, C), UpdateOffer.Decide(200, Manifest, "0.9.0", "arm64"));
        Assert.Null(UpdateOffer.Decide(200, Manifest, "1.0.0", "x64"));
        Assert.Null(UpdateOffer.Decide(200, Manifest, "1.0.1", "x64"));
        Assert.Null(UpdateOffer.Decide(200, """{"packages":[]}""", "0.9.0", "x64"));
        // Row order is not checked: the windows/x64 row first gives the same offer.
        var lines = Manifest.Split('\n');
        var reordered = string.Join('\n', lines[0], lines[4][..^2] + ",", lines[1], lines[2], lines[3].TrimEnd(',') + "]}");
        Assert.Equal(new PackageOffer("1.0.0", U, D), UpdateOffer.Decide(200, reordered, "0.9.0", "x64"));
    }

    // A valid manifest with newer packages only for other pairs offers nothing (review c.6970831101, t. 1).
    [Theory]
    [InlineData("x64", 2, 3, "arm64")]
    [InlineData("arm64", 1, 4, "x64")]
    public void Bez_retka_za_arhitekturu_nema_ponude(string arch, int macosRow, int windowsRow, string other)
    {
        var lines = Manifest.Split('\n');
        var body = lines[0] + lines[macosRow].TrimEnd(',') + "," + lines[windowsRow].TrimEnd(',', ']', '}') + "}]}";
        Assert.NotNull(UpdateOffer.Decide(200, body, "0.9.0", other)); // the body itself is valid and offers the other row
        Assert.Null(UpdateOffer.Decide(200, body, "0.9.0", arch));
    }

    // The url goes into the offer verbatim (decoded JSON string): relative, absolute, query, escapes, 2048 characters.
    [Theory]
    [InlineData("/preuzimanje/ductura.msi")]
    [InlineData("/preuzimanje/ductura.msi?v=1.0.0&kanal=stabilni")]
    [InlineData("/preuzimanje/%7Eductura/%41%2f.msi?x=%2F&y=a+b")]
    [InlineData("/preuzimanje/a;b=c/(d)!$'*,:@~.msi/")]
    [InlineData("https://preuzimanje.example.invalid/a//b/c.msi?")]
    [InlineData("https://dl.ductura.org/%7Eductura/%41.msi?x=%2F")]
    public void Adresa_retka_vraca_se_doslovno(string url)
    {
        Assert.Equal(new PackageOffer("1.0.0", url, D), Changed("\"url\":\"{U}\"", $"\"url\":\"{url}\""));
        Assert.Equal(new PackageOffer("1.0.0", url, C), Changed($"\"url\":\"{ArmUrl}\"", $"\"url\":\"{url}\"", "arm64"));
    }

    [Fact]
    public void Adresa_od_2048_znakova_prolazi_a_od_2049_ne()
    {
        var max = "/" + new string('a', 2047);
        Assert.Equal(new PackageOffer("1.0.0", max, D), Changed("\"url\":\"{U}\"", $"\"url\":\"{max}\""));
        Assert.Null(Changed("\"url\":\"{U}\"", $"\"url\":\"{max}a\""));
    }

    // One change in a row; an error in any row (macos too) means no offer. JSON escapes \\n, \\t ... decode to the character.
    [Theory]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\\n\"")] // 64 hex + LF: \z, not $ (review c.6968833253)
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\\r\\n\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\\t\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\\u00a0\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\\u007f\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\" {D}\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}d\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D63}\\u0661\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D63}D\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D63}g\"")]
    [InlineData("\"sha256\":\"a", "\"sha256\":\"A")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0.0\\n\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0.0\\t\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0.0\\r\\n\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0.0\\u00a0\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0.0\\u007f\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"01.0.0\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0.0.0\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":1,\"url\":\"{U}\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"{U}\\n\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"{U}\\r\\n\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"{U}\\t\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"\\\\\\\\server\\\\share\\\\a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"{U}\\u00a0\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"{U}\\u007f\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"C:\\\\Ductura\\\\a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"http://preuzimanje.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"//preuzimanje.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl.localhost/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl.local/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl.internal/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl.home.arpa/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://Preuzimanje.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://preuzimanje.example.invalid:443/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://preuzimanje.example.invalid/a.msi#x\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/../a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/%2e%2e/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/paket%00.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/a%2Eb.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/./a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/.\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/..\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/..?v=1\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/.?v=1\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/?v=1\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl.example.invalid//a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl.example.invalid/?v=1\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://xn--dl-0ka.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl.xn--p1ai/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl.xn--d1a.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://1.2.3.4/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl_x.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://-dl.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://dl.example.invalid./a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/a b.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/a%zz.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/a%0.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/a<b>.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/a\\\\b.msi\"")]
    [InlineData("\"system\":\"windows\",\"architecture\":\"x64\"", "\"system\":\"Windows\",\"architecture\":\"x64\"")]
    [InlineData("\"system\":\"windows\",\"architecture\":\"x64\"", "\"system\":\"windows\",\"architecture\":\"ia32\"")]
    [InlineData("\"system\":\"macos\",\"architecture\":\"arm64\"", "\"system\":\"linux\",\"architecture\":\"arm64\"")]
    [InlineData("\"system\":\"windows\",\"architecture\":\"arm64\"", "\"system\":\"windows\",\"architecture\":\"x64\"")]
    [InlineData("\"system\":\"macos\",\"architecture\":\"arm64\"", "\"system\":\"macos\",\"architecture\":\"ia32\"")]
    [InlineData("\"system\":\"macos\",\"architecture\":\"x64\"", "\"system\":\"macos\",\"architecture\":\"arm64\"")]
    [InlineData(",\"sha256\":\"{D}\"", "")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\",\"extra\":\"x\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\",\"sha256\":\"{D}\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\",\"__proto__\":\"x\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\",\"toString\":\"x\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":null")]
    [InlineData("\"sha256\":\"{D}\"", "\"Sha256\":\"{D}\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\",\"sha\\u003256\":\"{D}\"")] // escaped duplicate key
    [InlineData("\"sha256\":\"{D}\"", "\"sha\\ud800256\":\"{D}\"")]
    [InlineData("{\"packages\":[", "{\"packages\":null,\"x\":[")]
    // Root errors around the otherwise valid manifest (review c.6965285434, t. 2).
    [InlineData("{\"packages\":[", "{\"extra\":1,\"packages\":[")]
    [InlineData("{\"packages\":[", "{\"__proto__\":{},\"packages\":[")]
    [InlineData("{\"packages\":[", "{\"packages\":[],\"packages\":[")]
    [InlineData("{D}\"}]}", "{D}\"}],\"toString\":1}")]
    [InlineData("{\"packages\":[", "\ufeff{\"packages\":[")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/a\\ud800\"")] // lone surrogate as a \u escape: JSON reader
    public void Manifest_izvan_ugovora_ne_daje_ponudu(string find, string replace) => Assert.Null(Changed(find, replace));

    // A lone surrogate as a real character fails in UTF-8 transcoding (c.6965285434, t. 1). A loop, not InlineData:
    // xUnit serialises InlineData and would not keep a lone surrogate.
    [Fact]
    public void Nespareni_surogat_u_tijelu_ne_daje_ponudu()
    {
        foreach (var (find, replace) in new[] { ("\"url\":\"{U}\"", "\"url\":\"/a\ud800\""),
            ("\"url\":\"{U}\"", "\"url\":\"/a\udc00\ud800\""), ("\"sha256\":\"{D}\"", "\"sha\ud800256\":\"{D}\"") })
            Assert.Null(Changed(find, replace));
    }

    [Theory]
    [InlineData(503, "valid")]
    [InlineData(204, "valid")]
    [InlineData(200, null)]
    [InlineData(200, "")]
    [InlineData(200, "not json")]
    [InlineData(200, "[]")]
    [InlineData(200, "{}")]
    [InlineData(200, "{\"packages\":{}}")]
    [InlineData(200, "{\"packages\":[1]}")]
    public void Status_ili_tijelo_izvan_ugovora_ne_daje_ponudu(int status, string? body) =>
        Assert.Null(UpdateOffer.Decide(status, body == "valid" ? Manifest : body, "0.9.0", "x64"));

    // Version.TryParse in Packaging.IsNewer reads "1.0" as older than "1.0.0", so the installed version is checked first.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData(" 0.9.0")]
    [InlineData("0.9.0\n")]
    [InlineData("0.9.0\r\n")]
    [InlineData("0.9.0\t")]
    [InlineData("0.9.0\u00a0")]
    [InlineData("0.9.0\u007f")]
    [InlineData("0.9.0.0")]
    [InlineData("00.9.0")]
    [InlineData("٠.9.0")]
    public void Necitljiva_instalirana_verzija_ne_daje_ponudu(string? installed) =>
        Assert.Null(UpdateOffer.Decide(200, Manifest, installed, "x64"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X64")]
    [InlineData("x64 ")]
    [InlineData("ia32")]
    public void Nepoznata_arhitektura_u_pozivu_baca(string? architecture) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdateOffer.Decide(200, Manifest, "0.9.0", architecture!));

    // A version past int is valid in the contract: the manifest stands, only that row is never newer (Packaging.IsNewer).
    [Fact]
    public void Verzija_preko_int_ne_rusi_manifest()
    {
        Assert.Null(Changed("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"99999999999.0.0\",\"url\":\"{U}\""));
        Assert.Equal(new PackageOffer("1.0.0", U, D), Changed("\"version\":\"1.0.0\",\"url\":\"/preuzimanje/m.dmg\"",
            "\"version\":\"99999999999.0.0\",\"url\":\"/preuzimanje/m.dmg\""));
    }
}
