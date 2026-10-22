using System.Globalization;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «¿cuánto disco me queda?», «¿qué está consumiendo?», «¿a qué temperatura va la gráfica?» (stage 8, <c>estado_pc</c>):
/// what Task Manager shows, measured now (<see cref="PcStatus"/>), in figures for the model.
/// </summary>
public sealed class PcStatusTool(Func<CancellationToken, Task<PcStatus>>? measure = null) : ITool
{
    public string Name => "pc_status";

    public string Description => "How the PC is doing right now: processor, memory (RAM), graphics card (use, video memory, temperature), free disk space, battery, network, and which programs use the most. Use it before end_process to find what is hogging the PC.";

    public static readonly IReadOnlyList<string> Parts = ["overview", "cpu", "memory", "gpu", "disks", "battery", "network", "programs"];

    public string ParametersSchemaJson { get; } =
        $$$$"""{"type":"object","properties":{"about":{"type":"string","enum":[{{{{string.Join(",", Parts.Select(p => $"\"{p}\""))}}}}],"description":"What to look at (overview: a bit of everything)"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.pc_status.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var about = arguments.GetString("about")?.Trim().ToLowerInvariant() switch
        {
            { } given when Parts.Contains(given) => given,
            "ram" => "memory",
            "disk" or "storage" => "disks",
            "processes" or "apps" => "programs",
            "temperature" or "graphics" => "gpu",
            "processor" => "cpu",
            _ => "overview",
        };
        var status = await (measure ?? (token => Task.Run(() => PcStatus.MeasureAsync(cancellationToken: token), token)))(cancellationToken).ConfigureAwait(false);
        return new ToolResult(Describe(status, about)) { Done = Loc.T("tool.pc_status.done") };
    }

    /// <summary>The figures for the model, in English and with invariant numbers.</summary>
    public static string Describe(PcStatus s, string about)
    {
        var all = about == "overview";
        var text = new StringBuilder();
        if (all || about == "cpu")
            Line($"Processor: {s.Cpu}% in use{(s.CpuName is { } name ? $" ({name}, {s.Cores} threads)" : string.Empty)}. Its temperature cannot be read.");
        if (all || about == "memory")
            Line($"Memory (RAM): {Gb(s.MemoryUsed)} of {Gb(s.MemoryTotal)} in use ({Pct(s.MemoryUsed, s.MemoryTotal)}), {Gb(s.MemoryTotal - s.MemoryUsed)} free.");
        if (all || about == "gpu")
        {
            if (s.Gpus.Count == 0) Line("Graphics card: it cannot be read.");
            foreach (var gpu in s.Gpus)
            {
                var parts = new List<string>();
                if (gpu.Load is { } load) parts.Add($"{load}% in use");
                if (gpu.MemoryUsed is { } used && gpu.MemoryTotal is { } total) parts.Add($"video memory {Gb(used)} of {Gb(total)} in use");
                else if (gpu.MemoryTotal is { } only) parts.Add($"{Gb(only)} of video memory");
                parts.Add(gpu.Temperature is { } degrees ? $"{degrees} °C" : "temperature unknown");
                Line($"Graphics card {gpu.Name}: {string.Join(", ", parts)}.");
            }
        }
        if (all || about == "disks")
        {
            var disks = s.Disks.Select(d => $"{d.Letter}{(d.Label.Length > 0 ? $" ({d.Label})" : string.Empty)} {Gb(d.Free)} free of {Gb(d.Total)}");
            Line(s.Disks.Count == 0 ? "Disks: none could be read." : "Disks: " + string.Join("; ", disks) + ".");
        }
        if (all && s.Battery is not null || about == "battery")
        {
            Line(s.Battery is not { } b ? "Battery: this PC has none (it is plugged in)."
                : $"Battery: {b.Percent}%, {(b.Charging ? "charging" : b.PluggedIn ? "plugged in" : "on battery")}{(b.Minutes is { } m ? $", about {m / 60} h {m % 60} min left" : string.Empty)}.");
        }
        if (all || about == "network")
        {
            var networks = s.Network.Networks.Count == 0 ? "not connected to any network" : "connected to " + string.Join(", ", s.Network.Networks.Select(n => $"\"{n}\""));
            Line($"Network: {networks}; now {Speed(s.Network.DownPerSecond)} down and {Speed(s.Network.UpPerSecond)} up"
                 + (s.Network.Addresses.Count > 0 ? $"; local IP {s.Network.Addresses[0]}." : "."));
        }
        if (all) Line($"Turned on for {Uptime(s.UpTime)}.");
        if (all || about is "programs" or "cpu")
        {
            var busy = s.Programs.Where(p => p.Cpu >= 0.5).OrderByDescending(p => p.Cpu).Take(all ? 3 : 6).ToList();
            Line(busy.Count == 0 ? "No program is using the processor noticeably."
                : "Using the most processor: " + string.Join(", ", busy.Select(p => $"{Label(p)} {p.Cpu.ToString("0.#", CultureInfo.InvariantCulture)}%")) + ".");
        }
        if (all || about is "programs" or "memory")
        {
            var big = s.Programs.OrderByDescending(p => p.Memory).Take(all ? 3 : 6);
            Line("Using the most memory: " + string.Join(", ", big.Select(p => $"{Label(p)} {Gb(p.Memory)}")) + ".");
        }
        return text.ToString().TrimEnd();

        void Line(string line) => text.AppendLine(line);
    }

    /// <summary>«Google Chrome (chrome)», or the process name alone.</summary>
    private static string Label(ProgramUsage program)
    {
        var description = program.Pids.Count > 0 ? AppFinder.DescriptionOf(program.Pids[0])?.Trim() : null;
        return description is { Length: > 0 } d && !d.Equals(program.Name, StringComparison.OrdinalIgnoreCase) ? $"{d} ({program.Name})" : program.Name;
    }

    private static string Gb(long bytes) => bytes >= 1L << 40
        ? (bytes / (double)(1L << 40)).ToString("0.#", CultureInfo.InvariantCulture) + " TB"
        : bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("0.#", CultureInfo.InvariantCulture) + " GB"
        : (bytes / (double)(1L << 20)).ToString("0", CultureInfo.InvariantCulture) + " MB";

    private static string Pct(long part, long whole) => whole <= 0 ? "?" : Math.Round(part * 100.0 / whole).ToString(CultureInfo.InvariantCulture) + "%";

    private static string Speed(long bytesPerSecond) => bytesPerSecond switch
    {
        >= 1 << 20 => (bytesPerSecond / (double)(1 << 20)).ToString("0.#", CultureInfo.InvariantCulture) + " MB/s",
        >= 1 << 10 => (bytesPerSecond / (double)(1 << 10)).ToString("0", CultureInfo.InvariantCulture) + " KB/s",
        _ => "almost nothing",
    };

    private static string Uptime(TimeSpan up) => up.TotalDays >= 1
        ? $"{(int)up.TotalDays} days {up.Hours} h"
        : $"{(int)up.TotalHours} h {up.Minutes} min";
}

/// <summary>
/// «cierra lo que se ha colgado», «mata el proceso de Fall Guys» (stage 8, <c>terminar_proceso</c>): K-Desktop asks it to
/// close and, if it is still there after a moment, ends it by force (<see cref="AssistantActs"/>, op <c>kill</c>). It
/// always asks first (🔴), like K-Desktop's «Cerrar a la fuerza».
/// </summary>
public sealed class EndProcessTool(ICommandBridge bridge) : ITool
{
    public string Name => "end_process";

    public string Description => "Ends a program by force when it hangs or does not close: it is asked to close, and killed if it does not. Unsaved work is lost. Give the program as the user names it or as pc_status lists it; leave app empty for the program that is not responding (or else the one in front). For a normal close use control_app quit.";

    public string ParametersSchemaJson => ToolText.Optional(("app", "string", "The program (empty: the one that hangs, or the one in front)"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    // K-Desktop asks (killing is «always ask» in ActionRisk): asking here too would ask twice.
    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("act.kill", NameOf(arguments.GetString("app")));

    private static string NameOf(string? app) => ToolText.Clean(app) is { Length: > 0 } name ? name : Loc.T("act.hung");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var app = arguments.GetString("app")?.Trim() ?? string.Empty;
        var end = await bridge.ActAsync("kill", app, null, context.Assistant, cancellationToken).ConfigureAwait(false);
        if (end is null) return ToolResult.Fail("K-Desktop did not answer.");
        var name = end.Name is { Length: > 0 } found ? found : NameOf(app);
        return end.Status switch
        {
            "ok" => new ToolResult($"Done: {name} has been ended.") { Done = Loc.T("act.kill", name), Reply = Loc.T("act.kill.reply", name), Acted = true },
            "declined" => new ToolResult("Not done: the user said no to it. Tell them you did not do it.")
            {
                Done = Loc.T("tool.run_command.declined", Loc.T("act.kill", name)),
                Reply = Loc.T("tool.run_command.replyDeclined", Loc.T("act.kill", name)),
                Failed = true,
            },
            _ when end.Reason is { } reason => new ToolResult($"Not done: {reason switch
            {
                "noProcess" => $"\"{app}\" is not running",
                _ => "that is part of Windows or K-Desktop and must not be ended",
            }}. Say so.")
            {
                Done = Loc.T("act.reason." + reason, name),
                Reply = Loc.T("act.reason." + reason, name),
                Failed = true,
            },
            _ => new ToolResult("Failed. " + string.Join(" ", end.Messages)) { Done = Loc.T("tool.run_command.failed", Loc.T("act.kill", name)), Failed = true },
        };
    }
}
