using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// The start-up version check (DAN-944-1): health.minClientVersion against the client's own version.
// Pure function, so no network and no clock.
public class ClientVersionTests
{
    // Forms the contract pattern ^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$ must refuse.
    public static TheoryData<string> NotAVersion => new()
    {
        "", " 1.0.0", "1.0.0 ", "1.0.0\n", "1.0.0\r\n", "1.0.0\t", "1.0.0 ", "1.0.0\u007f",
        "v1.0.0", "01.0.0", "1.00.0", "1.0", "1.0.0.0", "1.0.0-rc.1", "1.0.0+b", "-1.0.0", "1..0",
        "١.٠.٠", "１.0.0", "1,0,0",
    };

    [Theory]
    [MemberData(nameof(NotAVersion))]
    public void Necitljiv_minClientVersion_je_kao_nedostupan_server(string min) =>
        Assert.Equal(VersionVerdict.ServerUnavailable, ClientVersion.Decide(true, min, "0.1.0"));

    [Theory]
    [MemberData(nameof(NotAVersion))]
    public void Necitljiva_vlastita_verzija_trazi_nadogradnju(string own) =>
        Assert.Equal(VersionVerdict.UpdateRequired, ClientVersion.Decide(true, "0.0.0", own));

    [Fact]
    public void Bez_vlastite_verzije_trazi_nadogradnju_i_kad_server_ne_odgovara()
    {
        Assert.Equal(VersionVerdict.UpdateRequired, ClientVersion.Decide(true, "0.0.0", null));
        Assert.Equal(VersionVerdict.UpdateRequired, ClientVersion.Decide(false, null, "1.0"));
    }

    [Theory]
    [InlineData("0.1.1", "0.1.0")]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("1.0.10", "1.0.9")]
    [InlineData("2.0.0", "1.99.99")]
    [InlineData("99999999999999999999.0.0", "18446744073709551615.0.0")]
    public void Stariji_klijent_trazi_nadogradnju_i_ne_otvara_web(string min, string own)
    {
        var verdict = ClientVersion.Decide(true, min, own);
        Assert.Equal(VersionVerdict.UpdateRequired, verdict);
        Assert.False(ClientVersion.OpensWeb(verdict));
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("0.0.0", "0.1.0")]
    [InlineData("1.99.99", "2.0.0")]
    [InlineData("18446744073709551615.0.0", "99999999999999999999.0.0")]
    public void Jednak_ili_noviji_klijent_otvara_web(string min, string own)
    {
        var verdict = ClientVersion.Decide(true, min, own);
        Assert.Equal(VersionVerdict.Current, verdict);
        Assert.True(ClientVersion.OpensWeb(verdict));
    }

    [Fact]
    public void Odgovor_bez_minClientVersion_ne_trazi_nadogradnju() =>
        Assert.Equal(VersionVerdict.Current, ClientVersion.Decide(true, null, "0.1.0"));

    [Theory]
    [InlineData(null)]
    [InlineData("9.9.9")]
    public void Nedostupan_server_otvara_web_bez_obzira_na_verziju(string? min)
    {
        // serverAnswered=false: no answer, time limit, non-200 or a body that fails the contract.
        var verdict = ClientVersion.Decide(false, min, "0.1.0");
        Assert.Equal(VersionVerdict.ServerUnavailable, verdict);
        Assert.True(ClientVersion.OpensWeb(verdict));
    }

    [Fact]
    public void Poruka_o_nadogradnji_je_na_hrvatskom() =>
        Assert.Equal("Ova verzija Ducture je zastarjela. Za nastavak instaliraj novu verziju.", ClientVersion.UpdateRequiredText);
}
