using System.Diagnostics;

namespace KDesktop.Core.Voice;

/// <summary>
/// Wake word, phase 1: what the always-on detector learns from the recordings of one word. Two opinions, both on
/// openWakeWord embeddings (one every 80 ms):
/// <list type="bullet">
/// <item>DTW: how close the last embeddings are to the user's own takes (templates), cosine distance, scaled so the
/// takes among themselves score ~1 and normal speech ~0;</item>
/// <item>a logistic-regression classifier over the last 16 embeddings (1.28 s, like openWakeWord's models), trained
/// here in a second or two with the takes plus variations (speed, volume, noise, position) against the silence and the
/// normal speech recorded in the set-up, background noise and cut-off halves of the word.</item>
/// </list>
/// Score = geometric mean of both (both must agree). Saved as <c>detector.json</c> next to the recordings.
/// </summary>
public sealed class WakeDetector
{
    /// <summary>Bumped whenever training changes, so saved detectors are trained again.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Embeddings the classifier looks at (1.28 s).</summary>
    public const int Frames = 16;

    /// <summary>Score needed at the middle sensitivity.</summary>
    public const double DefaultThreshold = 0.5;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Embedding sequence of each take (from shortly after the word starts to just after it ends).</summary>
    public List<float[][]> Templates { get; set; } = [];

    /// <summary>DTW distance among the takes (typical) and the lowest normal speech reached.</summary>
    public double PositiveDistance { get; set; }
    public double NegativeDistance { get; set; }

    /// <summary>Classifier: standardisation (mean, scale) and weights of the 16 × 96 inputs, plus bias.</summary>
    public float[] Mean { get; set; } = [];
    public float[] Scale { get; set; } = [];
    public float[] Weights { get; set; } = [];
    public float Bias { get; set; }

    /// <summary>Highest combined score normal speech and noise got in training, and what the takes got (lowest).</summary>
    public double NegativeScore { get; set; }
    public double PositiveScore { get; set; }

    // ── scoring ──────────────────────────────────────────────────────────

    /// <summary>
    /// Combined score (0–1) of the most recent embeddings (oldest first; at least <see cref="Frames"/> of them).
    /// </summary>
    public (double Score, double Dtw, double Classifier) Score(IReadOnlyList<float[]> recent)
    {
        var dtw = DtwScore(recent);
        var cls = Classify(recent);
        return (Math.Sqrt(dtw * cls), dtw, cls);
    }

    /// <summary>
    /// The best score <paramref name="audio"/> gets heard live (1.5 s of quiet before it and 0.5 s after, through the
    /// streaming features, as the listener hears it).
    /// </summary>
    public (double Score, double Dtw, double Classifier) BestLive(OwwFeatures features, float[] audio)
    {
        var stream = new OwwStream(features);
        var recent = new List<float[]>();
        (double Score, double Dtw, double Classifier) best = (0, 0, 0);
        var live = new float[MicCapture.SampleRate * 3 / 2].Concat(audio).Concat(new float[MicCapture.SampleRate / 2]).ToArray();
        for (var i = 0; i < live.Length; i += 512)
            foreach (var e in stream.Accept(live.AsSpan(i, Math.Min(512, live.Length - i))))
            {
                recent.Add(e);
                if (recent.Count > 40) recent.RemoveAt(0);
                if (recent.Count < Frames) continue;
                var score = Score(recent);
                if (score.Score > best.Score) best = score;
            }
        return best;
    }

    /// <summary>Embedding sequence of one take, as kept for DTW (the set-up uses it to spot odd takes).</summary>
    public static float[][] TemplateFor(OwwFeatures features, float[] take, float[]? silence)
    {
        var lead = (int)(1.5 * MicCapture.SampleRate);
        var noise = silence is { Length: > 1000 } ? silence : new float[MicCapture.SampleRate];
        var clip = Place(take, noise, lead, MicCapture.SampleRate / 5, 1, new Random(1), noiseGain: 1);
        return TemplateOf(features.EmbedClip(clip), lead, take.Length);
    }

    public double Classify(IReadOnlyList<float[]> recent)
    {
        if (recent.Count < Frames || Weights.Length != Frames * OwwFeatures.Size) return 0;
        double z = Bias;
        var offset = recent.Count - Frames;
        for (var f = 0; f < Frames; f++)
        {
            var e = recent[offset + f];
            for (var d = 0; d < OwwFeatures.Size; d++)
            {
                var i = f * OwwFeatures.Size + d;
                z += Weights[i] * ((e[d] - Mean[i]) / Scale[i]);
            }
        }
        return 1 / (1 + Math.Exp(-z));
    }

