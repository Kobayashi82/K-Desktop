using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 1.2, contract side: the messages this build knows read and write exactly like the examples of 0.1.</summary>
public class PipeMessageTests
{
    private static IEnumerable<string> ExampleFiles() => Directory.GetFiles(Contracts.Examples, "*.json");

    [Fact]
    public void KnownMessagesRoundTripTheirExamples()
    {
        var checkedTypes = new List<string>();
        foreach (var file in ExampleFiles())
        {
            var original = JsonNode.Parse(File.ReadAllText(file))!;
            var line = original.ToJsonString();
            var message = PipeJson.Parse(line);
            if (message is UnknownMessage) continue;

            var written = JsonNode.Parse(PipeJson.Serialize(message))!;
            Assert.True(JsonNode.DeepEquals(original, written), $"{Path.GetFileName(file)}: {written.ToJsonString()}");
            checkedTypes.Add(message.Type);
        }
        Assert.Equal(PipeJson.KnownTypes.Order(), checkedTypes.Order());
    }

    /// <summary>Stage 6.3: what is null is left out, and must still come back (a minimised window has no rectangle).</summary>
    [Fact]
    public void StepsWithoutAPlaceOrARectangleComeBack()
    {
        var minimized = new StepMessage("r", 2, 3, 1, "Window", "Restore", "Restaurar", 6000, new StepTarget(42, "chrome", "GitHub", "minimized", false));
        Assert.Equal(minimized, PipeJson.Parse(PipeJson.Serialize(minimized)));
        var nowhere = new StepMessage("r", 3, 3, 1, "Wait", "", "Esperar 1 s", 6000);
        Assert.Equal(nowhere, PipeJson.Parse(PipeJson.Serialize(nowhere)));
        Assert.Equal(new SkipMessage("r", 0, true), PipeJson.Parse(PipeJson.Serialize(new SkipMessage("r", 0, true))));
        Assert.Equal(new StepResultMessage("r", 1, "failed", "No hay ventana"), PipeJson.Parse(PipeJson.Serialize(new StepResultMessage("r", 1, "failed", "No hay ventana"))));
    }

    [Fact]
    public void TheTypeGoesFirstOnOneLine()
    {
        var line = PipeJson.Serialize(new HelloMessage(1, "1.0", 42));
        Assert.Equal("""{"t":"hello","protocol":1,"sdk":"1.0","pid":42}""", line);
    }

    [Fact]
    public void UnknownMessagesAreKeptAsTheyCame()
    {
        var message = Assert.IsType<UnknownMessage>(PipeJson.Parse("""{"t":"fromTheFuture","x":1}"""));
        Assert.Equal("fromTheFuture", message.Type);
        Assert.Equal("""{"t":"fromTheFuture","x":1}""", PipeJson.Serialize(message));
    }

    [Theory]
    [InlineData("""{"t":"hello","protocol":1,"pid":3}""")]           // sdk missing
    [InlineData("""{"t":"hello","protocol":"1","sdk":"1.0","pid":3}""")] // wrong type
    [InlineData("""{"t":"bye","reason":null}""")]
    [InlineData("""{"protocol":1}""")]
    [InlineData("""[1,2]""")]
    public void BadMessagesAreRejected(string line) => Assert.ThrowsAny<JsonException>(() => PipeJson.Parse(line));

    [Fact]
    public void ThePipeNameDependsOnTheDataFolderAndTheSession()
    {
        var a = AssistantPipe.NameFor(@"C:\Users\x\AppData\Roaming\K-Desktop 5", 1);
        Assert.Equal(a, AssistantPipe.NameFor(@"c:\users\x\appdata\roaming\k-desktop 5\", 1));
        Assert.NotEqual(a, AssistantPipe.NameFor(@"C:\pruebas\datos", 1));
        Assert.NotEqual(a, AssistantPipe.NameFor(@"C:\Users\x\AppData\Roaming\K-Desktop 5", 2));
        Assert.Matches(@"^KDesktop\.Assistant\.1\.[0-9a-f]{8}$", a);
    }
}
