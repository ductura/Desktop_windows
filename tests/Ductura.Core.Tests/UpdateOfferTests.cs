using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

/// <summary>
/// Update offer (DAN-1072-1) against manifests in the shape of <c>downloads.manifest.get</c>: <see cref="Contract"/>
/// (<c>openapi/v1.json</c>, path <c>/api/v1/downloads/manifest</c>, 200 body). The route's vectors from a locked
/// test-fakes release are 1072-2.
/// </summary>
public class UpdateOfferTests
{
    public const string Contract = "ductura/server@6f7a594edc8dabf969e6aaada5cbaa7c41c685a1:openapi/v1.json";
    private const string X64Url = "https://preuzimanje.example.invalid/ductura/1.0.0/ductura-windows-x64.msi";
    private const string Arm64Url = "https://preuzimanje.example.invalid/ductura/1.0.0/ductura-windows-arm64.msi";
    private static readonly string D = new('d', 64);

    // Synthetic manifest: one row per system and architecture, relative and absolute addresses (D-306 t. 1).
    private static readonly string Manifest = $$"""
        {"packages":[
        {"system":"macos","architecture":"arm64","version":"1.0.0","url":"https://preuzimanje.example.invalid/ductura/1.0.0/ductura-macos-arm64.dmg","sha256":"{{new string('a', 64)}}"},
        {"system":"macos","architecture":"x64","version":"1.0.0","url":"/preuzimanje/ductura/1.0.0/ductura-macos-x64.dmg","sha256":"{{new string('b', 64)}}"},
        {"system":"windows","architecture":"arm64","version":"1.0.0","url":"{{Arm64Url}}","sha256":"{{new string('c', 64)}}"},
        {"system":"windows","architecture":"x64","version":"1.0.0","url":"{{X64Url}}","sha256":"{{D}}"}]}
        """;

    private static string Body(string name) => name switch { "ready" => Manifest, "ready-empty" => """{"packages":[]}""", _ => throw new ArgumentException(name) };

    private static PackageOffer? Ready(string find, string replace, string installed = "0.9.0")
    {
        var body = Manifest;
        find = find.Replace("{D}", D).Replace("{U}", X64Url);
        Assert.Contains(find, body); // a mutation that does not apply would pass vacuously
        return UpdateOffer.Decide(200, body.Replace(find, replace.Replace("{D}", D).Replace("{U}", X64Url)), installed, "x64");
    }

    // Negative cases first. The sha256 of the windows/x64 row must be 64 lowercase hex characters.
    [Theory]
    [InlineData("ddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd")]
    [InlineData("ddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd")]
    [InlineData("DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD")]
    [InlineData("dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddg")]
    [InlineData("")]
    [InlineData("ddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd\\n")]
    [InlineData("ddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd\\u0661")]
    public void Los_sha256_ne_daje_ponudu(string sha) => Assert.Null(Ready("\"sha256\":\"{D}\"", $"\"sha256\":\"{sha}\""));