    /// <summary>1 when the end of <paramref name="recent"/> sounds like the takes, 0 when like normal speech.</summary>
    public double DtwScore(IReadOnlyList<float[]> recent)
    {
        if (Templates.Count == 0) return 0;
        var best = Templates.Min(t => Dtw.EndAligned(t, recent));
        var span = NegativeDistance - PositiveDistance;
        if (span <= 1e-6) return best <= PositiveDistance ? 1 : 0;
        return Math.Clamp((NegativeDistance - best) / span, 0, 1);
    }

    // ── training ─────────────────────────────────────────────────────────

    /// <summary>What a training run saw (for the log and the diagnostics).</summary>
    public sealed record Report(int Positives, int Negatives, double PositiveScore, double NegativeScore, long Milliseconds);

    /// <param name="takes">The takes of the word (speech trimmed, a little silence around).</param>
    /// <param name="silence">The silence recording (background noise), may be null.</param>
    /// <param name="speech">The normal speech recording, may be null.</param>
    public static (WakeDetector Detector, Report Report) Train(OwwFeatures features, IReadOnlyList<float[]> takes, float[]? silence, float[]? speech)
    {
        var watch = Stopwatch.StartNew();
        var random = new Random(1234);
        var noise = silence is { Length: > 1000 } ? silence : new float[MicCapture.SampleRate];
        var detector = new WakeDetector();

        // Each take sits after 1.5 s of the room's own noise (the classifier needs 1.28 s of context).
        var lead = (int)(1.5 * MicCapture.SampleRate);
        var tail = MicCapture.SampleRate / 5;

        // Templates and their DTW scale.
        foreach (var take in takes)
        {
            var clip = Place(take, noise, lead, tail, 1, random, noiseGain: 1);
            var e = features.EmbedClip(clip);
            detector.Templates.Add(TemplateOf(e, lead, take.Length));
        }

        var positives = new List<float[]>();
        var negatives = new List<float[]>();
        var positiveWindows = new List<float[][]>();

        // Positives: every take with variations; the windows that end just after the word.
        double[] speeds = [0.9, 1.0, 1.1];
        double[] gains = [0.6, 1.0, 1.5];
        foreach (var take in takes)
            foreach (var speed in speeds)
                foreach (var gain in gains)
                {
                    var stretched = Resample(take, speed);
                    var clip = Place(stretched, noise, lead, tail, gain, random, noiseGain: random.NextDouble() < 0.5 ? 1 : 2.5);
                    var e = features.EmbedClip(clip);
                    var end = EndIndex(lead + stretched.Length, e.Length);
                    for (var k = end; k <= Math.Min(e.Length - 1, end + 1); k++)
                        if (Window(e, k) is { } w)
                        {
                            positives.Add(Flatten(w));
                            positiveWindows.Add(w);
                        }
                    // The word cut off: half of it is not the word yet.
                    for (var k = Math.Max(Frames - 1, end - 8); k <= end - 4; k++)
                        if (Window(e, k) is { } w) negatives.Add(Flatten(w));
                }

        // Negatives: normal speech, silence and plain noise, every 80 ms.
        foreach (var recording in new[] { speech, silence }.OfType<float[]>())
        {
            var e = features.EmbedClip(Concat(noise[..Math.Min(noise.Length, lead)], recording));
            for (var k = Frames - 1; k < e.Length; k++) negatives.Add(Flatten(Window(e, k)!));
        }
        for (var i = 0; i < 4; i++)
        {
            var hiss = new float[3 * MicCapture.SampleRate];
            var level = 0.002 * Math.Pow(4, i);
            for (var s = 0; s < hiss.Length; s++) hiss[s] = (float)((random.NextDouble() * 2 - 1) * level);
            var e = features.EmbedClip(hiss);
            for (var k = Frames - 1; k < e.Length; k += 2) negatives.Add(Flatten(Window(e, k)!));
        }

        // DTW scale: the takes against each other, and the closest normal speech got.
        detector.PositiveDistance = PositiveDistanceOf(detector.Templates);
        var negativeDistances = new List<double>();
        foreach (var recording in new[] { speech, silence }.OfType<float[]>())
        {
            var e = features.EmbedClip(Concat(noise[..Math.Min(noise.Length, lead)], recording));
            for (var k = 8; k < e.Length; k++)
                negativeDistances.Add(detector.Templates.Min(t => Dtw.EndAligned(t, e[..(k + 1)])));
        }
        detector.NegativeDistance = negativeDistances.Count > 0
            ? Math.Max(Percentile(negativeDistances, 0.05), detector.PositiveDistance * 1.5)
            : detector.PositiveDistance * 2.5;

        // Classifier.
        TrainLogistic(detector, positives, negatives);

        // How the combination separates the training data (sets the sensitivity scale).
        detector.PositiveScore = Percentile(positiveWindows.Select(w => detector.Score(w).Score).ToList(), 0.1);
        var negativeScores = new List<double>();
        foreach (var recording in new[] { speech, silence }.OfType<float[]>())
        {
            var e = features.EmbedClip(Concat(noise[..Math.Min(noise.Length, lead)], recording));
            for (var k = Frames - 1; k < e.Length; k++) negativeScores.Add(detector.Score(e[..(k + 1)]).Score);
        }
        detector.NegativeScore = negativeScores.DefaultIfEmpty(0).Max();

        return (detector, new Report(positives.Count, negatives.Count, detector.PositiveScore, detector.NegativeScore, watch.ElapsedMilliseconds));
    }

