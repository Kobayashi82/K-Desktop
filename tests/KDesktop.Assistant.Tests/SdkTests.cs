using System.Text.Json;
using System.Windows;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 0.2: an assistant can be written against the SDK alone.</summary>
public class SdkTests
{
    /// <summary>The smallest possible assistant: no body, no gestures (the module falls back to Communicate).</summary>
    private sealed class EmptyAssistant : IAssistant
    {
        public IAssistantHost? Host { get; private set; }
        public Point? BubbleAnchor => null;
        public void Attach(IAssistantHost host) => Host = host;
        public void Enter(EntranceKind how) { }
        public Task ExitAsync(ExitKind why, CancellationToken ct) => Task.CompletedTask;
        public double Tick(TickInfo tick) => 1;
        public bool CanPerform(Gesture gesture) => false;
        public Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct) => Task.FromResult(GestureOutcome.NotPerformed);
        public void SetState(AssistantState state) { }
        public void OnVoiceLevel(double level) { }
        public void OnPointer(PointerEvent e) { }
        public void OnSystemEvent(SystemEvent e) { }
        public Task OnExtraAsync(string extraId, CancellationToken ct) => Task.CompletedTask;
        public void OnSettingsChanged(JsonElement settings) { }
        public void Dispose() { }
    }

    [Fact]
    public async Task EmptyAssistantDeclinesGestures()
    {
        IAssistant assistant = new EmptyAssistant();
        var gesture = new Gesture.Communicate("Hola", null);
        Assert.False(assistant.CanPerform(gesture));
        Assert.Equal(GestureOutcome.NotPerformed, await assistant.PerformAsync(gesture, null!, CancellationToken.None));
        Assert.Equal(1, assistant.Tick(new TickInfo(0, 0)));
    }

    [Fact]
    public void CommunicateAndReactAreAlwaysKnown()
    {
        var traits = new AssistantTraits(TravelMode.Teleport, 1000, false, false, new HashSet<GestureKind>());
        Assert.True(traits.Knows(GestureKind.Communicate));
        Assert.True(traits.Knows(GestureKind.React));
        Assert.False(traits.Knows(GestureKind.MoveWindow));
    }

    [Fact]
    public void EveryGestureRecordHasItsKind()
    {
        var records = typeof(Gesture).GetNestedTypes().Where(t => t.IsSubclassOf(typeof(Gesture))).ToList();
        Assert.Equal(Enum.GetValues<GestureKind>().Length, records.Count);
        Assert.All(records, r => Assert.True(Enum.TryParse<GestureKind>(r.Name, out _), r.Name));
    }

    /// <summary>The manifest lists gestures in camelCase: they must map 1:1 to <see cref="GestureKind"/>.</summary>
    [Fact]
    public void ManifestGestureNamesMatchTheSdk()
    {
        var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Contracts.DocsDir, "schemas", "assistant.schema.json")));
        var names = schema.RootElement.GetProperty("properties").GetProperty("traits").GetProperty("properties")
            .GetProperty("gestures").GetProperty("items").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToList();
        var kinds = Enum.GetNames<GestureKind>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]).ToList();
        Assert.Equal(kinds.Order(), names.Order());
    }

    [Fact]
    public void PolygonHitShapeHasItsBounds()
    {
        var shape = new HitShape.Polygon([new Point(10.2, 20), new Point(30, 5.5), new Point(18, 40.1)]);
        Assert.Equal(new PixelRect(10, 5, 30, 41), shape.Bounds);
    }
}
