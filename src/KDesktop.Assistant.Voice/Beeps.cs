namespace KDesktop.Assistant.Voice;

/// <summary>
/// «Solo pitidos» (stage 5.4): a sentence as a little robot's chirps, R2-D2 style, with the text in the bubble. One chirp
/// per syllable more or less, the tones taken from the words (the same sentence always sounds the same), rising at a
/// question and higher at an exclamation.
/// </summary>
public static class Beeps
{
    public const int SampleRate = 22_050;

    public static float[] Say(string text, double speed = 1)
    {
        var syllables = Syllables(text);
        if (syllables.Count == 0) return [];
        var question = text.TrimEnd().EndsWith('?');
        var exclaim = text.TrimEnd().EndsWith('!');
        speed = Math.Clamp(speed, 0.5, 2);
        var audio = new List<float>(syllables.Count * SampleRate / 6);
        var phase = 0.0;
        for (var i = 0; i < syllables.Count; i++)
        {
            var hash = (uint)Hash(syllables[i]) * 2654435761u + (uint)i;
            var last = i == syllables.Count - 1;
            var low = 650 + hash % 900 + (exclaim ? 300 : 0);
            var high = low * (0.75 + (hash >> 10) % 100 / 100.0);
            if (last && question) high = low * 1.7;
            var seconds = (0.06 + (hash >> 17) % 70 / 1000.0) / speed * (last ? 1.6 : 1);
            var count = (int)(seconds * SampleRate);
            for (var n = 0; n < count; n++)
            {
                var t = n / (double)count;
                var frequency = low + (high - low) * t;
                phase += 2 * Math.PI * frequency / SampleRate;
                // A rounded square (a little buzz), with a quick attack and release.
                var wave = Math.Tanh(3 * Math.Sin(phase)) * 0.6 + Math.Sin(2 * phase) * 0.15;
                var envelope = Math.Min(1, Math.Min(t * 12, (1 - t) * 8));
                audio.Add((float)(wave * envelope * 0.32));
            }
            // The pause between chirps; longer after a comma.
            var gap = (int)((syllables[i].EndsWith(',') ? 0.16 : 0.035 + (hash >> 23) % 40 / 1000.0) / speed * SampleRate);
            for (var n = 0; n < gap; n++) audio.Add(0);
        }
        return [.. audio];
    }

    /// <summary>Groups of vowels, roughly the syllables (with a comma kept on the one before a pause).</summary>
    private static List<string> Syllables(string text)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inVowel = false;
        foreach (var c in text.ToLowerInvariant())
        {
            if (c == ',' && result.Count > 0)
            {
                result[^1] += ",";
                continue;
            }
            if (!char.IsLetterOrDigit(c))
            {
                inVowel = false;
                continue;
            }
            var vowel = "aeiouáéíóúàèìòùâêîôûäëïöüy".Contains(c, StringComparison.Ordinal) || char.IsDigit(c);
            if (vowel && !inVowel && current.Length > 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            current.Append(c);
            inVowel = vowel;
        }
        if (current.Length > 0) result.Add(current.ToString());
        // A word without vowels, numbers: still a chirp each.
        return result.Count > 40 ? result[..40] : result;
    }

    /// <summary>A hash that does not change between runs (string hashes are random per process).</summary>
    private static int Hash(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in text) hash = (hash ^ c) * 16777619;
            return hash;
        }
    }
}
