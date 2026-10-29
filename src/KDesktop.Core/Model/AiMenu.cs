using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>
/// «Crear menú de IA» (D2, 5.0.371): a QuickMenu «IA» with five commands, each an «Asistente: preguntar» with {seleccion}
/// (translate and correct paste the answer over the selection; summarize, explain and reply show it on a sheet), «Leer en
/// voz alta» (D4, 5.0.373: an «Asistente: decir» with it), «Traducir la pantalla» (D6, 5.0.374: «Copiar texto de la
/// pantalla» of a region, then translated on a sheet), and the command that opens it, for the user to give it a hotkey.
/// Made only when asked; then they are the user's to change.
/// </summary>
public static class AiMenu
{
    /// <param name="Answer">Null: not a question, it is said (read aloud).</param>
    /// <param name="Screen">The text is read from the screen first (a region chosen with the mouse), into {resultado}.</param>
    private sealed record Entry(string Key, AssistantAnswer? Answer, string Icon, bool Screen = false);

    private static readonly Entry[] Entries =
    [
        new("translate", AssistantAnswer.Paste, "kd:Translate24:sky"),
        new("correct", AssistantAnswer.Paste, "kd:TextGrammarWand24:green"),
        new("summarize", AssistantAnswer.Sheet, "kd:TextBulletListSquare24:indigo"),
        new("explain", AssistantAnswer.Sheet, "kd:Lightbulb24:gold"),
        new("reply", AssistantAnswer.Sheet, "kd:ArrowReply24:teal"),
        new("read", null, "kd:Megaphone24:purple"),
        new("screen", AssistantAnswer.Sheet, "kd:ScanText24:orange", Screen: true),
    ];

    /// <summary>Adds the menu, its commands and the one that opens it to <paramref name="config"/>; the menu.</summary>
    /// <param name="assistant">Who answers (K-Desktop by default); each command can be changed afterwards.</param>
    public static (QuickMenu Menu, Command Opener) AddTo(AppConfig config, string assistant)
    {
        var menu = new QuickMenu { Name = Free(config.QuickMenus.Select(m => m.Name), Loc.T("aiMenu.name")) };
        foreach (var entry in Entries)
        {
            var command = new Command { Name = Free(config.Commands.Select(c => c.Name), Loc.T($"aiMenu.{entry.Key}")), Icon = entry.Icon };
            if (entry.Screen) command.Actions.Add(new ScreenTextAction { Area = CaptureArea.Region, ShowOsd = false });
            command.Actions.Add(entry.Answer is { } answer
                ? new AssistantAskAction { Assistant = assistant, Text = Loc.T($"aiMenu.{entry.Key}.ask"), Answer = answer }
                : new AssistantSayAction { Assistant = assistant, Text = Loc.T($"aiMenu.{entry.Key}.say") });
            config.Commands.Add(command);
            menu.Items.Add(QuickMenuOps.NewCommandItem(command));
        }
        config.QuickMenus.Add(menu);
        var opener = new Command { Name = Free(config.Commands.Select(c => c.Name), Loc.T("aiMenu.opener")) };
        opener.Actions.Add(new OpenQuickMenuAction { MenuId = menu.Id, MenuName = menu.Name });
        config.Commands.Add(opener);
        return (menu, opener);
    }

    /// <summary><paramref name="name"/>, or with a number after it when another one has it already.</summary>
    private static string Free(IEnumerable<string> taken, string name)
    {
        var names = taken.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var free = name;
        for (var i = 2; names.Contains(free); i++) free = $"{name} {i}";
        return free;
    }
}
