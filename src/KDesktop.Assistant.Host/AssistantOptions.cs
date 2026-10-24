namespace KDesktop.Assistant.Host;

/// <summary>
/// Command line of the assistant process:
/// <c>KDesktop.exe --assistant [--pipe name] [--data folder]</c> (started by K-Desktop),
/// <c>KDesktop.exe --assistant-dev folder</c> (an assistant from its build folder, reloaded when it is rebuilt) and
/// <c>KDesktop.exe --assistant --exit</c> (closes the one of this data folder) and
/// <c>KDesktop.exe --assistant-showcase package [--pipe name] [--stage-data folder] [--record folder [--manifest assistant.json] [--language xx]]</c>
/// (one assistant on the showcase's stage, phase 16: for the «Probar» tab of its card, or to record the clips of its showcase).
/// </summary>
public sealed record AssistantOptions
{
    public const string Argument = "--assistant";
    public const string DevArgument = "--assistant-dev";
    public const string ShowcaseArgument = "--assistant-showcase";

    /// <summary>The package to put on the showcase's stage (<c>--assistant-showcase</c>), or null.</summary>
    public string? Showcase { get; init; }

    /// <summary>The assistant's data folder on the stage (thrown away when it ends; a temporary one when not given).</summary>
    public string? StageData { get; init; }

    /// <summary>Where to record the clips of its showcase (<c>--record</c>; without a pipe), or null.</summary>
    public string? Record { get; init; }

    /// <summary>The assistant.json whose <c>showcase</c> is rewritten with the clips recorded (<c>--manifest</c>), or null.</summary>
    public string? Manifest { get; init; }

    /// <summary>
    /// The language the clips are recorded in (<c>--language</c>: the bubble's text and the stage's windows); English when not
    /// given, since a catalog is for everyone.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>Name of K-Desktop's pipe (null: run alone, as with <c>--assistant-dev</c> and nothing to talk to).</summary>
    public string? Pipe { get; init; }

    /// <summary>Build folder of an assistant under development (<c>--assistant-dev</c>).</summary>
    public string? DevFolder { get; init; }

    /// <summary>Close the running assistant process of this data folder and leave.</summary>
    public bool Exit { get; init; }

    /// <summary>Where to show the built-in probe circle (<c>--assistant-probe x,y</c>, diagnostics); null: none.</summary>
    public System.Windows.Point? Probe { get; init; }

    /// <summary>Stay elevated if started so (tests). Normally an elevated start relaunches itself as the user.</summary>
    public bool AllowElevated { get; init; }

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Any(a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase) || a.Equals(DevArgument, StringComparison.OrdinalIgnoreCase) ||
                      a.Equals(ShowcaseArgument, StringComparison.OrdinalIgnoreCase));

    public static AssistantOptions Parse(IReadOnlyList<string> args) => new()
    {
        Pipe = ValueOf(args, "--pipe"),
        DevFolder = ValueOf(args, DevArgument) is { } dev ? Path.GetFullPath(dev.Trim('"')) : null,
        Showcase = ValueOf(args, ShowcaseArgument) is { } showcase ? Path.GetFullPath(showcase.Trim('"')) : null,
        StageData = ValueOf(args, "--stage-data") is { } stageData ? Path.GetFullPath(stageData.Trim('"')) : null,
        Record = ValueOf(args, "--record") is { } record ? Path.GetFullPath(record.Trim('"')) : null,
        Manifest = ValueOf(args, "--manifest") is { } manifest ? Path.GetFullPath(manifest.Trim('"')) : null,
        Language = ValueOf(args, "--language")?.Trim('"'),
        Exit = args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase)),
        AllowElevated = args.Any(a => a.Equals("--allow-elevated", StringComparison.OrdinalIgnoreCase)),
        Probe = ValueOf(args, "--assistant-probe") is { } probe && probe.Split(',') is [var x, var y] &&
                int.TryParse(x, System.Globalization.CultureInfo.InvariantCulture, out var px) && int.TryParse(y, System.Globalization.CultureInfo.InvariantCulture, out var py)
            ? new System.Windows.Point(px, py) : null,
    };

    private static string? ValueOf(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
