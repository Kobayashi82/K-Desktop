using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;

namespace MyAssistant;

/// <summary>
/// A new assistant. It starts with what every assistant needs: a body drawn on a surface (only when something
/// changes), a hit area, and the two gestures the module always asks for (Communicate and React). Add more gestures
/// to «traits» in Package\assistant.json as you implement them.
/// </summary>
public sealed class MyAssistant : IAssistant
{
    private IAssistantHost _host = null!;
    private ISurface _surface = null!;
    private IHitArea _hit = null!;
    private Point _at;
    private Color _color = Colors.MediumPurple;
    private Color _flashColor = Colors.White;
    // A few ways to react, so it does not do the same every time: by weight, never twice in a row, the rare one rests.
    private readonly Variety<Color> _reactions = new Variety<Color>()
        .Add(Colors.White, weight: 2)
        .Add(Colors.Gold)
        .Add(Colors.HotPink, weight: 0.5, cooldown: 60);
    private double _flash = double.NaN;
    private double _flashLength = 0.5;
    private bool _flashPending;
    private bool _dirty = true;

    // Where K-Desktop's speech bubble points. To draw what it says its own way instead, put "speech": "own" in
    // assistant.json and implement ShowSpeech and HideSpeech (returning false from ShowSpeech brings the bubble back).
    public Point? BubbleAnchor => new Point(_at.X, _at.Y - 20 * _host.Common.Size);

    // Where its body is: the other assistants avoid stopping on it (and it avoids theirs: World.Others).
    public PixelRect? BodyBounds => BodyAt(_at);

    private PixelRect BodyAt(Point at)
    {
        var body = (int)(18 * _host.Common.Size);
        return new PixelRect((int)at.X - body, (int)at.Y - body, (int)at.X + body, (int)at.Y + body);
    }

    public void Attach(IAssistantHost host)
    {
        _host = host;
        _surface = host.Rendering.CreateSurface();
        _hit = host.Pointer.CreateHitArea();
    }

    public void Enter(EntranceKind how)
    {
        // Sit above the taskbar of the main monitor.
        var work = _host.World.Current.PrimaryMonitor?.WorkArea ?? new PixelRect(0, 0, 800, 600);
        _at = new Point(work.Right - 200, work.Bottom - 24);
        // Another assistant already stays there: a little further left, never on top of it.
        for (var i = 0; i < 20 && _host.World.TakenBy(BodyAt(_at).Inflate(4)) is not null; i++) _at.X -= 40 * _host.Common.Size;
        _dirty = true;
        // Startup and Summoned deserve an entrance (here, a flash); Reloaded comes back quietly, where it was.
        if (how != EntranceKind.Reloaded) Flash();
    }

    public async Task ExitAsync(ExitKind why, CancellationToken ct)
    {
        // Shutdown and Reloading: leave at once. Dismissed and Uninstalling: a goodbye (the host waits up to 3 s).
        if (why is ExitKind.Shutdown or ExitKind.Reloading) return;
        Flash();
        try { await Task.Delay(500, ct); } catch (OperationCanceledException) { }
    }

    public double Tick(TickInfo tick)
    {
        if (_flashPending)
        {
            _flashPending = false;
            _flash = tick.Now;
        }
        var flashing = !double.IsNaN(_flash) && tick.Now - _flash < _flashLength;
        if (_dirty || flashing || !double.IsNaN(_flash))
        {
            if (!flashing) _flash = double.NaN;
            _dirty = false;
            // The size the user chose in the «General» tab of its card (1 = as designed; "common" in assistant.json).
            var size = _host.Common.Size;
            var radius = (flashing ? 22 : 18) * size;
            var edge = (int)Math.Ceiling(24 * size);
            var area = new PixelRect((int)_at.X - edge, (int)_at.Y - edge, (int)_at.X + edge, (int)_at.Y + edge);
            var brush = new SolidColorBrush(flashing ? _flashColor : _color);
            brush.Freeze();
            _surface.Present(area, dc => dc.DrawEllipse(brush, null, _at, radius, radius));
            _hit.Place(new HitShape.Ellipse(BodyAt(_at)));
        }
        // Animating: every frame. At rest: a long sleep (anything that matters calls RequestTick).
        return flashing ? _host.Rendering.FrameInterval : 30;
    }

