using KDesktop.Core.Assistant;

namespace KDesktop.Core.Tests;

/// <summary>Stage 5.3: «sí», «no» and «no vuelvas a preguntar» said aloud answer the little window of permission.</summary>
public class SpokenReplyTests
{
    [Theory]
    [InlineData("Sí.", SpokenReplyKind.Yes)]
    [InlineData("vale", SpokenReplyKind.Yes)]
    [InlineData("Sí, hazlo ya.", SpokenReplyKind.Yes)]
    [InlineData("claro que sí", SpokenReplyKind.Yes)]
    [InlineData("Por favor, sí.", SpokenReplyKind.Yes)]
    [InlineData("No.", SpokenReplyKind.No)]
    [InlineData("No, mejor no.", SpokenReplyKind.No)]
    [InlineData("mejor no", SpokenReplyKind.No)]
    [InlineData("Déjalo", SpokenReplyKind.No)]
    [InlineData("Sí, y no vuelvas a preguntar.", SpokenReplyKind.Always)]
    [InlineData("no me preguntes más", SpokenReplyKind.Always)]
    [InlineData("siempre", SpokenReplyKind.Always)]
    // Any installed language, as the voice.
    [InlineData("Yes please", SpokenReplyKind.Yes)]
    [InlineData("nope", SpokenReplyKind.No)]
    [InlineData("Don't ask again", SpokenReplyKind.Always)]
    [InlineData("oui", SpokenReplyKind.Yes)]
    [InlineData("nein danke", SpokenReplyKind.No)]
    [InlineData("Não", SpokenReplyKind.No)]
    public void ShortAnswersAreUnderstood(string said, SpokenReplyKind expected) => Assert.Equal(expected, SpokenReply.Of(said));

    [Theory]
    [InlineData("")]
    [InlineData("¿qué hora es?")]
    [InlineData("pon música en el salón")]
    [InlineData("por favor")]
    // A yes that goes on and on is not an answer any more.
    [InlineData("sí pero antes abre el navegador y busca el tiempo de mañana")]
    public void AnythingElseIsNotAnAnswer(string said) => Assert.Null(SpokenReply.Of(said));
}
