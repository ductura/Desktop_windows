using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

public class ManifestVectorsTests
{
    private const string Route = "downloads.manifest.get";
    private const string InstalledVersion = "0.9.0";

    // Expected Windows offers for each released vector, with the manifest URL and SHA-256 preserved verbatim.
    private static readonly Dictionary<string, (PackageOffer? X64, PackageOffer? Arm64)> Expected = new(StringComparer.Ordinal)
    {
        ["ready"] = (
            new PackageOffer("1.0.0", "https://preuzimanje.example.invalid/ductura/1.0.0/ductura-windows-x64.msi", new string('d', 64)),
            new PackageOffer("1.0.0", "https://preuzimanje.example.invalid/ductura/1.0.0/ductura-windows-arm64.msi", new string('c', 64))),
        ["ready-empty"] = (null, null),
        ["unavailable"] = (null, null),
    };

    public static TheoryData<string, string> Cases
    {
        get
        {
            var cases = new TheoryData<string, string>();
            foreach (var name in Expected.Keys)
            {
                cases.Add(name, "x64");
                cases.Add(name, "arm64");
            }
            return cases;
        }
    }

    [Fact]
    public async Task Svaki_manifest_vektor_izdanja_ima_ocekivani_ishod()
    {
        var vectors = await TestFakesRelease.LockedVectors(Route);
        Assert.Equal(Expected.Keys.Order(StringComparer.Ordinal), vectors.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Klijent_daje_ocekivanu_ponudu_za_manifest_vektor(string name, string architecture)
    {
        var response = (await TestFakesRelease.LockedVectors(Route))[name].GetProperty("response");
        var status = response.GetProperty("status").GetInt32();
        var body = response.TryGetProperty("body", out var json) ? json.GetRawText() : null;
        var expected = Expected[name];

        Assert.Equal(architecture == "x64" ? expected.X64 : expected.Arm64,
            UpdateOffer.Decide(status, body, InstalledVersion, architecture));
    }
}
