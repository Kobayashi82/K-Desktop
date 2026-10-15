using System.Diagnostics;
using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

/// <summary>
/// Plays macro steps with SendInput, following the recorded timing against a single clock (no drift).
/// Whatever the macro left pressed is released at the end, also when it is stopped half-way.
/// </summary>
public static class MacroPlayer
{
    public static async Task PlayAsync(IReadOnlyList<MacroStep> steps, double speed, CancellationToken token)
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
                Perform(step, keys, buttons);
            }
        }
        finally
        {
            foreach (var (vk, ext) in keys) InputSimulator.KeyUp(vk, ext);
            foreach (var b in buttons) InputSimulator.MouseUp(b);
            Win32.timeEndPeriod(1);
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
