using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using KDesktop.Core;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// Records a wake word with the user's voice: name → 3 s of silence (background noise) → the word several times
/// (each take trimmed and checked; Parakeet's transcription shown) → 10 s of normal speech (calibrates the threshold)
/// → a live try. Also opens straight on the try for a saved word.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The recorder and the token source are disposed when the window closes.")]
public partial class WakeWordDialog : FluentWindow
{
    private const int Wanted = 6, Minimum = 4;
    private static readonly TimeSpan SilenceLength = TimeSpan.FromSeconds(3), SpeechLength = TimeSpan.FromSeconds(10);
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly App _app = App.Current;
    private readonly WakeWord? _existing;
    private readonly bool _testOnly;
    private readonly WakeWordRecorder _recorder = new();
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly StackPanel[] _steps;
    private readonly List<TakeRow> _takes = [];
    private readonly CancellationTokenSource _cancel = new();
    private ProgressBar? _meter;
    private int _step;
    private bool _busy;

    private float[]? _silence, _speech;
    private double _noise;
    private bool _speechDone;
    private WakeWordVerifier.Calibration? _calibration;

    /// <summary>The word saved (null when cancelled or only tried).</summary>
    public WakeWord? Saved { get; private set; }

    private WakeWordDialog(WakeWord? existing, bool testOnly)
    {
        InitializeComponent();
        _existing = existing;
        _testOnly = testOnly;
        _steps = [Step0, Step1, Step2, Step3, Step4];
        for (var i = 0; i < _steps.Length; i++)
            StepDots.Children.Add(new Ellipse { Width = 8, Height = 8, Margin = new Thickness(4, 0, 0, 0) });

        var micId = _app.Config.Settings.VoiceMicId;
        var mic = MicCapture.Devices().FirstOrDefault(d => d.Id == micId).Name ?? Loc.T("wake.micDefault");
        MicText.Text = Loc.T("wake.mic", mic);
        NameBox.Text = existing?.Name ?? string.Empty;

        _meterTimer.Tick += (_, _) =>
        {
            if (_meter is not null) _meter.Value = _busy ? Math.Sqrt(_recorder.Level) : 0;
        };
        _meterTimer.Start();
        // Saying the word here is a recording or a test, not a call: the always-on listening waits until it closes.
        var hold = _app.WakeListener.Hold();
        Closed += (_, _) =>
        {
            hold.Dispose();
            _cancel.Cancel();
            _cancel.Dispose();
            _meterTimer.Stop();
            _recorder.Dispose();
        };
        // Loads Parakeet while the user goes through the first steps.
        _app.Voice.SpeechToText.Preload();

        if (testOnly)
        {
            StepDots.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Collapsed;
            ShowStep(4, animate: false);
        }
        else ShowStep(0, animate: false);
        Loaded += (_, _) =>
        {
            if (!testOnly)
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }
        };
    }

    /// <summary>Records a new wake word, or <paramref name="existing"/> again. Returns the saved one.</summary>
    public static WakeWord? Record(Window? owner, WakeWord? existing = null)
    {
        var dlg = new WakeWordDialog(existing, testOnly: false) { Owner = owner };
        dlg.ShowDialog();
        return dlg.Saved;
    }

    /// <summary>Lets the user try a saved wake word.</summary>
    public static void Test(Window? owner, WakeWord word)
    {
        var dlg = new WakeWordDialog(word, testOnly: true) { Owner = owner };
        dlg.ShowDialog();
    }

    private string WordName => NameBox.Text.Trim();

    // ── steps ────────────────────────────────────────────────────────────

    private void ShowStep(int step, bool animate = true)
    {
        _step = step;
        for (var i = 0; i < _steps.Length; i++) _steps[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
        StepTitle.Text = _testOnly ? Loc.T("wake.testTitle", _existing!.Name) : $"{step + 1}. " + Loc.T($"wake.step{step}");
        for (var i = 0; i < StepDots.Children.Count; i++)
        {
            var dot = (Ellipse)StepDots.Children[i];
            dot.SetResourceReference(Shape.FillProperty, i == step ? "Kd.Accent" : i < step ? "Kd.AccentSoft" : "ControlStrokeColorDefaultBrush");
        }
        ErrorText.Visibility = Visibility.Collapsed;

        switch (step)
        {
            case 1: _meter = SilenceLevel; break;
            case 2:
                _meter = WordLevel;
                WordText.Text = Loc.T("wake.wordText", WordName, Wanted, Minimum);
                UpdateTakes();
                break;
            case 3: _meter = SpeechLevel; break;
            case 4:
                _meter = TestLevel;
                TestText.Text = Loc.T("wake.testText", _testOnly ? _existing!.Name : WordName);
                TrainDetector();
                break;
            default: _meter = null; break;
        }
        UpdateButtons();

        if (!animate) return;
        StepHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(180)));
        StepShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    private void UpdateButtons()
    {
        NextButton.Content = _testOnly ? Loc.T("common.close") : _step == 4 ? Loc.T("wake.save") : Loc.T("wake.next");
        NextButton.IsEnabled = !_busy && _step switch
        {
            0 => WordName.Length > 0,
            1 => _silence is not null,
            2 => Kept.Count() >= Minimum,
            3 => _speechDone,
            _ => true,
        };
        BackButton.IsEnabled = !_busy && _step > 0;
        SilenceButton.IsEnabled = SpeechButton.IsEnabled = RecordButton.IsEnabled = TestButton.IsEnabled = !_busy;
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e) => UpdateButtons();

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_testOnly)
        {
            Close();
            return;
        }
        if (_step < 4)
        {
            // A different word: the takes of the old one no longer count.
            if (_step == 0 && _takes.Count > 0 && !string.Equals(_takesName, WordName, StringComparison.OrdinalIgnoreCase)) ClearTakes();
            ShowStep(_step + 1);
            return;
        }
        Save();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step > 0) ShowStep(_step - 1);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    /// <summary>Runs a recording step: busy, nothing else clickable, errors shown.</summary>
    private async Task Busy(Func<CancellationToken, Task> work)
    {
        if (_busy) return;
        if (!VoiceModels.AllInstalled)
        {
            ShowError(Loc.T("wake.needModels"));
            return;
        }
        _busy = true;
        ErrorText.Visibility = Visibility.Collapsed;
        UpdateButtons();
        try
        {
            await work(_cancel.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Error en el asistente de la palabra de activación", ex);
            ShowError(ex is System.Runtime.InteropServices.COMException ? Loc.T("wake.micError") : ex.Message);
        }
        finally
        {
            _busy = false;
            if (IsLoaded) UpdateButtons();
        }
    }

    // ── 1 · silence ──────────────────────────────────────────────────────

    private async void OnSilence(object sender, RoutedEventArgs e) => await Busy(async cancel =>
    {
        SilenceButton.Content = Loc.T("wake.recording");
        using (Countdown(SilenceTime, SilenceLength))
            _silence = await _recorder.RecordAsync(_app.Config.Settings.VoiceMicId, SilenceLength, cancel);
        SilenceButton.Content = Loc.T("wake.again");
        _noise = WakeWordRecorder.Rms(_silence);
        var level = _noise < 0.01 ? "low" : _noise < 0.03 ? "medium" : "high";
        SilenceResult.Text = Loc.T("wake.noise." + level, _noise.ToString("0.000", Loc.Instance.Culture));
        SilenceResult.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, level == "high" ? "Kd.Warning" : "TextFillColorSecondaryBrush");
    });

    /// <summary>Fills <paramref name="bar"/> over <paramref name="length"/>; disposing stops it.</summary>
    private static IDisposable Countdown(ProgressBar bar, TimeSpan length)
    {
        bar.Value = 0;
        bar.Visibility = Visibility.Visible;
        bar.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(0, 1, length));
        return new Disposer(() =>
        {
            bar.BeginAnimation(RangeBase.ValueProperty, null);
            bar.Value = 0;
            bar.Visibility = Visibility.Collapsed;
        });
    }

    private sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    // ── 2 · the word ─────────────────────────────────────────────────────

    /// <summary>The word the current takes are of.</summary>
    private string? _takesName;

    private IEnumerable<TakeRow> Kept => _takes.Where(t => t.Take.Issue == TakeIssue.None);

    /// <summary>Records takes one after another until there are enough good ones or one goes wrong.</summary>
    private async void OnRecordTake(object sender, RoutedEventArgs e) => await Busy(async cancel =>
    {
        _takesName = WordName;
        while (Kept.Count() < Wanted)
        {
            RecordButton.Content = Loc.T("wake.speakNow", Kept.Count() + 1, Wanted, WordName);
            var audio = await _recorder.RecordUtteranceAsync(_app.Config.Settings.VoiceMicId, TimeSpan.FromSeconds(4), cancel);
            var take = WakeWordRecorder.Check(audio, _noise, Kept.Select(t => t.Take.Seconds).ToList());
            float[][]? template = null;
            if (take.Issue == TakeIssue.None)
            {
                RecordButton.Content = Loc.T("wake.understanding");
                take.Text = WakeWordVerifier.Clean(await Task.Run(() => _app.Voice.SpeechToText.Transcribe(take.Speech), cancel));
                // Sounds unlike the other takes (another word, a cough…)?
                var silence = _silence;
                template = await Task.Run(() => WakeDetector.TemplateFor(_app.Voice.Features, take.Speech, silence), cancel);
                if (IsOdd(template)) take = take with { Issue = TakeIssue.Odd };
            }
            _takes.Add(new TakeRow(take) { Template = template });
            UpdateTakes();
            if (take.Issue != TakeIssue.None) break;
            await Task.Delay(500, cancel);
        }
    });

    /// <summary>
    /// Much further (DTW over openWakeWord embeddings) from the takes already kept than they are from each other.
    /// </summary>
    private bool IsOdd(float[][] template)
    {
        var others = Kept.Select(t => t.Template).OfType<float[][]>().ToList();
        if (others.Count < 2) return false;
        var distance = others.Min(o => Dtw.EndAligned(o, template));
        var typical = others.Select(o => others.Where(x => !ReferenceEquals(x, o)).Min(x => Dtw.EndAligned(x, o))).Order().ToList()[others.Count / 2];
        var odd = distance > Math.Max(typical * 2.2, typical + 0.08);
        Log.Info($"Toma de «{WordName}»: distancia {distance:0.000} a las demás (normal entre ellas {typical:0.000}){(odd ? " → atípica" : "")}");
        return odd;
    }

    private void OnRemoveTake(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not FrameworkElement { DataContext: TakeRow row }) return;
        _takes.Remove(row);
        UpdateTakes();
        UpdateButtons();
    }

    private void ClearTakes()
    {
        _takes.Clear();
        _speechDone = false;
        _calibration = null;
        SpeechResult.Text = string.Empty;
    }

    private void UpdateTakes()
    {
        // Only the last failed take is kept on screen (it says what to fix); the good ones stay.
        var failed = _takes.LastOrDefault(t => t.Take.Issue != TakeIssue.None);
        _takes.RemoveAll(t => t.Take.Issue != TakeIssue.None && !ReferenceEquals(t, failed));
        TakeList.ItemsSource = null;
        TakeList.ItemsSource = _takes.ToList();
        var kept = Kept.Count();
        TakeCount.Text = Loc.T("wake.takeCount", kept, Wanted);
        RecordButton.Content = Loc.T(kept == 0 ? "wake.record" : kept < Wanted ? "wake.recordMore" : "wake.recordAgain");
        // Different takes: the calibration has to be done again.
        _speechDone = _speechDone && _calibration is not null && kept == _calibratedTakes;
        UpdateButtons();
    }

    /// <summary>One take in the list.</summary>
    public sealed class TakeRow(WakeTake take)
    {
        public WakeTake Take { get; } = take;

        /// <summary>Its embedding sequence (to compare takes by sound).</summary>
        public float[][]? Template { get; init; }

        public SymbolRegular Symbol => Take.Issue == TakeIssue.None ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.ErrorCircle24;

        public Brush Brush => (Brush)Application.Current.FindResource(Take.Issue == TakeIssue.None ? "Kd.Success" : "Kd.Warning");

        public string Description => Take.Issue == TakeIssue.None
            ? Loc.T("wake.take", Take.Text.Length > 0 ? Take.Text : Loc.T("wake.takeNoText"), Take.Seconds.ToString("0.0", Loc.Instance.Culture))
            : Loc.T("wake.issue." + Take.Issue);

        public Visibility RemoveVisibility => Take.Issue == TakeIssue.None ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── 3 · normal speech ────────────────────────────────────────────────

    private int _calibratedTakes;

    private async void OnSpeech(object sender, RoutedEventArgs e) => await Busy(async cancel =>
    {
        SpeechButton.Content = Loc.T("wake.recording");
        using (Countdown(SpeechTime, SpeechLength))
            _speech = await _recorder.RecordAsync(_app.Config.Settings.VoiceMicId, SpeechLength, cancel);
        SpeechButton.Content = Loc.T("wake.understanding");
        var speech = _speech;
        var negatives = await Task.Run(() => WakeWordRecorder.Utterances(speech)
            .Select(u => _app.Voice.SpeechToText.Transcribe(u)).Where(t => t.Length > 0).ToList(), cancel);
        SpeechButton.Content = Loc.T("wake.again");

        _calibration = WakeWordVerifier.Calibrate(WordName, Kept.Select(t => t.Take.Text).Where(t => t.Length > 0).ToList(), negatives);
        _calibratedTakes = Kept.Count();
        _speechDone = true;
        SpeechResult.Text = negatives.Count == 0
            ? Loc.T("wake.speechNone", _calibration.Threshold.ToString("0.00", Loc.Instance.Culture))
            : Loc.T("wake.speechDone", _calibration.Negative.ToString("0.00", Loc.Instance.Culture), _calibration.Threshold.ToString("0.00", Loc.Instance.Culture));
        Log.Info($"Palabra de activación «{WordName}»: umbral {_calibration.Threshold:0.000}, habla normal {_calibration.Negative:0.000}, " +
                 $"grabaciones {string.Join(" ", _calibration.Positives.Select(p => p.ToString("0.00", CultureInfo.CurrentCulture)))}; negativos: {string.Join(" | ", negatives)}");
    });

    // ── 4 · try it ───────────────────────────────────────────────────────

    /// <summary>The word as it would be saved now (or the saved one, when only trying it).</summary>
    private WakeWord Candidate() => _testOnly ? _existing! : new WakeWord
    {
        Id = _existing?.Id ?? Core.Model.Ids.New(),
        Name = WordName,
        Enabled = _existing?.Enabled ?? true,
        Sensitivity = _existing?.Sensitivity ?? 0.5,
        Variants = Kept.Select(t => t.Take.Text).Where(t => t.Length > 0).Distinct().ToList(),
        Threshold = _calibration?.Threshold ?? WakeWordVerifier.DefaultThreshold,
        NegativeScore = _calibration?.Negative ?? 0,
        NoiseLevel = _noise,
    };

    /// <summary>The always-on detector of the word, trained in the background while the user tries it.</summary>
    private Task<WakeDetector?>? _detector;

    private void TrainDetector()
    {
        if (_testOnly)
        {
            var word = _existing!;
            _detector = word.Detector is { } trained ? Task.FromResult<WakeDetector?>(trained)
                : Task.Run(() =>
                {
                    _app.Voice.WakeWords.Train(word, _app.Voice.Features);
                    return word.Detector;
                });
            return;
        }
        var takes = Kept.Select(t => t.Take.Speech).ToList();
        var (silence, speech, name) = (_silence, _speech, WordName);
        _detector = Task.Run(() =>
        {
            try
            {
                var (detector, report) = WakeDetector.Train(_app.Voice.Features, takes, silence, speech);
                Log.Info($"Detector de prueba de «{name}»: {report.Milliseconds} ms, tomas ≥ {report.PositiveScore:0.00}, habla normal ≤ {report.NegativeScore:0.00}");
                return detector;
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo entrenar el detector de la palabra de activación", ex);
                return null;
            }
        });
    }

    private async void OnTest(object sender, RoutedEventArgs e) => await Busy(async cancel =>
    {
        TestButton.Content = Loc.T("wake.listening");
        var audio = await _recorder.RecordUtteranceAsync(_app.Config.Settings.VoiceMicId, TimeSpan.FromSeconds(6), cancel);
        TestButton.Content = Loc.T("wake.understanding");
        var segments = WakeWordRecorder.Segments(audio);
        var text = "";
        if (segments.Count > 0)
        {
            var pad = MicCapture.SampleRate / 8;
            var from = Math.Max(0, segments[0].Start - pad);
            var to = Math.Min(audio.Length, segments[^1].Start + segments[^1].Length + pad);
            text = await Task.Run(() => _app.Voice.SpeechToText.Transcribe(audio[from..to]), cancel);
        }
        // Phase 1: the detector, hearing the recording as it would live.
        (double Score, double Dtw, double Classifier)? live = null;
        if (_detector is not null)
        {
            if (!_detector.IsCompleted) TestButton.Content = Loc.T("wake.training");
            if (await _detector is { } trained) live = await Task.Run(() => trained.BestLive(_app.Voice.Features, audio), cancel);
        }
        TestButton.Content = Loc.T("wake.test");

        var word = Candidate();
        if (!_testOnly && _detector is { IsCompletedSuccessfully: true }) word.Detector = _detector.Result;
        var match = WakeWordVerifier.Find(text, word);
        var heard = live is null || live.Value.Score >= word.DetectorThreshold;
        var yes = heard && match is { Accepted: true };
        TestCard.Visibility = Visibility.Visible;
        TestIcon.Symbol = yes ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.DismissCircle24;
        TestIcon.SetResourceReference(ForegroundProperty, yes ? "Kd.Success" : "Kd.Warning");
        TestVerdict.Text = Loc.T(yes ? "wake.testYes" : "wake.testNo");
        var c = Loc.Instance.Culture;
        TestDetail.Text = text.Length == 0 ? Loc.T("wake.testNothing")
            : Loc.T("wake.testDetail", text)
              + "\n" + (live is { } l
                  ? Loc.T("wake.testScores", l.Score.ToString("0.00", c), word.DetectorThreshold.ToString("0.00", c),
                          (match?.Score ?? 0).ToString("0.00", c), word.EffectiveThreshold.ToString("0.00", c))
                  : Loc.T("wake.testScore2", (match?.Score ?? 0).ToString("0.00", c), word.EffectiveThreshold.ToString("0.00", c)))
              + (yes && match!.Rest.Length > 0 ? "\n" + Loc.T("wake.testOrder", match.Rest) : "");
        TestCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        Log.Info($"Prueba de «{word.Name}»: «{text}» → detector {(live is { } d ? $"{d.Score:0.000} (DTW {d.Dtw:0.00}, clasificador {d.Classifier:0.00}; umbral {word.DetectorThreshold:0.000})" : "—")}, " +
                 $"verificación {match?.Score ?? 0:0.000} (umbral {word.EffectiveThreshold:0.000})");
    });

    // ── save ─────────────────────────────────────────────────────────────

    private void Save()
    {
        var word = Candidate();
        try
        {
            Directory.CreateDirectory(word.Folder);
            foreach (var old in Directory.GetFiles(word.Folder, "*.wav")) File.Delete(old);
            var i = 1;
            foreach (var take in Kept)
            {
                var file = $"word_{i++}.wav";
                WakeWordStore.WriteWav(System.IO.Path.Combine(word.Folder, file), take.Take.Speech);
                word.Recordings.Add(file);
            }
            if (_silence is not null) WakeWordStore.WriteWav(System.IO.Path.Combine(word.Folder, "silence.wav"), _silence);
            if (_speech is not null) WakeWordStore.WriteWav(System.IO.Path.Combine(word.Folder, "speech.wav"), _speech);
            var micId = _app.Config.Settings.VoiceMicId;
            word.Microphone = MicCapture.Devices().FirstOrDefault(d => d.Id == micId).Name;
            word.Created = DateTime.Now;
            _app.Voice.WakeWords.Save(word);
            // The detector trained while trying it; otherwise the listener trains it from the recordings.
            if (_detector is { IsCompletedSuccessfully: true, Result: { } detector }) _app.Voice.WakeWords.Save(word, detector);
            Saved = word;
            _app.Achievements.Unlock("wake-create");
            Log.Info($"Palabra de activación guardada: «{word.Name}» ({word.Recordings.Count} grabaciones, umbral {word.Threshold:0.000})");
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo guardar la palabra de activación", ex);
            ShowError(ex.Message);
        }
    }
}
