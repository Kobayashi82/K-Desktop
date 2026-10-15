using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// http.sys receives the connections in the System process, so the program rule of <see cref="FirewallRule"/> does
/// not cover them and Windows never asks: the web server gets its own inbound rule for its TCP port. Local network
/// only; only the proxy when "only from the proxy" is set.
/// </summary>
internal static class WebFirewall
{
    private static string RuleName => $"{AppPaths.VersionName} (web{AppPaths.InstanceSuffix})";

    private static WebServerOptions? _applied;

    public static void Ensure(WebServerOptions options)
    {
        if (!ProcessLauncher.IsElevated || !options.AllowRemote) return;
        var key = options with { BasePath = string.Empty };
        if (key == _applied) return;
        _applied = key;

        var remoteIp = options.ProxyOnly && !string.IsNullOrWhiteSpace(options.TrustedProxy) ? options.TrustedProxy.Trim() : "localsubnet";
        _ = Task.Run(() =>
        {
            try
            {
                StartupTask.Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName}\"");
                var code = StartupTask.Run("netsh", $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
                                                    $"protocol=TCP localport={options.Port} remoteip={remoteIp} enable=yes profile=any");
                if (code == 0)
                {
                    Log.Info($"Web: regla del firewall para el puerto TCP {options.Port} ({remoteIp})");
                    return;
                }
                Log.Warn("Web: no se pudo crear la regla del firewall");
            }
            catch (Exception ex) { Log.Warn("Web: error creando la regla del firewall: " + ex.Message); }
            _applied = null;
        });
    }
}
