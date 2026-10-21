using KDesktop.Core.Assistant;

namespace KDesktop.Core.Tests;

/// <summary>Stage 5.7: an assistant's own voice, heard back after a wake word, is not taken for an order.</summary>
public class SelfEchoTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0);

    [Fact]
    public void WhatItIsSayingIsAnEcho()
    {
        var echo = new SelfEcho();
        echo.Said("lander/1", "Hola, soy el Lander. He ordenado tu escritorio y ahora abro el navegador.", Now);
        // The recognizer misses a word or two, and the wake word cut off the start.
        Assert.True(echo.IsEcho("y ahora abro navegador", Now.AddSeconds(3), out var said));
        Assert.Contains("navegador", said);
        Assert.True(echo.IsEcho("hola soy el lander", Now.AddSeconds(1), out _));
    }

    [Fact]
    public void ShortOrdersAreNeverAnEcho()
    {
        var echo = new SelfEcho();
        echo.Said("lander/1", "Para que te des cuenta, calla un momento y escucha.", Now);
        Assert.False(echo.IsEcho("para", Now.AddSeconds(1), out _));
        Assert.False(echo.IsEcho("calla ya", Now.AddSeconds(1), out _));
    }

    [Fact]
    public void AnOrderOfYourOwnIsNotAnEcho()
    {
        var echo = new SelfEcho();
        echo.Said("lander/1", "Son las doce y cuarto. ¿Quieres que te avise de algo?", Now);
        Assert.False(echo.IsEcho("abre la carpeta de descargas", Now.AddSeconds(2), out _));
        Assert.False(echo.IsEcho("avísame en diez minutos de la pizza", Now.AddSeconds(2), out _));
    }

    [Fact]
    public void OnceItHasHadTimeToSayItItIsNoLongerAnEcho()
    {
        var echo = new SelfEcho();
        const string text = "¿Abro la carpeta de descargas?";
        echo.Said("lander/1", text, Now);
        Assert.True(echo.IsEcho("abro la carpeta de descargas", Now.AddSeconds(1), out _));
        // You say it back once it has finished: an order.
        Assert.False(echo.IsEcho("abre la carpeta de descargas", Now + SelfEcho.SayingTime(text) + TimeSpan.FromSeconds(2), out _));
    }

    [Fact]
    public void AnAnswerBeingWrittenKeepsSounding()
    {
        var echo = new SelfEcho();
        echo.Said("dot/7", "Vale,", Now);
        echo.Said("dot/7", "Vale, pongo un temporizador de diez minutos para la pizza.", Now.AddSeconds(1));
        Assert.True(echo.IsEcho("un temporizador de diez minutos", Now.AddSeconds(4), out _));
    }

    [Fact]
    public void WordsInOrderAreCounted()
    {
        Assert.Equal(3, SelfEcho.InOrder(["a", "c", "e"], ["a", "b", "c", "d", "e"]));
        Assert.Equal(1, SelfEcho.InOrder(["e", "a"], ["a", "b", "c", "d", "e"]));
        // A transcription slip still counts: «navegador» / «navegadores».
        Assert.Equal(2, SelfEcho.InOrder(["abre", "navegadores"], ["abro", "el", "navegador"]));
    }
}