    [Theory]
    [InlineData("\"system\":\"windows\",\"architecture\":\"x64\"", "\"system\":\"Windows\",\"architecture\":\"x64\"")]
    [InlineData("\"system\":\"windows\",\"architecture\":\"x64\"", "\"system\":\"linux\",\"architecture\":\"x64\"")]
    [InlineData("\"system\":\"windows\",\"architecture\":\"x64\"", "\"system\":\"windows\",\"architecture\":\"X64\"")]
    [InlineData("\"system\":\"windows\",\"architecture\":\"x64\"", "\"system\":\"windows\",\"architecture\":\"ia32\"")]
    [InlineData("\"system\":\"macos\",\"architecture\":\"arm64\"", "\"system\":\"linux\",\"architecture\":\"arm64\"")]
    [InlineData("\"system\":\"macos\",\"architecture\":\"arm64\"", "\"system\":\"macos\",\"architecture\":\"ia32\"")]
    [InlineData("\"system\":\"windows\",\"architecture\":\"arm64\"", "\"system\":\"windows\",\"architecture\":\"x64\"")]
    [InlineData("\"system\":\"macos\",\"architecture\":\"x64\"", "\"system\":\"macos\",\"architecture\":\"arm64\"")]
    [InlineData(",\"sha256\":\"{D}\"", "")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\",\"extra\":\"x\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":\"{D}\",\"sha256\":\"{D}\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"extra\":\"{D}\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":1")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha256\":null")]
    [InlineData("\"sha256\":\"a", "\"sha256\":\"A")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":1,\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"01.0.0\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0.0.0\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0.0 \",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"v1.0.0\",\"url\":\"{U}\"")]
    [InlineData("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"1.0.0\\n\",\"url\":\"{U}\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"http://preuzimanje.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"//preuzimanje.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://localhost/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://Preuzimanje.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://u@preuzimanje.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://preuzimanje.example.invalid:443/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://preuzimanje.example.invalid/a.msi#x\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"https://xn--d1a.example.invalid/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/../a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/preuzimanje/%2e%2e/a.msi\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/a\\ud800\"")]
    public void Redak_izvan_ugovora_ne_daje_ponudu(string find, string replace) => Assert.Null(Ready(find, replace));

    // Root errors around an otherwise valid manifest, so the offer exists without them.
    [Theory]
    [InlineData("{\"packages\":[", "{\"extra\":1,\"packages\":[")]
    [InlineData("{\"packages\":[", "{\"packages\":[],\"packages\":[")]
    [InlineData("{D}\"}]}", "{D}\"}],\"packages\":[]}")]
    public void Korijen_izvan_ugovora_ponistava_valjanu_ponudu(string find, string replace)
    {
        Assert.NotNull(UpdateOffer.Decide(200, Manifest, "0.9.0", "x64"));
        Assert.Null(Ready(find, replace));
    }

    // A lone surrogate as a real character (not a \u escape) fails while transcoding to UTF-8.
    [Theory]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/a\ud800\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/a\udc00\"")]
    [InlineData("\"url\":\"{U}\"", "\"url\":\"/a\udc00\ud800\"")]
    [InlineData("\"sha256\":\"{D}\"", "\"sha\ud800256\":\"{D}\"")]
    public void Nespareni_surogat_u_tijelu_ne_daje_ponudu(string find, string replace) => Assert.Null(Ready(find, replace));

    [Fact]
    public void Predugacka_adresa_ne_daje_ponudu() =>
        Assert.Null(Ready("\"url\":\"{U}\"", $"\"url\":\"/{new string('a', 2048)}\""));

    [Theory]
    [InlineData(503, "ready")]
    [InlineData(404, "ready")]
    [InlineData(204, "ready")]
    [InlineData(200, null)]
    [InlineData(200, "")]
    [InlineData(200, "not json")]
    [InlineData(200, "[]")]
    [InlineData(200, "{}")]
    [InlineData(200, "{\"packages\":[],\"extra\":1}")]
    [InlineData(200, "{\"packages\":[],\"packages\":[]}")]
    [InlineData(200, "{\"extra\":[]}")]
    [InlineData(200, "{\"packages\":{}}")]
    [InlineData(200, "{\"packages\":[1]}")]
    public void Tijelo_izvan_ugovora_ne_daje_ponudu(int status, string? body) =>
        Assert.Null(UpdateOffer.Decide(status, body == "ready" ? Body("ready") : body, "0.9.0", "x64"));

    // Version.TryParse inside Packaging.IsNewer reads "1.0" as older than "1.0.0"; the installed version is checked first.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData(" 0.9.0")]
    [InlineData("0.9.0.0")]
    [InlineData("00.9.0")]
    public void Necitljiva_instalirana_verzija_ne_daje_ponudu(string? installed) =>
        Assert.Null(UpdateOffer.Decide(200, Body("ready"), installed, "x64"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X64")]
    [InlineData("ia32")]
    public void Nepoznata_arhitektura_u_pozivu_baca(string? architecture) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdateOffer.Decide(200, Body("ready"), "0.9.0", architecture!));

    // A version past int is valid in the contract: the manifest stands, only that row is never newer.
    [Fact]
    public void Verzija_preko_int_ne_rusi_manifest()
    {
        Assert.Null(Ready("\"version\":\"1.0.0\",\"url\":\"{U}\"", "\"version\":\"99999999999.0.0\",\"url\":\"{U}\""));
        Assert.NotNull(Ready("\"version\":\"1.0.0\",\"url\":\"https://preuzimanje.example.invalid/ductura/1.0.0/ductura-macos-arm64.dmg\"",
            "\"version\":\"99999999999.0.0\",\"url\":\"https://preuzimanje.example.invalid/ductura/1.0.0/ductura-macos-arm64.dmg\""));
    }

    // Expected outcome: status, body, installed version and architecture, then the offered version, url and sha256 (or none).
    public static TheoryData<int, string, string, string, string?, string?, string?> Expected => new()
    {
        { 200, "ready", "0.9.0", "x64", "1.0.0", X64Url, D },
        { 200, "ready", "0.9.0", "arm64", "1.0.0", Arm64Url, new string('c', 64) },
        { 200, "ready", "1.0.0", "x64", null, null, null },
        { 200, "ready", "1.0.1", "x64", null, null, null },
        { 200, "ready-empty", "0.9.0", "x64", null, null, null },
        { 503, "ready", "0.9.0", "x64", null, null, null },
    };

    // The offered url is the row's url verbatim: absolute, relative, with a query, with percent escapes and sub-delims.
    // Any resolution, normalisation (case of %xx, %7E to ~, default path) or trimming of the query fails here.
    [Theory]
    [InlineData("/preuzimanje/ductura.msi")]
    [InlineData("/preuzimanje/ductura.msi?v=1.0.0&kanal=stabilni")]
    [InlineData("/preuzimanje/ductura.msi?")]
    [InlineData("/preuzimanje/%7Eductura/%41%2f.msi?x=%2F&y=a+b")]
    [InlineData("/preuzimanje/a;b=c/(d)!$'*,:@~.msi")]
    [InlineData("/preuzimanje/ductura.msi/")]
    [InlineData("https://preuzimanje.example.invalid/ductura/1.0.1/Ductura-Setup.msi")]
    [InlineData("https://preuzimanje.example.invalid/ductura.msi?v=1.0.0&kanal=stabilni")]
    [InlineData("https://preuzimanje.example.invalid/%7Eductura/%41.msi?x=%2F")]
    [InlineData("https://preuzimanje.example.invalid/a//b/c.msi")]
    public void Adresa_retka_vraca_se_doslovno(string url)
    {
        foreach (var (architecture, rowUrl, sha) in new[] { ("x64", X64Url, D), ("arm64", Arm64Url, new string('c', 64)) })
        {
            var body = Manifest.Replace($"\"url\":\"{rowUrl}\"", $"\"url\":\"{url}\"");
            Assert.NotEqual(Manifest, body); // a mutation that does not apply would pass vacuously
            Assert.Equal(new PackageOffer("1.0.0", url, sha), UpdateOffer.Decide(200, body, "0.9.0", architecture));
        }
    }

    [Theory]
    [MemberData(nameof(Expected))]
    public void Manifest_daje_ocekivan_ishod(int status, string body, string installed, string architecture, string? version, string? url, string? sha) =>
        Assert.Equal(version is null ? null : new PackageOffer(version, url!, sha!), UpdateOffer.Decide(status, Body(body), installed, architecture));
}
