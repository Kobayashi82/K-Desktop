using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 8.3: reading. The screen through the model's eyes (a picture to a model that sees, the answer as text) and web
/// pages as text (the one given or the one open in the browser).
/// </summary>
public sealed class SightTests
{
    private static JsonElement Args(object arguments) => JsonSerializer.SerializeToElement(arguments);

    private sealed class FakeScreen(string? address = null) : IScreen
    {
        public int Captures { get; private set; }

        public bool Whole { get; private set; }

        public ScreenUse Use { get; private set; }

        public Task<ScreenPicture?> CaptureAsync(bool wholeScreen, ScreenUse use, CancellationToken cancellationToken)
        {
            Captures++;
            Whole = wholeScreen;
            Use = use;
            var pixels = use.HasFlag(ScreenUse.Read) ? new BgraImage(640, 480, new byte[640 * 480 * 4]) : null;
            byte[] jpeg = use.HasFlag(ScreenUse.See) ? [0xFF, 0xD8, 0xFF] : [];
            return Task.FromResult<ScreenPicture?>(new ScreenPicture(jpeg, "the window «Error» of setup", 640, 480, "Error", pixels, new ScreenRect(100, 50, 640, 480)));
        }

        /// <summary>Who looked at what, after each capture.</summary>
        public List<(string? Assistant, int Captures, ScreenRect? Area)> Looks { get; } = [];

        public void Looked(string? assistant, ScreenPicture picture) => Looks.Add((assistant, Captures, picture.Area));

        public Task<string?> BrowserAddressAsync(CancellationToken cancellationToken) => Task.FromResult(address);
    }

    private sealed class FakeReader(string text) : IScreenReader
    {
        public int Reads { get; private set; }

        public bool CanRead => true;

        public Task<ReadText?> ReadAsync(BgraImage image, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<ReadText?>(new ReadText(text, text.Split('\n').Length, "es-ES"));
        }
    }

    private sealed class FakeVision(bool sees, string answer = "") : IVision
    {
        public string? Asked { get; private set; }

        public bool CanSee(string? assistant) => sees;

        public Task<string?> LookAsync(string? assistant, byte[] jpeg, string question, CancellationToken cancellationToken)
        {
            Asked = question;
            return Task.FromResult<string?>(answer);
        }
    }

    [Fact]
    public async Task AModelThatSeesReadsTheWindowInFront()
    {
        var screen = new FakeScreen();
        var vision = new FakeVision(true, "The error says: \"Falta MSVCP140.dll\".");
        var result = await new LookAtScreenTool(screen, vision).RunAsync(Args(new { question = "read the error" }), new ToolContext("lander"), default);
        Assert.False(result.Failed);
        Assert.Contains("MSVCP140.dll", result.Text, StringComparison.Ordinal);
        Assert.Contains("read the error", vision.Asked, StringComparison.Ordinal);
        Assert.False(screen.Whole);
        Assert.Contains("Error", result.Done, StringComparison.Ordinal);

        await new LookAtScreenTool(screen, vision).RunAsync(Args(new { whole_screen = true }), new ToolContext("lander"), default);
        Assert.True(screen.Whole);
    }

    [Fact]
    public async Task TextIsReadByTheOcrAndPicturesAreLeftToTheModel()
    {
        var screen = new FakeScreen();
        var vision = new FakeVision(true, "A red logo with a rocket.");
        var reader = new FakeReader("No se encuentra MSVCP140.dll\nVuelve a instalar el programa");
        var tool = new LookAtScreenTool(screen, vision, reader);

        // «¿Qué pone en el error?»: the OCR, not the model.
        var read = await tool.RunAsync(Args(new { question = "qué pone en el error" }), new ToolContext("lander", "¿qué pone en el error?"), default);
        Assert.Contains("MSVCP140.dll", read.Text, StringComparison.Ordinal);
        Assert.Null(vision.Asked);
        Assert.Equal(1, reader.Reads);
        Assert.True(screen.Use.HasFlag(ScreenUse.Read));

        // Its figure scans what it read, once the picture is taken.
        Assert.Equal(("lander", 1, new ScreenRect(100, 50, 640, 480)), Assert.Single(screen.Looks));

        // The model says so itself.
        await tool.RunAsync(Args(new { question = "the error", read_text = true }), new ToolContext("lander"), default);
        Assert.Null(vision.Asked);
        Assert.Equal(2, reader.Reads);

        // «¿De qué color es el logo?»: the model looks at it.
        var seen = await tool.RunAsync(Args(new { question = "what colour is the logo" }), new ToolContext("lander", "¿de qué color es el logo?"), default);
        Assert.Contains("rocket", seen.Text, StringComparison.Ordinal);
        Assert.Equal(2, reader.Reads);
        Assert.False(screen.Use.HasFlag(ScreenUse.Read));
    }

