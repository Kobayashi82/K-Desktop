using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// http.sys receives the connections in the System process, so the program rule of <see cref="FirewallRule"/> does
/// not cover them and Windows never asks: the web server gets its own inbound rule for its TCP port. Local network
/// and private addresses only: a VPN tunnel such as WireGuard is often a /32, so its other end (the proxy on a VPS)
/// is not in "localsubnet".
/// </summary>
internal static class WebFirewall
{
    private static string RuleName => $"{AppPaths.StableName} (web{AppPaths.InstanceSuffix})";

    private const string RemoteIps = "localsubnet,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,100.64.0.0/10,fc00::/7";

    private static WebServerOptions? _applied;

    public static void Ensure(WebServerOptions options)
    {
        if (!ProcessLauncher.IsElevated) return;
        var key = options with { BasePath = string.Empty };
        if (key == _applied) return;
        _applied = key;

        _ = Task.Run(() =>
        {
            try
            {
                StartupTask.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"");
                var code = StartupTask.Run("netsh", $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
                                                    $"protocol=TCP localport={options.Port} remoteip={RemoteIps} enable=yes profile=any");
                if (code == 0)
                {
                    Log.Info($"Web: regla del firewall para el puerto TCP {options.Port} ({RemoteIps})");
                    return;
                }
                Log.Warn("Web: no se pudo crear la regla del firewall");
            }
            catch (Exception ex) { Log.Warn("Web: error creando la regla del firewall: " + ex.Message); }
            _applied = null;
        });
    }
}
