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
/// the package runs: only its pictures and sounds are read. An animation with variants (stage 13.11) gets one picked each
/// time it starts (and, one that repeats, on each round) with a <see cref="Variety{T}"/>; an entrance or an exit, one for
/// its reason.
/// </summary>
internal sealed class CharacterAssistant(CharacterFile character) : IAssistant
{
    private const double Gravity = 2600;
    private const double SleepAfter = 180;
    private const double ReactLongest = 2.5;
    private const string SpotFile = "spot.json";

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
    /// <summary>When it started fading out (closing K-Desktop, stage 15.3); NaN while it does not.</summary>
    private double _fadingSince = double.NaN;
    private const double ShutdownFade = 0.3;
    /// <summary>Its entrance, played on the first tick (until then its clock does not run: started at once, it would be over).</summary>
    private CharacterAnimation? _entrance;
    /// <summary>Where its body was last drawn (what the others avoid, stage 13.14); null off screen.</summary>
    private PixelRect? _body;

    /// <summary>The picture of the files dragged over it, since when, and when they were dropped (it shrinks into it).</summary>
    private (ImageSource Image, double Since, double? Caught)? _carried;
    private const double CatchSeconds = 0.35;

    /// <summary>One animation that plays once over the rest (a reaction, a click, coming in, leaving), its variant and until when.</summary>
    private (string Name, CharacterAnimation Animation, double Start, double Until)? _once;
    private string _playing = "idle";
    private CharacterAnimation _animation = character.For("idle");
    private double _playingSince;

    /// <summary>How each list of variants is picked from (by the list: an animation that falls back shares the other's).</summary>
    private readonly Dictionary<IReadOnlyList<CharacterAnimation>, Variety<CharacterAnimation>> _varieties = new(ReferenceEqualityComparer.Instance);
    /// <summary>Its size as the user wants it (the common settings, stage 13.13).</summary>
    private double Scale => _host.Common.Size;

    /// <summary>Walks about on its own (how often: <see cref="Later"/>).</summary>
    private bool Wanders => _host.Common.Liveliness > 0;
    private double Now { get; set; }

    /// <summary>Where it stands: a stretch of the taskbar or of a window's top (and that window).</summary>
    private sealed record Ground(double Y, double Left, double Right, long Window);

    public Point? BubbleAnchor => new Point(_at.X, _at.Y - HeightPx);

    public PixelRect? BodyBounds => _gone ? null : _body;

    private double Dpi => _host.World.Current.MonitorAt(new Point(_at.X, _at.Y - 1))?.DpiScale ?? _host.World.Current.PrimaryMonitor?.DpiScale ?? 1;

    private double HeightPx => character.Height * Dpi * Scale;

    public void Attach(IAssistantHost host)
    {
        _host = host;
        _surface = host.Rendering.CreateSurface();
        _hit = host.Pointer.CreateHitArea();
        host.World.Changed += OnWorldChanged;
    }

    public void Enter(EntranceKind how)
    {
        // Back from a reload (a new version, another language…): where it was, without its entrance.
        var back = how == EntranceKind.Reloaded ? SavedSpot() : null;
        var spot = back ?? RandomSpot() ?? new Point(400, 400);
        _at = spot;
        _ground = GroundAt(_at.X, _at.Y);
        _nextWander = Later(3, 5);
        if (how != EntranceKind.Reloaded) _entrance = Pick("enter", how == EntranceKind.Startup ? "startup" : "summoned");
        // Back where it was, someone else may stay there now.
        Settle();
        _host.RequestTick();
    }

