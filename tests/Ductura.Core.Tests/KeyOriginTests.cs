using Ductura.Core;
using Xunit;

namespace Ductura.Core.Tests;

// PROPOSAL P-3 (status PRIJEDLOG): invented input only.
public class KeyOriginTests
{
    private static readonly IntPtr Keyboard = new(0x1234);

    [Fact]
    public void Tipka_bez_uredaja_je_od_programa()
    {
        Assert.Equal(KeyOrigin.Program, KeyOriginTally.Classify(IntPtr.Zero));
        Assert.Equal(KeyOrigin.Device, KeyOriginTally.Classify(Keyboard));
    }

    [Fact]
    public void Broji_samo_pritiske_ne_otpustanja()
    {
        var t = new KeyOriginTally();
        t.Record(Keyboard, released: false);
        t.Record(Keyboard, released: true);
        t.Record(IntPtr.Zero, released: false);
        t.Record(IntPtr.Zero, released: true);
        Assert.Equal(1, t.DeviceKeys);
        Assert.Equal(1, t.ProgramKeys);
        Assert.Equal("{\"deviceKeys\":1,\"programKeys\":1}", t.ToJson());
    }

    private static KeyOriginTally Tally(int device, int program)
    {
        var t = new KeyOriginTally();
        for (var i = 0; i < device; i++) t.Record(Keyboard, false);
        for (var i = 0; i < program; i++) t.Record(IntPtr.Zero, false);
        return t;
    }

    [Fact]
    public void R5_okida_kad_program_posalje_vecinu_tipki()
    {
        Assert.True(KeysFromProgramRule.Fires(Tally(10, 400), desktopTask: true, assistiveToolOrAccommodation: false));
    }

    [Theory]
    [InlineData(2000, 30)] // a few keys from a program, e.g. a shortcut tool: below the count
    [InlineData(2000, 60)] // over the count, but a small share of all keys
    [InlineData(0, 0)]
    public void R5_ne_okida_ispod_praga(int device, int program)
    {
        Assert.False(KeysFromProgramRule.Fires(Tally(device, program), desktopTask: true, assistiveToolOrAccommodation: false));
    }

    [Fact]
    public void R5_nikad_ne_okida_uz_pomocnu_tehnologiju_ili_prilagodbu()
    {
        // The on-screen keyboard sends every key through SendInput: all of them look like a program's.
        Assert.False(KeysFromProgramRule.Fires(Tally(0, 3000), desktopTask: true, assistiveToolOrAccommodation: true));
    }

    [Fact]
    public void R5_ne_postoji_izvan_zadatka_za_desktop_aplikaciju()
    {
        Assert.False(KeysFromProgramRule.Fires(Tally(0, 3000), desktopTask: false, assistiveToolOrAccommodation: false));
    }
}
