using System.Text.Json;
using System.Text.RegularExpressions;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 0.1: every pipe message in protocolo.md has its example, and every example is a documented message.</summary>
public partial class ProtocolExampleTests
{
    private static HashSet<string> DocumentedMessages()
    {
        var doc = File.ReadAllText(Path.Combine(Contracts.DocsDir, "protocolo.md"));
        // Rows of the message tables: | `name` | A→K / K→A / ambas | …  («ping / pong» documents two).
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match row in MessageRow().Matches(doc))
            foreach (Match name in Code().Matches(row.Groups[1].Value))
                names.Add(name.Groups[1].Value);
        return names;
    }

    [Fact]
    public void EveryDocumentedMessageHasAnExample()
    {
        var examples = Directory.GetFiles(Contracts.Examples, "*.json").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        var missing = DocumentedMessages().Where(m => !examples.Contains(m)).ToList();
        Assert.True(missing.Count == 0, "Sin ejemplo: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryExampleIsADocumentedMessage()
    {
        var documented = DocumentedMessages();
        var extra = Directory.GetFiles(Contracts.Examples, "*.json").Select(Path.GetFileNameWithoutExtension)
            .Where(e => !documented.Contains(e!)).ToList();
        Assert.True(extra.Count == 0, "Sin documentar: " + string.Join(", ", extra));
    }

    [Fact]
    public void ExamplesAreSingleLineFramable()
    {
        foreach (var file in Directory.GetFiles(Contracts.Examples, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.Equal(Path.GetFileNameWithoutExtension(file), doc.RootElement.GetProperty("t").GetString());

            // What goes down the pipe: compact, one line, well under the 1 MiB limit.
            var line = JsonSerializer.Serialize(doc.RootElement);
            Assert.DoesNotContain('\n', line);
            Assert.True(line.Length < 64 * 1024);
        }
    }

    [GeneratedRegex(@"^\|\s*((?:`[a-zA-Z]+`\s*/?\s*)+)\|\s*(?:A→K|K→A|ambas)\s*\|", RegexOptions.Multiline)]
    private static partial Regex MessageRow();

    [GeneratedRegex("`([a-zA-Z]+)`")]
    private static partial Regex Code();
}
