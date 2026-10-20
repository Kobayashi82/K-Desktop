namespace KDesktop.Assistant.Host;

/// <summary>
/// Command line of the assistant process:
/// <c>KDesktop.exe --assistant [--pipe name] [--data folder]</c> (started by K-Desktop),
/// <c>KDesktop.exe --assistant-dev folder</c> (an assistant from its build folder, reloaded when it is rebuilt) and
/// <c>KDesktop.exe --assistant --exit</c> (closes the one of this data folder).
/// </summary>
public sealed record AssistantOptions
{
    public const string Argument = "--assistant";
    public const string DevArgument = "--assistant-dev";

    /// <summary>Name of K-Desktop's pipe (null: run alone, as with <c>--assistant-dev</c> and nothing to talk to).</summary>
    public string? Pipe { get; init; }

    /// <summary>Build folder of an assistant under development (<c>--assistant-dev</c>).</summary>
    public string? DevFolder { get; init; }

    /// <summary>Close the running assistant process of this data folder and leave.</summary>
    public bool Exit { get; init; }

    /// <summary>Stay elevated if started so (tests). Normally an elevated start relaunches itself as the user.</summary>
    public bool AllowElevated { get; init; }

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Any(a => a.Equals(Argument, StringComparison.OrdinalIgnoreCase) || a.Equals(DevArgument, StringComparison.OrdinalIgnoreCase));

    public static AssistantOptions Parse(IReadOnlyList<string> args) => new()
    {
        Pipe = ValueOf(args, "--pipe"),
        DevFolder = ValueOf(args, DevArgument) is { } dev ? Path.GetFullPath(dev.Trim('"')) : null,
        Exit = args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase)),
        AllowElevated = args.Any(a => a.Equals("--allow-elevated", StringComparison.OrdinalIgnoreCase)),
    };

    private static string? ValueOf(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
