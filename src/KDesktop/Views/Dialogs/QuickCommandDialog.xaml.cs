using System.IO;
using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>"Comando rápido": creates a command from a program, folder or URL in one step.</summary>
public partial class QuickCommandDialog : FluentWindow
{
    private bool _nameEdited;

    public Command? Result { get; private set; }

    public QuickCommandDialog()
    {
        InitializeComponent();
        NameBox.TextChanged += (_, _) => _nameEdited = NameBox.IsKeyboardFocused || _nameEdited;
        Loaded += (_, _) => TargetBox.Focus();
    }

    public static Command? Create(Window? owner)
    {
        var dlg = new QuickCommandDialog { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    private void OnTargetChanged(object sender, TextChangedEventArgs e)
    {
        if (_nameEdited) return;
        NameBox.Text = CommandFactory.FromTarget(TargetBox.Text)?.Name ?? string.Empty;
    }

    private void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = Loc.T("editor.filterPrograms") + "|*.exe;*.lnk;*.bat;*.cmd;*.vbs;*.ps1;*.msc;*.url|" + Loc.T("editor.filterAll") + "|*.*",
            DereferenceLinks = false,
        };
        if (dlg.ShowDialog(this) == true) TargetBox.Text = dlg.FileName;
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog(this) == true) TargetBox.Text = dlg.FolderName;
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        var cmd = CommandFactory.FromTarget(TargetBox.Text, NameBox.Text);
        if (cmd is null)
        {
            TargetBox.Focus();
            return;
        }
        if (AdminCheck.IsChecked == true && cmd.Actions.FirstOrDefault() is RunAppAction run) run.RunAsAdmin = true;
        Result = cmd;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
