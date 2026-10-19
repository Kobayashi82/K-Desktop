using KDesktop.Core.Achievements;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Pages;

/// <summary>One achievement as shown on the achievements page.</summary>
public sealed class AchievementItem
{
    public AchievementItem(AchievementDef def, StatsData data)
    {
        Def = def;
        Unlocked = data.Unlocked.TryGetValue(def.Id, out var date);
        Date = Unlocked ? date : null;
        Progress = Math.Min(AchievementService.Progress(data, def), def.Target);
    }

    public AchievementDef Def { get; }
    public bool Unlocked { get; }
    public DateTime? Date { get; }
    public long Progress { get; }

    public bool Hidden => Def.Secret && !Unlocked;
    public string Category => Loc.T($"achcat.{Def.Category}");
    public string Name => Hidden ? Loc.T("ach.secret.name") : Def.Name;
    public string Description => Hidden ? Loc.T("ach.secret.desc") : Def.Description;

    public SymbolRegular Symbol =>
        Hidden ? SymbolRegular.QuestionCircle24
        : Def.Id == AchievementCatalog.Master ? SymbolRegular.Crown20
        : Enum.TryParse<SymbolRegular>(Def.Symbol, out var s) ? s : SymbolRegular.Trophy24;

    public bool ShowProgress => !Unlocked && Def.HasProgress && !Hidden;
    public double Fraction => Def.Target <= 0 ? 0 : (double)Progress / Def.Target;
    public string ProgressText => $"{Progress.ToString("N0", Loc.Instance.Culture)} / {Def.Target.ToString("N0", Loc.Instance.Culture)}";

    public string Footer => Unlocked
        ? Loc.T("ach.unlockedOn", Date!.Value.ToString("d", Loc.Instance.Culture))
        : ShowProgress ? ProgressText : Loc.T("ach.locked");

    public bool Matches(string query) =>
        string.IsNullOrEmpty(query) ||
        Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
