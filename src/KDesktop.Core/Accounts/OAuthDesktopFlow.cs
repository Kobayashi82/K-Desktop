using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace KDesktop.Core.Accounts;

/// <summary>Where a provider signs in and what it is asked for.</summary>
public sealed record OAuthProvider(AccountProvider Provider, string AuthorizeUrl, string TokenUrl, string? RevokeUrl, string RedirectHost,
    IReadOnlyList<string> BaseScopes)
{
    /// <summary>K-Desktop's application registered in Microsoft Entra ID (tenant «common»: personal and work accounts). Empty until it is registered: then the user's own is needed.</summary>
    public const string MicrosoftBuiltInClientId = "";

    public static OAuthProvider Google { get; } = new(AccountProvider.Google,
        "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", "https://oauth2.googleapis.com/revoke",
        "127.0.0.1", ["openid", "email", "profile"]);

    public static OAuthProvider Microsoft { get; } = new(AccountProvider.Microsoft,
        "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token", null,
        "localhost", ["openid", "email", "profile", "offline_access", "User.Read"]);

    public static OAuthProvider Of(AccountProvider provider) => provider == AccountProvider.Google ? Google : Microsoft;

    /// <summary>The permissions for what an account is used for.</summary>
    public IReadOnlyList<string> ScopesFor(AccountUse uses)
    {
        var scopes = new List<string>(BaseScopes);
        if (Provider == AccountProvider.Google)
        {
            if (uses.HasFlag(AccountUse.CalendarWrite)) scopes.AddRange(["https://www.googleapis.com/auth/calendar.events", "https://www.googleapis.com/auth/calendar.calendarlist.readonly"]);
            else if (uses.HasFlag(AccountUse.CalendarRead)) scopes.Add("https://www.googleapis.com/auth/calendar.readonly");
            if (uses.HasFlag(AccountUse.Mail)) scopes.Add("https://mail.google.com/");
        }
        else
        {
            if (uses.HasFlag(AccountUse.CalendarWrite)) scopes.Add("Calendars.ReadWrite");
            else if (uses.HasFlag(AccountUse.CalendarRead)) scopes.Add("Calendars.Read");
            if (uses.HasFlag(AccountUse.Mail)) scopes.AddRange([MicrosoftImap, MicrosoftSmtp]);
        }
        return scopes;
    }

    public const string MicrosoftImap = "https://outlook.office.com/IMAP.AccessAsUser.All";
    public const string MicrosoftSmtp = "https://outlook.office.com/SMTP.Send";
}

/// <summary>The tokens a server gave.</summary>
public sealed record OAuthTokens(string AccessToken, DateTimeOffset Expires, string? RefreshToken, string? IdToken, IReadOnlyList<string> Scopes);

