using KDesktop.Core.Voice;

namespace KDesktop.Core.Tests;

/// <summary>5.0.200: the wake word said while talking about the assistant, not to it, starts nothing.</summary>
public class WakePassingTests
{
    private static readonly WakeWord Lander = new() { Name = "Lander", Variants = ["Lander", "Land."] };
    private static readonly WakeWord KDesktop = new() { Name = "KDesktop", Variants = ["KDesktop", "cadesco"] };

    private static bool Passing(string text, WakeWord word) =>
        WakeWordVerifier.Find(text, word) is { } match && WakeWordVerifier.SaidInPassing(text, match);

    /// <summary>How the user's real calls were transcribed (registros 20–25/10): all of them talk to it.</summary>
    [Theory]
    [InlineData("Lander")]
    [InlineData("Lander.")]
    [InlineData("Vale, ahora Lander")]
    [InlineData("Eh. Vale, Lander.")]
    [InlineData("Uh Lander.")]
    [InlineData("En lander.")]
    [InlineData("Pero Lander")]
    [InlineData("Oye Lander, abre Steam")]
    [InlineData("Gracias por todo. Lander, ¿qué hora es?")]
    [InlineData("Hey Lander")]
    public void TalkingToIt(string text) => Assert.False(Passing(text, Lander));

    [Theory]
    [InlineData("le estaba enseñando a mi hermano el Lander")]
    [InlineData("mi amigo dice que Lander abre steam")]
    [InlineData("tengo que arreglar lo de la palabra de Lander")]
    [InlineData("I was showing my brother the Lander")]
    public void TalkingAboutIt(string text) => Assert.True(Passing(text, Lander));

    [Fact]
    public void OtherWordsToo()
    {
        Assert.False(Passing("Oye, KDesktop, vacía la papelera", KDesktop));
        Assert.True(Passing("sí, el programa se llama KDesktop", KDesktop));
    }
}