    public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React or GestureKind.Announce;

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        if (gesture is Gesture.React react)
        {
            // Its context: the first of the day, a celebration or the third in a row of the same kind deserves more.
            var context = react.Context;
            var big = context.FirstToday || context.Streak >= 3 || react.Reaction == Reaction.Celebration;
            var seconds = big ? 1.2 : 0.5;
            Flash(react.Reaction == Reaction.Failure ? Colors.IndianRed : _reactions.Pick(), seconds);
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), ct); } catch (OperationCanceledException) { }
            return GestureOutcome.Committed;
        }
        if (gesture is Gesture.Announce announce)
        {
            // It is about to say something on its own (a timer, a notice): catch the user's eye first, then the host speaks.
            // Here, two flashes; longer when the user has been away a while. Be quick: the host waits at most ctx.TimeLeft.
            var seconds = Math.Min(announce.UserAway > 60 ? 1.6 : 0.8, ctx.TimeLeft.TotalSeconds);
            Flash(Colors.Gold, seconds);
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), ct); } catch (OperationCanceledException) { }
            return GestureOutcome.Committed;
        }
        if (gesture is Gesture.Communicate)
        {
            // Stay visible while the module shows the bubble.
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return GestureOutcome.Committed;
        }
        return GestureOutcome.NotPerformed;
    }

    // Its own animations, for the showcase (the «Try» tab of its card in K-Desktop, and the catalog). Entrances, exits,
    // states, reactions and gestures are always there: the host launches those itself.
    public IReadOnlyList<ShowcaseItem> Showcase { get; } =
        [new("rainbow", new Dictionary<string, string> { ["en"] = "Rainbow", ["es"] = "Arcoíris" }, ShowcaseGroup.Tricks)];

    public async Task<bool> PlayShowcaseAsync(string id, CancellationToken ct)
    {
        if (id != "rainbow") return false;
        foreach (var color in new[] { Colors.Red, Colors.Orange, Colors.Gold, Colors.LimeGreen, Colors.DeepSkyBlue, Colors.MediumPurple })
        {
            Flash(color, 0.25);
            await Task.Delay(250, ct);
        }
        return true;
    }

    // What the module is doing (listening, thinking, speaking…) and with what: detail.Activity says it is searching the
    // internet, reading a page, looking at the screen…, and detail.Text says so in its language («Buscando en internet»).
    public void SetState(AssistantState state, StateDetail detail)
    {
        _color = state switch
        {
            AssistantState.Listening => Colors.MediumSeaGreen,
            AssistantState.Thinking or AssistantState.Busy or AssistantState.AwaitingConfirmation => Colors.SteelBlue,
            AssistantState.ThinkingHard => Colors.DarkSlateBlue,
            AssistantState.LoadingBrain => Colors.SlateGray,
            _ => Colors.MediumPurple,
        };
        // No brain: a shrug (here, a red flash) right before it says so.
        if (state == AssistantState.NoBrain) Flash(Colors.IndianRed);
        _dirty = true;
        _host.RequestTick();
    }

    // The states of SDK 1.12 and earlier only; the host calls the one above.
    public void SetState(AssistantState state) => SetState(state, StateDetail.None);

    public void OnVoiceLevel(double level) { }

    public void OnPointer(PointerEvent e)
    {
        switch (e.Kind)
        {
            case PointerEventKind.Click:
                Flash();
                break;
            // Files dragged from the Explorer over it, and dropped (SDK 1.16): it catches them (K-Desktop does the rest).
            // e.Preview (SDK 1.17) is their picture: Windows hides its own over the assistant, so it may draw it its way.
            case PointerEventKind.FilesOver:
                Flash(Colors.LightSkyBlue, 0.3);
                break;
            case PointerEventKind.FilesDropped:
                Flash(Colors.DeepSkyBlue, 0.8);
                break;
        }
    }

    public void OnSystemEvent(SystemEvent e)
    {
        if (e is SystemEvent.NotificationShown) Flash();
    }

    public Task OnExtraAsync(string extraId, CancellationToken ct) => Task.CompletedTask;

    public void OnSettingsChanged(JsonElement settings)
    {
        // Its settings.json changed, or the common settings (its size): draw again.
        _dirty = true;
        _host.RequestTick();
    }

    public void Dispose()
    {
        _hit.Dispose();
        _surface.Dispose();
    }

    private void Flash(Color? color = null, double seconds = 0.5)
    {
        _flashColor = color ?? Colors.White;
        _flashLength = seconds;
        _flashPending = true;
        _host.RequestTick();
    }
}
