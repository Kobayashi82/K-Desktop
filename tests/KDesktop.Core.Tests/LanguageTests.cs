using System.Text.Json;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

public class LanguageTests
{
    private static readonly string[] Languages = ["es", "en", "fr", "de", "it"];

    private static Dictionary<string, string> Load(string code)
    {
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "src", "KDesktop.Core", "Languages"))) dir = Path.GetDirectoryName(dir)!;
        var json = File.ReadAllText(Path.Combine(dir, "src", "KDesktop.Core", "Languages", code + ".json"));
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    }

    /// <summary>Every key that some language has must be in all six (a missing one would show up in English or as the key).</summary>
    [Fact]
    public void AllLanguagesHaveTheSameKeys()
    {
        var all = Languages.ToDictionary(l => l, Load);
        var keys = all.Values.SelectMany(d => d.Keys).ToHashSet();
        foreach (var (code, strings) in all)
        {
            var missing = keys.Where(k => !strings.ContainsKey(k)).Order().ToList();
            Assert.True(missing.Count == 0, $"{code}: faltan {string.Join(", ", missing.Take(20))}");
        }
    }

    /// <summary>«Exportar plantilla» gives the texts of the language in use (Spanish in the tests) with every key, under the new code.</summary>
    [Fact]
    public void TemplateHasTheCurrentLanguage()
    {
        var template = JsonSerializer.Deserialize<Dictionary<string, string>>(Localization.Loc.Template("nl"))!;
        var en = Load("en");
        var es = Load("es");
        Assert.Equal(en.Keys.Order(), template.Keys.Order());
        Assert.Equal("nl", template["_language"]);
        foreach (var key in en.Keys.Where(k => k != "_language")) Assert.Equal(es[key], template[key]);
    }

    [Fact]
    public void EveryEventKindHasItsTexts()
    {
        var es = Load("es");
        foreach (var info in EventCatalog.All)
        {
            Assert.Contains($"event.kind.{info.Kind}", es.Keys);
            Assert.Contains($"event.kind.{info.Kind}.desc", es.Keys);
            if (info.Create().HasEnd) Assert.Contains($"event.endHint.{info.Kind}", es.Keys);
        }
    }

    [Fact]
    public void EveryAchievementHasItsTexts()
    {
        var es = Load("es");
        foreach (var def in Achievements.AchievementCatalog.All.Where(a => a.Kind is null))
        {
            Assert.Contains($"ach.{def.Id}.name", es.Keys);
            if (def.Stat is null || def.Stat == Achievements.Stat.Kinds) Assert.Contains($"ach.{def.Id}.desc", es.Keys);
            else Assert.Contains(def.Target == 1 ? $"goal.{def.Stat}.1" : $"goal.{def.Stat}", es.Keys);
        }
    }
}
