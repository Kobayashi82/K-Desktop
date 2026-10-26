using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Voice → Perfiles de voz (stage 9.4): K-Desktop's profiles and the user's, and (5.0.279: in the same
/// page, instead of the list, with a way back to it) the editor of one —
/// its name, engine, speed and volume, and its chains of effects (always, and in each situation: happy, after a crash,
/// from another monitor), each effect with its switch and sliders, in an order changed by dragging. K-Desktop's are only
/// looked at (duplicated to make one's own). What is heard is the profile as it is on screen: «Probar», and again a moment
/// after each change. The user's are kept as they change. Made in code (like Options → Tools).
/// </summary>
public partial class VoiceProfilesSection : UserControl
{
    private readonly App _app = App.Current;
    private readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _test = new() { Interval = TimeSpan.FromMilliseconds(450) };

    /// <summary>The profile chosen (its id) and the copy being edited.</summary>
    private string _selected = VoiceProfile.DefaultId;
    private VoiceProfile _draft = VoiceProfile.Default;

    /// <summary>The editor shows (else the list).</summary>
    private bool _editing;

    /// <summary>The chain shown: <c>chain</c> (always) or a mood (<c>happy</c>, <c>error</c>, <c>far</c>).</summary>
    private string _chain = "chain";

    /// <summary>The situations an assistant sets (<c>SetVoiceMood</c>): its crash and its celebration. «far» (another monitor)
    /// is gone with the automatic distance (the user asked); a profile that has it keeps it, unused.</summary>
    private static readonly string[] Chains = ["chain", "happy", "error"];

    private bool _autoTest = true;
    private string? _testText;
    private StackPanel? _effects;
    private TextBlock? _testNote;
    private ScrollViewer? _scroll;

    public VoiceProfilesSection()
    {
        InitializeComponent();
        _save.Tick += (_, _) =>
        {
            _save.Stop();
            Keep();
        };
        _test.Tick += (_, _) =>
        {
            _test.Stop();
            Test();
        };
        Loaded += (_, _) => FitWidth();
        IsVisibleChanged += (_, _) =>
        {
            // Coming to the page: the list.
            if (IsVisible) Select(VoiceProfiles.Find(_selected) is not null ? _selected : VoiceProfile.DefaultId, edit: false);
            else if (_save.IsEnabled)
            {
                _save.Stop();
                Keep();
            }
        };
    }

    /// <summary>As wide as the page lets it (its sliders need the room; the sections are as wide as their content).</summary>
    private void FitWidth()
    {
        if (_scroll is not null) return;
        DependencyObject? at = this;
        while (at is not null and not ScrollViewer) at = VisualTreeHelper.GetParent(at);
        if (at is not ScrollViewer scroll || Parent is not FrameworkElement holder) return;
        _scroll = scroll;
        void Fit() => Width = Math.Max(0, Math.Min(double.IsInfinity(holder.MaxWidth) ? double.MaxValue : holder.MaxWidth, scroll.ViewportWidth));
        scroll.SizeChanged += (_, _) => Fit();
        scroll.ScrollChanged += (_, e) => { if (e.ViewportWidthChange != 0) Fit(); };
        Fit();
    }

    private bool IsBuiltIn => VoiceProfiles.IsBuiltIn(_draft.Id);

    /// <summary>
    /// Shows that profile in the editor (a copy of it: changing it changes nothing until it is kept); with
    /// <paramref name="edit"/> false, the list.
    /// </summary>
    private void Select(string id, bool edit = true)
    {
        _editing = edit;
        if (_save.IsEnabled)
        {
            _save.Stop();
            Keep();
        }
        var profile = VoiceProfiles.Find(id) ?? VoiceProfile.Default;
        _selected = profile.Id;
        _draft = VoiceProfile.Parse(profile.ToJson(), profile.Id);
        Build();
        // Another screen (a profile, or the list): from its top. Build alone (a change in the editor) stays where it was.
        _scroll?.ScrollToTop();
    }

    private void Build()
    {
        Body.Children.Clear();
        if (!_editing)
        {
            Body.Children.Add(ListCard());
            return;
        }
        var back = Wide(SymbolRegular.ArrowLeft24, Loc.T("settings.nav.voiceProfiles"), _ => Back());
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new Thickness(0, 0, 0, 12);
        Body.Children.Add(back);
        Body.Children.Add(EditorCard());
    }

    /// <summary>Back to the list (what was being changed is kept first).</summary>
    private void Back() => Select(_selected, edit: false);

    // ── the list ─────────────────────────────────────────────────────────

    private Border ListCard()
    {
        var (card, stack) = Card(Loc.T("voiceProfiles.title"), Loc.T("voiceProfiles.intro"));
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        buttons.Children.Add(Wide(SymbolRegular.Sparkle24, Loc.T("voiceProfiles.random"), NewRandom, primary: true));
        buttons.Children.Add(Wide(SymbolRegular.ArrowDownload24, Loc.T("voiceProfiles.import"), Import));
        stack.Children.Add(buttons);

        var rows = new StackPanel();
        foreach (var profile in VoiceProfiles.All)
        {
            if (rows.Children.Count > 0)
            {
                var line = new Border { Height = 1 };
                line.SetResourceReference(Border.BackgroundProperty, "CardStrokeColorDefaultBrush");
                rows.Children.Add(line);
            }
            rows.Children.Add(ProfileRow(profile));
        }
        stack.Children.Add(new Border { Style = (Style)Application.Current.FindResource("ListPanel"), Child = rows, Padding = new Thickness(6, 0, 6, 0) });
        return card;
    }

    private FrameworkElement ProfileRow(VoiceProfile profile)
    {
        var builtIn = VoiceProfiles.IsBuiltIn(profile.Id);
        var name = VoiceProfiles.NameOf(profile);
        var grid = new Grid { Margin = new Thickness(8, 8, 8, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var glyph = new SymbolIcon { Symbol = builtIn ? SymbolRegular.PersonVoice24 : SymbolRegular.Person24, FontSize = 18 };
        glyph.SetResourceReference(ForegroundProperty, "Kd.AccentText");
        var tile = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Child = glyph, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
        grid.Children.Add(tile);

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        body.Children.Add(title);
        body.Children.Add(Caption(Summary(profile)));
        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        chips.Children.Add(Chip(Loc.T(builtIn ? "voiceProfiles.builtIn" : "voiceProfiles.own")));
        foreach (var use in UsesOf(profile.Id)) chips.Children.Add(Chip(use, accent: true));
        body.Children.Add(chips);
        var open = new Border { Child = body, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        open.MouseLeftButtonUp += (_, _) => Select(profile.Id);
        Grid.SetColumn(open, 1);
        grid.Children.Add(open);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        buttons.Children.Add(Small(SymbolRegular.Copy24, Loc.T("voiceProfiles.duplicate"), () => Duplicate(profile), name));
        buttons.Children.Add(Small(SymbolRegular.Save24, Loc.T("voiceProfiles.export"), () => Export(profile), name));
        if (!builtIn) buttons.Children.Add(Small(SymbolRegular.Delete24, Loc.T("voiceProfiles.delete"), () => Delete(profile), name));
        buttons.Children.Add(Small(SymbolRegular.ChevronRight24, Loc.T("voiceProfiles.edit"), () => Select(profile.Id), name));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var row = new Border { Child = grid, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetName(row, name);
        return row;
    }

    /// <summary>Its effects in a few words («Tono +3 · Coro · Eco»), or that it has none.</summary>
    private static string Summary(VoiceProfile profile)
    {
        var parts = profile.Chain.OfType<JsonObject>().Where(fx => fx["enabled"]?.GetValue<bool>() != false)
            .Select(fx => fx["fx"]?.GetValue<string>() is { } name ? EffectName(name) : null).OfType<string>().ToList();
        var engine = EngineName(profile.Engine);
        return parts.Count == 0 ? engine + " · " + Loc.T("voiceProfiles.noEffects") : engine + " · " + string.Join(" · ", parts);
    }

    /// <summary>Who speaks with it: everyone (Options → Voice), or some assistants of their own.</summary>
    private IEnumerable<string> UsesOf(string id)
    {
        var s = _app.Config.Settings;
        if (s.AssistantVoiceProfile == id) yield return Loc.T("voiceProfiles.inUseAll");
        var who = s.AssistantPersonal.Where(p => p.Value.Profile == id).Select(p => App.AssistantName(p.Key)).ToList();
        if (who.Count > 0) yield return Loc.T("voiceProfiles.inUseBy", string.Join(", ", who));
    }

    private void NewRandom(FrameworkElement from)
    {
        var random = new Random();
        var json = new JsonObject
        {
            ["name"] = Loc.T("voiceProfiles.randomName", random.Next(100, 1000)),
            ["engine"] = new JsonObject { ["type"] = "kokoro", ["voice"] = "default", ["speed"] = 1.0, ["volume"] = 1.0 },
            ["chain"] = VoiceEffectCatalog.Random(random),
            ["moods"] = new JsonObject
            {
                ["error"] = new JsonArray(new JsonObject { ["fx"] = "glitch", ["chance"] = 0.15 }),
                ["happy"] = new JsonArray(new JsonObject { ["fx"] = "pitch", ["semitones"] = 1.5 }),
            },
        };
        var id = VoiceProfiles.Save(VoiceProfile.Parse(json, string.Empty));
        Select(id);
        Test();
    }

    private void Duplicate(VoiceProfile profile)
    {
        var name = Loc.T("voiceProfiles.copyName", VoiceProfiles.NameOf(profile));
        if (name.Length > 40) name = name[..40];
        var id = VoiceProfiles.Save(VoiceProfile.Parse(profile.ToJson(), string.Empty) with { Name = name });
        Select(id);
    }

    private void Export(VoiceProfile profile)
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("voiceProfiles.exportTitle"),
            Filter = Loc.T("voiceProfiles.importFilter") + "|*.json",
            FileName = string.Concat(VoiceProfiles.NameOf(profile).Split(Path.GetInvalidFileNameChars())) + ".json",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, VoiceProfiles.Export(profile)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = Say(ex.Message); }
    }

    private void Import(FrameworkElement from)
    {
        var dialog = new OpenFileDialog { Title = Loc.T("voiceProfiles.importTitle"), Filter = Loc.T("voiceProfiles.importFilter") + "|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string json;
        try { json = File.ReadAllText(dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _ = Say(ex.Message); return; }
        if (VoiceProfiles.Import(json, out var errors) is { } id) Select(id);
        else _ = Say(Loc.T("voiceProfiles.importBroken", string.Join("\n", errors.Take(3))));
    }

    private async void Delete(VoiceProfile profile)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("voiceProfiles.title"),
            Width = 480,
            MaxWidth = 480,
            Content = Loc.T("voiceProfiles.deleteQuestion", profile.Name),
            PrimaryButtonText = Loc.T("voiceProfiles.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        _save.Stop();
        VoiceProfiles.Delete(profile.Id);
        // Whoever spoke with it speaks as before it was chosen.
        var s = _app.Config.Settings;
        if (s.AssistantVoiceProfile == profile.Id) s.AssistantVoiceProfile = string.Empty;
        foreach (var (who, personal) in s.AssistantPersonal.Where(p => p.Value.Profile == profile.Id).ToList())
            _app.SetAssistantPersonal(who, personal with { Profile = string.Empty });
        Select(VoiceProfile.DefaultId, edit: false);
    }

    // ── the editor ───────────────────────────────────────────────────────

    private Border EditorCard()
    {
        var name = VoiceProfiles.NameOf(_draft);
        var (card, stack) = Card(name, null, SymbolRegular.SlideMicrophone24);
        var heading = stack.Children[0];
        stack.Children.RemoveAt(0);
        stack.Children.Insert(0, EditorHead(heading));
        var editable = new StackPanel { IsEnabled = !IsBuiltIn };
        if (IsBuiltIn)
        {
            var note = new Border { Style = (Style)FindResource("InfoBox"), Margin = new Thickness(0, 0, 0, 12) };
            var line = new DockPanel();
            var copy = Wide(SymbolRegular.Copy24, Loc.T("voiceProfiles.duplicate"), _ => Duplicate(_draft), primary: true);
            copy.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(copy, Dock.Right);
            line.Children.Add(copy);
            var text = new TextBlock { Text = Loc.T("voiceProfiles.builtInNote"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            line.Children.Add(text);
            note.Child = line;
            stack.Children.Add(note);
        }
        else
        {
            var nameBox = new TextBox { Text = _draft.Name, MaxLength = 40, ClearButtonEnabled = false, Tag = "voiceName" };
            AutomationProperties.SetName(nameBox, Loc.T("voiceProfiles.name"));
            nameBox.TextChanged += (_, _) =>
            {
                if (nameBox.Text.Trim().Length == 0) return;
                _draft = _draft with { Name = nameBox.Text.Trim() };
                _save.Stop();
                _save.Start();
            };
            // As wide as the combo of the voice under it.
            editable.Children.Add(Setting(Loc.T("voiceProfiles.name"), null, nameBox, ControlWidth));
        }

        // Engine, speed and volume.
        var engines = new[] { "kokoro", "piper", "windows", "beeps" };
        var engineBox = new ComboBox { ItemsSource = engines.Select(EngineName).ToList(), SelectedIndex = Math.Max(0, Array.IndexOf(engines, _draft.Engine)) };
        AutomationProperties.SetName(engineBox, Loc.T("voiceProfiles.engine"));
        engineBox.SelectionChanged += (_, _) =>
        {
            if (engineBox.SelectedIndex < 0) return;
            _draft = _draft with { Engine = engines[engineBox.SelectedIndex] };
            Changed();
        };
        editable.Children.Add(Setting(Loc.T("voiceProfiles.engine"), Loc.T("voiceProfiles.engineHint"), engineBox, ControlWidth));
        // Under the combo, as wide as it.
        editable.Children.Add(Setting(Loc.T("voiceProfiles.speed"), null, SliderWithValue(0.5, 2, 0.05, _draft.Speed, v => v.ToString("0.00", Loc.Instance.Culture) + "×",
            v => { _draft = _draft with { Speed = Math.Round(v, 2) }; Changed(); }, Loc.T("voiceProfiles.speed")), ControlWidth));
        editable.Children.Add(Setting(Loc.T("voiceProfiles.volume"), null, SliderWithValue(0, 1, 0.05, _draft.Volume, Percent,
            v => { _draft = _draft with { Volume = Math.Round(v, 2) }; Changed(); }, Loc.T("voiceProfiles.volume")), ControlWidth));

        // Which chain: always, or in a situation.
        var pills = new WrapPanel { Margin = new Thickness(0, 14, 0, 4) };
        foreach (var chain in Chains)
        {
            var count = ChainOf(chain, create: false)?.Count ?? 0;
            var pill = new RadioButton
            {
                Style = (Style)FindResource("Pill"),
                Content = Loc.T("voiceProfiles.chain." + chain) + (count > 0 ? $" ({count})" : string.Empty),
                IsChecked = chain == _chain,
                GroupName = "voiceChain",
            };
            AutomationProperties.SetName(pill, Loc.T("voiceProfiles.chain." + chain));
            pill.Checked += (_, _) =>
            {
                if (_chain == chain) return;
                _chain = chain;
                Build();
            };
            pills.Children.Add(pill);
        }
        stack.Children.Add(editable);
        stack.Children.Add(pills);
        var hint = Caption(Loc.T(_chain == "chain" ? "voiceProfiles.chainHint.chain" : "voiceProfiles.chainHint.mood"));
        hint.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(hint);

        _effects = new StackPanel { IsEnabled = !IsBuiltIn };
        FillEffects();
        stack.Children.Add(_effects);

        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), IsEnabled = !IsBuiltIn };
        actions.Children.Add(Wide(SymbolRegular.Add24, Loc.T("voiceProfiles.addEffect"), AddEffect));
        actions.Children.Add(Wide(SymbolRegular.Sparkle24, Loc.T("voiceProfiles.shuffle"), _ =>
        {
            var chain = ChainOf(_chain, create: true)!;
            chain.Clear();
            foreach (var fx in VoiceEffectCatalog.Random(new Random()).ToList()) chain.Add(fx!.DeepClone());
            Changed();
            Build();
        }));
        stack.Children.Add(actions);

        stack.Children.Add(TestBox());
        return card;
    }

    /// <summary>What the voice, speed and volume of the editor take (the name too: one under the other).</summary>
    private const double ControlWidth = 240;

    /// <summary>
    /// The name of the profile and, on its right, «Usar para todos» (or that all use it) and its icons, as in the list:
    /// duplicate, export and, if it is the user's, delete.
    /// </summary>
    private Grid EditorHead(UIElement heading)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (heading is FrameworkElement head) head.Margin = new Thickness(0);
        grid.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var name = VoiceProfiles.NameOf(_draft);
        var profile = _draft;
        // Options → Voz → «Cómo suenan» with it: everyone speaks with it (deleting it puts back «La de cada asistente»).
        if (_app.Config.Settings.AssistantVoiceProfile == _draft.Id) buttons.Children.Add(Chip(Loc.T("voiceProfiles.inUseAll"), accent: true));
        else
        {
            var use = Wide(SymbolRegular.Checkmark24, Loc.T("voiceProfiles.useForAll"), _ =>
            {
                Keep();
                _app.Config.Settings.AssistantVoiceProfile = _draft.Id;
                Build();
            });
            use.ToolTip = Loc.T("voiceProfiles.useForAllHint");
            use.Margin = new Thickness(0, 0, 6, 0);
            buttons.Children.Add(use);
        }
        buttons.Children.Add(Small(SymbolRegular.Copy24, Loc.T("voiceProfiles.duplicate"), () => { Keep(); Duplicate(_draft); }, name));
        buttons.Children.Add(Small(SymbolRegular.Save24, Loc.T("voiceProfiles.export"), () => { Keep(); Export(_draft); }, name));
        if (!IsBuiltIn) buttons.Children.Add(Small(SymbolRegular.Delete24, Loc.T("voiceProfiles.delete"), () => Delete(profile), name));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);
        return grid;
    }

    /// <summary>The chain shown (the profile's, or a mood's; <paramref name="create"/>: an empty one for a mood without it).</summary>
    private JsonArray? ChainOf(string chain, bool create)
    {
        if (chain == "chain") return _draft.Chain;
        if (_draft.Moods.TryGetValue(chain, out var found)) return found;
        if (!create) return null;
        var moods = new Dictionary<string, JsonArray>(_draft.Moods, StringComparer.Ordinal) { [chain] = [] };
        _draft = _draft with { Moods = moods };
        return moods[chain];
    }

    private void FillEffects()
    {
        _effects!.Children.Clear();
        var chain = ChainOf(_chain, create: false);
        if (chain is null || chain.Count == 0)
        {
            var none = Caption(Loc.T("voiceProfiles.noEffects"));
            none.Margin = new Thickness(0, 4, 0, 8);
            _effects.Children.Add(none);
            return;
        }
        for (var i = 0; i < chain.Count; i++)
            if (chain[i] is JsonObject fx) _effects.Children.Add(EffectCard(chain, fx, i));
    }

    private Border EffectCard(JsonArray chain, JsonObject fx, int index)
    {
        var kind = fx["fx"]?.GetValue<string>() ?? "?";
        var on = fx["enabled"]?.GetValue<bool>() != false;
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var handle = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeNS, Padding = new Thickness(2, 4, 10, 4), ToolTip = Loc.T("voiceProfiles.drag"), VerticalAlignment = VerticalAlignment.Center };
        var dots = new SymbolIcon { Symbol = SymbolRegular.ReOrderDotsVertical24, FontSize = 18 };
        dots.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");
        handle.Child = dots;
        head.Children.Add(handle);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = $"{index + 1}. {EffectName(kind)}", FontSize = 14, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        titles.Children.Add(title);
        titles.Children.Add(Caption(Loc.T("voiceFx." + kind + ".hint")));
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var toggle = new ToggleSwitch { IsChecked = on, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(toggle, Loc.T("voiceProfiles.effectOn", EffectName(kind)));
        toggle.Checked += (_, _) => { fx.Remove("enabled"); Changed(); FadeParams(stack, true); };
        toggle.Unchecked += (_, _) => { fx["enabled"] = false; Changed(); FadeParams(stack, false); };
        buttons.Children.Add(toggle);
        buttons.Children.Add(Small(SymbolRegular.ArrowUp24, Loc.T("voiceProfiles.moveUp"), () => Move(chain, index, index - 1), EffectName(kind), index > 0));
        buttons.Children.Add(Small(SymbolRegular.ArrowDown24, Loc.T("voiceProfiles.moveDown"), () => Move(chain, index, index + 1), EffectName(kind), index < chain.Count - 1));
        buttons.Children.Add(Small(SymbolRegular.Dismiss24, Loc.T("voiceProfiles.remove"), () =>
        {
            chain.RemoveAt(index);
            Changed();
            Build();
        }, EffectName(kind)));
        Grid.SetColumn(buttons, 2);
        head.Children.Add(buttons);
        stack.Children.Add(head);

        var values = new StackPanel { Margin = new Thickness(28, 6, 0, 0), Opacity = on ? 1 : 0.45, Tag = "values" };
        foreach (var value in VoiceEffectCatalog.Effects.GetValueOrDefault(kind) ?? [])
        {
            var label = Loc.T("voiceFx." + kind + "." + value.Name);
            if (value.Toggle)
            {
                var yes = new ToggleSwitch { Content = label, IsChecked = fx[value.Name]?.GetValue<bool>() ?? value.Default >= 0.5, Margin = new Thickness(0, 4, 0, 4) };
                AutomationProperties.SetName(yes, label);
                yes.Checked += (_, _) => { fx[value.Name] = true; Changed(); };
                yes.Unchecked += (_, _) => { fx[value.Name] = false; Changed(); };
                values.Children.Add(yes);
                continue;
            }
            var current = fx[value.Name]?.GetValue<double>() ?? value.Default;
            values.Children.Add(Setting(label, null, SliderWithValue(value.Min, value.Max, value.Step, current, v => Format(value, v),
                v => { fx[value.Name] = VoiceEffectCatalog.Json(value, v); Changed(); }, label), 360, compact: true));
        }
        stack.Children.Add(values);

        var card = new Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
            RenderTransform = new TranslateTransform(),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Kd.Subtle");
        card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        AutomationProperties.SetName(card, EffectName(kind));
        Draggable(handle, card, chain, index);
        return card;
    }

    private static void FadeParams(StackPanel stack, bool on)
    {
        foreach (var child in stack.Children.OfType<StackPanel>().Where(s => s.Tag as string == "values")) child.Opacity = on ? 1 : 0.45;
    }

    /// <summary>Dragging the handle moves the effect: it follows the pointer and goes where it is let go.</summary>
    private void Draggable(FrameworkElement handle, Border card, JsonArray chain, int index)
    {
        Point? start = null;
        handle.MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(_effects);
            handle.CaptureMouse();
            card.Opacity = 0.85;
            Panel.SetZIndex(card, 1);
            e.Handled = true;
        };
        handle.MouseMove += (_, e) =>
        {
            if (start is not { } from || !handle.IsMouseCaptured) return;
            ((TranslateTransform)card.RenderTransform).Y = e.GetPosition(_effects).Y - from.Y;
        };
        handle.MouseLeftButtonUp += (_, e) =>
        {
            if (start is null) return;
            handle.ReleaseMouseCapture();
            var y = e.GetPosition(_effects).Y;
            start = null;
            // Where it is let go: before the first card whose middle is below the pointer.
            var target = chain.Count - 1;
            var cards = _effects!.Children.OfType<Border>().ToList();
            for (var i = 0; i < cards.Count; i++)
            {
                var top = cards[i].TranslatePoint(new Point(0, 0), _effects).Y - ((TranslateTransform)cards[i].RenderTransform).Y;
                if (y < top + cards[i].ActualHeight / 2)
                {
                    target = i > index ? i - 1 : i;
                    break;
                }
            }
            Move(chain, index, target);
        };
        handle.LostMouseCapture += (_, _) =>
        {
            if (start is null) return;
            start = null;
            FillEffects();
        };
    }

    private void Move(JsonArray chain, int from, int to)
    {
        to = Math.Clamp(to, 0, chain.Count - 1);
        if (from != to)
        {
            var fx = chain[from]!;
            chain.RemoveAt(from);
            chain.Insert(to, fx);
            Changed();
        }
        Build();
    }

    private void AddEffect(FrameworkElement from)
    {
        var menu = new ContextMenu { PlacementTarget = from, Placement = PlacementMode.Bottom };
        foreach (var fx in VoiceEffectCatalog.Names)
        {
            var item = new MenuItem { Header = EffectName(fx), ToolTip = Loc.T("voiceFx." + fx + ".hint") };
            item.Click += (_, _) =>
            {
                ChainOf(_chain, create: true)!.Add(VoiceEffectCatalog.New(fx));
                Changed();
                Build();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ── hearing it ───────────────────────────────────────────────────────

    private Border TestBox()
    {
        var box = new Border { Style = (Style)FindResource("InfoBox"), Margin = new Thickness(0, 14, 0, 0) };
        var stack = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBox { Text = _testText ?? DefaultTestText(), ClearButtonEnabled = false, Tag = "voiceTestText" };
        AutomationProperties.SetName(text, Loc.T("voiceProfiles.testText"));
        text.TextChanged += (_, _) => _testText = text.Text;
        text.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Test();
            e.Handled = true;
        };
        row.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        var play = Icon(SymbolRegular.Play24, Loc.T("voiceProfiles.test"), Test, primary: true);
        play.Margin = new Thickness(0);
        // Both as tall as the box beside them.
        play.VerticalAlignment = VerticalAlignment.Stretch;
        buttons.Children.Add(play);
        var stop = Icon(SymbolRegular.Stop24, Loc.T("voiceProfiles.stop"), _app.StopAssistantVoiceTest);
        stop.Margin = new Thickness(8, 0, 0, 0);
        stop.VerticalAlignment = VerticalAlignment.Stretch;
        buttons.Children.Add(stop);
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        stack.Children.Add(row);
        var auto = new ToggleSwitch { Content = Loc.T("voiceProfiles.testAuto"), IsChecked = _autoTest, Margin = new Thickness(0, 10, 0, 0) };
        AutomationProperties.SetName(auto, Loc.T("voiceProfiles.testAuto"));
        auto.Checked += (_, _) => _autoTest = true;
        auto.Unchecked += (_, _) => _autoTest = false;
        stack.Children.Add(auto);
        _testNote = Caption(string.Empty);
        _testNote.Visibility = Visibility.Collapsed;
        _testNote.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(_testNote);
        box.Child = stack;
        return box;
    }

    private string DefaultTestText() => App.TestSentence(_app.AssistantLanguage, VoiceProfiles.NameOf(_draft));

    /// <summary>Says the sentence with the profile as it is on screen (in the situation shown).</summary>
    private void Test()
    {
        var ok = _app.TestVoiceProfile(_testText is { Length: > 0 } t ? t : DefaultTestText(), _draft, _chain == "chain" ? null : _chain);
        if (_testNote is null) return;
        _testNote.Text = Loc.T("voiceProfiles.notRunning");
        _testNote.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Something changed: kept in a moment (the user's), and heard again in a moment (if they want).</summary>
    private void Changed()
    {
        if (!IsBuiltIn)
        {
            _save.Stop();
            _save.Start();
        }
        if (_autoTest)
        {
            _test.Stop();
            _test.Start();
        }
    }

    private void Keep()
    {
        if (IsBuiltIn) return;
        try { VoiceProfiles.Save(_draft); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Core.Log.Warn($"Perfil de voz {_draft.Id}: no se ha podido guardar: {ex.Message}"); }
    }

    // ── pieces ───────────────────────────────────────────────────────────

    private static string EffectName(string fx) => Loc.T("voiceFx." + fx);

    private static string EngineName(string engine) => engine switch
    {
        "kokoro" => Loc.T("assistantVoice.engineKokoro"),
        "piper" => Loc.T("assistantVoice.enginePiper"),
        "windows" => Loc.T("assistantVoice.engineWindows"),
        "beeps" => Loc.T("assistantVoice.engineBeeps"),
        _ => engine,
    };

    private static readonly HashSet<string> Fractions = ["mix", "noise", "depth", "feedback", "chance"];

    private static string Percent(double v) => Math.Round(v * 100).ToString(Loc.Instance.Culture) + " %";

    private static string Format(VoiceEffectValue value, double v)
    {
        if (Fractions.Contains(value.Name)) return Percent(v);
        var text = value.Whole ? Math.Round(v).ToString(Loc.Instance.Culture) : Math.Round(v, value.Step < 0.1 ? 2 : 1).ToString(Loc.Instance.Culture);
        return value.Name switch
        {
            "semitones" => (v > 0 ? "+" : string.Empty) + text,
            "shift" => text + "×",
            "freq" or "note" or "rate" or "bandwidth" => text + " Hz",
            "ms" => text + " ms",
            _ => text,
        };
    }

    /// <summary>A slider and its value next to it; <paramref name="changed"/> with each move.</summary>
    private static FrameworkElement SliderWithValue(double min, double max, double step, double value, Func<double, string> format, Action<double> changed, string name)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 50 });
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            SmallChange = step,
            LargeChange = step * 10,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(slider, name);
        var shown = new TextBlock { Text = format(slider.Value), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        shown.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        slider.ValueChanged += (_, e) =>
        {
            shown.Text = format(e.NewValue);
            changed(e.NewValue);
        };
        grid.Children.Add(slider);
        Grid.SetColumn(shown, 1);
        grid.Children.Add(shown);
        return grid;
    }

    /// <summary>A row: its name (and a hint) on the left, its control on the right.</summary>
    private FrameworkElement Setting(string label, string? hint, FrameworkElement control, double width, bool compact = false)
    {
        var grid = new Grid { Margin = new Thickness(0, compact ? 2 : 6, 0, compact ? 2 : 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        var name = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = compact ? 13 : 14 };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        texts.Children.Add(name);
        if (hint is { Length: > 0 }) texts.Children.Add(Caption(hint));
        grid.Children.Add(texts);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private (Border Card, StackPanel Content) Card(string title, string? intro, SymbolRegular? icon = null)
    {
        var stack = new StackPanel();
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, icon is null ? 0 : 12) };
        if (icon is { } symbol)
        {
            var glyph = new SymbolIcon { Symbol = symbol, FontSize = 18 };
            glyph.SetResourceReference(ForegroundProperty, "Kd.AccentText");
            var tile = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Child = glyph, Margin = new Thickness(0, 0, 12, 0) };
            tile.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
            heading.Children.Add(tile);
        }
        heading.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) });
        stack.Children.Add(heading);
        if (intro is { Length: > 0 })
        {
            var text = Caption(intro);
            text.Margin = new Thickness(0, 4, 0, 12);
            stack.Children.Add(text);
        }
        return (new Border { Style = (Style)FindResource("Card"), Child = stack }, stack);
    }

    private TextBlock Caption(string text) => new() { Style = (Style)FindResource("Caption"), Text = text, TextWrapping = TextWrapping.Wrap };

    private static Border Chip(string text, bool accent = false)
    {
        var label = new TextBlock { Text = text, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 };
        label.SetResourceReference(TextBlock.ForegroundProperty, accent ? "Kd.AccentText" : "TextFillColorSecondaryBrush");
        var chip = new Border { Child = label, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 3), Margin = new Thickness(0, 0, 6, 2) };
        chip.SetResourceReference(Border.BackgroundProperty, accent ? "Kd.AccentSoft" : "Kd.ChipBackground");
        return chip;
    }

    /// <param name="target">What it acts on, for its accessible name («Borrar Mi robot»).</param>
    /// <summary>A button with only its icon (its name in the tooltip), as tall as what is beside it.</summary>
    private static Button Icon(SymbolRegular symbol, string name, Action click, bool primary = false)
    {
        var button = new Button
        {
            Icon = new SymbolIcon { Symbol = symbol },
            Appearance = primary ? ControlAppearance.Primary : ControlAppearance.Secondary,
            Padding = new Thickness(10, 6, 10, 6),
            ToolTip = name,
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    private static Button Small(SymbolRegular symbol, string name, Action click, string target, bool enabled = true)
    {
        var button = new Button
        {
            Icon = new SymbolIcon { Symbol = symbol },
            Appearance = ControlAppearance.Transparent,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(2, 0, 0, 0),
            ToolTip = name,
            IsEnabled = enabled,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(button, name + " " + target);
        button.Click += (_, _) => click();
        return button;
    }

    private static Button Wide(SymbolRegular symbol, string text, Action<FrameworkElement> click, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 36,
            Icon = new SymbolIcon { Symbol = symbol },
            Appearance = primary ? ControlAppearance.Primary : ControlAppearance.Secondary,
            Margin = new Thickness(0, 0, 8, 6),
        };
        button.Click += (_, _) => click(button);
        return button;
    }

    private static Task Say(string text) => new Wpf.Ui.Controls.MessageBox
    {
        Title = Loc.T("voiceProfiles.title"),
        Width = 480,
        MaxWidth = 480,
        Content = text,
        CloseButtonText = Loc.T("common.close"),
    }.ShowDialogAsync();
}
