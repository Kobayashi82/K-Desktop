using System.Text.Json;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Orders for later (5.0.288): telling them apart, and the tool that programs them.</summary>
public sealed class ScheduleTests
{
    [Theory]
    [InlineData("a las siete vacía la papelera")]
    [InlineData("Lander, a las 19:00 mira el tiempo y mándamelo por Telegram")]
    [InlineData("dentro de dos horas apaga el ordenador")]
    [InlineData("en diez minutos minimiza Steam")]
    [InlineData("mañana vacía la papelera")]
    [InlineData("el viernes a las nueve abre Steam")]
    [InlineData("todos los días a las ocho dime las noticias")]
    [InlineData("at 7 empty the recycle bin")]
    [InlineData("tomorrow morning check the weather")]
    public void AnOrderThatSaysWhenIsForLater(string said) => Assert.True(QuickRules.IsForLater(said, ["Lander"]));

    [Theory]
    [InlineData("vacía la papelera")]
    [InlineData("¿qué tiempo hará mañana?")]
    [InlineData("abre la carpeta de la mañana")]
    [InlineData("sube el volumen al 50")]
    [InlineData("minimiza Steam")]
    [InlineData("pon un temporizador de diez minutos?")]
    public void AnOrderForNowIsNot(string said) => Assert.False(QuickRules.IsForLater(said, ["Lander"]));

    private static JsonElement Args(string json) => ToolArguments.Parse(json);

    [Fact]
    public async Task ItSendsTheOrderWithItsToolsAndCommands()
    {
        var registry = new ToolRegistry();
        var bridge = new ScheduleBridge();
        CommandTools.AddTo(registry, bridge);
        var tool = (ScheduleTool)registry.Find("schedule")!;
        var result = await tool.RunAsync(Args("""{"order":"vacía la papelera y apaga el ordenador","time":"19:00","day":"2099-01-02","tools":["run_event","Apagar"],"commands":["Vaciar papelera"]}"""),
            new ToolContext("lander"), default);

        Assert.False(result.Failed);
        var sent = bridge.Sent!;
        Assert.Equal("add", sent.Op);
        Assert.Equal("lander", sent.Assistant);
        Assert.Equal("19:00", sent.Time);
        Assert.Equal("2099-01-02", sent.Date);
        Assert.Null(sent.Repeat);
        // «Apagar» is no tool: one of the user's commands; with commands, run_command is approved too.
        Assert.Equal(["run_event", "run_command"], sent.Tools);
        Assert.Equal(["Vaciar papelera", "Apagar"], sent.Commands);
        Assert.NotNull(result.Undo);
        Assert.Contains("Apagar", result.Text, StringComparison.Ordinal); // the unknown one is told to the model
    }

    [Fact]
    public void ItIsAsRiskyAsWhatItApproves()
    {
        var registry = new ToolRegistry();
        var bridge = new ScheduleBridge();
        CommandTools.AddTo(registry, bridge);
        var tool = registry.Find("schedule")!;
        Assert.Equal(ToolRisk.Safe, registry.RiskOf(tool, Args("""{"order":"x","time":"19:00","tools":["list_scheduled"]}""")));
        Assert.Equal(ToolRisk.AskOnce, registry.RiskOf(tool, Args("""{"order":"x","time":"19:00","commands":["Vaciar papelera"]}""")));
        Assert.Equal(ToolRisk.AskAlways, registry.RiskOf(tool, Args("""{"order":"x","time":"19:00","tools":["end_process"]}""")));
    }

    [Fact]
    public async Task WithoutATimeNothingIsSent()
    {
        var registry = new ToolRegistry();
        var bridge = new ScheduleBridge();
        CommandTools.AddTo(registry, bridge);
        var result = await registry.Find("schedule")!.RunAsync(Args("""{"order":"vacía la papelera"}"""), new ToolContext("lander"), default);
        Assert.True(result.Failed);
        Assert.Null(bridge.Sent);
    }

    /// <summary>K-Desktop for schedule: keeps what it was sent and says it was programmed (with «Apagar» unknown).</summary>
    private sealed class ScheduleBridge : ICommandBridge
    {
        public ScheduleMessage? Sent { get; private set; }

        public Task<ScheduleDoneMessage?> ScheduleAsync(ScheduleMessage request, CancellationToken cancellationToken)
        {
            Sent = request;
            return Task.FromResult<ScheduleDoneMessage?>(new ScheduleDoneMessage("s", true,
                [new ScheduledEntry("e1", request.Order ?? "", $"{request.Date} {request.Time}", request.Order ?? "", request.Assistant)], Unknown: ["Apagar"]));
        }

        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }
}
