using System.Text.Json;
using System.Text.RegularExpressions;
using KDesktop.Assistant.Voice.Kokoro;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Kokoro's Spanish without espeak-ng (5.0.94): our rules must write what espeak-ng writes, which is what Kokoro learnt
/// Spanish with. The reference (<c>Data\espeak-es.json</c>) is misaki's EspeakG2P("es") on the texts of K-Desktop and of
/// docs/assistant.
/// </summary>
public partial class SpanishPhonemesTests
{
    [Theory]
    [InlineData("Hola, ¿qué tal?", "ˈola, kˈe tˈal?")]
    [InlineData("Recibido, Houston.", "rˌeθiβˈiðo, ˈowston.")]
    [InlineData("Lo recordaré: Se llama Kobayashi.", "lo rˌekoɾðaɾˈe: se ʎˈama kˌoβaʝˈasi.")]
    [InlineData("La vuelta ha sido realizada.", "la βwˈelta ˈa sˈiðo rˌealiθˈaða.")]
    [InlineData("Cuando diga la vuelta, quiere que se abra la página.", "kwˌando ðˈiɣa la βwˈelta, kjˈeɾe ke se ˈaβɾa la pˈaxina.")]
    [InlineData("Tengo cuarenta y cuatro años.", "tˈɛŋɡo kwaɾˈɛnta i kwˈatɾo ˈaɲos.")]
    [InlineData("El perro de San Roque no tiene rabo.", "el pˈero ðe sˈan rˈoke nˈo tjˈene rˈaβo.")]
    [InlineData("¡Misión cumplida!", "misjˈon kumplˈiða!")]
    [InlineData("Ahora son las diecisiete y cuarenta.", "aˈoɾa sˈon las ðjˌeθisjˈete i kwaɾˈɛnta.")]
    [InlineData("Pon un temporizador", "pˈon ˈun tˌempoɾˌiθaðˈoɾ")]
    [InlineData("Mira mamá, sin manos", "mˈiɾa mamˈa, sin mˈanos")]
    [InlineData("Conectado a", "kˌonektˈaðo ˈa")]
    public void ItSaysWhatEspeakSays(string text, string expected) => Assert.Equal(expected, SpanishPhonemes.ToPhonemes(text));

    [Theory]
    [InlineData("Mide 1,65 metros", "mˈiðe ˈuno kˈoma sesˈɛnta i θˈinko mˈetɾos")]
    [InlineData("Son las 17:40", "sˈon las ðjˌeθisjˈete kwaɾˈɛnta")]
    [InlineData("El 20 %", "el bˈAnte poɾ θjˈɛnto")]
    [InlineData("1.234 personas", "mˈil ðˌosθjˈɛntos tɾˌeinta i kwˈatɾo peɾsˈonas")]
    public void NumbersAreSaidInWords(string text, string expected)
    {
        // The words, then the same rules: what matters is that no digit is left.
        var said = SpanishPhonemes.ToPhonemes(text);
        Assert.DoesNotMatch(@"\d", said);
        Assert.StartsWith(expected.Split(' ')[0], said, StringComparison.Ordinal);
    }

    [Fact]
    public void NearlyEveryWordIsAsEspeakSaysIt()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Data", "espeak-es.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        int words = 0, same = 0, sentences = 0, whole = 0;
        foreach (var item in doc.RootElement.GetProperty("cases").EnumerateObject())
        {
            var theirs = Spaces().Replace(item.Value.GetString()!.Replace("¿", "", StringComparison.Ordinal).Replace("¡", "", StringComparison.Ordinal), " ").Trim();
            var ours = SpanishPhonemes.ToPhonemes(item.Name);
            sentences++;
            if (ours == theirs) whole++;
            var a = ours.Split(' ');
            var b = theirs.Split(' ');
            if (a.Length != b.Length) continue;
            words += a.Length;
            same += a.Zip(b).Count(p => p.First == p.Second);
        }
        // Measured when written: 98.9 % of the words (what is left: foreign words and acronyms) and 85 % whole sentences.
        Assert.True(same >= words * 0.985, $"{same}/{words} palabras");
        Assert.True(whole >= sentences * 0.84, $"{whole}/{sentences} frases");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
