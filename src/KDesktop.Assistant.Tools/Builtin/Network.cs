using System.Text;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «¿Hay internet?», «¿responde el NAS?», «¿cuál es mi IP?», «¿a qué wifi estoy conectado?» (B6, 5.0.349): measured now in
/// the assistant process by <see cref="NetworkCheck"/> (pings like «Equipo accesible», Windows' connectivity test, the
/// WLAN service), told to the model in figures. When the internet fails it says where (no network, the router, DNS, a
/// sign-in page) so the answer can be useful.
/// </summary>
public sealed class NetworkCheckTool(Func<bool, bool, string?, CancellationToken, Task<NetworkReport>>? check = null) : ITool
{
    public string Name => "network_check";

    public string Description => "Checks the network now: whether there is internet (and why not: no network, router, DNS, a sign-in page), whether a machine or website answers (ping, its web, a port), the local and public IP, and the Wi-Fi it is connected to with its signal.";

    public static readonly IReadOnlyList<string> Parts = ["internet", "host", "ip", "wifi"];

    public string ParametersSchemaJson { get; } =
        $$$$"""{"type":"object","properties":{"about":{"type":"string","enum":[{{{{string.Join(",", Parts.Select(p => $"\"{p}\""))}}}}],"description":"What to check (host: whether host answers)"},"host":{"type":"string","description":"For host: the machine, website or address as the user says it (\"google.com\", \"192.168.1.20\", \"NAS\", \"router\", \"server:22\")"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => HostOf(arguments) is { } host
        ? Loc.T("tool.network_check.intentHost", host)
        : Loc.T("tool.network_check.intent");

    private static string? HostOf(JsonElement arguments) => ToolText.Clean(arguments.GetString("host")) is { Length: > 0 } host ? host : null;

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var host = HostOf(arguments);
        var about = arguments.GetString("about")?.Trim().ToLowerInvariant() switch
        {
            { } given when Parts.Contains(given) => given,
            "ping" or "reachable" or "website" => "host",
            "public" or "address" or "public_ip" or "local_ip" => "ip",
            "wireless" or "wi-fi" or "signal" => "wifi",
            _ => host is null ? "internet" : "host",
        };
        if (about == "host" && host is null) about = "internet";
        var report = await (check ?? NetworkCheck.RunAsync)(about is "internet" or "wifi", about == "ip", about == "host" ? host : null, cancellationToken).ConfigureAwait(false);
        return new ToolResult(Describe(report, about, host)) { Done = about == "host" ? Loc.T("tool.network_check.doneHost", host!) : Loc.T("tool.network_check.done") };
    }

    /// <summary>What was found, for the model in English.</summary>
    public static string Describe(NetworkReport r, string about, string? asked = null)
    {
        var text = new StringBuilder();
        var link = r.Link;
        var named = link.Networks.Select(n => $"\"{n}\"").ToList();
        if (link.Wifi is { } wifi)
        {
            var name = wifi.Name ?? (link.Networks.Count > 0 ? link.Networks[0] : null);
            var details = new List<string>();
            if (wifi.Signal is { } signal) details.Add($"signal {signal}% ({signal switch { >= 75 => "good", >= 45 => "fair", _ => "weak" }})");
            if (wifi.Megabits is { } speed) details.Add($"{speed} Mb/s link");
            Line($"Connected by Wi-Fi{(name is null ? string.Empty : $" to \"{name}\"")}{(details.Count > 0 ? " (" + string.Join(", ", details) + ")" : string.Empty)}{(link.Cable ? ", and also by cable" : string.Empty)}.");
        }
        else if (link.Cable) Line($"Connected by cable{(named.Count > 0 ? " to " + string.Join(", ", named) : string.Empty)}, not by Wi-Fi.");
        else if (named.Count > 0) Line("Connected to " + string.Join(", ", named) + ".");
        else Line("Not connected to any network.");
        if (link.Addresses.Count > 0 && (about is "ip" or "wifi" or "internet"))
            Line($"Local IP: {link.Addresses[0]}{(link.Addresses.Count > 1 ? $" (also {string.Join(", ", link.Addresses.Skip(1).Take(3))})" : string.Empty)}{(link.Router is { } router ? $"; router {router}" : string.Empty)}.");

        if (r.Internet is { } internet)
        {
            var ping = internet.Milliseconds is { } ms ? $" (ping {ms} ms)" : string.Empty;
            Line(internet.State switch
            {
                InternetState.Online => $"Internet works{ping}.",
                InternetState.NoDns => $"Internet answers{ping} but names do not resolve (DNS): web pages will not open. Restarting the router, or using DNS 1.1.1.1 or 8.8.8.8, usually fixes it.",
                InternetState.Portal => "A sign-in page (like in a hotel or a train) is in the way: opening the browser shows it.",
                InternetState.NoNetwork => "There is no internet: the PC is not connected to any network.",
                _ => internet.RouterAnswers switch
                {
                    true => "There is no internet: the router answers, so the problem is the router's line or the provider. Restarting the router may help.",
                    false => "There is no internet and the router does not answer either: check the cable or Wi-Fi, or restart the router.",
                    _ => "There is no internet.",
                },
            });
        }

        if (r.PublicIpAsked)
            Line(r.PublicIp is { } ip ? $"Public IP (what the internet sees): {ip}." : "The public IP could not be found (no internet?).");

        if (about == "host")
        {
            if (r.Host is not { } h) Line($"\"{asked}\" is not a machine, website or address to check.");
            else if (!h.Found) Line($"\"{h.Host}\" was not found: no machine or website has that name (or names do not resolve).");
            else
            {
                var who = h.Address is { } address && address != h.Host ? $"{h.Host} ({address})" : h.Host;
                var parts = new List<string> { h.PingMilliseconds is { } ms ? $"answers ping in {ms} ms" : "does not answer ping" };
                if (h.Port is { } port && h.PortOpen is { } open) parts.Add($"port {port} is {(open ? "open" : "closed or filtered")}");
                if (h.WebStatus is { } status) parts.Add($"its website answers (HTTP {status}{(status >= 500 ? ", a server error" : string.Empty)})");
                else if (h.PingMilliseconds is null && h.Port is null) parts.Add("its website does not answer either");
                Line($"{who} {string.Join("; ", parts)}.");
                if (h.PingMilliseconds is null && h.WebStatus is null && h.PortOpen != true)
                    Line("It may be off or disconnected (some machines ignore pings).");
            }
        }
        return text.ToString().TrimEnd();

        void Line(string line) => text.AppendLine(line);
    }
}
