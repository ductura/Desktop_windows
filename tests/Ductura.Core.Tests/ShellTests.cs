using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// The window shows WebView2 only with a verified address (DAN-956); --shell-smoke-out reports what it showed.
public class ShellTests
{
    private static WebAddress? Web(string url) => WebAddress.TryCreate(url, "ductura.org", out var web) ? web : null;

    [Theory]
    [InlineData("http://ductura.org/app/")]
    [InlineData("https://evil.org/app/")]
    [InlineData("")]
    public void Neispravna_adresa_daje_poruku_bez_WebView2(string url)
    {
        var view = Startup.Shell(LaunchOptions.Parse([]), Web(url));
        Assert.Null(view.WebViewUrl);
        Assert.Equal(Startup.InvalidAddressText, view.Message);
        Assert.Equal((1, "Ductura poruka"), Startup.ShellSmoke(ShellOutcome.Message, null));
    }

    [Fact]
    public void Bez_runtimea_kod_3_i_poruka()
    {
        Assert.Equal((3, "Ductura bez WebView2"), Startup.ShellSmoke(ShellOutcome.NoRuntime, null));
        Assert.Equal("Za prikaz Ducture treba Microsoft Edge WebView2 Runtime.", Startup.NoRuntimeText);
    }

    [Fact]
    public void Druga_greska_pokretanja_kod_4_i_poruka()
    {
        Assert.Equal((4, "Ductura greška WebView2"), Startup.ShellSmoke(ShellOutcome.Failed, null));
        Assert.Equal("Prikaz Ducture nije se mogao pokrenuti.", Startup.WebViewFailedText);
    }

    [Fact]
    public void WebView2_bez_adrese_nije_uspjeh() =>
        Assert.Equal(2, Startup.ShellSmoke(ShellOutcome.WebView, null).ExitCode);

    [Fact]
    public void Ispravna_adresa_daje_WebView2_s_adresom_iz_paketa()
    {
        var web = Web("https://ductura.org/app/");
        var view = Startup.Shell(LaunchOptions.Parse(["ductura://doc/demo-1"]), web);
        Assert.Equal("https://ductura.org/app/#/doc/demo-1", view.WebViewUrl);
        Assert.Null(view.Message);
        Assert.Equal((0, "Ductura WebView2 https://ductura.org/app/"),
            Startup.ShellSmoke(ShellOutcome.WebView, "https://ductura.org/app/"));
    }

    [Fact]
    public void Shell_smoke_out_bez_vrijednosti_se_ignorira()
    {
        var o = LaunchOptions.Parse(["--shell-smoke-out"]);
        Assert.Null(o.ShellSmokeOutput);
        Assert.False(o.Smoke);
    }

    [Theory]
    [InlineData(@"C:\Users\Test Č\AppData\Local\Temp\shell.txt")]
    [InlineData(@"C:\temp\shell.txt")]
    public void Shell_smoke_out_cuva_putanju_i_ne_ukljucuje_smoke(string path)
    {
        var o = LaunchOptions.Parse(["--shell-smoke-out", path, "ductura://doc/demo-1"]);
        Assert.Equal(path, o.ShellSmokeOutput);
        Assert.False(o.Smoke);
        Assert.Null(o.SmokeOutput);
        Assert.Equal("demo-1", o.OpenDocumentId);
    }
}
