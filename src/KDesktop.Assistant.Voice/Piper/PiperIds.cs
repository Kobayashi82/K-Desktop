using System.Text.Json;

namespace KDesktop.Assistant.Voice.Piper;

/// <summary>
/// The numbers a Piper voice gives its phonemes (<c>phoneme_id_map</c> of its <c>.onnx.json</c>), as piper-phonemize turns
/// phonemes into them: the start and a pause symbol, then each phoneme the voice knows followed by the pause symbol, and the
/// end. Phonemes are code points (the text comes decomposed), except the vowel pairs a newer voice learnt as one symbol.
/// </summary>
public sealed class PiperIds
{
    private const string Pad = "_", Start = "^", End = "$";

    private readonly Dictionary<string, long[]> _ids = new(StringComparer.Ordinal);
    private readonly HashSet<string> _clusters;

    public PiperIds(JsonElement config)
    {
        foreach (var phoneme in config.GetProperty("phoneme_id_map").EnumerateObject())
            _ids[phoneme.Name] = [.. phoneme.Value.EnumerateArray().Select(e => e.GetInt64())];
        // Newer voices (piper 1.5) learnt some vowel pairs as one symbol («aɪ»): only those that say so.
        _clusters = config.TryGetProperty("vowel_clusters", out var clusters) && clusters.ValueKind == JsonValueKind.Array
            ? [.. clusters.EnumerateArray().Select(c => string.Concat(c.EnumerateArray().Select(p => p.GetString()))).Where(_ids.ContainsKey)]
            : [];
    }

    /// <summary>The numbers of some phonemes (<see cref="EspeakPhonemes.Ipa"/>); unknown phonemes are left out.</summary>
    public List<long> Of(string phonemes)
    {
        var ids = new List<long>(phonemes.Length * 2 + 3);
        ids.AddRange(_ids[Start]);
        ids.AddRange(_ids[Pad]);
        var runes = phonemes.EnumerateRunes().Select(r => r.ToString()).ToList();
        for (var i = 0; i < runes.Count; i++)
        {
            var phoneme = runes[i];
            if (i + 1 < runes.Count && _clusters.Contains(phoneme + runes[i + 1]))
            {
                phoneme += runes[i + 1];
                i++;
            }
            if (!_ids.TryGetValue(phoneme, out var id)) continue;
            ids.AddRange(id);
            ids.AddRange(_ids[Pad]);
        }
        ids.AddRange(_ids[End]);
        return ids;
    }
}
