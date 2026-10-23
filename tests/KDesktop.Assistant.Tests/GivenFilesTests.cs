using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stages 14.1 and 14.3: files given to an assistant (dropped on it). Their text is read (text in any encoding, Word,
/// PowerPoint, web pages, PDF), a long one gives the parts that answer the question, deleting and moving always ask, and
/// the order that brings them goes to the AI with them at hand (or opens them without it).
/// </summary>
public sealed class GivenFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-given-" + Guid.NewGuid().ToString("N")[..8]);

    public GivenFilesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Office(string name, string part, string xml)
    {
        var path = Path.Combine(_folder, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(part).Open());
        writer.Write(xml);
        return path;
    }

    private string Pdf(string name, params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(PageSize.A4);
            page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(40, 780), font);
        }
        return Write(name, builder.Build());
    }

    [Fact]
    public void TheTextOfEachKindOfFileIsRead()
    {
        Assert.Equal("Hola, ¿qué tal?", FileText.Read(Write("utf8.txt", Encoding.UTF8.GetBytes("Hola, ¿qué tal?")))!.Text);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal("Año señal", FileText.Read(Write("ansi.txt", Encoding.GetEncoding(1252).GetBytes("Año señal")))!.Text);
        Assert.Null(FileText.Read(Write("binary.dat", [1, 2, 0, 0, 5])));
        Assert.Null(FileText.Read(Write("photo.png", [0x89, 0x50, 0x4E, 0x47])));

        var docx = Office("carta.docx", "word/document.xml",
            """<w:document xmlns:w="w"><w:body><w:p><w:r><w:t>Primera</w:t></w:r><w:r><w:t xml:space="preserve"> línea</w:t></w:r></w:p><w:p><w:r><w:t>Segunda</w:t><w:tab/><w:t>tabla</w:t></w:r></w:p></w:body></w:document>""");
        Assert.Equal("Primera línea\nSegunda\ttabla\n", FileText.Read(docx)!.Text);

        var html = FileText.Read(Write("page.html", Encoding.UTF8.GetBytes("<html><head><title>T</title><script>x()</script></head><body><p>Texto de la página</p></body></html>")))!;
        Assert.Contains("Texto de la página", html.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("x()", html.Text, StringComparison.Ordinal);

        var pdf = FileText.Read(Pdf("factura.pdf", "Factura 2026-114", "Total a pagar 87,40 euros"))!;
        Assert.Equal(2, pdf.Pages);
        Assert.Contains("Factura 2026-114", pdf.Text, StringComparison.Ordinal);
        Assert.Equal(2, pdf.PageAt(pdf.Text.IndexOf("Total", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ALongTextGivesThePartsThatAnswer()
    {
        // 40 paragraphs about nothing, and one about the deadline.
        var text = new StringBuilder();
        for (var i = 0; i < 40; i++)
        {
            if (i == 27) text.Append("La fecha límite para entregar la documentación es el 15 de marzo de 2027, sin prórroga posible. ");
            text.Append("Este párrafo habla del tiempo, de las nubes y de cosas que no importan nada para la pregunta. ").Append(i).Append(".\n\n");
            text.Append(string.Concat(Enumerable.Repeat("Relleno sin sentido para ocupar sitio. ", 12))).Append("\n\n");
        }
        var content = new FileContent(text.ToString(), [0]);
        var chunks = LongText.Chunks(content);
        Assert.True(chunks.Count > 12);
        Assert.All(chunks, c => Assert.InRange(c.Text.Length, 1, LongText.ChunkChars));
        Assert.InRange(chunks.Sum(c => c.Text.Length), text.Length - 2000, text.Length);

        var best = await LongText.BestAsync(chunks, "¿cuál es la fecha límite de entrega?", null, "a", 3000, CancellationToken.None);
        Assert.Contains(best, c => c.Text.Contains("15 de marzo de 2027", StringComparison.Ordinal));
        Assert.True(best.Count <= 2);

        // By meaning: the question shares no word with the answer, the vectors find it.
        var meaning = new FakeMeaning(t => t.Contains("límite", StringComparison.Ordinal) || t.Contains("cuándo", StringComparison.Ordinal) ? 1 : 0);
        var found = await LongText.BestAsync(chunks, "¿hasta cuándo tengo?", meaning, "b", 1300, CancellationToken.None);
        Assert.Contains("15 de marzo", Assert.Single(found).Text, StringComparison.Ordinal);

        var spread = LongText.Spread(chunks, 6000);
        Assert.Equal(0, spread[0].Index);
        Assert.Equal(chunks[^1].Index, spread[^1].Index);
    }

    [Fact]
    public async Task ReadFileTakesTheGivenFileAndSaysTheirPages()
    {
        var pages = Enumerable.Range(1, 30).Select(i => i == 23 ? "El importe total es de 4.312 euros." : $"Página {i}: " + string.Concat(Enumerable.Repeat("texto de relleno ", 30))).ToArray();
        var pdf = Pdf("informe.pdf", pages);
        var tool = new ReadFileTool();
        var context = new ToolContext("dot") { Files = [pdf] };
        var whole = await tool.RunAsync(Json("""{"question":"¿cuál es el importe total?"}"""), context, CancellationToken.None);
        Assert.Contains("[page 23]", whole.Text, StringComparison.Ordinal);
        Assert.Contains("4.312 euros", whole.Text, StringComparison.Ordinal);
        Assert.Contains("30 pages", whole.Text, StringComparison.Ordinal);

        var small = Write("nota.txt", Encoding.UTF8.GetBytes("Comprar pan."));
        var read = await tool.RunAsync(Json($$"""{"path":"{{small.Replace("\\", "\\\\")}}"}"""), new ToolContext("dot"), CancellationToken.None);
        Assert.Contains("Comprar pan.", read.Text, StringComparison.Ordinal);
        Assert.False(read.Failed);
        Assert.True((await tool.RunAsync(Json("{}"), new ToolContext("dot"), CancellationToken.None)).Failed);
    }

    [Fact]
    public async Task MovingIsUndoneAndDeletingNeverHappensWithoutAsking()
    {
        var file = Write("mover.txt", [65]);
        var to = Path.Combine(_folder, "destino");
        Directory.CreateDirectory(to);
        var move = new MoveFileTool();
        var moved = await move.RunAsync(Json($$"""{"to":"{{to.Replace("\\", "\\\\")}}"}"""), new ToolContext("dot") { Files = [file] }, CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(to, "mover.txt")));
        Assert.True(await move.UndoAsync(moved.Undo!, CancellationToken.None));
        Assert.True(File.Exists(file));

        // The user's policy is «never ask»: deleting asks anyway, and a «no» deletes nothing.
        var recycled = new List<string>();
        var registry = new ToolRegistry();
        registry.Add(new DeleteFileTool(p => { recycled.Add(p); return true; }));
        var asked = new List<string>();
        var runner = new ToolRunner(registry, () => new HashSet<string>(StringComparer.Ordinal), (question, _) => { asked.Add(question.Text); return Task.FromResult("no"); }, _ => { },
            Path.Combine(_folder, "undo.json"), askBefore: () => AskPolicy.Never);
        await runner.RunAsync("delete_file", Json("{}"), new ToolContext("dot") { Files = [file] }, "bórralo", "ai", CancellationToken.None);
        Assert.Single(asked);
        Assert.Empty(recycled);
    }

    [Fact]
    public async Task AnOrderWithFilesGoesToTheAiWithThemAndOpeningNeedsNone()
    {
        var file = Write("factura.pdf", [37]);
        var builtin = new BuiltinTools(Path.Combine(_folder, "data"));
        using var _ = builtin;
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        var runner = new ToolRunner(registry, () => new HashSet<string>(StringComparer.Ordinal), (_, _) => Task.FromResult("yes"), _ => { }, Path.Combine(_folder, "undo.json"));
        var model = new Scripted();
        model.Answers.Enqueue("Es una factura de la luz.");
        var talk = new TalkService(_ => (model, "fake:4b"), runner, _ => AssistantPersona.Module(), new Silent());

        await talk.HandleAsync(new HeardMessage("Resúmeme «factura.pdf».", "bubble", "dot", [file]));
        var request = Assert.Single(model.Requests);
        Assert.Contains(request.Tools, t => t.Name == "read_file");
        Assert.Contains(request.Tools, t => t.Name == "delete_file");
        Assert.Contains(file, request.Messages[^1].Content, StringComparison.Ordinal);

        // Later, «bórralo» still has the file at hand.
        model.Answers.Enqueue("¿Seguro?");
        await talk.HandleAsync(new HeardMessage("¿y qué fecha tiene?", "bubble", "dot"));
        Assert.Contains(file, model.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Vectors of one number: what the test says each text means.</summary>
    private sealed class FakeMeaning(Func<string, float> meaning) : ITextMeaning
    {
        public Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>?>([.. texts.Select(t => new[] { meaning(t), 1 - meaning(t) })]);
    }

    private sealed class Silent : ITalkOutput
    {
        public void Chat(ChatLineMessage line) { }

        public void State(string? assistant, string state) { }

        public Task SpeakAsync(string? assistant, string text, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Scripted : IChatModel
    {
        public Queue<string> Answers { get; } = new();

        public List<ChatRequest> Requests { get; } = [];

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string answer;
            lock (Requests)
            {
                Requests.Add(request);
                answer = Answers.TryDequeue(out var next) ? next : "Vale.";
            }
            await Task.Yield();
            yield return new ChatChunk(answer);
            yield return new ChatChunk("", Done: true);
        }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<IReadOnlyList<string>> ListLoadedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task PreloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UnloadAsync(string model, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() { }
    }
}
