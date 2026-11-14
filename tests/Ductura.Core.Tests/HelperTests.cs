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

    [Theory]
    [InlineData("ductura://doc/demo-1", "demo-1")]
    [InlineData("ductura://doc/demo-1/", "demo-1")]
    [InlineData("ductura://doc/", null)]
    public void Poveznica_otvara_dokument(string arg, string? expected) =>
        Assert.Equal(expected, LaunchOptions.Parse([arg]).OpenDocumentId);
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
