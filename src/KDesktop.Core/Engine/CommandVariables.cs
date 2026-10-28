using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace KDesktop.Core.Engine;

/// <summary>
/// The variables of one run of a command (5.0.338): {resultado}, what the last action that gives something gave (the text
/// read from the screen, a web answer, a script's output…), the ones saved with a name («Guardar en variable») and
/// {seleccion}, taken once. The commands it calls and the commands of the same event share them. It flows with the run
/// (<see cref="AsyncLocal{T}"/>), so <see cref="TextVariables.Expand"/> finds it from any action.
/// </summary>
public sealed partial class CommandVariables
{
    private static readonly AsyncLocal<CommandVariables?> CurrentRun = new();

    private readonly ConcurrentDictionary<string, string> _named = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The run in progress on this flow (null: outside a command, like an action's «Probar»).</summary>
    public static CommandVariables? Current => CurrentRun.Value;

    /// <summary>Makes <paramref name="variables"/> the current ones for the rest of this flow (an async method restores it on return).</summary>
    internal static void Use(CommandVariables? variables) => CurrentRun.Value = variables;

    /// <summary>What the last action that gives something gave («» before any).</summary>
    public string Result { get; private set; } = string.Empty;

    /// <summary>What was selected when the first action that uses it ran; null until then.</summary>
    public string? Selection { get; internal set; }

    /// <summary>The ones saved with a name, as they are now.</summary>
    public IReadOnlyDictionary<string, string> Named => _named;

    /// <summary>Leaves <paramref name="value"/> in {resultado} and, with a <paramref name="name"/>, in {name} too.</summary>
    public void SetResult(string value, string? name = null)
    {
        Result = value;
        if (NameOf(name) is { } key) _named[key] = value;
    }

    public bool TryGet(string name, out string value) => _named.TryGetValue(name, out value!);

    /// <summary>A usable variable name (letters, digits and «_», not starting with a digit), without braces; null if it is not one.</summary>
    public static string? NameOf(string? name)
    {
        var key = (name ?? string.Empty).Trim().Trim('{', '}').Trim();
        return key.Length > 0 && NamePattern().IsMatch(key) ? key : null;
    }

    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{N}_]*$")]
    private static partial Regex NamePattern();
}
