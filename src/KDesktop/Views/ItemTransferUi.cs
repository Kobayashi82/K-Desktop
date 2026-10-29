using System.IO;
using System.Windows;
using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Storage;
using Microsoft.Win32;

namespace KDesktop.Views;

/// <summary>
/// «Exportar…» and «Importar…» of a command, macro, K-Board profile, event or QuickMenu (E6), from the right-click menu of
/// their lists and the header of their editors (and a .kdx dropped on the window). What it does is in <see cref="ItemTransfer"/>.
/// </summary>
internal static class ItemTransferUi
{
    private static App App => App.Current;

    private static string Folder() =>
        Directory.Exists(App.Config.Settings.TransferFolder) ? App.Config.Settings.TransferFolder! : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private static string Filter => Loc.T("transfer.filter") + "|*" + ItemTransfer.Extension;

    public static void Export(DependencyObject from, TransferKind kind, string id, string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("transfer.exportTitle", name),
            Filter = Filter,
            InitialDirectory = Folder(),
            FileName = (safe.Length > 0 ? safe : "K-Desktop") + ItemTransfer.Extension,
        };
        if (dialog.ShowDialog(Window.GetWindow(from)) != true) return;
        App.Config.Settings.TransferFolder = Path.GetDirectoryName(dialog.FileName);
        try
        {
            ItemTransfer.Export(App.Config, kind, id, dialog.FileName);
            App.Ui.ShowNotice(Loc.T("transfer.exported", name), OsdKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error($"No se pudo exportar «{name}»", ex);
            App.Ui.ShowNotice(Loc.T("transfer.exportError", name), OsdKind.Warning);
        }
    }

    public static void Import(DependencyObject from)
    {
        var dialog = new OpenFileDialog { Title = Loc.T("transfer.importTitle"), Filter = Filter, InitialDirectory = Folder() };
        if (dialog.ShowDialog(Window.GetWindow(from)) != true) return;
        App.Config.Settings.TransferFolder = Path.GetDirectoryName(dialog.FileName);
        _ = ImportAsync(dialog.FileName);
    }

    /// <summary>Imports a .kdx: asks first when something of it is here but different, then opens what came in.</summary>
    public static async Task ImportAsync(string path)
    {
        ItemTransfer.Incoming incoming;
        try { incoming = ItemTransfer.Read(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"«{Path.GetFileName(path)}» no es un elemento de K-Desktop: {ex.Message}");
            App.Ui.ShowNotice(Loc.T("transfer.importError"), OsdKind.Warning);
            return;
        }

        var choice = ItemTransfer.ClashChoice.Copy;
        var clashes = ItemTransfer.Clashes(App.Config, incoming.Bundle);
        if (clashes.Count > 0)
        {
            const int Shown = 8;
            var lines = clashes.Take(Shown).Select(c => "•  " + Loc.T("transfer.kind." + c.Kind) + " «" + c.Name + "»").ToList();
            if (clashes.Count > Shown) lines.Add("…");
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Width = 520,
                MaxWidth = 520,
                Title = Loc.T("transfer.clashTitle"),
                Content = Loc.T("transfer.clashText", string.Join("\n", lines)),
                PrimaryButtonText = Loc.T("transfer.copy"),
                SecondaryButtonText = Loc.T("transfer.keepMine"),
                CloseButtonText = Loc.T("common.cancel"),
            };
            var answer = await box.ShowDialogAsync();
            if (answer == Wpf.Ui.Controls.MessageBoxResult.None) return;
            if (answer == Wpf.Ui.Controls.MessageBoxResult.Secondary) choice = ItemTransfer.ClashChoice.KeepMine;
        }

        ItemTransfer.Result result;
        try { result = ItemTransfer.Apply(App.Config, incoming, choice); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Error($"No se pudo importar «{Path.GetFileName(path)}»", ex);
            App.Ui.ShowNotice(Loc.T("transfer.importError"), OsdKind.Warning);
            return;
        }

        var text = result.Added == 0 ? Loc.T("transfer.alreadyHere", result.RootName) : Loc.T("transfer.imported", result.RootName);
        if (result.HotkeysDropped > 0) text += " " + Loc.T("transfer.hotkeysDropped");
        App.Ui.ShowNotice(text, result.Added == 0 ? OsdKind.Info : OsdKind.Success);
        App.ShowMainWindow(w =>
        {
            switch (result.Kind)
            {
                case TransferKind.Command: w.OpenCommand(result.RootId); break;
                case TransferKind.Macro: w.OpenMacro(result.RootId); break;
                case TransferKind.KBoard: w.OpenKBoard(result.RootId); break;
                case TransferKind.Event: w.OpenEvent(result.RootId); break;
                case TransferKind.QuickMenu: w.OpenQuickMenu(result.RootId); break;
            }
        });
    }
}
