using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Services;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Pages;

/// <summary>A line of the «Registro», with its texts ready to show.</summary>
public sealed class HistoryRow(HistoryEntry entry)
{
    public HistoryEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public RunOutcome Outcome => Entry.Outcome;
    public Thickness Indent => new(Math.Min(Entry.Depth, 4) * 26, 0, 0, 0);

    public SymbolRegular Symbol => Entry.Outcome switch
    {
        RunOutcome.Error => SymbolRegular.ErrorCircle24,
        RunOutcome.Stopped => SymbolRegular.RecordStop24,
        RunOutcome.Cancelled => SymbolRegular.DismissCircle24,
        RunOutcome.NotUnderstood => SymbolRegular.QuestionCircle24,
        _ => SymbolRegular.CheckmarkCircle24,
    };

    /// <summary>The command's icon (a microphone for what the voice did on its own).</summary>
    public ImageSource? Icon => App.Current.Config.FindCommand(Entry.CommandId) is { } command
        ? IconProvider.ForCommand(command)
        : IconProvider.Kd(Entry.Source == TriggerSource.Voice ? "Mic24" : "Flash24", Entry.Source == TriggerSource.Voice ? "teal" : "gold");

    /// <summary>«Atajo · Evento «Juegos» · Ventana: …», then why it failed or stopped.</summary>
    public string Line
    {
        get
        {
            var parts = new List<string> { Loc.T($"history.source.{Entry.Source}") };
            if (!string.IsNullOrWhiteSpace(Entry.Detail)) parts.Add(Entry.Detail!);
            var outcome = Entry.Outcome switch
            {
                RunOutcome.Error => Loc.T("history.error"),
                RunOutcome.Stopped => Loc.T("history.stopped"),
                RunOutcome.Cancelled => Loc.T("history.cancelled"),
                RunOutcome.NotUnderstood => Loc.T("history.notUnderstood"),
                _ => null,
            };
            if (outcome is not null) parts.Add(string.IsNullOrWhiteSpace(Entry.Message) ? outcome : $"{outcome}: {Entry.Message}");
            else if (!string.IsNullOrWhiteSpace(Entry.Message)) parts.Add(Entry.Message!);
            return string.Join("  ·  ", parts);
        }
    }

    public string When
    {
        get
        {
            var time = Entry.Time;
            if (time.Date == DateTime.Today) return time.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            if (time.Date == DateTime.Today.AddDays(-1)) return Loc.T("history.yesterday", time.ToString("HH:mm", CultureInfo.CurrentCulture));
            return time.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
        }
    }

    public string Duration => Entry.Outcome == RunOutcome.NotUnderstood ? string.Empty
        : Entry.Milliseconds < 1000 ? $"{Entry.Milliseconds} ms"
        : Entry.Milliseconds < 60_000 ? string.Create(CultureInfo.CurrentCulture, $"{Entry.Milliseconds / 1000.0:0.#} s")
        : string.Create(CultureInfo.CurrentCulture, $"{Entry.Milliseconds / 60_000.0:0.#} min");

    public bool Matches(string search) =>
        search.Length == 0 ||
        Conditions.Contains(Entry.Name, search) || Conditions.Contains(Entry.Detail, search) || Conditions.Contains(Entry.Message, search);
}

/// <summary>«Registro»: the last runs (newest first), from where, how long and how they went.</summary>
public partial class HistoryPage : UserControl, IDisposable
{
    /// <summary>What the source filter offers: everything, only what went wrong, or one origin.</summary>
    private sealed record SourceOption(string Name, Func<HistoryEntry, bool> Accepts);

    private readonly App _app = App.Current;
    private readonly ObservableCollection<HistoryRow> _rows = [];
    private readonly ICollectionView _view;

    public HistoryPage()
    {
        InitializeComponent();
        SourceFilter.ItemsSource = new List<SourceOption>
        {
            new(Loc.T("history.filter.all"), _ => true),
            new(Loc.T("history.filter.problems"), e => e.Outcome is not RunOutcome.Ok),
            new(Loc.T("history.source.Hotkey"), e => e.Source == TriggerSource.Hotkey),
            new(Loc.T("history.source.QuickMenu"), e => e.Source == TriggerSource.QuickMenu),
            new(Loc.T("history.source.Voice"), e => e.Source == TriggerSource.Voice),
            new(Loc.T("history.source.Web"), e => e.Source == TriggerSource.Web),
            new(Loc.T("history.source.Event"), e => e.Source is TriggerSource.Event or TriggerSource.Startup or TriggerSource.Exit),
            new(Loc.T("history.source.Remote"), e => e.Source == TriggerSource.Remote),
            new(Loc.T("history.source.Launcher"), e => e.Source == TriggerSource.Launcher),
            new(Loc.T("history.source.Manual"), e => e.Source is TriggerSource.Manual or TriggerSource.Command),
        };
        SourceFilter.SelectedIndex = 0;

        foreach (var entry in _app.Engine.History.Entries.Reverse()) _rows.Add(new HistoryRow(entry));
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = o => o is HistoryRow row && Accepts(row);
        List.ItemsSource = _view;
        _app.Engine.History.Changed += OnHistoryChanged;
        Refresh();
    }

    private bool Accepts(HistoryRow row) =>
        (SourceFilter.SelectedItem as SourceOption)?.Accepts(row.Entry) != false && row.Matches(SearchBox.Text.Trim());

    private void OnHistoryChanged(HistoryEntry? entry) => Dispatcher.BeginInvoke(() =>
    {
        if (entry is null) _rows.Clear();
        else
        {
            _rows.Insert(0, new HistoryRow(entry));
            while (_rows.Count > ExecutionHistory.Capacity) _rows.RemoveAt(_rows.Count - 1);
        }
        Refresh();
    });

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_view is null) return; // during InitializeComponent
        _view.Refresh();
        Refresh();
    }

    private void Refresh()
    {
        var shown = _view.Cast<object>().Count();
        CountText.Text = shown == _rows.Count ? _rows.Count.ToString(CultureInfo.CurrentCulture) : $"{shown} / {_rows.Count}";
        Empty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClear(object sender, RoutedEventArgs e) => _app.Engine.History.Clear();

    /// <summary>Double click: opens the command in the editor.</summary>
    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || sender is not FrameworkElement { DataContext: HistoryRow row }) return;
        if (row.Entry.CommandId is { } id && _app.Config.FindCommand(id) is not null) (Window.GetWindow(this) as MainWindow)?.OpenCommand(id);
    }

    public void Dispose() => _app.Engine.History.Changed -= OnHistoryChanged;
}
