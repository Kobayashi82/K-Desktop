using KDesktop.Core.Engine;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

public class HttpHeaderTests
{
    [Fact]
    public void SecretValueIsSavedEncrypted()
    {
        var a = new HttpRequestAction { Url = "http://ha.local" };
        a.Headers.Add(new HttpHeader { Name = "Authorization", Value = "Bearer token123", Secret = true });
        a.Headers.Add(new HttpHeader { Name = "X-Plain", Value = "visible" });

        var json = Json.Serialize<CommandAction>(a);
        Assert.DoesNotContain("token123", json);
        Assert.Contains("\"protected\": \"dpapi:", json);
        Assert.Contains("\"value\": \"visible\"", json);

        var back = (HttpRequestAction)Json.Deserialize<CommandAction>(json)!;
        Assert.Equal("Bearer token123", back.Headers[0].Value);
        Assert.True(back.Headers[0].Secret);
        Assert.Equal("visible", back.Headers[1].Value);
    }

    [Fact]
    public void UnreadableSecretIsKeptUntilRetyped()
    {
        const string foreign = "dpapi:AAAAAAAAAAAAAAAA";
        var json = "{\"type\":\"httpRequest\",\"url\":\"x\",\"headers\":[{\"name\":\"A\",\"secret\":true,\"protected\":\"" + foreign + "\"}]}";
        var a = (HttpRequestAction)Json.Deserialize<CommandAction>(json)!;
        Assert.True(a.Headers[0].Unreadable);
        Assert.Equal("", a.Headers[0].Value);
        Assert.Contains(foreign, Json.Serialize<CommandAction>(a));

        a.Headers[0].Value = "new";
        Assert.False(a.Headers[0].Unreadable);
        Assert.DoesNotContain(foreign, Json.Serialize<CommandAction>(a));
    }

    [Fact]
    public void SummaryNeverShowsHeaders()
    {
        var a = new HttpRequestAction { Method = HttpVerb.Post, Url = "https://ha.local/api/services/light/turn_off" };
        a.Headers.Add(new HttpHeader { Name = "Authorization", Value = "Bearer secret" });
        Assert.Equal("POST ha.local/api/services/light/turn_off", a.Summary);
    }

    [Fact]
    public void VariablesAreExpandedAndEscaped()
    {
        var now = new DateTime(2026, 10, 16, 9, 5, 3);
        Assert.Equal("2026-10-16 09:05:03 \"x\"", TextVariables.Expand("{fecha} {HORA} {clipboard}", now: now, clipboard: () => "\"x\""));
        Assert.Equal("a%20b", TextVariables.Expand("{portapapeles}", Uri.EscapeDataString, now, () => "a b"));
        Assert.Equal("{\"keep\":1} {other}", TextVariables.Expand("{\"keep\":1} {other}", now: now, clipboard: () => ""));
    }
}
