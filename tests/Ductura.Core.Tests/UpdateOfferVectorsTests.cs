using System.Text.Json;
using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

/// <summary>
/// Update offer (DAN-1072) against the <c>downloads.manifest.get</c> vectors. No test-fakes release carries the route yet
/// (3.0.1 predates DAN-931), so the three vectors are vendored from <see cref="Source"/> at a pinned commit and
/// <see cref="Kopija_vektora_odgovara_izvoru"/> fails when the copy differs from that commit.
/// </summary>
public class UpdateOfferVectorsTests
{
    public const string Source = "https://raw.githubusercontent.com/ductura/server/6f7a594edc8dabf969e6aaada5cbaa7c41c685a1/server/contracts/generated/fake-server/vectors.json";
    private const string Route = "downloads.manifest.get";
    private const string X64Url = "https://preuzimanje.example.invalid/ductura/1.0.0/ductura-windows-x64.msi";
    private static readonly string D = new('d', 64);

    private static Dictionary<string, JsonElement> Vectors(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateArray()
            .Where(v => v.GetProperty("route").GetString() == Route)
            .ToDictionary(v => v.GetProperty("name").GetString()!, v => v.Clone(), StringComparer.Ordinal);

    private static readonly Dictionary<string, JsonElement> Vendored =
        Vectors(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "vendor", "downloads-manifest.vectors.json")));

    private static string Body(string name) => Vendored[name].GetProperty("response").GetProperty("body").GetRawText();

    private static PackageOffer? Ready(string find, string replace, string installed = "0.9.0")
    {
        var body = Body("ready");
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

    // Expected outcome per vector: installed version and architecture, then the offered version, url and sha256 (or none).
    public static TheoryData<string, string, string, string?, string?, string?> Expected => new()
    {
        { "ready", "0.9.0", "x64", "1.0.0", X64Url, D },
        { "ready", "0.9.0", "arm64", "1.0.0", "https://preuzimanje.example.invalid/ductura/1.0.0/ductura-windows-arm64.msi", new string('c', 64) },
        { "ready", "1.0.0", "x64", null, null, null },
        { "ready", "1.0.1", "x64", null, null, null },
        { "ready-empty", "0.9.0", "x64", null, null, null },
        { "unavailable", "0.9.0", "x64", null, null, null },
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void Svaki_vektor_ima_ocekivan_ishod(string name, string installed, string architecture, string? version, string? url, string? sha)
    {
        var response = Vendored[name].GetProperty("response");
        var offer = UpdateOffer.Decide(response.GetProperty("status").GetInt32(), response.GetProperty("body").GetRawText(), installed, architecture);
        Assert.Equal(version is null ? null : new PackageOffer(version, url!, sha!), offer);
    }

    [Fact]
    public void Tablica_pokriva_sve_vektore() =>
        Assert.Equal(Expected.Select(row => (string)row[0]).Distinct().Order(StringComparer.Ordinal), Vendored.Keys.Order(StringComparer.Ordinal));

    [Fact]
    public async Task Kopija_vektora_odgovara_izvoru()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var source = Vectors(await http.GetStringAsync(Source));
        Assert.Equal(source.Keys.Order(StringComparer.Ordinal), Vendored.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, vector) in source)
            Assert.Equal(JsonSerializer.Serialize(vector), JsonSerializer.Serialize(Vendored[name]));
    }
}
