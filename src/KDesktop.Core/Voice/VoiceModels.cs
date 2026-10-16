namespace KDesktop.Core.Voice;

/// <summary>Where the voice models live inside <see cref="AppPaths.ModelsDir"/> and whether they are there.</summary>
public static class VoiceModels
{
    /// <summary>Silero VAD: tells speech from silence (endpointing).</summary>
    public static string VadFile => Path.Combine(AppPaths.ModelsDir, "silero_vad.onnx");

    /// <summary>NVIDIA Parakeet TDT 0.6B v3 (multilingual, Spanish included), int8, in sherpa-onnx's layout.</summary>
    public static string ParakeetDir => Path.Combine(AppPaths.ModelsDir, "parakeet-tdt-0.6b-v3");

    public static string ParakeetEncoder => Path.Combine(ParakeetDir, "encoder.int8.onnx");
    public static string ParakeetDecoder => Path.Combine(ParakeetDir, "decoder.int8.onnx");
    public static string ParakeetJoiner => Path.Combine(ParakeetDir, "joiner.int8.onnx");
    public static string ParakeetTokens => Path.Combine(ParakeetDir, "tokens.txt");

    /// <summary>openWakeWord's feature models (mel spectrogram + Google speech embedding), for the wake word detector.</summary>
    public static string WakeDir => Path.Combine(AppPaths.ModelsDir, "openwakeword");

    public static string WakeMelspec => Path.Combine(WakeDir, "melspectrogram.onnx");
    public static string WakeEmbedding => Path.Combine(WakeDir, "embedding_model.onnx");

    public static bool HasVad => File.Exists(VadFile);

    public static bool HasWakeDetector => File.Exists(WakeMelspec) && File.Exists(WakeEmbedding);

    /// <summary>Every model file is there (voice orders and the always-on wake word detector).</summary>
    public static bool AllInstalled => Ready && HasWakeDetector;

    public static bool HasSpeechToText =>
        File.Exists(ParakeetEncoder) && File.Exists(ParakeetDecoder) && File.Exists(ParakeetJoiner) && File.Exists(ParakeetTokens);

    /// <summary>Everything voice commands need.</summary>
    public static bool Ready => HasVad && HasSpeechToText;
}
