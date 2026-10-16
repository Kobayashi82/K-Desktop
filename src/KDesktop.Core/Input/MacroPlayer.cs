using System.Diagnostics;
using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

/// <summary>
/// Plays macro steps with SendInput, following the recorded timing against a single clock (no drift).
/// Whatever the macro left pressed is released at the end, also when it is stopped half-way.
/// Game mode (<see cref="Macro.GameMode"/>): clicks happen where the pointer is, and a key press or a click stays down
/// <see cref="GameHold"/> ms, since many games look at the keys once per frame and miss an instant press.
/// </summary>
public static class MacroPlayer
{
    /// <summary>How long a key press or a click is held in game mode (ms): about two frames at 60 fps.</summary>
    public const int GameHold = 35;

    public static async Task PlayAsync(IReadOnlyList<MacroStep> steps, double speed, bool gameMode, CancellationToken token)
    {
        speed = Math.Clamp(speed, 0.1, 20);
        var keys = new HashSet<(int Vk, bool Extended)>();
        var buttons = new HashSet<MouseButton>();
        Win32.timeBeginPeriod(1);
        try
        {
            var clock = Stopwatch.StartNew();
            double due = 0;
            foreach (var step in steps)
            {
                due += Math.Max(0, step.Delay) / speed;
                var wait = due - clock.Elapsed.TotalMilliseconds;
                if (wait >= 1) await Task.Delay(TimeSpan.FromMilliseconds(wait), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (gameMode) await PerformForGameAsync(step, keys, buttons, token).ConfigureAwait(false);
                else Perform(step, keys, buttons);
            }
        }
        finally
        {
            foreach (var (vk, ext) in keys) InputSimulator.KeyUp(vk, ext);
            foreach (var b in buttons) InputSimulator.MouseUp(b);
            Win32.timeEndPeriod(1);
        }
    }

    /// <summary>Game mode: no pointer jumps before clicks, and presses that last.</summary>
    private static async Task PerformForGameAsync(MacroStep s, HashSet<(int, bool)> keys, HashSet<MouseButton> buttons, CancellationToken token)
    {
        switch (s.Kind)
        {
            case MacroStepKind.KeyPress when s.Vk != 0:
                InputSimulator.KeyDown(s.Vk, s.Extended);
                keys.Add((s.Vk, s.Extended));
                await Task.Delay(GameHold, token).ConfigureAwait(false);
                InputSimulator.KeyUp(s.Vk, s.Extended);
                keys.Remove((s.Vk, s.Extended));
                break;
            case MacroStepKind.MouseDown:
                InputSimulator.MouseDown(s.Button);
                buttons.Add(s.Button);
                break;
            case MacroStepKind.MouseUp:
                InputSimulator.MouseUp(s.Button);
                buttons.Remove(s.Button);
                break;
            case MacroStepKind.Click:
                InputSimulator.MouseDown(s.Button);
                buttons.Add(s.Button);
                await Task.Delay(GameHold, token).ConfigureAwait(false);
                InputSimulator.MouseUp(s.Button);
                buttons.Remove(s.Button);
                break;
            case MacroStepKind.MouseMoveBy:
                InputSimulator.MoveRaw(s.X, s.Y); // what games read, whatever the step says
                break;
            default:
                Perform(s, keys, buttons);
                break;
        }
    }

    private static void Perform(MacroStep s, HashSet<(int, bool)> keys, HashSet<MouseButton> buttons)
    {
        switch (s.Kind)
        {
            case MacroStepKind.KeyDown when s.Vk != 0:
                InputSimulator.KeyDown(s.Vk, s.Extended);
                keys.Add((s.Vk, s.Extended));
                break;
            case MacroStepKind.KeyUp when s.Vk != 0:
                InputSimulator.KeyUp(s.Vk, s.Extended);
                keys.Remove((s.Vk, s.Extended));
                break;
            case MacroStepKind.KeyPress when s.Vk != 0:
                InputSimulator.KeyPress(s.Vk, s.Extended);
                break;
            case MacroStepKind.Text when !string.IsNullOrEmpty(s.Text):
                InputSimulator.SendText(s.Text, releaseModifiers: false);
                break;
            case MacroStepKind.MouseMove:
                InputSimulator.MoveTo(s.X, s.Y);
                break;
            case MacroStepKind.MouseMoveBy when s.Raw:
                InputSimulator.MoveRaw(s.X, s.Y);
                break;
            case MacroStepKind.MouseMoveBy:
                InputSimulator.MoveBy(s.X, s.Y);
                break;
            case MacroStepKind.MouseDown:
                InputSimulator.MoveTo(s.X, s.Y);
                InputSimulator.MouseDown(s.Button);
                buttons.Add(s.Button);
                break;
            case MacroStepKind.MouseUp:
                InputSimulator.MoveTo(s.X, s.Y);
                InputSimulator.MouseUp(s.Button);
                buttons.Remove(s.Button);
                break;
            case MacroStepKind.Click:
                InputSimulator.MoveTo(s.X, s.Y);
                InputSimulator.MouseClick(s.Button);
                break;
            case MacroStepKind.Wheel when s.Wheel != 0:
                InputSimulator.Wheel(s.Wheel, s.Horizontal);
                break;
        }
    }
}
