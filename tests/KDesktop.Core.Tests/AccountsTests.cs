using System.Net;
using System.Text;
using KDesktop.Core.Accounts;

namespace KDesktop.Core.Tests;

public sealed class AccountsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kd-accounts-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public void PkceAsInTheRfc()
    {
        // RFC 7636, appendix B.
        var challenge = OAuthDesktopFlow.Base64Url(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")));
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", challenge);
        var pkce = OAuthDesktopFlow.NewPkce();
        Assert.InRange(pkce.Verifier.Length, 43, 128);
    }

    [Fact]
    public void AuthorizeAddress()
    {
        var url = OAuthDesktopFlow.AuthorizeUrl(OAuthProvider.Google, "id.apps", "http://127.0.0.1:5000/", OAuthProvider.Google.ScopesFor(AccountUse.CalendarWrite), new("v", "c"), "s", null);
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", url, StringComparison.Ordinal);
        Assert.Contains("access_type=offline", url, StringComparison.Ordinal);
        Assert.Contains("calendar.events", url, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=http%3a%2f%2f127.0.0.1%3a5000%2f", url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://mail.google.com/", OAuthProvider.Google.ScopesFor(AccountUse.Mail));
    }

    [Fact]
    public void LoopbackAnswers()
    {
        Assert.Equal("abc", OAuthDesktopFlow.ParseRequest("GET /?code=abc&state=s1 HTTP/1.1", "s1")!.Code);
        Assert.Equal("state", OAuthDesktopFlow.ParseRequest("GET /?code=abc&state=other HTTP/1.1", "s1")!.Error);
        Assert.Equal("access_denied", OAuthDesktopFlow.ParseRequest("GET /?error=access_denied&state=s1 HTTP/1.1", "s1")!.Error);
        Assert.Null(OAuthDesktopFlow.ParseRequest("GET /favicon.ico HTTP/1.1", "s1"));
        Assert.Null(OAuthDesktopFlow.ParseRequest("POST /?code=abc HTTP/1.1", "s1"));
    }

    [Fact]
    public void TokensAndIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var t = OAuthDesktopFlow.ParseTokens("""{"access_token":"a1","expires_in":3599,"refresh_token":"r1","scope":"openid email","id_token":"x.eyJlbWFpbCI6ImFuYUBnbWFpbC5jb20iLCJuYW1lIjoiQW5hIn0.y"}""", HttpStatusCode.OK, now);
        Assert.Equal("a1", t.AccessToken);
        Assert.Equal("r1", t.RefreshToken);
        Assert.True(t.Expires > now.AddMinutes(55) && t.Expires < now.AddMinutes(60));
        Assert.Equal(("ana@gmail.com", "Ana"), OAuthDesktopFlow.Identity(t.IdToken));
        Assert.Throws<AccountDisconnectedException>(() => OAuthDesktopFlow.ParseTokens("""{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""", HttpStatusCode.BadRequest, now));
        Assert.Throws<AccountException>(() => OAuthDesktopFlow.ParseTokens("<html>", HttpStatusCode.BadGateway, now));
    }

    [Fact]
    public async Task RefreshesAndMarksRevokedAccounts()
    {
        var handler = new TokenHandler();
        var file = Path.Combine(_folder, "accounts.json");
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(file, """{"accounts":[{"id":"a1","provider":"google","email":"ana@gmail.com","scopes":["https://www.googleapis.com/auth/calendar.events","https://mail.google.com/"],"refreshTokenProtected":"x:r1","clientId":"app"}],"apps":{}}""");
        var service = new AccountService(_folder, s => "x:" + s, s => s.StartsWith("x:", StringComparison.Ordinal) ? s[2..] : null, new HttpClient(handler));
        Assert.Equal("t1", await service.AccessTokenAsync("a1"));
        Assert.Equal("t1", await service.AccessTokenAsync("a1")); // kept in memory
        Assert.Equal(1, handler.Calls);
        // The server gave a new refresh token: kept.
        Assert.Contains("x:r2", await File.ReadAllTextAsync(file), StringComparison.Ordinal);

        handler.Revoked = true;
        service.Forget("a1");
        await Assert.ThrowsAsync<AccountDisconnectedException>(() => service.AccessTokenAsync("a1"));
        Assert.True(service.Get("a1")!.Disconnected);
    }

    private sealed class TokenHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Revoked { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Yield();
            var json = Revoked ? """{"error":"invalid_grant"}""" : $$"""{"access_token":"t{{Calls}}","expires_in":3600,"refresh_token":"r2"}""";
            return new HttpResponseMessage(Revoked ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
