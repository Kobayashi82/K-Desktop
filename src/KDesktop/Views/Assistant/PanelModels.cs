using System.Text.Json.Nodes;
using System.Windows.Media;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Assistant;

/// <summary>A card of an assistant's panel; it redraws itself from what the assistant published.</summary>
public abstract class PanelCardModel(AssistantPanelModel owner, PanelCard card) : Observable
{
    public AssistantPanelModel Owner { get; } = owner;

    public PanelCard Card { get; } = card;

    public string Label { get; } = AssistantLook.Text(card.Label);

    public bool HasLabel => Label.Length > 0;

    /// <summary>Its name for screen readers (the item of the list that holds it).</summary>
    public override string ToString() => Label;

    public abstract void Refresh(JsonObject? data);

    protected JsonNode? Bound(JsonObject? data) => AssistantUi.Resolve(data, Card.Bind);
}

public sealed class StatCard(AssistantPanelModel owner, PanelCard card) : PanelCardModel(owner, card)
{
    public string Value { get; private set; } = "—";

    public SymbolRegular? Symbol { get; } = AssistantLook.Symbol(card.Icon);

    public bool HasSymbol => Symbol is not null;

    public override void Refresh(JsonObject? data)
    {
        Value = AssistantUi.Display(Bound(data), Card.Format, Loc.Instance.Culture, Loc.Instance.Language);
        Notify(nameof(Value));
    }
}

/// <summary>Stats in a row, side by side (consecutive «stat» cards).</summary>
public sealed class StatsRow(IReadOnlyList<StatCard> stats)
{
    public IReadOnlyList<StatCard> Stats { get; } = stats;

    public override string ToString() => string.Join(", ", Stats.Select(s => s.Label));
}

public sealed class ProgressCard(AssistantPanelModel owner, PanelCard card) : PanelCardModel(owner, card)
{
    public double Fraction { get; private set; }

    public string Text { get; private set; } = "—";

    public override void Refresh(JsonObject? data)
    {
        var value = AssistantUi.Number(Bound(data));
        var max = Card.Max switch
        {
            JsonValue v when v.TryGetValue<string>(out var key) => AssistantUi.Number(AssistantUi.Resolve(data, key)),
            var node => AssistantUi.Number(node),
        };
        Fraction = value is { } v1 && max is > 0 and var m ? Math.Clamp(v1 / m, 0, 1) : 0;
        Text = value is { } v2 ? AssistantUi.FormatNumber(Card.Format, v2, Loc.Instance.Culture, max) : "—";
        NotifyAll();
    }
}

public sealed class ListCard(AssistantPanelModel owner, PanelCard card) : PanelCardModel(owner, card)
{
    public IReadOnlyList<string> Lines { get; private set; } = [];

    public bool IsEmpty => Lines.Count == 0;

    public string EmptyText { get; } = AssistantLook.Text(card.Empty) is { Length: > 0 } empty ? empty : "—";

    public override void Refresh(JsonObject? data)
    {
        Lines = AssistantUi.Lines(Bound(data), Loc.Instance.Language);
        NotifyAll();
    }
}

public sealed record AchievementModel(string Name, string? Description, bool Unlocked, double? Progress, ImageSource? Image)
{
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool ShowProgress => !Unlocked && Progress is not null;
    public double Fraction => Progress ?? 0;
    public bool NoImage => Image is null;

    public override string ToString() => Name;
}

public sealed class AchievementsCard(AssistantPanelModel owner, PanelCard card) : PanelCardModel(owner, card)
{
    public IReadOnlyList<AchievementModel> Items { get; private set; } = [];

    /// <summary>«3 / 12».</summary>
    public string Summary { get; private set; } = string.Empty;

    public bool IsEmpty => Items.Count == 0;

    public static string EmptyText => Loc.T("assistantPanel.noData");

    public override void Refresh(JsonObject? data)
    {
        var list = PanelAchievement.List(Bound(data), Loc.Instance.Language);
        Items = [.. list
            .OrderByDescending(a => a.Unlocked)
            .Select(a => new AchievementModel(a.Name, a.Description, a.Unlocked, a.Progress,
                a.Icon is { } icon ? AssistantLook.Image(Owner.Assistant, icon, 96) : null))];
        Summary = Items.Count == 0 ? string.Empty : $"{Items.Count(a => a.Unlocked)} / {Items.Count}";
        NotifyAll();
    }
}

public sealed record PanelButtonModel(string Label, string Extra, SymbolRegular Symbol)
{
    public override string ToString() => Label;
}

public sealed class ButtonsCard(AssistantPanelModel owner, PanelCard card) : PanelCardModel(owner, card)
{
    public IReadOnlyList<PanelButtonModel> Buttons { get; } =
        [.. card.Items.Select(b => new PanelButtonModel(AssistantLook.Text(b.Label), b.Extra, AssistantLook.Symbol(b.Icon) ?? SymbolRegular.Play24))];

    public override void Refresh(JsonObject? data) { }
}

public sealed class TextCard(AssistantPanelModel owner, PanelCard card) : PanelCardModel(owner, card)
{
    public string Text { get; private set; } = AssistantLook.Text(card.Text);

    public override void Refresh(JsonObject? data)
    {
        if (Card.Bind is null) return;
        Text = AssistantUi.Display(Bound(data), Card.Format, Loc.Instance.Culture, Loc.Instance.Language);
        Notify(nameof(Text));
    }
}

/// <summary>
/// The assistant's own tab of Panel → Assistant (§13.2), drawn from its panel.json: stats side by side, then the
/// other cards in order. The data is what it published since it was put on the desktop.
/// </summary>
public sealed class AssistantPanelModel
{
    private readonly List<PanelCardModel> _cards = [];

    public AssistantPanelModel(InstalledAssistant assistant, PanelLayout layout)
    {
        Assistant = assistant;
        var blocks = new List<object>();
        List<StatCard>? row = null;
        foreach (var card in layout.Cards)
        {
            PanelCardModel? model = card.Type switch
            {
                "stat" => new StatCard(this, card),
                "progress" => new ProgressCard(this, card),
                "list" => new ListCard(this, card),
                "achievements" => new AchievementsCard(this, card),
                "buttons" => new ButtonsCard(this, card),
                "text" => new TextCard(this, card),
                _ => null, // a card of a newer K-Desktop
            };
            if (model is null) continue;
            _cards.Add(model);
            if (model is StatCard stat)
            {
                if (row is null) blocks.Add(new StatsRow(row = []));
                row.Add(stat);
            }
            else
            {
                row = null;
                blocks.Add(model);
            }
        }
        Blocks = blocks;
    }

    public InstalledAssistant Assistant { get; }

    /// <summary>What is drawn, in order: <see cref="StatsRow"/>s and single cards.</summary>
    public IReadOnlyList<object> Blocks { get; }


    public void Refresh(JsonObject? data)
    {
        foreach (var card in _cards) card.Refresh(data);
    }
}
