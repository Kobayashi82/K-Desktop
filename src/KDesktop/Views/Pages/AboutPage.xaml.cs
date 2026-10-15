using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Views.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = Loc.T("about.version", version?.ToString(3) ?? "5.0",
            Loc.T(ProcessLauncher.IsElevated ? "about.elevated" : "about.notElevated"));
    }
}
