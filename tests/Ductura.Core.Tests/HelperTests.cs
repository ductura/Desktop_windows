using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Prazni_argumenti_znace_obicno_pokretanje()
    {
        var o = LaunchOptions.Parse([]);
        Assert.False(o.Smoke);
        Assert.Null(o.OpenDocumentId);
    }

    [Fact]
    public void Smoke_out_ukljucuje_smoke_i_putanju()
    {
        var o = LaunchOptions.Parse(["--smoke-out", @"C:\temp\ok.txt"]);
        Assert.True(o.Smoke);
        Assert.Equal(@"C:\temp\ok.txt", o.SmokeOutput);
    }

    // The work id rule itself is in WorkIdTests; here the link: prefix, segment and URL.
    [Theory]
    [InlineData("ductura://doc/demo-1", "demo-1")]
    [InlineData("ductura://doc/demo-1/", "demo-1")]
    [InlineData("DUCTURA://DOC/demo-1", "demo-1")]
    [InlineData("ductura://doc/rad-%C4%8D", "rad-č")]
    [InlineData("ductura://doc/..%2Fdoc", "../doc")]
    [InlineData("ductura://doc/", null)]
    [InlineData("ductura://doc//", null)]
    [InlineData("ductura://doc/demo-1//", null)]
    [InlineData("ductura://doc/../doc", null)]
    [InlineData("ductura://doc/doc%00", null)]
    [InlineData("ductura://doc/%ED%A0%80", null)]
    [InlineData("ductura://doc/%C2%A0doc", null)]
    [InlineData("ductura://doc/doc\t", null)]
    public void Poveznica_otvara_samo_ispravan_ID_rada(string arg, string? expected)
    {
        var id = LaunchOptions.Parse([arg]).OpenDocumentId;
        Assert.Equal(expected, id);
        if (id is null) Assert.Equal(Packaging.WebUrl, Packaging.DocumentUrl(id));
    }
}

public class DocumentUrlTests
{
    [Fact]
    public void Neispravan_ID_ne_ulazi_u_URL_ni_kad_je_predan_izravno()
    {
        foreach (var id in new[] { null, "", " doc", "doc\0", "doc\uD800", new string('d', WorkId.Max + 1) })
            Assert.Equal(Packaging.WebUrl, Packaging.DocumentUrl(id));
    }

    [Theory]
    [InlineData("doc';DROP TABLE x;--", "doc%27%3BDROP%20TABLE%20x%3B--")]
    [InlineData("../doc", "..%2Fdoc")]
    [InlineData("%00", "%2500")]
    [InlineData("a#b?c", "a%23b%3Fc")]
    [InlineData(@"C:\doc", "C%3A%5Cdoc")]
    public void Ispravan_ID_ulazi_u_URL_samo_escapiran(string id, string escaped) =>
        Assert.Equal($"{Packaging.WebUrl}/app/web/#/doc/{escaped}", Packaging.DocumentUrl(id));
}

public class PackagingTests
{
    [Fact]
    public void Naziv_paketa_odgovara_preloaderu()
    {
        Assert.Equal("Ductura-Setup-x64.exe", Packaging.InstallerFileName());
        Assert.Equal("Ductura-Setup-arm64.exe", Packaging.InstallerFileName("arm64"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Packaging.InstallerFileName("x86"));
    }

    [Fact]
    public void Precac_je_u_mapi_Ductura()
    {
        var path = Packaging.StartMenuShortcut(Path.Combine("Start Menu", "Programs"));
        Assert.Equal(Path.Combine("Start Menu", "Programs", "Ductura", "Ductura.lnk"), path);
    }

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    [InlineData("nije-verzija", "0.1.0", false)]
    public void Usporedba_verzija(string offered, string installed, bool expected) =>
        Assert.Equal(expected, Packaging.IsNewer(offered, installed));
}
