using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Assistant.Tools.Knowledge;
using KDesktop.Core.Assistant;
using KDesktop.Core.Platform;
using KDesktop.Core.Voice;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Phase 19: the folders of documents the assistants know. They are read once (text, Markdown, code, Word, web pages, PDF,
/// and pictures and scanned pages with the OCR), kept on disk and loaded again without reading anything that did not change,
/// watched, searched by words and meaning (search_knowledge), and the reading waits when it is told to.
/// </summary>
public sealed class KnowledgeTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "kdesktop-knowledge-" + Guid.NewGuid().ToString("N")[..8]);

    public KnowledgeTests()
    {
        Directory.CreateDirectory(Docs);
        Directory.CreateDirectory(Store);
    }

    private string Docs => Path.Combine(_root, "Documentos");

    private string Store => Path.Combine(_root, "Knowledge");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(Docs, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.UTF8);
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
        var path = Path.Combine(Docs, name);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static async Task Until(Func<bool> done, string what)
    {
        var until = DateTime.UtcNow + Patience;
        while (!done())
        {
            if (DateTime.UtcNow > until) Assert.Fail("No llega: " + what);
            await Task.Delay(50);
        }
    }

    private static async Task ReadyAsync(KnowledgeBase knowledge, int files) =>
        await Until(() => knowledge.State() is { Phase: "ready" } s && s.Files == files && s.Done == files, $"{files} archivos leídos ({knowledge.State()})");

    private KnowledgeBase Make(ITextMeaning? meaning = null, IScreenReader? ocr = null) =>
        new(Store, meaning, ocr) { Rest = 0 };

    [Theory]
    [InlineData("facturas", "factura")]
    [InlineData("papeles", "papel")]
    [InlineData("papel", "papel")]
    [InlineData("tomates", "tomate")]
    [InlineData("tomate", "tomate")]
    [InlineData("canciones", "cancion")]
    [InlineData("invoices", "invoice")]
    [InlineData("luz", "luz")]
    [InlineData("2026", "2026")]
    [InlineData("mes", "mes")]
    public void PluralsComeOffTheSameInDocumentsAndQuestions(string word, string stem) => Assert.Equal(stem, KnowledgeIndex.Stem(word));

    [Fact]
    public void ADiskTheSystemOrAFolderAlreadyThereAreNotFolders()
    {
        Assert.Equal("root", KnowledgeFolders.Problem("C:\\", []));
        Assert.Equal("system", KnowledgeFolders.Problem(Environment.GetFolderPath(Environment.SpecialFolder.Windows), []));
        Assert.Equal("system", KnowledgeFolders.Problem(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), []));
        Assert.Equal("missing", KnowledgeFolders.Problem(Path.Combine(_root, "no-existe"), []));
        // The temporary folder is in AppData: not documents.
        Assert.Equal("system", KnowledgeFolders.Problem(Docs, []));
        var mine = Path.Combine(AppContext.BaseDirectory, "knowledge-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(mine, "Facturas"));
        try
        {
            Assert.Null(KnowledgeFolders.Problem(Path.Combine(mine, "Facturas"), []));
            Assert.Equal("already", KnowledgeFolders.Problem(Path.Combine(mine, "Facturas"), [mine]));
        }
        finally
        {
            Directory.Delete(mine, true);
        }
        Assert.Equal([_root], KnowledgeFolders.Clean([Docs, _root, _root + "\\", Path.Combine(Docs, "a.txt")]));
        // Files: one that can be read, inside a chosen folder, and one that cannot.
        var program = Path.Combine(AppContext.BaseDirectory, "KDesktop.Assistant.Tests.dll");
        Assert.Equal("kind", KnowledgeFolders.Problem(program, []));
        Assert.True(KnowledgeFolders.Readable("factura.PDF", false));
        Assert.False(KnowledgeFolders.Readable("foto.jpg", false));
        Assert.True(KnowledgeFolders.Readable("foto.jpg", true));
    }

    [Fact]
    public async Task EachKindOfDocumentIsReadAndFoundByItsWords()
    {
        Write("notas/compra.md", "# Lista\n\nComprar tomates, pan y aceite de oliva.");
        Write("codigo/config.py", "# Puerto del servidor\nSERVER_PORT = 8754\n");
        var docx = Path.Combine(Docs, "carta.docx");
        using (var zip = ZipFile.Open(docx, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write("""<w:document xmlns:w="w"><w:body><w:p><w:r><w:t>La fianza del piso es de dos mensualidades.</w:t></w:r></w:p></w:body></w:document>""");
        Pdf("factura-luz.pdf", "Factura de la luz de septiembre", "Total a pagar: 64,32 euros. Referencia ES-4471.");
        Write("foto.png", "no es una imagen");
        Write("node_modules/paquete/leeme.md", "Esto no se lee: es de un programa.");
        // A web page saved with the browser: the page is read, its folder of styles and scripts is not; minified code has no text.
        Write("pagina_files/estilos.css", "body { color: red; }");
        Write("pagina.html", "<html><body><p>El horario de la piscina es de nueve a dos.</p></body></html>");
        Write("app.min-ish.js", string.Concat(Enumerable.Repeat("var a=1;function b(){return a+2}", 200)));
        Write("programa.exe", "MZ");

        using var knowledge = Make();
        knowledge.Configure([Docs], readPictures: false);
        knowledge.Start();
        await ReadyAsync(knowledge, 6);
        Assert.True(knowledge.State().WordsOnly);
        Assert.EndsWith("pagina.html", (await knowledge.SearchAsync("horario de la piscina", 6, null, CancellationToken.None)).Hits[0].Path, StringComparison.Ordinal);
        Assert.Equal("empty", knowledge.FilesIn(Docs, "app.min-ish").Files.Single().State);

        var pdf = Assert.Single((await knowledge.SearchAsync("¿cuánto pagué de luz? ES-4471", 6, null, CancellationToken.None)).Hits);
        Assert.EndsWith("factura-luz.pdf", pdf.Path, StringComparison.Ordinal);
        Assert.Equal(2, pdf.Page);
        Assert.Contains("64,32", pdf.Text, StringComparison.Ordinal);
        Assert.Contains("SERVER_PORT", (await knowledge.SearchAsync("puerto del servidor 8754", 6, null, CancellationToken.None)).Hits[0].Text, StringComparison.Ordinal);
        Assert.EndsWith("carta.docx", (await knowledge.SearchAsync("fianza del piso", 6, null, CancellationToken.None)).Hits[0].Path, StringComparison.Ordinal);
        // Plurals and the file's name count too.
        Assert.EndsWith("compra.md", (await knowledge.SearchAsync("los tomates", 6, null, CancellationToken.None)).Hits[0].Path, StringComparison.Ordinal);
        Assert.Empty((await knowledge.SearchAsync("receta de tortilla de patatas", 6, null, CancellationToken.None)).Hits);
        Assert.Empty((await knowledge.SearchAsync("es de un programa", 6, null, CancellationToken.None)).Hits);

        var files = knowledge.FilesIn(Docs, filter: "ready");
        Assert.Equal(5, files.Total);
        Assert.All(files.Files, f => Assert.Equal("ready", f.State));
    }

    [Fact]
    public async Task TheUserPausesTheReadingAndTakesFilesOut()
    {
        Write("a.txt", "El código de la alarma es 4512.");
        var wifi = Write("sub/b.txt", "La contraseña del wifi está en la pegatina del router.");
        using var knowledge = Make();
        // Stopped from the start: it looks at the folder but reads nothing.
        knowledge.Configure([Docs], false, paused: true);
        knowledge.Start();
        await Until(() => knowledge.State() is { Phase: "paused", Paused: "user", Files: 2, Done: 0 }, "en pausa por el usuario");
        Assert.Equal(2, knowledge.FilesIn(Docs, filter: "waiting").Total);
        knowledge.Configure([Docs], false, paused: false);
        await ReadyAsync(knowledge, 2);

        // Taken out: forgotten and listed apart; put back: read again.
        knowledge.Configure([Docs], false, [wifi]);
        await ReadyAsync(knowledge, 1);
        Assert.Empty((await knowledge.SearchAsync("contraseña del wifi", 6, null, CancellationToken.None)).Hits);
        var files = knowledge.FilesIn(Docs);
        Assert.Equal(1, files.Total);
        Assert.Equal(1, files.Counts!["skipped"]);
        Assert.Equal("skipped", Assert.Single(knowledge.FilesIn(Docs, filter: "skipped").Files).State);
        // Words of the path within the folder.
        Assert.Empty(knowledge.FilesIn(Docs, "sub").Files);
        Assert.Single(knowledge.FilesIn(Docs, "SUB", "skipped").Files);
        knowledge.Configure([Docs], false, []);
        await ReadyAsync(knowledge, 2);
        Assert.NotEmpty((await knowledge.SearchAsync("contraseña del wifi", 6, null, CancellationToken.None)).Hits);
    }

    [Fact]
    public async Task WhatWasReadIsKeptAndOnlyTheChangesAreReadAgain()
    {
        var note = Write("a.txt", "El código de la alarma es 4512.");
        Write("b.txt", "La contraseña del wifi está en la pegatina del router.");
        using (var first = Make())
        {
            first.Configure([Docs], false);
            first.Start();
            await ReadyAsync(first, 2);
        }
        var written = Directory.GetFiles(Store, "*.kdk").ToDictionary(f => f, File.GetLastWriteTimeUtc);
        Assert.Equal(2, written.Count);

        // Again: nothing to read. One changed while it was off: only that one.
        File.WriteAllText(note, "El código de la alarma es ahora 9930.");
        File.SetLastWriteTimeUtc(note, DateTime.UtcNow.AddMinutes(1));
        using var again = Make();
        again.Configure([Docs], false);
        again.Start();
        await ReadyAsync(again, 2);
        var changed = Directory.GetFiles(Store, "*.kdk").Count(f => File.GetLastWriteTimeUtc(f) != written.GetValueOrDefault(f));
        Assert.Equal(1, changed);
        Assert.Contains("9930", (await again.SearchAsync("código de la alarma", 6, null, CancellationToken.None)).Hits[0].Text, StringComparison.Ordinal);

        // Watched while it runs: a new file, a deleted one.
        Write("c.txt", "El dentista es el martes a las cinco.");
        await Until(() => again.State().Files == 3 && again.State().Done == 3, "el archivo nuevo");
        Assert.NotEmpty((await again.SearchAsync("dentista martes", 6, null, CancellationToken.None)).Hits);
        File.Delete(note);
        await Until(() => again.State().Files == 2, "el archivo borrado");
        Assert.Empty((await again.SearchAsync("alarma", 6, null, CancellationToken.None)).Hits);
        Assert.Equal(2, Directory.GetFiles(Store, "*.kdk").Length);

        // Out of the folders: forgotten.
        again.Configure([], false);
        await Until(() => Directory.GetFiles(Store, "*.kdk").Length == 0, "olvidar al quitar la carpeta");
        Assert.Equal("off", again.State().Phase);
    }

    [Fact]
    public async Task AFileChosenByItselfIsReadAndWatchedAlone()
    {
        var contract = Write("contrato.txt", "El alquiler sube un 3 % cada enero.");
        Write("otro.txt", "Este no se ha elegido: la piscina abre en junio.");
        using var knowledge = Make();
        knowledge.Configure([contract], false);
        knowledge.Start();
        await ReadyAsync(knowledge, 1);
        Assert.NotEmpty((await knowledge.SearchAsync("alquiler enero", 6, null, CancellationToken.None)).Hits);
        Assert.Empty((await knowledge.SearchAsync("piscina junio", 6, null, CancellationToken.None)).Hits);

        File.WriteAllText(contract, "El alquiler sube un 5 % cada febrero.");
        await Until(() => knowledge.SearchAsync("alquiler febrero", 6, null, CancellationToken.None).Result.Hits.Count > 0, "el archivo cambiado");
        File.Delete(contract);
        await Until(() => knowledge.State().Files == 0, "el archivo borrado");
        Assert.Empty(Directory.GetFiles(Store, "*.kdk"));
    }

    [Fact]
    public async Task TheMeaningFindsAQuestionInOtherWordsAndAnotherModelReadsItAgain()
    {
        Write("seguro.txt", "Póliza del coche con Mapfre: cubre lunas, asistencia en carretera y robo.");
        Write("receta.txt", "Bizcocho: 200 g de harina, tres huevos y un yogur.");
        var meaning = new TopicMeaning("model-1");
        using (var knowledge = Make(meaning))
        {
            knowledge.Configure([Docs], false);
            knowledge.Start();
            await ReadyAsync(knowledge, 2);
            Assert.False(knowledge.State().WordsOnly);

            // No word in common: the vectors find it.
            var found = await knowledge.SearchAsync("¿qué me cubre si se me rompe el parabrisas del vehículo?", 6, null, CancellationToken.None);
            Assert.True(found.Mixed);
            Assert.EndsWith("seguro.txt", Assert.Single(found.Hits).Path, StringComparison.Ordinal);
        }

        // Another model next time: the documents get its vectors (from the text kept), found by meaning again.
        var embedded = meaning.Documents;
        meaning.Model = "model-2";
        using var again = Make(meaning);
        again.Configure([Docs], false);
        again.Start();
        await ReadyAsync(again, 2);
        Assert.Equal(embedded * 2, meaning.Documents);
        Assert.Single((await again.SearchAsync("parabrisas del vehículo", 6, null, CancellationToken.None)).Hits);
    }

    [Fact]
    public async Task ItWaitsWhileItIsToldAndCanBeSearchedMeanwhile()
    {
        Write("uno.txt", "La reunión con Lucía es el jueves.");
        string? reason = "game";
        using var knowledge = Make();
        knowledge.WaitReason = () => reason;
        knowledge.Configure([Docs], false);
        knowledge.Start();
        await Until(() => knowledge.State() is { Phase: "paused", Paused: "game" }, "en pausa por el juego");
        Assert.Equal(0, knowledge.State().Done);
        Assert.Empty((await knowledge.SearchAsync("reunión Lucía", 6, null, CancellationToken.None)).Hits);
        reason = null;
        await ReadyAsync(knowledge, 1);
        Assert.NotEmpty((await knowledge.SearchAsync("reunión Lucía", 6, null, CancellationToken.None)).Hits);
    }

    [Fact]
    public async Task ThePictureOfAScannedPageIsReadWithTheOcr()
    {
        if (!ScreenText.Available) return;
        var png = PngWithText("Contrato de alquiler. La fianza es de 1.800 euros.");
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddPng(new MemoryStream(png), new UglyToad.PdfPig.Core.PdfRectangle(20, 500, 575, 760));
        File.WriteAllBytes(Path.Combine(Docs, "escaneado.pdf"), builder.Build());
        File.WriteAllBytes(Path.Combine(Docs, "foto.png"), png);

        using var knowledge = Make(ocr: new WindowsScreenReader());
        knowledge.Configure([Docs], readPictures: true);
        knowledge.Start();
        await ReadyAsync(knowledge, 2);
        var hits = (await knowledge.SearchAsync("fianza alquiler", 6, null, CancellationToken.None)).Hits;
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Contains("fianza", h.Text, StringComparison.Ordinal));
        Assert.All(knowledge.FilesIn(Docs).Files, f => Assert.True(f.Ocr));
    }

    [Fact]
    public async Task TheToolSaysWhereItComesFromOrThatThereAreNoFolders()
    {
        Pdf("manual-lavadora.pdf", "Indice", "Error E21: el agua no se vacia. Limpie el filtro de la bomba.");
        using var knowledge = Make();
        var tool = new SearchKnowledgeTool(knowledge);
        var none = await tool.RunAsync(Json("""{"query":"error E21"}"""), new ToolContext("dot"), CancellationToken.None);
        Assert.True(none.Failed);
        Assert.Contains("Lo que sabe", none.Text, StringComparison.Ordinal);

        knowledge.Configure([Docs], false);
        knowledge.Start();
        await ReadyAsync(knowledge, 1);
        var result = await tool.RunAsync(Json("""{"query":"qué significa el error E21 de la lavadora"}"""), new ToolContext("dot"), CancellationToken.None);
        Assert.False(result.Failed);
        Assert.Contains("manual-lavadora.pdf, page 2", result.Text, StringComparison.Ordinal);
        Assert.Contains("Limpie el filtro", result.Text, StringComparison.Ordinal);
        Assert.Contains("manual-lavadora.pdf", result.Done, StringComparison.Ordinal);
        var nothing = await tool.RunAsync(Json("""{"query":"cumpleaños de mi madre"}"""), new ToolContext("dot"), CancellationToken.None);
        Assert.Contains("did not find", nothing.Text, StringComparison.Ordinal);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Black text on white, big enough for the OCR, as a PNG.</summary>
    private static byte[] PngWithText(string text)
    {
        byte[] bytes = [];
        var thread = new Thread(() =>
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1600, 400));
                var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Arial"), 44, Brushes.Black, 1.0) { MaxTextWidth = 1500 };
                context.DrawText(formatted, new Point(40, 80));
            }
            var bitmap = new RenderTargetBitmap(1600, 400, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            bytes = stream.ToArray();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return bytes;
    }

    /// <summary>
    /// Vectors of two numbers: «car» or «recipe», by a few words each; a question is near the documents of its topic. Counts
    /// the pieces it was asked for.
    /// </summary>
    private sealed class TopicMeaning(string model) : ITextMeaning
    {
        public string Model { get; set; } = model;

        public int Documents { get; private set; }

        public Task<TextVectors?> EmbedAsync(IReadOnlyList<string> texts, EmbedUse use, CancellationToken cancellationToken)
        {
            if (use == EmbedUse.Document) Documents += texts.Count;
            return Task.FromResult<TextVectors?>(new TextVectors(Model, [.. texts.Select(Vector)]));
        }

        private static readonly string[] Car = ["coche", "vehículo", "parabrisas", "lunas", "póliza"];

        private static readonly string[] Food = ["harina", "huevos", "bizcocho"];

        private static float[] Vector(string text)
        {
            var car = Car.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
            var food = Food.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
            return car ? [1, 0] : food ? [0, 1] : [0.7071f, 0.7071f];
        }
    }
}
