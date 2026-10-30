using System.Text.Json;
using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Accounts;

/// <summary>
/// The connected accounts (calendar, phase 6; the mail uses them too): connecting one with OAuth, its tokens (the refresh
/// token encrypted with DPAPI, out of any plain copy), renewing them by itself, and disconnecting it (Google: revoked in
/// the server). A revoked account stays as «Desconectada — volver a conectar» without blocking anything else.
/// </summary>
public sealed class AccountService : IDisposable
{
    public static string DefaultFolder => Path.Combine(AppPaths.DataDir, "Accounts");

    private sealed class Data
    {
        public List<Account> Accounts { get; set; } = [];
        public AccountApps Apps { get; set; } = new();
    }

    private readonly string _file;
    private readonly Func<string, string> _protect;
    private readonly Func<string, string?> _unprotect;
    private readonly OAuthDesktopFlow _flow;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, OAuthTokens> _tokens = [];
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private Data _data;

    public AccountService(string folder, Func<string, string> protect, Func<string, string?> unprotect, HttpClient? http = null)
    {
        _file = Path.Combine(folder, "accounts.json");
        _protect = protect;
        _unprotect = unprotect;
        _flow = new OAuthDesktopFlow(http ?? CalendarService.CreateHttp());
        try { _data = CalendarJson.Load<Data>(_file) ?? new Data(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cuentas: no se han podido leer: {ex.Message}");
            _data = new Data();
        }
    }

    /// <summary>Opens the browser (set by K-Desktop: the system's one).</summary>
    public Action<string> OpenBrowser { get; set; } = url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>An account was connected, changed or disconnected (any thread).</summary>
    public event Action? Changed;

    public IReadOnlyList<Account> Accounts
    {
        get { lock (_gate) return [.. _data.Accounts]; }
    }

    public Account? Get(string id)
    {
        lock (_gate) return _data.Accounts.FirstOrDefault(a => a.Id == id);
    }

    // ── the applications ─────────────────────────────────────────────────

    public string GoogleClientId
    {
        get { lock (_gate) return _data.Apps.GoogleClientId; }
    }

    /// <summary>Whether accounts can be connected: the user put their Client ID.</summary>
    public bool CanConnect(AccountProvider provider) => GoogleClientId.Trim().Length > 0;

    /// <summary>Saves the user's own application (Opciones › Cuentas). An empty secret keeps the one there.</summary>
    public void SetApps(string googleClientId, string? googleClientSecret)
    {
        lock (_gate)
        {
            _data.Apps.GoogleClientId = googleClientId.Trim();
            if (!string.IsNullOrWhiteSpace(googleClientSecret)) _data.Apps.GoogleClientSecretProtected = _protect(googleClientSecret.Trim());
            if (googleClientId.Trim().Length == 0) _data.Apps.GoogleClientSecretProtected = string.Empty;
            Save();
        }
        Changed?.Invoke();
    }

    public bool HasGoogleSecret
    {
        get { lock (_gate) return _data.Apps.GoogleClientSecretProtected.Length > 0; }
    }

    private (string Id, string? Secret) App(string? accountClient = null)
    {
        lock (_gate)
        {
            var id = accountClient is { Length: > 0 } g ? g : _data.Apps.GoogleClientId;
            return (id, _data.Apps.GoogleClientSecretProtected.Length > 0 ? _unprotect(_data.Apps.GoogleClientSecretProtected) : null);
        }
    }

    // ── connecting ───────────────────────────────────────────────────────

    /// <summary>
    /// Connects an account (or the same one again, with more permissions: «consentimiento incremental»): the browser opens
    /// for the user to sign in and allow it. Returns it, already saved.
    /// </summary>
    public async Task<Account> ConnectAsync(AccountProvider provider, AccountUse uses, string? loginHint = null, CancellationToken cancel = default)
    {
        if (!CanConnect(provider)) throw new AccountException(Loc.T("accounts.error.noGoogleApp"));
        var oauth = OAuthProvider.Of(provider);
        var (clientId, secret) = App();
        var scopes = oauth.ScopesFor(uses);
        var tokens = await _flow.SignInAsync(oauth, clientId, secret, scopes, OpenBrowser, loginHint, cancel).ConfigureAwait(false);
        if (tokens.RefreshToken is null) throw new AccountException(Loc.T("accounts.error.noRefresh"));
        var (email, name) = OAuthDesktopFlow.Identity(tokens.IdToken);
        Account account;
        lock (_gate)
        {
            account = _data.Accounts.FirstOrDefault(a => a.Provider == provider && email.Length > 0 && string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase))
                      ?? new Account { Provider = provider };
            account.Email = email;
            account.Name = name;
            account.Uses |= uses;
            account.Scopes = [.. account.Scopes.Union(tokens.Scopes.Count > 0 ? tokens.Scopes : scopes, StringComparer.OrdinalIgnoreCase)];
            account.RefreshTokenProtected = _protect(tokens.RefreshToken);
            account.ClientId = clientId;
            account.Disconnected = false;
            account.Connected = DateTimeOffset.UtcNow;
            if (!_data.Accounts.Contains(account)) _data.Accounts.Add(account);
            _tokens[account.Id] = tokens;
            Save();
        }
        Log.Info($"Cuentas: conectada {provider} {email} ({uses})");
        Changed?.Invoke();
        return account;
    }