/// <summary>
/// Signing in from a desktop program (RFC 8252): PKCE, the system's browser and the answer back on
/// <c>http://127.0.0.1:{free port}</c> (Microsoft: <c>localhost</c>), with a page «Ya puedes cerrar esta pestaña». It can
/// be cancelled and gives up after a while. No password ever goes through K-Desktop.
/// </summary>
public sealed class OAuthDesktopFlow(HttpClient http)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>The page shown when it is done (set by K-Desktop, in the user's language).</summary>
    public static string DonePage { get; set; } = "<!doctype html><meta charset=utf-8><title>K-Desktop</title><body style=\"font-family:Segoe UI,sans-serif;text-align:center;margin-top:20vh\"><h2>K-Desktop</h2><p>{0}</p>";

    public sealed record Pkce(string Verifier, string Challenge);

    public static Pkce NewPkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new Pkce(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The address the browser opens.</summary>
    public static string AuthorizeUrl(OAuthProvider provider, string clientId, string redirect, IEnumerable<string> scopes, Pkce pkce, string state, string? loginHint)
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        q["client_id"] = clientId;
        q["response_type"] = "code";
        q["redirect_uri"] = redirect;
        q["scope"] = string.Join(' ', scopes);
        q["state"] = state;
        q["code_challenge"] = pkce.Challenge;
        q["code_challenge_method"] = "S256";
        if (provider.Provider == AccountProvider.Google)
        {
            // A refresh token every time (otherwise only the first consent gives one) and the permissions it already had.
            q["access_type"] = "offline";
            q["prompt"] = "consent";
            q["include_granted_scopes"] = "true";
        }
        else q["prompt"] = "select_account";
        if (!string.IsNullOrWhiteSpace(loginHint)) q["login_hint"] = loginHint;
        return provider.AuthorizeUrl + "?" + q;
    }

    /// <summary>
    /// Signs in: opens <paramref name="openBrowser"/> and waits for the answer. Returns the tokens (the code already
    /// exchanged). Throws <see cref="AccountException"/> when the user says no or it takes too long.
    /// </summary>
    /// <param name="tokenScopes">What the code is exchanged for (Microsoft: one resource at a time; null: all).</param>
    public async Task<OAuthTokens> SignInAsync(OAuthProvider provider, string clientId, string? clientSecret, IReadOnlyList<string> scopes,
        Action<string> openBrowser, string? loginHint, IReadOnlyList<string>? tokenScopes, CancellationToken cancel)
    {
        using var v4 = new TcpListener(IPAddress.Loopback, 0);
        v4.Start();
        var port = ((IPEndPoint)v4.LocalEndpoint).Port;
        // «localhost» may be ::1 in the browser: listen there too, on the same port.
        TcpListener? v6 = null;
        if (Socket.OSSupportsIPv6)
        {
            try
            {
                v6 = new TcpListener(IPAddress.IPv6Loopback, port);
                v6.Start();
            }
            catch (SocketException)
            {
                v6 = null;
            }
        }
        try
        {
            var redirect = $"http://{provider.RedirectHost}:{port}/";
            var pkce = NewPkce();
            var state = Base64Url(RandomNumberGenerator.GetBytes(16));
            openBrowser(AuthorizeUrl(provider, clientId, redirect, scopes, pkce, state, loginHint));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(Timeout);
            var answer = await WaitForAnswerAsync(v4, v6, state, timeout.Token).ConfigureAwait(false);
            if (answer.Error is { } error)
                throw new AccountException(error is "access_denied" ? Localization.Loc.T("accounts.error.denied") : Localization.Loc.T("accounts.error.server", answer.Description ?? error));
            return await RedeemAsync(provider, clientId, clientSecret, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = answer.Code!,
                ["redirect_uri"] = redirect,
                ["code_verifier"] = pkce.Verifier,
            }, tokenScopes, cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new AccountException(Localization.Loc.T("accounts.error.timeout"));
        }
        finally
        {
            v4.Stop();
            v6?.Stop();
        }
    }

    public sealed record LoopbackAnswer(string? Code, string? Error, string? Description);

    private static async Task<LoopbackAnswer> WaitForAnswerAsync(TcpListener v4, TcpListener? v6, string state, CancellationToken cancel)
    {
        while (true)
        {
            var accepts = new List<Task<TcpClient>> { v4.AcceptTcpClientAsync(cancel).AsTask() };
            if (v6 is not null) accepts.Add(v6.AcceptTcpClientAsync(cancel).AsTask());
            var first = await Task.WhenAny(accepts).ConfigureAwait(false);
            using var client = await first.ConfigureAwait(false);
            client.ReceiveTimeout = 10_000;
            var stream = client.GetStream();
            var line = await ReadRequestLineAsync(stream, cancel).ConfigureAwait(false);
            var answer = ParseRequest(line, state);
            var body = answer is null ? "<!doctype html><title>K-Desktop</title>" : DonePage.Replace("{0}",
                WebUtility.HtmlEncode(answer.Error is null ? Localization.Loc.T("accounts.browserDone") : Localization.Loc.T("accounts.browserFailed")), StringComparison.Ordinal);
            var bytes = Encoding.UTF8.GetBytes(body);
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {(answer is null ? "404 Not Found" : "200 OK")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
            await stream.WriteAsync(head, cancel).ConfigureAwait(false);
            await stream.WriteAsync(bytes, cancel).ConfigureAwait(false);
            await stream.FlushAsync(cancel).ConfigureAwait(false);
            if (answer is not null) return answer;
        }
    }

    private static async Task<string> ReadRequestLineAsync(Stream stream, CancellationToken cancel)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancel).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var end = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (end >= 0) return text[..end];
        }
        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    /// <summary>
    /// «GET /?code=…&amp;state=… HTTP/1.1» → the answer; null for anything else (the favicon, a request of another page).
    /// A wrong state is an error (another page trying its luck).
    /// </summary>
    public static LoopbackAnswer? ParseRequest(string requestLine, string state)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2 || parts[0] != "GET") return null;
        var target = parts[1];
        var question = target.IndexOf('?', StringComparison.Ordinal);
        if (question < 0 || target[..question] is not ("/" or "")) return null;
        var q = HttpUtility.ParseQueryString(target[(question + 1)..]);
        if (q["error"] is { } error) return new LoopbackAnswer(null, error, q["error_description"]);
        if (q["code"] is not { Length: > 0 } code) return null;
        if (q["state"] != state) return new LoopbackAnswer(null, "state", "state mismatch");
        return new LoopbackAnswer(code, null, null);
    }

    /// <summary>A new access token from the refresh token (for <paramref name="scopes"/>: Microsoft gives one resource at a time).</summary>
    public Task<OAuthTokens> RefreshAsync(OAuthProvider provider, string clientId, string? clientSecret, string refreshToken, IReadOnlyList<string>? scopes, CancellationToken cancel) =>
        RedeemAsync(provider, clientId, clientSecret, new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, scopes, cancel);

    private async Task<OAuthTokens> RedeemAsync(OAuthProvider provider, string clientId, string? clientSecret, Dictionary<string, string> form,
        IReadOnlyList<string>? scopes, CancellationToken cancel)
    {
        form["client_id"] = clientId;
        if (!string.IsNullOrEmpty(clientSecret)) form["client_secret"] = clientSecret;
        if (scopes is { Count: > 0 }) form["scope"] = string.Join(' ', scopes);
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(provider.TokenUrl, content, cancel).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        return ParseTokens(json, response.StatusCode, DateTimeOffset.UtcNow);
    }

    /// <summary>The token endpoint's answer. <c>invalid_grant</c> (revoked, expired, password changed) is <see cref="AccountDisconnectedException"/>.</summary>
    public static OAuthTokens ParseTokens(string json, HttpStatusCode status, DateTimeOffset now)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { throw new AccountException(Localization.Loc.T("accounts.error.server", ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture))); }
        using (doc)
        {
            var root = doc.RootElement;
            string? Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (Get("error") is { } error)
            {
                var description = Get("error_description") ?? error;
                if (error is "invalid_grant" or "interaction_required" or "consent_required") throw new AccountDisconnectedException(Localization.Loc.T("accounts.error.reconnect", description));
                throw new AccountException(Localization.Loc.T("accounts.error.server", description));
            }
            if (Get("access_token") is not { Length: > 0 } access) throw new AccountException(Localization.Loc.T("accounts.error.server", ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var seconds = root.TryGetProperty("expires_in", out var e) ? e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.TryParse(e.GetString(), out var s) ? s : 3600 : 3600;
            var scopes = (Get("scope") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new OAuthTokens(access, now.AddSeconds(Math.Max(60, seconds) - 60), Get("refresh_token"), Get("id_token"), scopes);
        }
    }

    /// <summary>Who signed in, from the id_token (it came straight from the server over HTTPS: not checked again).</summary>
    public static (string Email, string Name) Identity(string? idToken)
    {
        if (idToken?.Split('.') is not [_, var payload, ..]) return (string.Empty, string.Empty);
        try
        {
            var padded = payload.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            using var doc = JsonDocument.Parse(Convert.FromBase64String(padded));
            var root = doc.RootElement;
            string? Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (Get("email") ?? Get("preferred_username") ?? Get("upn") ?? string.Empty, Get("name") ?? string.Empty);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return (string.Empty, string.Empty);
        }
    }

    /// <summary>Tells the server to forget a token (Google). Microsoft has no such thing for desktop programs: the account is only forgotten here.</summary>
    public async Task<bool> RevokeAsync(OAuthProvider provider, string token, CancellationToken cancel)
    {
        if (provider.RevokeUrl is null) return false;
        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token });
            using var response = await http.PostAsync(provider.RevokeUrl, content, cancel).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            Log.Warn($"Cuentas: no se ha podido revocar el token: {ex.Message}");
            return false;
        }
    }
}