    /// <summary>Threshold for this word: between what normal speech reached and what the takes reach.</summary>
    public double Threshold(double sensitivity)
    {
        var middle = PositiveScore > NegativeScore ? (PositiveScore + NegativeScore) / 2 : DefaultThreshold;
        middle = Math.Clamp(middle, 0.3, 0.85);
        return Math.Clamp(middle + (0.5 - sensitivity) * 0.3, 0.2, 0.95);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>A take placed after <paramref name="lead"/> samples of room noise, with some after it.</summary>
    private static float[] Place(float[] take, float[] noise, int lead, int tail, double gain, Random random, double noiseGain)
    {
        var clip = new float[lead + take.Length + tail];
        var offset = random.Next(noise.Length);
        for (var i = 0; i < clip.Length; i++) clip[i] = (float)(noise[(offset + i) % noise.Length] * noiseGain);
        for (var i = 0; i < take.Length; i++) clip[lead + i] += (float)(take[i] * gain);
        return clip;
    }

    private static float[] Concat(float[] a, float[] b)
    {
        var c = new float[a.Length + b.Length];
        a.CopyTo(c, 0);
        b.CopyTo(c, a.Length);
        return c;
    }

    /// <summary>Plays the audio <paramref name="speed"/> times faster (linear interpolation; pitch moves too).</summary>
    public static float[] Resample(float[] audio, double speed)
    {
        if (Math.Abs(speed - 1) < 1e-6) return audio;
        var output = new float[(int)(audio.Length / speed)];
        for (var i = 0; i < output.Length; i++)
        {
            var x = i * speed;
            var j = (int)x;
            var t = (float)(x - j);
            output[i] = j + 1 < audio.Length ? audio[j] * (1 - t) + audio[j + 1] * t : audio[Math.Min(j, audio.Length - 1)];
        }
        return output;
    }

    /// <summary>The first embedding whose window reaches the end of the word.</summary>
    private static int EndIndex(int wordEnd, int count)
    {
        var k = 0;
        while (k < count - 1 && OwwFeatures.WindowEnd(k) < wordEnd) k++;
        return k;
    }

    /// <summary>The embeddings from shortly after the word starts to the one that ends with it.</summary>
    private static float[][] TemplateOf(float[][] e, int wordStart, int wordLength)
    {
        var end = EndIndex(wordStart + wordLength, e.Length);
        var start = 0;
        while (start < end && OwwFeatures.WindowEnd(start) < wordStart + MicCapture.SampleRate / 4) start++;
        start = Math.Min(start, Math.Max(0, end - 2));
        return e[start..(end + 1)];
    }

    /// <summary>The <see cref="Frames"/> embeddings ending at <paramref name="k"/>, or null too close to the start.</summary>
    private static float[][]? Window(float[][] e, int k) => k >= Frames - 1 && k < e.Length ? e[(k - Frames + 1)..(k + 1)] : null;

    private static float[] Flatten(float[][] window)
    {
        var x = new float[Frames * OwwFeatures.Size];
        for (var f = 0; f < Frames; f++) Array.Copy(window[f], 0, x, f * OwwFeatures.Size, OwwFeatures.Size);
        return x;
    }

    private static double PositiveDistanceOf(List<float[][]> templates)
    {
        if (templates.Count < 2) return 0.1;
        var distances = new List<double>();
        for (var i = 0; i < templates.Count; i++)
        {
            // Take i as if it were heard live (with a little before it), against the other takes.
            var others = templates.Where((_, j) => j != i).ToList();
            distances.Add(others.Min(t => Dtw.EndAligned(t, templates[i])));
        }
        return Percentile(distances, 0.5);
    }

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return 0;
        var sorted = values.Order().ToList();
        return sorted[Math.Clamp((int)Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }

    /// <summary>
    /// L2-regularised logistic regression, full-batch gradient descent with Adam; both classes weigh the same however
    /// many examples each has.
    /// </summary>
    private static void TrainLogistic(WakeDetector d, List<float[]> positives, List<float[]> negatives)
    {
        var n = Frames * OwwFeatures.Size;
        var all = positives.Concat(negatives).ToList();
        var mean = new float[n];
        var scale = new float[n];
        foreach (var x in all)
            for (var i = 0; i < n; i++) mean[i] += x[i];
        for (var i = 0; i < n; i++) mean[i] /= all.Count;
        foreach (var x in all)
            for (var i = 0; i < n; i++) scale[i] += (x[i] - mean[i]) * (x[i] - mean[i]);
        for (var i = 0; i < n; i++) scale[i] = (float)Math.Max(1e-3, Math.Sqrt(scale[i] / all.Count));

        var xs = all.Select(x =>
        {
            var z = new float[n];
            for (var i = 0; i < n; i++) z[i] = (x[i] - mean[i]) / scale[i];
            return z;
        }).ToList();
        var ys = positives.Select(_ => 1.0).Concat(negatives.Select(_ => 0.0)).ToArray();
        var weightOf = ys.Select(y => y > 0.5 ? 0.5 / positives.Count : 0.5 / negatives.Count).ToArray();

        var w = new double[n];
        double b = 0;
        var m = new double[n + 1];
        var v = new double[n + 1];
        const double rate = 0.01, beta1 = 0.9, beta2 = 0.999, lambda = 0.02;
        var grad = new double[n + 1];
        for (var step = 1; step <= 400; step++)
        {
            Array.Clear(grad);
            for (var s = 0; s < xs.Count; s++)
            {
                var x = xs[s];
                var z = b;
                for (var i = 0; i < n; i++) z += w[i] * x[i];
                var err = (1 / (1 + Math.Exp(-z)) - ys[s]) * weightOf[s];
                for (var i = 0; i < n; i++) grad[i] += err * x[i];
                grad[n] += err;
            }
            for (var i = 0; i < n; i++) grad[i] += lambda * w[i];
            for (var i = 0; i <= n; i++)
            {
                m[i] = beta1 * m[i] + (1 - beta1) * grad[i];
                v[i] = beta2 * v[i] + (1 - beta2) * grad[i] * grad[i];
                var update = rate * (m[i] / (1 - Math.Pow(beta1, step))) / (Math.Sqrt(v[i] / (1 - Math.Pow(beta2, step))) + 1e-8);
                if (i < n) w[i] -= update;
                else b -= update;
            }
        }
        d.Mean = mean;
        d.Scale = scale;
        d.Weights = w.Select(x => (float)x).ToArray();
        d.Bias = (float)b;
    }
}

/// <summary>Dynamic time warping over embedding sequences (cosine distance).</summary>
public static class Dtw
{
    public static double CosineDistance(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 1 : 1 - dot / Math.Sqrt(na * nb);
    }

    /// <summary>
    /// How far the end of <paramref name="stream"/> is from <paramref name="template"/>: the template must match a
    /// stretch that finishes at the last embedding but may start anywhere (subsequence DTW), the path cost divided by
    /// the template length.
    /// </summary>
    public static double EndAligned(float[][] template, IReadOnlyList<float[]> stream)
    {
        var n = template.Length;
        var m = Math.Min(stream.Count, n * 2 + 2);
        if (n == 0 || m == 0) return double.MaxValue;
        var offset = stream.Count - m;
        // cost[i, j]: template[0..i] aligned with stream[..j], free start in the stream.
        var previous = new double[m];
        var current = new double[m];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < m; j++)
            {
                var d = CosineDistance(template[i], stream[offset + j]);
                double best;
                if (i == 0) best = j == 0 ? 0 : Math.Min(0, current[j - 1]); // free start: begin anywhere
                else
                {
                    best = previous[j];
                    if (j > 0) best = Math.Min(best, Math.Min(previous[j - 1], current[j - 1]));
                }
                current[j] = d + best;
            }
            (previous, current) = (current, previous);
        }
        return previous[m - 1] / n;
    }
}
