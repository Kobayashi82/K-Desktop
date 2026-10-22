using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using ModelContextProtocol.Protocol;

namespace KDesktop.Assistant.Tools.Mcp;

/// <summary>A tool of an MCP server as it is kept between runs (so choosing tools never needs the server started).</summary>
/// <param name="Name">Its name for the model: <c>mcp_&lt;servidor&gt;_&lt;herramienta&gt;</c>.</param>
/// <param name="Tool">Its name in the server.</param>
/// <param name="Schema">The JSON schema of its parameters.</param>
/// <param name="ReadOnly">The server says it only reads (<c>readOnlyHint</c>).</param>
public sealed record McpToolInfo(string Name, string Tool, string Description, string Schema, bool ReadOnly);

/// <summary>
/// A tool of an MCP server for the assistants (plan §7.3): it asks first (🟡) unless the server says it only reads; the
/// model never sees a secret (masked in what comes back).
/// </summary>
public sealed class McpTool(McpServers servers, string server, McpToolInfo info) : ITool
{
    /// <summary>How much of what comes back the model gets.</summary>
    public const int MaxResult = 4000;

    public McpToolInfo Info => info;

    public string Server => server;

    public string Name => info.Name;

    public string Description => info.Description;

    public string ParametersSchemaJson => info.Schema;

    public IReadOnlyList<string> Examples => [];

    public ToolRisk Risk => info.ReadOnly ? ToolRisk.Safe : ToolRisk.AskOnce;

    public bool ReadsOnly => info.ReadOnly;

    public string Intent(JsonElement arguments) => Loc.T("tools.mcp.intent", info.Tool.Replace('_', ' '), server);

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (arguments.ValueKind == JsonValueKind.Object)
            foreach (var property in arguments.EnumerateObject()) values[property.Name] = property.Value.Clone();
        CallToolResult result;
        try
        {
            result = await servers.CallAsync(server, info.Tool, values, cancellationToken).ConfigureAwait(false);
        }
        catch (McpServerException ex)
        {
            return ToolResult.Fail("Not done: " + ex.Message) with { Reply = ex.Message };
        }
        catch (ModelContextProtocol.McpException ex)
        {
            return ToolResult.Fail("Failed: " + servers.Mask(ex.Message));
        }
        var text = servers.Mask(TextOf(result));
        if (text.Length > MaxResult) text = text[..MaxResult].TrimEnd() + "…";
        if (result.IsError == true) return ToolResult.Fail("Failed: " + (text.Length > 0 ? text : "the server said it went wrong."));
        return new ToolResult(text.Length > 0 ? text : "Done.") { Acted = !info.ReadOnly };
    }

    /// <summary>What a call gave back, as text: its texts, its structured answer, and what else came (images, files).</summary>
    public static string TextOf(CallToolResult result)
    {
        var text = new StringBuilder();
        foreach (var block in result.Content ?? [])
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(block switch
            {
                TextContentBlock t => t.Text,
                ImageContentBlock => "[image]",
                AudioContentBlock => "[audio]",
                EmbeddedResourceBlock { Resource: TextResourceContents r } => r.Text,
                ResourceLinkBlock link => "[" + link.Uri + "]",
                _ => "[" + block.Type + "]",
            });
        }
        if (text.Length == 0 && result.StructuredContent is { } structured) text.Append(structured.ToString());
        return text.ToString().Trim();
    }

    /// <summary>
    /// Its schema for the model: an object with properties always (some servers leave them out, and llama-server then
    /// refuses the request), without <c>$schema</c>.
    /// </summary>
    public static string SchemaOf(JsonElement input)
    {
        JsonObject schema;
        try { schema = JsonNode.Parse(input.GetRawText()) as JsonObject ?? []; }
        catch (JsonException) { schema = []; }
        schema.Remove("$schema");
        schema["type"] = "object";
        if (schema["properties"] is not JsonObject) schema["properties"] = new JsonObject();
        return schema.ToJsonString();
    }
}