    [Fact]
    public async Task WithoutVisionTheOcrStillReads()
    {
        var screen = new FakeScreen();
        var result = await new LookAtScreenTool(screen, new FakeVision(false), new FakeReader("Total: 1.234,56 €"))
            .RunAsync(Args(new { question = "what is this" }), new ToolContext(null), default);
        Assert.False(result.Failed);
        Assert.Contains("1.234,56", result.Text, StringComparison.Ordinal);
        Assert.False(screen.Use.HasFlag(ScreenUse.See));

        var nothing = await new LookAtScreenTool(screen, new FakeVision(false), new FakeReader(""))
            .RunAsync(Args(new { }), new ToolContext(null), default);
        Assert.Contains("No text", nothing.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenTheOcrFindsNoTextTheModelLooks()
    {
        var vision = new FakeVision(true, "Only a photo of a beach.");
        var result = await new LookAtScreenTool(new FakeScreen(), vision, new FakeReader(""))
            .RunAsync(Args(new { question = "read it", read_text = true }), new ToolContext("lander"), default);
        Assert.Contains("beach", result.Text, StringComparison.Ordinal);
        Assert.NotNull(vision.Asked);
    }

    [Theory]
    [InlineData("¿Qué pone aquí?", true)]
    [InlineData("léeme el mensaje de error", true)]
    [InlineData("copia el texto de esta ventana", true)]
    [InlineData("what does this say", true)]
    [InlineData("¿qué ves en mi pantalla?", false)]
    [InlineData("¿de qué color es el botón?", false)]
    public void WhatAsksForTheText(string said, bool text)
    {
        Assert.Equal(text, LookAtScreenTool.WantsText(said, null));
    }

    [Fact]
    public async Task WindowsReadsTheTextOfAPicture()
    {
        if (!Core.Platform.ScreenText.Available) return; // no OCR language in this Windows
        BgraImage? image = null;
        var thread = new Thread(() => image = Rendered("Error 0x80070005: Acceso denegado", "El año pasado gastamos 1.234,56 €"));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        var read = await Core.Platform.ScreenText.ReadAsync(image!, "es");
        Assert.NotNull(read);
        Assert.Contains("0x80070005", read.Text, StringComparison.Ordinal);
        Assert.Contains("El año pasado", read.Text, StringComparison.Ordinal);
        Assert.Contains("1.234,56", read.Text, StringComparison.Ordinal);
        Assert.Equal(2, read.Lines);
        Assert.Equal("", (await Core.Platform.ScreenText.ReadAsync(new BgraImage(300, 100, Enumerable.Repeat((byte)255, 300 * 100 * 4).ToArray())))?.Text);
    }

    [Theory]
    [InlineData("EI año pasado", "El año pasado")]
    [InlineData("versión 5.O.143", "versión 5.0.143")]
    [InlineData("1l0 euros", "110 euros")]
    [InlineData("OK, I agree", "OK, I agree")]
    [InlineData("EI", "EI")]
    public void ScreenFontsMistakesAreTidied(string seen, string meant) => Assert.Equal(meant, Core.Platform.ScreenText.Tidy(seen));

    /// <summary>Lines of text as a screen draws them (Segoe UI, 12 px, on white), at 96 dpi; on an STA thread.</summary>
    private static BgraImage Rendered(params string[] lines)
    {
        const int width = 700, line = 18;
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, width, line * lines.Length + 20));
            var face = new System.Windows.Media.Typeface("Segoe UI");
            for (var i = 0; i < lines.Length; i++)
                dc.DrawText(new System.Windows.Media.FormattedText(lines[i], System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                    face, 12, System.Windows.Media.Brushes.Black, 1), new System.Windows.Point(10, 10 + i * line));
        }
        var height = line * lines.Length + 20;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return new BgraImage(width, height, pixels);
    }

    [Fact]
    public void WhatTheModelSaysOverAndOverIsCutToOnce()
    {
        var said = "La ruta es `cd \"C:\\Programas" + string.Concat(Enumerable.Repeat("\\Utilidades\\backups", 30)) + "`";
        var cut = KDesktop.Assistant.Brain.ModelVision.WithoutRepeats(said);
        Assert.True(cut.Length < 60, cut);
        Assert.Equal("Hola, hola. Todo bien.", KDesktop.Assistant.Brain.ModelVision.WithoutRepeats("Hola, hola. Todo bien."));
    }

    [Fact]
    public async Task AModelThatDoesNotSeeSaysSoWithoutCapturing()
    {
        var screen = new FakeScreen();
        var result = await new LookAtScreenTool(screen, new FakeVision(false)).RunAsync(Args(new { }), new ToolContext(null), default);
        Assert.True(result.Failed);
        Assert.Equal(0, screen.Captures);
        Assert.Contains("Cerebro", result.Reply, StringComparison.Ordinal);
    }

    [Fact]
    public void APagesTextWithoutScriptsMenusOrTags()
    {
        const string html = """
            <html><head><title>Receta de lentejas &amp; chorizo</title><style>p{color:red}</style><script>var x = "<p>no</p>";</script></head>
            <body><nav><a href="/">Inicio</a> <a href="/menu">Menú</a></nav>
            <article><h1>Lentejas</h1><p>Pon las lentejas&nbsp;en remojo.</p><p>Cuécelas 40 minutos con chorizo, cebolla y pimentón.</p>
            <p>Sírvelas calientes; están mejor al día siguiente, cuando el caldo ha espesado y todo ha cogido sabor. Puedes congelarlas en raciones.</p>
            <p>Si las quieres más ligeras, quita el chorizo y añade verduras: zanahoria, puerro y pimiento verde, todo bien picado.</p></article>
            <footer>© 2026 Recetas</footer></body></html>
            """;
        var page = WebText.Of(html, 9000);
        Assert.Equal("Receta de lentejas & chorizo", page.Title);
        Assert.Contains("Pon las lentejas en remojo.", page.Text, StringComparison.Ordinal);
        Assert.Contains("Cuécelas 40 minutos", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Inicio", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("var x", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("color:red", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<", page.Text, StringComparison.Ordinal);
        Assert.False(page.Cut);

        var cut = WebText.Plain(string.Join(' ', Enumerable.Repeat("Una frase corta.", 200)), 300);
        Assert.True(cut.Cut);
        Assert.True(cut.Text.Length <= 300);
        Assert.EndsWith(".", cut.Text, StringComparison.Ordinal);
    }

    private sealed class FakeWeb(string html, string type = "text/html; charset=utf-8") : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8) };
            response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(type);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task TheGivenPageOrTheOneOpenInTheBrowser()
    {
        var web = new FakeWeb("<html><title>Tiempo</title><body><main><p>Mañana lloverá en Madrid por la tarde, con 14 grados de máxima y viento del oeste.</p></main></body></html>");
        var given = await new ReadWebTool(new FakeScreen(), new WebReader(web)).RunAsync(Args(new { url = "aemet.es/madrid" }), new ToolContext(null), default);
        Assert.Equal("https://aemet.es/madrid", web.Asked?.ToString());
        Assert.Contains("lloverá en Madrid", given.Text, StringComparison.Ordinal);
        Assert.Contains("Tiempo", given.Done, StringComparison.Ordinal);

        var open = await new ReadWebTool(new FakeScreen("www.elpais.com/noticia"), new WebReader(web)).RunAsync(Args(new { }), new ToolContext(null), default);
        Assert.False(open.Failed);
        Assert.Equal("https://www.elpais.com/noticia", web.Asked?.ToString());

        var none = await new ReadWebTool(new FakeScreen(null), new WebReader(web)).RunAsync(Args(new { }), new ToolContext(null), default);
        Assert.True(none.Failed);

        var pdf = await new ReadWebTool(null, new WebReader(new FakeWeb("%PDF", "application/pdf"))).RunAsync(Args(new { url = "x.com/a.pdf" }), new ToolContext(null), default);
        Assert.True(pdf.Failed);
    }

    /// <summary>Keeps the body of the chat request and answers it as finished at once.</summary>
    private sealed class BodyCatcher(string answer) : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null) Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8) };
        }
    }

    [Fact]
    public async Task APictureGoesAsPartsToOpenAIServersAndAsImagesToOllama()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2 };
        Assert.Equal("image/png", ModelHttp.ImageType(png));
        Assert.Equal("image/jpeg", ModelHttp.ImageType([0xFF, 0xD8]));
        var request = new ChatRequest("m", [ChatMessage.UserWithImage("¿qué ves?", png)], []);
        var base64 = Convert.ToBase64String(png);

        var openAi = new BodyCatcher("data: [DONE]\n\n");
        using (var model = new OpenAICompatibleChatModel("http://127.0.0.1:1/v1", http: new HttpClient(openAi)))
            await foreach (var _ in model.StreamAsync(request, default)) { }
        using (var body = JsonDocument.Parse(openAi.Body))
        {
            var content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
            Assert.Equal("¿qué ves?", content[0].GetProperty("text").GetString());
            Assert.Equal("data:image/png;base64," + base64, content[1].GetProperty("image_url").GetProperty("url").GetString());
        }

        var ollama = new BodyCatcher("{\"message\":{\"content\":\"\"},\"done\":true}\n");
        using (var model = new OllamaChatModel("http://127.0.0.1:1", new HttpClient(ollama)))
            await foreach (var _ in model.StreamAsync(request, default)) { }
        using (var body = JsonDocument.Parse(ollama.Body))
        {
            var message = body.RootElement.GetProperty("messages")[0];
            Assert.Equal("¿qué ves?", message.GetProperty("content").GetString());
            Assert.Equal(base64, message.GetProperty("images")[0].GetString());
        }
    }
}
