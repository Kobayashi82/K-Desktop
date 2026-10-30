using System.Text.Json.Serialization;

namespace KDesktop.Core.Accounts;

/// <summary>Who an account is from: only Google (Microsoft was dropped in 5.0.429: a personal account cannot register the application).</summary>
public enum AccountProvider { Google }

/// <summary>What an account is used for in K-Desktop (the permissions asked when it was connected).</summary>
[Flags]
public enum AccountUse
{
    None = 0,
    /// <summary>Read the calendars.</summary>
    CalendarRead = 1,
    /// <summary>Create, change and delete citas.</summary>
    CalendarWrite = 2,
    /// <summary>Read, move and send mail (IMAP and SMTP with XOAUTH2).</summary>
    Mail = 4,
}

/// <summary>
/// A Google account connected with OAuth (Accounts\accounts.json): who it is, what it was allowed and its
/// refresh token encrypted with DPAPI (only this user on this PC can use it; a backup restored elsewhere has to connect it
/// again). The access tokens live only in memory.
/// </summary>
public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

    public AccountProvider Provider { get; set; }

    /// <summary>«ana@gmail.com».</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>«Ana García».</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The permissions granted (as the server said them).</summary>
    public List<string> Scopes { get; set; } = [];

    /// <summary>What it was connected for.</summary>
    public AccountUse Uses { get; set; }

    /// <summary>The refresh token, encrypted (<see cref="Platform.Secrets"/>).</summary>
    public string? RefreshTokenProtected { get; set; }

    /// <summary>The application it was connected with (the user's own Client ID).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The server said no more («revoked», a changed password…): «Desconectada — volver a conectar».</summary>
    public bool Disconnected { get; set; }

    public DateTimeOffset Connected { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore] public string Display => string.IsNullOrWhiteSpace(Name) || Name == Email ? Email : $"{Name} · {Email}";
}

/// <summary>The applications K-Desktop connects with (Opciones › Cuentas).</summary>
public sealed class AccountApps
{
    /// <summary>The user's own Google Client ID (a «Desktop app» of their Google Cloud project): Google's calendar and mail permissions need it.</summary>
    public string GoogleClientId { get; set; } = string.Empty;

    /// <summary>Its secret (Google gives one to desktop apps too; it is not really secret, but it is kept encrypted).</summary>
    public string GoogleClientSecretProtected { get; set; } = string.Empty;
}

/// <summary>Something went wrong with an account: the message says it in the user's words.</summary>
public class AccountException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The server does not accept the account any more: it has to be connected again.</summary>
public sealed class AccountDisconnectedException(string message) : AccountException(message);
