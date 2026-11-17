using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// Every navigation of the shell (DAN-960): a host from the list stays, another https host goes to the
// browser, everything else is refused. Refusal rows come first.
public class NavigationTests
{
    private static readonly string[] Hosts = ["ductura.org"];

    private static NavigationDecision Decide(string? url) => Navigation.Decide(url, Hosts);

    [Theory]
    // empty
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    // schemes other than https, in any letter case
    [InlineData("http://ductura.org/app/")]
    [InlineData("HTTP://ductura.org/app/")]
    [InlineData("file:///C:/Users/x/doc.html")]
    [InlineData("FILE://server/share/doc.html")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("javascript://ductura.org/%0aalert(1)")]
    [InlineData("data:text/html,<p>x</p>")]
    [InlineData("blob:https://ductura.org/1234")]
    [InlineData("about:blank")]
    [InlineData("ftp://ductura.org/app/")]
    [InlineData("ws://ductura.org/app/")]
    [InlineData("wss://ductura.org/app/")]
    [InlineData("ductura://doc/demo-1")]
    [InlineData("mailto:x@ductura.org")]
    // not absolute, Windows paths
    [InlineData("/app/")]
    [InlineData("ductura.org/app/")]
    [InlineData(@"C:\Users\x\doc.html")]
    [InlineData(@"\\server\share\doc.html")]
    [InlineData(@"https://ductura.org\app\")]
    [InlineData(@"https:\\ductura.org\app\")]
    [InlineData("https:ductura.org/app/")]
    [InlineData("https:/ductura.org/app/")]
    [InlineData("https:///ductura.org/app/")]
    [InlineData("//ductura.org/app/")]
    [InlineData("C:/Users/x/doc.html")]
    // control, whitespace and non-ASCII characters
    [InlineData(" https://ductura.org/app/")]
    [InlineData("https://ductura.org/app/ ")]
    [InlineData("https://ductura.org/app/\r\n")]
    [InlineData("https://ductura.org/a\npp/")]
    [InlineData("https://ductura.org/a\tpp/")]
    [InlineData("https://ductura.org/a\u00a0pp/")]
    [InlineData("https://ductura.org/a\u007fpp/")]
    [InlineData("https://ductura.org/rad-č/")]
    [InlineData("https://dúctura.org/app/")]
    // user in the address
    [InlineData("https://user@ductura.org/app/")]
    [InlineData("https://ductura.org@evil.com/app/")]
    [InlineData("https://user:pass@evil.com/")]
    [InlineData("https://@ductura.org/app/")]
    [InlineData("https://:@ductura.org/app/")]
    public void Odbija(string? url) => Assert.Equal(NavigationDecision.Block, Decide(url));

    [Theory]
    [InlineData("https://evil.org/app/")]
    [InlineData("https://x.ductura.org/app/")]
    [InlineData("https://ductura.org.evil.com/app/")]
    [InlineData("https://ductura.org./app/")]
    [InlineData("https://127.0.0.1/app/")]
    [InlineData("https://[::1]/app/")]
    [InlineData("https://xn--dctura-3ya.org/app/")]
    [InlineData("https://localhost/")]
    [InlineData("https://ductura.org:8443/app/")]
    // a port, even the default one, is not the shell address
    [InlineData("https://ductura.org:443/app/")]
    [InlineData("https://ductura.org:0443/app/")]
    [InlineData("https://ductura.org:/app/")]
    // the host in another letter case is not the shell address (WebView2 reports it lowercase)
    [InlineData("https://Ductura.org/app/")]
    [InlineData("https://www.srce.hr/")]
    public void Drugi_https_host_ide_u_preglednik(string url) =>
        Assert.Equal(NavigationDecision.Browser, Decide(url));

    [Theory]
    [InlineData("https://ductura.org/app/")]
    [InlineData("https://ductura.org/app/#/doc/demo-1")]
    [InlineData("https://ductura.org/app/?x=1")]
    [InlineData("https://ductura.org")]
    [InlineData("HTTPS://ductura.org/app/")]
    [InlineData("https://ductura.org/x#@evil.com")]
    [InlineData("https://ductura.org?@evil.com")]
    [InlineData("https://ductura.org/%C4%8D/")]
    public void Host_s_popisa_ostaje_u_ljusci(string url) =>
        Assert.Equal(NavigationDecision.Shell, Decide(url));

    [Fact]
    public void Popis_null_ili_s_velikim_slovima_nista_ne_drzi_u_ljusci()
    {
        Assert.Equal(NavigationDecision.Browser, Navigation.Decide("https://ductura.org/app/", null));
        Assert.Equal(NavigationDecision.Browser, Navigation.Decide("https://ductura.org/app/", ["Ductura.org"]));
    }

    [Fact]
    public void Prazan_popis_hostova_nista_ne_drzi_u_ljusci() =>
        Assert.Equal(NavigationDecision.Browser, Navigation.Decide("https://ductura.org/app/", []));

    [Fact]
    public void Vrlo_duga_adresa_ne_baca_iznimku_nego_odbija() =>
        // System.Uri accepts at most 65519 characters.
        Assert.Equal(NavigationDecision.Block, Decide("https://ductura.org/" + new string('a', 100_000)));
}