    public async Task ExitAsync(ExitKind why, CancellationToken ct)
    {
        if (why == ExitKind.Reloading) SaveSpot();
        var exit = why is ExitKind.Dismissed or ExitKind.Uninstalling ? Pick("exit", why == ExitKind.Dismissed ? "dismissed" : "uninstalling") : null;
        if (exit is null)
        {
            // Closing K-Desktop (or switching the module off): no goodbye, but not a blink either: it fades out quickly.
            if (why == ExitKind.Shutdown && _body is not null)
            {
                _fadingSince = Now;
                _host.RequestTick();
                try { await Task.Delay(TimeSpan.FromSeconds(ShutdownFade), ct); }
                catch (OperationCanceledException) { }
            }
            _gone = true;
            _surface.Hide();
            return;
        }
        PlayOnce("exit", exit);
        _host.RequestTick();
        try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(ReactLongest, exit.Seconds)), ct); }
        catch (OperationCanceledException) { }
        _gone = true;
        _surface.Hide();
    }

    public double Tick(TickInfo tick)
    {
        Now = tick.Now;
        var dt = Math.Min(tick.Elapsed, 0.05);
        if (_entrance is { } entrance)
        {
            _entrance = null;
            PlayOnce("enter", entrance);
        }
        if (_gone) return 1;
        if (_host.World.System.FullscreenMonitors.Contains(MonitorIndex()))
        {
            // Out of the way of whatever is full screen: to another monitor, or hidden until it ends.
            if (RandomSpot() is { } elsewhere && !_host.World.System.FullscreenMonitors.Contains(MonitorIndex(elsewhere))) MoveTo(elsewhere);
            else
            {
                _surface.Hide();
                _hit.Hide();
                _body = null;
                return 1;
            }
        }

        var moving = Step(dt);
        if (!moving && Wanders && !_dragging && _once is null && _state == AssistantState.Idle && !_talking && Now >= _nextWander && !Asleep) Wander();

        var name = Animation();
        if (name != _playing)
        {
            _playing = name;
            Start(_once is { } over && over.Name == name ? over.Animation : Pick(name));
        }
        else if (_once is null && _animation.Loop && Now - _playingSince >= _animation.Seconds && character.VariantsFor(name).Count > 1)
            // One that repeats, with variants: another one (or the same) on each round.
            Start(Pick(name));
        Draw();

        var animation = _animation;
        var frames = animation.FrameCount;
        if (moving || _dragging || _carried is not null || !double.IsNaN(_fadingSince)) return _host.Rendering.FrameInterval;
        // The next frame of what plays (a still picture: only the next wander, or a look at whether the user went away).
        var nextFrame = frames > 1 ? 1 / animation.Fps : double.PositiveInfinity;
        var nextOnce = _once is { } once ? Math.Max(0.01, once.Until - Now) : double.PositiveInfinity;
        var nextWander = Wanders && character.WalkSpeed > 0 ? Math.Max(0.05, _nextWander - Now) : 5;
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
                _nextWander = Later(2, 4);
                Settle();
            }
            return true;
        }
        if (_walkTo is { } target && character.WalkSpeed > 0)
        {
            var step = character.WalkSpeed * Dpi * Scale * dt;
            var left = target - _at.X;
            _right = left > 0;
            if (Math.Abs(left) <= step)
            {
                _at.X = target;
                _walkTo = null;
                _nextWander = Later(4, 10);
                Settle();
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
        _nextWander = Later(5, 10);
        if (character.WalkSpeed <= 0 || _ground is not { } ground) return;
        // Standing where another one stays (both arrived at once): out of the way first.
        if (Taken(_at.X, _at.Y))
        {
            Settle();
            return;
        }
        if (_random.NextDouble() < 0.35) return;
        var width = ground.Right - ground.Left;
        var margin = Math.Min(width / 2, HeightPx * 0.4);
        // On a window, now and then it walks off the end.
        if (ground.Window != 0 && _random.NextDouble() < 0.2)
            _walkTo = _random.NextDouble() < 0.5 ? ground.Left - HeightPx : ground.Right + HeightPx;
        else
        {
            var target = ground.Left + margin + _random.NextDouble() * Math.Max(1, width - 2 * margin);
            _walkTo = Taken(target, ground.Y) ? FreeOn(ground, target) : target;
        }
        _host.RequestTick();
    }

    // ── room for everybody (stage 13.14) ─────────────────────────────────

    /// <summary>How wide its body is (the last frame drawn, or about its height before the first one).</summary>
    private double WidthPx => _body is { } body ? body.Width : HeightPx * 0.8;

    /// <summary>Where another assistant stays over where its body would be, standing at <paramref name="x"/> on <paramref name="y"/>.</summary>
    private bool Taken(double x, double y)
    {
        var half = WidthPx / 2;
        var body = new PixelRect((int)(x - half), (int)(y - HeightPx), (int)(x + half), (int)y);
        return _host.World.TakenBy(body.Inflate((int)Math.Ceiling(4 * Dpi))) is not null;
    }

    /// <summary>The nearest place to <paramref name="x"/> on that ground where nobody else stays, or null.</summary>
    private double? FreeOn(Ground ground, double x)
    {
        var margin = Math.Min((ground.Right - ground.Left) / 2, WidthPx / 2);
        var left = ground.Left + margin;
        var right = ground.Right - margin;
        var step = Math.Max(4, WidthPx / 4);
        for (var d = 0.0; d <= right - left; d += step)
        {
            if (x + d <= right && x + d >= left && !Taken(x + d, ground.Y)) return x + d;
            if (x - d >= left && x - d <= right && !Taken(x - d, ground.Y)) return x - d;
        }
        return null;
    }

    /// <summary>It stopped where another assistant stays: it walks aside to the nearest free place (passing by is fine).</summary>
    private void Settle()
    {
        if (_ground is not { } ground || character.WalkSpeed <= 0 || !Taken(_at.X, _at.Y)) return;
        if (FreeOn(ground, _at.X) is not { } free) return;
        _host.Log.Info($"se aparta: hay otro asistente donde se ha parado ({_at.X:0} → {free:0})");
        _walkTo = free;
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

    /// <summary>Where it stood when it left to come back at once (its data folder), if that is still ground.</summary>
    private Point? SavedSpot()
    {
        try
        {
            var file = Path.Combine(_host.DataDirectory, SpotFile);
            if (!File.Exists(file)) return null;
            var saved = JsonSerializer.Deserialize<double[]>(File.ReadAllText(file));
            return saved is [var x, var y] && GroundAt(x, y) is not null ? new Point(x, y) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void SaveSpot()
    {
        try
        {
            Directory.CreateDirectory(_host.DataDirectory);
            File.WriteAllText(Path.Combine(_host.DataDirectory, SpotFile), JsonSerializer.Serialize(new[] { _at.X, _at.Y }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* it comes back somewhere else */ }
    }

    /// <summary>A place on the taskbar of a monitor that is not full screen, where no other assistant stays if it can be.</summary>
    private Point? RandomSpot()
    {
        var world = _host.World.Current;
        var monitors = world.Monitors.Where(m => !_host.World.System.FullscreenMonitors.Contains(m.Index)).ToList();
        if (monitors.Count == 0) return null;
        Point? spot = null;
        // A few tries for a place where no other assistant stays.
        for (var i = 0; i < 8; i++)
        {
            var monitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            if (_random.NextDouble() < 0.3) monitor = monitors[_random.Next(monitors.Count)];
            var work = monitor.WorkArea;
            spot = new Point(work.Left + 80 + _random.NextDouble() * Math.Max(1, work.Width - 160), work.Bottom);
            if (!Taken(spot.Value.X, spot.Value.Y)) break;
        }
        return spot;
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
        if (AssistantStates.IsWorking(_state) || _state == AssistantState.NoBrain) return "think";
        if (_walkTo is not null) return "walk";
        if (Asleep) return "sleep";
        return "idle";
    }

    /// <summary>Plays <paramref name="name"/> once over the rest (<paramref name="animation"/>: that variant; null: one is picked).</summary>
    private void PlayOnce(string name, CharacterAnimation? animation = null)
    {
        animation ??= Pick(name);
        var length = animation.Loop ? Math.Min(ReactLongest, Math.Max(1, animation.Seconds)) : animation.Seconds;
        _once = (name, animation, Now, Now + Math.Clamp(length, 0.2, ReactLongest));
        _playing = string.Empty;
        _host.RequestTick();
    }

    private void Start(CharacterAnimation animation)
    {
        _animation = animation;
        _playingSince = Now;
        PlaySound(animation);
    }

    /// <summary>
    /// A variant of <paramref name="name"/> (its own or the one it falls back to): by weight and cooldown, never the same
    /// one twice in a row for those that play once (one that repeats may go on with the same).
    /// </summary>
    private CharacterAnimation Pick(string name) => VarietyOf(character.VariantsFor(name)).Pick();

    /// <summary>A variant of an entrance or an exit for that reason; null when it has none for it (then there is none).</summary>
    private CharacterAnimation? Pick(string name, string reason) =>
        character.VariantsFor(name, reason).Count == 0 ? null : VarietyOf(character.VariantsFor(name)).Pick(v => v.IsFor(reason));

    private Variety<CharacterAnimation> VarietyOf(IReadOnlyList<CharacterAnimation> variants)
    {
        if (_varieties.TryGetValue(variants, out var variety)) return variety;
        variety = new Variety<CharacterAnimation>(clock: () => Now) { NoRepeat = !variants[0].Loop };
        foreach (var variant in variants) variety.Add(variant, variant.Weight, variant.Cooldown);
        return _varieties[variants] = variety;
    }

    private void Draw()
    {
        var animation = _animation;
        var frames = FramesOf(_playing, animation);
        if (frames.Length == 0) return;
        var index = (int)Math.Floor((Now - _playingSince) * animation.Fps);
        index = animation.Loop || _once is null ? index % frames.Length : Math.Min(index, frames.Length - 1);
        var frame = frames[Math.Max(0, index)];
        var h = HeightPx;
        var w = frame.Height > 0 ? h * frame.Width / frame.Height : h;
        // The rows under its feet (character.json «ground») go below the ground.
        var below = character.Ground > 0 && frame is BitmapSource bitmap && bitmap.PixelHeight > 0 ? h * character.Ground / bitmap.PixelHeight : 0;
        var rect = new Rect(_at.X - w / 2, _at.Y - h + below, w, h);
        var mirror = _right == character.FacesLeft;
        var carried = Carried(rect);
        var drawn = carried is { } c ? Rect.Union(rect, c.Rect) : rect;
        var area = new PixelRect((int)Math.Floor(drawn.Left) - 1, (int)Math.Floor(drawn.Top) - 1, (int)Math.Ceiling(drawn.Right) + 1, (int)Math.Ceiling(drawn.Bottom) + 1);
        var opacity = double.IsNaN(_fadingSince) ? 1 : Math.Clamp(1 - (Now - _fadingSince) / ShutdownFade, 0, 1);
        _surface.Present(area, dc =>
        {
            if (opacity < 1) dc.PushOpacity(opacity);
            if (mirror) dc.PushTransform(new ScaleTransform(-1, 1, _at.X, 0));
            dc.DrawImage(frame, rect);
            if (mirror) dc.Pop();
            if (carried is { } held)
            {
                dc.PushOpacity(held.Opacity);
                dc.DrawImage(held.Image, held.Rect);
                dc.Pop();
            }
            if (opacity < 1) dc.Pop();
        });
        _body = new PixelRect((int)rect.Left, (int)rect.Top, (int)rect.Right, (int)rect.Bottom);
        _hit.Place(new HitShape.Rectangle(_body.Value));
    }

    /// <summary>
    /// Where the picture of the files goes: over its head, as wide as it is tall at most and bobbing; once dropped it
    /// shrinks into its middle. Null when there is none (or it has gone in).
    /// </summary>
    private (ImageSource Image, Rect Rect, double Opacity)? Carried(Rect body)
    {
        if (_carried is not { } carried) return null;
        var shrink = 1.0;
        if (carried.Caught is { } caught)
        {
            var t = (Now - caught) / CatchSeconds;
            if (t >= 1)
            {
                _carried = null;
                return null;
            }
            shrink = 1 - t * t;
        }
        var image = carried.Image;
        var side = body.Height * 0.8;
        var fit = Math.Min(side / Math.Max(1, image.Width), side / Math.Max(1, image.Height));
        var w = image.Width * fit * shrink;
        var h = image.Height * fit * shrink;
        var bob = carried.Caught is null ? Math.Sin((Now - carried.Since) * 4) * 3 * Dpi : 0;
        // Over the head, and into the middle of the body as it shrinks.
        var aboveY = body.Top - side / 2 - 4 * Dpi + bob;
        var y = aboveY + (body.Top + body.Height / 2 - aboveY) * (1 - shrink);
        return (image, new Rect(body.Left + body.Width / 2 - w / 2, y - h / 2, w, h), 0.5 + 0.5 * shrink);
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

    private void PlaySound(CharacterAnimation animation)
    {
        if (animation.Sound is not { } path || !_host.Audio.Enabled) return;
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

    public bool CanPerform(Gesture gesture) => gesture.Kind is GestureKind.Communicate or GestureKind.React ||
        gesture.Kind == GestureKind.Announce && !_gone && !_dragging;

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
            case Gesture.Announce announce:
                await AnnounceAsync(announce, ctx, ct);
                return GestureOutcome.Committed;
            default:
                return GestureOutcome.NotPerformed;
        }
    }

    /// <summary>
    /// Before saying something on its own (stage 13.6): it walks along where it stands towards the cursor, as far as its time
    /// lets it and stopping beside it, turns to it and plays «announce» (or «happy»). Asleep, it wakes up for it.
    /// </summary>
    private async Task AnnounceAsync(Gesture.Announce announce, IGestureContext ctx, CancellationToken ct)
    {
        var animation = Pick("announce");
        var length = Math.Clamp(animation.Seconds, 0.2, ReactLongest);
        var world = _host.World.Current;
        try
        {
            if (announce.Cursor is { } cursor && _ground is { } ground && !_falling && character.WalkSpeed > 0 &&
                world.MonitorAt(cursor) is { } monitor && monitor == world.MonitorAt(new Point(_at.X, _at.Y - 1)))
            {
                var reach = character.WalkSpeed * Dpi * Scale * Math.Max(0, ctx.TimeLeft.TotalSeconds - length - 0.2);
                var target = cursor.X > _at.X ? cursor.X - WidthPx : cursor.X + WidthPx;
                target = Math.Clamp(target, _at.X - reach, _at.X + reach);
                var half = Math.Min((ground.Right - ground.Left) / 2, WidthPx / 2);
                target = Math.Clamp(target, ground.Left + half, ground.Right - half);
                if (Taken(target, ground.Y)) target = FreeOn(ground, target) ?? _at.X;
                if (Math.Abs(target - _at.X) > 4 * Dpi)
                {
                    _walkTo = target;
                    _host.RequestTick();
                    while (_walkTo is not null && !_falling) await Task.Delay(30, ct);
                }
                _right = cursor.X > _at.X;
            }
            PlayOnce("announce", animation);
            await Task.Delay(TimeSpan.FromSeconds(length), ct);
        }
        catch (OperationCanceledException)
        {
            // Its time is up: it speaks from where it got to.
        }
    }

    // ── the showcase (stage 13.9) ────────────────────────────────────────

    /// <summary>
    /// Its animations that nothing of the host's shows (talking, listening, reacting… come with the host's items): only the
    /// ones its character.json has. Without names: K-Desktop names them.
    /// </summary>
    private static readonly (string Name, ShowcaseGroup Group)[] OwnAnimations =
        [("walk", ShowcaseGroup.Moving), ("sleep", ShowcaseGroup.Idle), ("surprised", ShowcaseGroup.Feelings), ("click", ShowcaseGroup.Playing), ("catch", ShowcaseGroup.Playing)];

    private static readonly IReadOnlyDictionary<string, string> NoNames = new Dictionary<string, string>();

    public IReadOnlyList<ShowcaseItem> Showcase =>
        [.. OwnAnimations.Where(a => character.Animations.ContainsKey(a.Name)).Select(a => new ShowcaseItem(a.Name, NoNames, a.Group))];

    public async Task<bool> PlayShowcaseAsync(string id, CancellationToken ct)
    {
        if (_gone || _dragging || _falling || !Showcase.Any(i => i.Id == id)) return false;
        if (id == "walk")
        {
            // A few steps along where it stands, to the side with more room.
            if (_ground is not { } ground || character.WalkSpeed <= 0) return false;
            var half = Math.Min((ground.Right - ground.Left) / 2, WidthPx / 2);
            var way = ground.Right - _at.X > _at.X - ground.Left ? 1 : -1;
            var target = Math.Clamp(_at.X + way * 3 * WidthPx, ground.Left + half, ground.Right - half);
            if (Taken(target, ground.Y)) target = FreeOn(ground, target) ?? _at.X;
            _walkTo = target;
            _host.RequestTick();
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(TimeSpan.FromSeconds(6));
                while (_walkTo is not null && !_falling) await Task.Delay(30, limit.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _walkTo = null;
            }
            return true;
        }
        PlayOnce(id);
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, (_once?.Until ?? Now) - Now)), ct);
        return true;
    }

    public void SetState(AssistantState state, StateDetail detail) => SetState(state);

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
            case PointerEventKind.FilesOver:
                // Their picture over its head while they are over it (Windows' own is hidden there, SDK 1.17).
                _carried = e.Preview is { } preview ? (preview, Now, null) : null;
                _host.RequestTick();
                break;
            case PointerEventKind.FilesAway:
                _carried = null;
                _host.RequestTick();
                break;
            case PointerEventKind.FilesDropped:
                // Files dropped on it (stage 13.8): it catches them (the picture shrinks into it); K-Desktop asks what to do
                // with them.
                if ((_carried?.Image ?? e.Preview) is { } caught) _carried = (caught, _carried?.Since ?? Now, Now);
                PlayOnce("catch");
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

    /// <summary>Its size, liveliness and dragging are the common settings (<see cref="IAssistantHost.Common"/>): read as it goes.</summary>
    public void OnSettingsChanged(JsonElement settings) => _host.RequestTick();

    /// <summary>When it may walk about again: sooner the livelier the user wants it (1 = as usual).</summary>
    private double Later(double least, double spread) => Now + (least + _random.NextDouble() * spread) / Math.Max(0.1, _host.Common.Liveliness);

    public void Dispose()
    {
        if (_host is null) return; // never attached
        _host.World.Changed -= OnWorldChanged;
        _hit.Dispose();
        _surface.Dispose();
    }
}
