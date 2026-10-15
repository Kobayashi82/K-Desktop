using System.Net;

namespace KDesktop.Core.Remote.Web;

public sealed partial class WebServer
{
    private async Task HandleApiAsync(WebRequest request, HttpListenerContext context)
    {
        var response = context.Response;
        switch (request.Method, request.Path)
        {
            case ("GET", "/api/ping"):
                await WriteJsonAsync(response, new { app = AppPaths.VersionName, machine = Environment.MachineName }).ConfigureAwait(false);
                break;
            default:
                await WriteJsonAsync(response, new { error = "notFound" }, 404).ConfigureAwait(false);
                break;
        }
    }
}
