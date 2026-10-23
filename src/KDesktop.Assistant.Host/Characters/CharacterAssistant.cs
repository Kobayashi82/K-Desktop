using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Assistant.Host.Characters;

/// <summary>
/// An assistant without code (phase 12): the host's own <see cref="IAssistant"/> for a package whose entry is a
/// <see cref="CharacterFile"/>. It stands on the taskbar or on the top of a window, walks about now and then (and may
/// walk off a window and fall), follows the window it stands on, can be dragged and dropped, sleeps when the user is away,
/// and plays an animation for each thing: listening, thinking, talking (Communicate), reacting (React), a click. Nothing of
/// the package runs: only its pictures and sounds are read.
/// </summary>
internal sealed class CharacterAssistant(CharacterFile character) : IAssistant
{
    private const double Gravity = 2600;
    private const double SleepAfter = 180;
    private const double ReactLongest = 2.5;

    private readonly Random _random = new();
    private readonly Dictionary<string, ImageSource[]> _frames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISound?> _sounds = new(StringComparer.OrdinalIgnoreCase);
    private IAssistantHost _host = null!;
    private ISurface _surface = null!;
    private IHitArea _hit = null!;

    /// <summary>Where its feet are (screen pixels, the middle of its base).</summary>
    private Point _at;
    private Vector _velocity;
    private bool _falling;
    private bool _dragging;
    private Vector _grab;
    private Ground? _ground;
    private double? _walkTo;
    private bool _right = true;
    private double _nextWander;
    private AssistantState _state;
    private bool _talking;
    private bool _gone;

    /// <summary>One animation that plays once over the rest (a reaction, a click, coming in, leaving) and until when.</summary>
    private (string Name, double Start, double Until)? _once;
    private string _playing = "idle";
    private double _playingSince;
    private double _scale = 1;
    private bool _wander = true;
    private double Now { get; set; }

    /// <summary>Where it stands: a stretch of the taskbar or of a window's top (and that window).</summary>
    private sealed record Ground(double Y, double Left, double Right, long Window);

    public Point? BubbleAnchor => new Point(_at.X, _at.Y - HeightPx);

    private double Dpi => _host.World.Current.MonitorAt(new Point(_at.X, _at.Y - 1))?.DpiScale ?? _host.World.Current.PrimaryMonitor?.DpiScale ?? 1;

    private double HeightPx => character.Height * Dpi * _scale;

    public void Attach(IAssistantHost host)
    {
        _host = host;
        _surface = host.Rendering.CreateSurface();
        _hit = host.Pointer.CreateHitArea();
        ReadSettings(host.Settings);
        host.World.Changed += OnWorldChanged;
    }

    public void Enter(EntranceKind how)
    {
        var spot = RandomSpot() ?? new Point(400, 400);
        _at = spot;
        _ground = GroundAt(_at.X, _at.Y);
        _nextWander = Now + 3 + _random.NextDouble() * 5;
        if (character.Animations.ContainsKey("enter")) PlayOnce("enter");
        _host.RequestTick();
    }