    /// <summary>
    /// Forgets an account here and revokes its token in Google. What used it (calendars, mail) stops; nothing is
    /// deleted in the server.
    /// </summary>
    public async Task DisconnectAsync(string id, CancellationToken cancel = default)
    {
        Account? account;
        string? refresh;
        lock (_gate)
        {
            account = _data.Accounts.FirstOrDefault(a => a.Id == id);
            if (account is null) return;
            refresh = account.RefreshTokenProtected is { } p ? _unprotect(p) : null;
            _data.Accounts.Remove(account);
            _tokens.Remove(id);
            Save();
        }
        if (refresh is not null) await _flow.RevokeAsync(OAuthProvider.Of(account.Provider), refresh, cancel).ConfigureAwait(false);
        Log.Info($"Cuentas: desconectada {account.Provider} {account.Email}");
        Changed?.Invoke();
    }

    // ── tokens ───────────────────────────────────────────────────────────

    /// <summary>
    /// A valid access token for an account (renewed when it is about to expire). A revoked account is marked
    /// «Desconectada» and <see cref="AccountDisconnectedException"/> is thrown; no network: the error as it comes.
    /// </summary>
    public async Task<string> AccessTokenAsync(string accountId, CancellationToken cancel = default)
    {
        var account = Get(accountId) ?? throw new AccountDisconnectedException(Loc.T("accounts.error.gone"));
        if (account.Disconnected) throw new AccountDisconnectedException(Loc.T("accounts.error.reconnect", account.Email));
        lock (_gate)
        {
            if (_tokens.TryGetValue(accountId, out var t) && t.Expires > DateTimeOffset.UtcNow) return t.AccessToken;
        }
        await _refreshing.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (_tokens.TryGetValue(accountId, out var t) && t.Expires > DateTimeOffset.UtcNow) return t.AccessToken;
            }
            var refresh = account.RefreshTokenProtected is { } p ? _unprotect(p) : null;
            if (refresh is null) throw Disconnect(account, Loc.T("accounts.error.otherPc"));
            var oauth = OAuthProvider.Of(account.Provider);
            var (clientId, secret) = App(account.ClientId);
            OAuthTokens tokens;
            try
            {
                tokens = await _flow.RefreshAsync(oauth, clientId, secret, refresh, cancel).ConfigureAwait(false);
            }
            catch (AccountDisconnectedException ex)
            {
                throw Disconnect(account, ex.Message);
            }
            lock (_gate)
            {
                _tokens[accountId] = tokens;
                // A server that turns its refresh tokens over: the new one is kept.
                if (tokens.RefreshToken is { } rotated && rotated != refresh)
                {
                    account.RefreshTokenProtected = _protect(rotated);
                    Save();
                }
            }
            return tokens.AccessToken;
        }
        finally
        {
            _refreshing.Release();
        }
    }

    /// <summary>A 401 from the API: the token in memory is dropped and asked again next time.</summary>
    public void Forget(string accountId)
    {
        lock (_gate)
        {
            _tokens.Remove(accountId);
        }
    }

    private AccountDisconnectedException Disconnect(Account account, string why)
    {
        lock (_gate)
        {
            account.Disconnected = true;
            Save();
        }
        Log.Warn($"Cuentas: {account.Provider} {account.Email} desconectada: {why}");
        Changed?.Invoke();
        return new AccountDisconnectedException(Loc.T("accounts.error.reconnect", account.Email));
    }

    public void Dispose() => _refreshing.Dispose();

    private void Save()
    {
        try { CalendarJson.Save(_file, _data); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Cuentas: no se han podido guardar: {ex.Message}");
        }
    }
}
