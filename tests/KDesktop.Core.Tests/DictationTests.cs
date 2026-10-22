using KDesktop.Core.Voice;

namespace KDesktop.Core.Tests;

/// <summary>Stage 10.5: how the dictated pieces are typed, and the orders said while dictating.</summary>
public class DictationTests
{
    /// <summary>What would be typed, an erase written as «⌫n».</summary>
    private sealed class Screen : IDictationOutput
    {
        public string Text { get; private set; } = string.Empty;

        public void Type(string text) => Text += text;

        public void Erase(int characters) => Text = Text[..^characters];
    }

    private static (Screen Screen, DictationWriter Writer) Start()
    {
        var screen = new Screen();
        return (screen, new DictationWriter(screen));
    }

    [Fact]
    public void PiecesAreJoinedWithASpaceAndCounted()
    {
        var (screen, writer) = Start();
        writer.Write("Hola, ¿qué tal?");
        writer.Write("  Mañana nos vemos.  ");
        writer.Write("");
        Assert.Equal("Hola, ¿qué tal? Mañana nos vemos.", screen.Text);
        Assert.Equal(6, writer.Words);
        Assert.Equal(2, writer.Pieces);
        Assert.Equal("Mañana nos vemos.", writer.Last);
    }

    [Fact]
    public void LineBreaksSaidAloneOrAtTheEnd()
    {
        var (screen, writer) = Start();
        writer.Write("Querida Ana.");
        Assert.Equal(DictationOrder.NewLine, writer.Write("Nueva línea."));
        writer.Write("te escribo desde casa. Punto y aparte.");
        writer.Write("Un abrazo, nueva línea");
        writer.Write("Víctor");
        Assert.Equal("Querida Ana.\nTe escribo desde casa.\n\nUn abrazo\nVíctor", screen.Text);
    }

    [Fact]
    public void DeleteThatTakesBackThePiecesOneByOne()
    {
        var (screen, writer) = Start();
        writer.Write("Uno.");
        writer.Write("Dos.");
        writer.Write("Tres.");
        Assert.Equal(DictationOrder.Undo, writer.Write("Borra eso."));
        Assert.Equal("Uno. Dos.", screen.Text);
        writer.Write("Bórralo");
        writer.Write("Cuatro.");
        Assert.Equal("Uno. Cuatro.", screen.Text);
        writer.Write("Borra eso");
        writer.Write("borra eso");
        writer.Write("borra eso"); // nothing left: nothing happens
        writer.Write("cinco.");
        Assert.Equal("Cinco.", screen.Text);
    }

    [Fact]
    public void DeletingAfterABreakStartsTheLineAgain()
    {
        var (screen, writer) = Start();
        writer.Write("Hola. Punto y aparte.");
        writer.Write("Mañana al cine.");
        writer.Write("Borra eso.");
        writer.Write("mañana al teatro.");
        Assert.Equal("Hola.\n\nMañana al teatro.", screen.Text);
    }

    [Theory]
    [InlineData("Deja de dictar.", DictationOrder.Stop)]
    [InlineData("Stop dictating", DictationOrder.Stop)]
    [InlineData("New paragraph.", DictationOrder.NewParagraph)]
    [InlineData("Lösch das!", DictationOrder.Undo)]
    [InlineData("Cancella l'ultimo.", DictationOrder.Undo)]
    [InlineData("À la ligne.", DictationOrder.NewLine)]
    public void TheOrdersAreUnderstoodInEveryLanguage(string said, DictationOrder order) =>
        Assert.Equal((string.Empty, order), DictationWriter.Orders.Split(said));

    [Theory]
    [InlineData("Voy a dejar de dictar mañana.")]
    [InlineData("Borra eso de la lista.")]
    [InlineData("Escribe una nueva línea de código.")]
    public void TheWordsInsideASentenceAreText(string said)
    {
        var (screen, writer) = Start();
        Assert.Equal(DictationOrder.None, writer.Write(said));
        Assert.Equal(said, screen.Text);
    }

    [Fact]
    public void StopDictatingWritesNothing()
    {
        var (screen, writer) = Start();
        writer.Write("Hecho.");
        Assert.Equal(DictationOrder.Stop, writer.Write("Fin del dictado."));
        Assert.Equal("Hecho.", screen.Text);
    }
}
