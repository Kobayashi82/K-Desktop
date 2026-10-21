using System.Text.Json;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// 5.0.105: dates and clock times, exact (Bonsai did them with the calculator and got them wrong); and a request that
/// asks llama-server to think with a budget.
/// </summary>
public class DateMathTests
{
    /// <summary>Wednesday 21 October 2026, 10:00.</summary>
    private static readonly FixedTime Now = new(new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.FromHours(2)));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        private readonly TimeZoneInfo _zone = TimeZoneInfo.CreateCustomTimeZone("test", now.Offset, "test", "test");

        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => _zone;
    }

    private static async Task<ToolResult> Run(object arguments) =>
        await new DateMathTool(Now).RunAsync(JsonSerializer.SerializeToElement(arguments), new ToolContext("lander"), CancellationToken.None);

    [Theory]
    [InlineData("between", "", "2026-12-25", null, "65 days (9 weeks and 2 days)")]
    [InlineData("between", "2026-10-21", "2026-12-31", null, "71 days (10 weeks and 1 days)")]
    [InlineData("between", "2026-12-25", "2026-10-21", null, "in the past")]
    [InlineData("between", "17:40", "20:25", null, "2 h 45 min")]
    [InlineData("weekday", "2026-12-25", null, null, "is a Friday")]
    [InlineData("weekday", "25/12/2026", null, null, "is a Friday")]
    [InlineData("add", "17:40", null, "2h 45m", "= 20:25 (same day)")]
    [InlineData("add", "17:40", null, "2:45", "= 20:25 (same day)")]
    [InlineData("add", "23:30", null, "90m", "= 01:00 (+1 day(s), Thursday 2026-10-22)")]
    [InlineData("add", "", null, "65d", "= Friday 2026-12-25")]
    [InlineData("add", "2026-10-21", null, "-3w", "= Wednesday 2026-09-30")]
    [InlineData("add", "2026-01-31", null, "1mo", "= Saturday 2026-02-28")]
    [InlineData("days_in", "", null, null, "The year 2026 has 365 days (not a leap year)")]
    [InlineData("days_in", "2028", null, null, "366 days (a leap year)")]
    [InlineData("days_in", "2028-02", null, null, "has 29 days")]
    public async Task DatesAndTimesComeOutExact(string op, string date, string? to, string? amount, string expected)
    {
        var result = await Run(new { op, date, to, amount });
        Assert.False(result.Failed, result.Text);
        Assert.Contains(expected, result.Text, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(result.Done));
    }

    [Theory]
    [InlineData("between", "", null, null)]
    [InlineData("weekday", "mañana", null, null)]
    [InlineData("add", "17:40", null, "dos horas")]
    [InlineData("guess", "", null, null)]
    public async Task WhatCannotBeReadSaysSo(string op, string date, string? to, string? amount)
    {
        var result = await Run(new { op, date, to, amount });
        Assert.True(result.Failed);
        Assert.StartsWith("Cannot work it out: ", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRequestAsksForThinkingWithABudgetOnTopOfTheAnswer()
    {
        var request = new ChatRequest("bonsai", [ChatMessage.User("¿cuántos días faltan para Navidad?")], []) { MaxTokens = 220, ThinkTokens = 200 };
        using (var body = JsonDocument.Parse(OpenAICompatibleChatModel.BuildChatBody(request)))
        {
            var root = body.RootElement;
            Assert.True(root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
            Assert.Equal(200, root.GetProperty("thinking_budget_tokens").GetInt32());
            Assert.Equal(420, root.GetProperty("max_tokens").GetInt32());
            Assert.False(root.TryGetProperty("reasoning_effort", out _));
        }
        using (var body = JsonDocument.Parse(OpenAICompatibleChatModel.BuildChatBody(request with { ThinkTokens = 0 })))
        {
            var root = body.RootElement;
            Assert.False(root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
            Assert.False(root.TryGetProperty("thinking_budget_tokens", out _));
            Assert.Equal(220, root.GetProperty("max_tokens").GetInt32());
        }
        using (var body = JsonDocument.Parse(OpenAICompatibleChatModel.BuildChatBody(request, disableReasoning: false)))
            Assert.False(body.RootElement.TryGetProperty("chat_template_kwargs", out _));
    }
}
