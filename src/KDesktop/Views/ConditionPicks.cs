using System.IO;
using System.Windows;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Microsoft.Win32;

namespace KDesktop.Views;

/// <summary>«Existe un archivo o carpeta»: its buttons to choose one, in «Si…» and in an event's «Solo si».</summary>
public static class ConditionPicks
{
    public static void File(ConditionAction condition, Window? owner)
    {
        var file = new OpenFileDialog { Title = Loc.T("editor.chooseFile"), Filter = Loc.T("editor.filterAll") + "|*.*", DereferenceLinks = false };
        if (Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(condition.Text)) is { Length: > 0 } folder && Directory.Exists(folder)) file.InitialDirectory = folder;
        if (file.ShowDialog(owner) == true) condition.Text = file.FileName;
    }

    public static void Folder(ConditionAction condition, Window? owner)
    {
        var initial = Environment.ExpandEnvironmentVariables(condition.Text);
        var dlg = new OpenFolderDialog { Title = Loc.T("editor.chooseFolder") };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dlg.InitialDirectory = initial;
        if (dlg.ShowDialog(owner) == true) condition.Text = dlg.FolderName;
    }
}