    public async Task ExitAsync(ExitKind why, CancellationToken ct)
    {
        if (!character.Animations.ContainsKey("exit") || why == ExitKind.Shutdown)
        {
            _gone = true;
            _surface.Hide();
            return;
        }
        PlayOnce("exit");
        _host.RequestTick();
        var animation = character.For("exit");
        try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(ReactLongest, animation.FrameCount / animation.Fps)), ct); }
        catch (OperationCanceledException) { }
        _gone = true;
        _surface.Hide();
    }

    public double Tick(TickInfo tick)
    {
        Now = tick.Now;
        var dt = Math.Min(tick.Elapsed, 0.05);
        if (_gone) return 1;
        if (_host.World.System.FullscreenMonitors.Contains(MonitorIndex()))
        {
            // Out of the way of whatever is full screen: to another monitor, or hidden until it ends.
            if (RandomSpot() is { } elsewhere && !_host.World.System.FullscreenMonitors.Contains(MonitorIndex(elsewhere))) MoveTo(elsewhere);
            else
            {
                _surface.Hide();
                _hit.Hide();
                return 1;
            }
        }

        var moving = Step(dt);
        if (!moving && _wander && !_dragging && _once is null && _state == AssistantState.Idle && !_talking && Now >= _nextWander && !Asleep) Wander();

        var name = Animation();
        if (name != _playing)
        {
            _playing = name;
            _playingSince = Now;
            PlaySound(name);
        }
        Draw();

        var animation = character.For(_playing);
        var frames = animation.FrameCount;
        if (moving || _dragging) return _host.Rendering.FrameInterval;
        // The next frame of what plays (a still picture: only the next wander, or a look at whether the user went away).
        var nextFrame = frames > 1 ? 1 / animation.Fps : double.PositiveInfinity;
        var nextOnce = _once is { } once ? Math.Max(0.01, once.Until - Now) : double.PositiveInfinity;
        var nextWander = _wander && character.WalkSpeed > 0 ? Math.Max(0.05, _nextWander - Now) : 5;
        return Math.Max(0.016, Math.Min(Math.Min(nextFrame, nextOnce), Math.Min(nextWander, 5)));
    }

    // ── moving ───────────────────────────────────────────────────────────

    /// <summary>One step of falling, walking or following its window; whether something moved.</summary>
    private bool Step(double dt)
    {
        if (_dragging) return true;
        if (_falling)
        {
            _velocity.Y += Gravity * Dpi * dt;
            var before = _at.Y;
            _at += _velocity * dt;
            if (LandingBetween(before, _at.Y, _at.X) is { } ground)
            {
                _at.Y = ground.Y;
                _ground = ground;
                _falling = false;
                _velocity = default;
                _nextWander = Now + 2 + _random.NextDouble() * 4;
            }
            return true;
        }
        if (_walkTo is { } target && character.WalkSpeed > 0)
        {
            var step = character.WalkSpeed * Dpi * _scale * dt;
            var left = target - _at.X;
            _right = left > 0;
            if (Math.Abs(left) <= step)
            {
                _at.X = target;
                _walkTo = null;
                _nextWander = Now + 4 + _random.NextDouble() * 10;
            }
            else _at.X += Math.Sign(left) * step;
            // Off the end of where it stands: it falls.
            if (_ground is { } ground && (_at.X < ground.Left || _at.X > ground.Right)) StartFalling();
            return true;
        }
        return false;
    }

    private void Wander()
    {
        _nextWander = Now + 5 + _random.NextDouble() * 10;
        if (character.WalkSpeed <= 0 || _ground is not { } ground || _random.NextDouble() < 0.35) return;
        var width = ground.Right - ground.Left;
        var margin = Math.Min(width / 2, HeightPx * 0.4);
        // On a window, now and then it walks off the end.
        if (ground.Window != 0 && _random.NextDouble() < 0.2)
            _walkTo = _random.NextDouble() < 0.5 ? ground.Left - HeightPx : ground.Right + HeightPx;
        else
            _walkTo = ground.Left + margin + _random.NextDouble() * Math.Max(1, width - 2 * margin);
        _host.RequestTick();
    }

    private void StartFalling()
    {
        _falling = true;
        _ground = null;
        _walkTo = null;
        _host.RequestTick();
    }

    private void MoveTo(Point spot)
    {
        _at = spot;
        _ground = GroundAt(spot.X, spot.Y);
        _falling = _ground is null;
        _walkTo = null;
    }

    /// <summary>The window it stands on moved, closed or was covered: it follows it, or falls.</summary>
    private void OnWorldChanged(WorldSnapshot world)
    {
        if (_dragging || _falling || _ground is not { } ground) return;
        if (ground.Window == 0)
        {
            // The taskbar of its monitor moved (another resolution, a monitor less).
            if (GroundAt(_at.X, _at.Y) is null) StartFalling();
            return;
        }
        if (world.Window(ground.Window) is not { } window || window.IsMaximized)
        {
            StartFalling();
            return;
        }
        var b = window.Bounds;
        var dx = _at.X - ground.Left;
        _ground = new Ground(b.Top, b.Left, b.Right, ground.Window);
        _at = new Point(Math.Clamp(b.Left + dx, b.Left, b.Right), b.Top);
        if (!Visible(_at.X, b.Top, window.Handle)) StartFalling();
        _host.RequestTick();
    }

    /// <summary>The first ground between two heights below <paramref name="x"/> (falling from <paramref name="from"/> to <paramref name="to"/>).</summary>
    private Ground? LandingBetween(double from, double to, double x)
    {
        Ground? best = null;
        foreach (var ground in Grounds(x))
            if (ground.Y >= from - 1 && ground.Y <= to && (best is null || ground.Y < best.Y)) best = ground;
        if (best is not null) return best;
        // Below everything (out of the screen): on the taskbar of the monitor nearest to it.
        var monitors = _host.World.Current.Monitors;
        if (monitors.Count == 0) return null;
        var near = monitors.OrderBy(m => Math.Abs(m.Bounds.Center.X - x)).First();
        if (to < near.WorkArea.Bottom) return null;
        _at.X = Math.Clamp(x, near.WorkArea.Left + 10, near.WorkArea.Right - 10);
        return new Ground(near.WorkArea.Bottom, near.WorkArea.Left, near.WorkArea.Right, 0);
    }

    /// <summary>Where it can stand at <paramref name="x"/>: the bottom of the work area and the visible tops of the windows.</summary>
    private IEnumerable<Ground> Grounds(double x)
    {
        var world = _host.World.Current;
        foreach (var monitor in world.Monitors)
            if (x >= monitor.WorkArea.Left && x < monitor.WorkArea.Right)
                yield return new Ground(monitor.WorkArea.Bottom, monitor.WorkArea.Left, monitor.WorkArea.Right, 0);
        foreach (var window in world.Windows)
        {
            var b = window.Bounds;
            if (window.IsMaximized || b.Width < 80 || x < b.Left || x >= b.Right) continue;
            if (world.MonitorAt(new Point(x, b.Top)) is not { } monitor || b.Top <= monitor.WorkArea.Top + HeightPx) continue;
            if (Visible(x, b.Top, window.Handle)) yield return new Ground(b.Top, b.Left, b.Right, window.Handle);
        }
    }

    private Ground? GroundAt(double x, double y) => Grounds(x).Where(g => Math.Abs(g.Y - y) < 3).OrderBy(g => g.Window == 0).FirstOrDefault();

    /// <summary>The top of that window at <paramref name="x"/> is not under another window.</summary>
    private bool Visible(double x, double top, long handle)
    {
        foreach (var window in _host.World.Current.Windows)
        {
            if (window.Handle == handle) return true;
            if (window.Bounds.Contains(new Point(x, top - 2))) return false;
        }
        return true;
    }

    /// <summary>A free place: the taskbar of a monitor that is not full screen, or the top of a window.</summary>
    private Point? RandomSpot()
    {
        var world = _host.World.Current;
        var monitors = world.Monitors.Where(m => !_host.World.System.FullscreenMonitors.Contains(m.Index)).ToList();
        if (monitors.Count == 0) return null;
        var monitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        if (_random.NextDouble() < 0.3) monitor = monitors[_random.Next(monitors.Count)];
        var work = monitor.WorkArea;
        return new Point(work.Left + 80 + _random.NextDouble() * Math.Max(1, work.Width - 160), work.Bottom);
    }

    private int MonitorIndex(Point? at = null) => _host.World.Current.MonitorAt(at ?? new Point(_at.X, _at.Y - 1))?.Index ?? -1;

    // ── what it shows ────────────────────────────────────────────────────

    private bool Asleep => _state == AssistantState.Sleeping || (_state == AssistantState.Idle && !_talking && _host.World.System.UserIdleSeconds > SleepAfter);

    /// <summary>The animation for now: one that plays once, else what it is doing.</summary>
    private string Animation()
    {
        if (_once is { } once)
        {
            if (Now < once.Until) return once.Name;
            _once = null;
        }
        if (_dragging) return "drag";
        if (_falling) return "fall";
        if (_talking || _state == AssistantState.Speaking) return "talk";
        if (_state == AssistantState.Listening) return "listen";
        if (_state is AssistantState.Thinking or AssistantState.Busy or AssistantState.AwaitingConfirmation) return "think";
        if (_walkTo is not null) return "walk";
        if (Asleep) return "sleep";
        return "idle";
    }

    /// <summary>Plays <paramref name="name"/> once over the rest.</summary>
    private void PlayOnce(string name)
    {
        var animation = character.For(name);
        var length = animation.Loop ? Math.Min(ReactLongest, Math.Max(1, animation.FrameCount / animation.Fps)) : animation.FrameCount / animation.Fps;
        _once = (name, Now, Now + Math.Clamp(length, 0.2, ReactLongest));
        _playing = string.Empty;
        _host.RequestTick();
    }

    private void Draw()
    {
        var animation = character.For(_playing);
        var frames = FramesOf(_playing, animation);
        if (frames.Length == 0) return;
        var index = (int)Math.Floor((Now - _playingSince) * animation.Fps);
        index = animation.Loop || _once is null ? index % frames.Length : Math.Min(index, frames.Length - 1);
        var frame = frames[Math.Max(0, index)];
        var h = HeightPx;
        var w = frame.Height > 0 ? h * frame.Width / frame.Height : h;
        var rect = new Rect(_at.X - w / 2, _at.Y - h, w, h);
        var mirror = _right == character.FacesLeft;
        var area = new PixelRect((int)Math.Floor(rect.Left) - 1, (int)Math.Floor(rect.Top) - 1, (int)Math.Ceiling(rect.Right) + 1, (int)Math.Ceiling(rect.Bottom) + 1);
        _surface.Present(area, dc =>
        {
            if (mirror) dc.PushTransform(new ScaleTransform(-1, 1, _at.X, 0));
            dc.DrawImage(frame, rect);
            if (mirror) dc.Pop();
        });
        _hit.Place(new HitShape.Rectangle(new PixelRect((int)rect.Left, (int)rect.Top, (int)rect.Right, (int)rect.Bottom)));
    }

    /// <summary>The frames of an animation, read from the package once (a strip is cut into its frames).</summary>
    private ImageSource[] FramesOf(string name, CharacterAnimation animation)
    {
        var key = animation.Frames[0] + "|" + animation.Count;
        if (_frames.TryGetValue(key, out var cached)) return cached;
        var frames = new List<ImageSource>();
        try
        {
            if (animation.Strip)
            {
                var strip = Load(animation.Frames[0]);
                var width = strip.PixelWidth / animation.Count;
                for (var i = 0; i < animation.Count && width > 0; i++)
                {
                    var crop = new CroppedBitmap(strip, new Int32Rect(i * width, 0, width, strip.PixelHeight));
                    crop.Freeze();
                    frames.Add(crop);
                }
            }
            else frames.AddRange(animation.Frames.Select(Load));
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UnauthorizedAccessException)
        {
            _host.Log.Warn($"No se han podido leer los fotogramas de «{name}»", ex);
        }
        return _frames[key] = [.. frames];
    }

    private BitmapSource Load(string path)
    {
        var file = Path.Combine(_host.PackageDirectory, path.Replace('/', Path.DirectorySeparatorChar));
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(file);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void PlaySound(string name)
    {
        if (character.For(name).Sound is not { } path || !_host.Audio.Enabled) return;
        if (!_sounds.TryGetValue(path, out var sound))
        {
            try { sound = _host.Audio.LoadWav(path); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                _host.Log.Warn($"No se ha podido leer el sonido {path}", ex);
                sound = null;
            }
            _sounds[path] = sound;
        }
        if (sound is null) return;
        var width = _host.World.Current.PrimaryMonitor?.Bounds.Width ?? 1920;
        _host.Audio.Play(sound, pan: Math.Clamp(_at.X / width * 2 - 1, -1, 1));
    }

    // ── what it is asked ─────────────────────────────────────────────────

    public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React;

    public async Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct)
    {
        switch (gesture)
        {
            case Gesture.Communicate:
                _talking = true;
                _host.RequestTick();
                try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
                _talking = false;
                _host.RequestTick();
                return GestureOutcome.Committed;
            case Gesture.React react:
                var name = react.Reaction switch
                {
                    Reaction.Success or Reaction.Celebration => "happy",
                    Reaction.Failure => "sad",
                    _ => "surprised",
                };
                PlayOnce(name);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, (_once?.Until ?? Now) - Now)), ct); } catch (OperationCanceledException) { }
                return GestureOutcome.Committed;
            default:
                return GestureOutcome.NotPerformed;
        }
    }

    public void SetState(AssistantState state)
    {
        _state = state;
        if (state != AssistantState.Idle) _walkTo = null;
        _host.RequestTick();
    }

    public void OnVoiceLevel(double level) { }

    public void OnPointer(PointerEvent e)
    {
        switch (e.Kind)
        {
            case PointerEventKind.Click:
                PlayOnce("click");
                break;
            case PointerEventKind.DoubleClick:
                PlayOnce("happy");
                break;
            case PointerEventKind.DragStart:
                _dragging = true;
                _falling = false;
                _walkTo = null;
                _ground = null;
                _grab = _at - e.Position;
                _host.RequestTick();
                break;
            case PointerEventKind.DragMove:
                var before = _at;
                _at = e.Position + _grab;
                if (Math.Abs(_at.X - before.X) > 0.5) _right = _at.X > before.X;
                _host.RequestTick();
                break;
            case PointerEventKind.DragEnd:
                _dragging = false;
                _velocity = default;
                _ground = GroundAt(_at.X, _at.Y);
                if (_ground is null) StartFalling();
                _host.RequestTick();
                break;
        }
    }

    public void OnSystemEvent(SystemEvent e)
    {
        switch (e)
        {
            case SystemEvent.RecycleBinEmptied:
                PlayOnce("happy");
                break;
            case SystemEvent.NotificationShown:
                PlayOnce("surprised");
                break;
            case SystemEvent.MonitorsChanged:
                if (_host.World.Current.MonitorAt(_at) is null && RandomSpot() is { } spot) MoveTo(spot);
                break;
        }
        _host.RequestTick();
    }

    public Task OnExtraAsync(string extraId, CancellationToken ct) => Task.CompletedTask;

    public void OnSettingsChanged(JsonElement settings)
    {
        ReadSettings(settings);
        _host.RequestTick();
    }

    /// <summary>The settings any of them may have in its settings.json: <c>scale</c> (0.5–2) and <c>wander</c>.</summary>
    private void ReadSettings(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) return;
        if (settings.TryGetProperty("scale", out var scale) && scale.TryGetDouble(out var s)) _scale = Math.Clamp(s, 0.5, 2);
        if (settings.TryGetProperty("wander", out var wander) && wander.ValueKind is JsonValueKind.True or JsonValueKind.False) _wander = wander.GetBoolean();
    }

    public void Dispose()
    {
        if (_host is null) return; // never attached
        _host.World.Changed -= OnWorldChanged;
        _hit.Dispose();
        _surface.Dispose();
    }
}
