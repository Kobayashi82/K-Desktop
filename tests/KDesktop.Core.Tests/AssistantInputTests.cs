using KDesktop.Core.Assistant;
using KDesktop.Core.Input;

namespace KDesktop.Core.Tests;

/// <summary>What the assistant process gets from K-Desktop's hook: the keys of its game and hard scrolls.</summary>
public class AssistantInputTests
{
    private const int Up = 0x26, Down = 0x28, A = 0x41;

    private static KeyboardInput Key(int vk, bool down) => new(vk, down, Extended: false, Injected: false);

    [Fact]
    public void CapturedKeysAreSwallowedAndToldOncePerPress()
    {
        using var router = new HotkeyRouter();
        var changes = new List<(int, bool)>();
        router.SetGameKeys(new HashSet<int> { Up, Down }, (vk, down) => changes.Add((vk, down)));

        Assert.True(router.OnKey(Key(Up, true)));
        Assert.True(router.OnKey(Key(Up, true)));   // auto-repeat: swallowed, not told again
        Assert.True(router.OnKey(Key(Up, false)));
        Assert.False(router.OnKey(Key(A, true)));   // not the game's
        Assert.False(router.OnKey(Key(A, false)));
        Assert.Equal([(Up, true), (Up, false)], changes);
    }

    [Fact]
    public void AKeyHeldWhenTheGameEndsIsSwallowedOnItsReleaseToo()
    {
        using var router = new HotkeyRouter();
        var changes = new List<(int, bool)>();
        router.SetGameKeys(new HashSet<int> { Up }, (vk, down) => changes.Add((vk, down)));
        Assert.True(router.OnKey(Key(Up, true)));

        router.SetGameKeys(null, null);
        // The application never saw it go down: it must not see it go up either.
        Assert.True(router.OnKey(Key(Up, false)));
        Assert.False(router.OnKey(Key(Up, true)));
        Assert.False(router.OnKey(Key(Up, false)));
        Assert.Equal([(Up, true)], changes);
    }

    [Fact]
    public void InjectedKeysAreNotCaptured()
    {
        using var router = new HotkeyRouter();
        router.SetGameKeys(new HashSet<int> { Up }, (_, _) => { });
        Assert.False(router.OnKey(new KeyboardInput(Up, true, false, Injected: true)));
    }

    [Fact]
    public void AFewQuickNotchesAreNotAHardScroll()
    {
        var meter = new FastScrollMeter();
        Assert.Null(meter.Add(0.0, -120, false));
        Assert.Null(meter.Add(0.1, -120, false));
        Assert.Null(meter.Add(0.2, -120, false));
    }

    [Fact]
    public void ManyQuickNotchesAreAHardScrollDownwards()
    {
        var meter = new FastScrollMeter();
        double? strength = null;
        for (var i = 0; i < 6 && strength is null; i++) strength = meter.Add(i * 0.03, -120, false);
        Assert.NotNull(strength);
        Assert.InRange(strength!.Value, 0.25, 1);

        // Then it rests a moment, and the energy starts again from nothing.
        Assert.Null(meter.Add(0.5, -120, false));
        Assert.Null(meter.Add(0.52, 120, false));
    }

    [Fact]
    public void UpwardsIsNegativeAndSlowScrollingFadesAway()
    {
        var meter = new FastScrollMeter();
        double? strength = null;
        for (var i = 0; i < 6 && strength is null; i++) strength = meter.Add(i * 0.03, 120, false);
        Assert.True(strength < 0);

        var slow = new FastScrollMeter();
        for (var i = 0; i < 30; i++) Assert.Null(slow.Add(i * 1.0, -120, false));
    }
}
