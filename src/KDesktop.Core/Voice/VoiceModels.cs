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

    public static bool HasVad => File.Exists(VadFile);

    public static bool HasSpeechToText =>
        File.Exists(ParakeetEncoder) && File.Exists(ParakeetDecoder) && File.Exists(ParakeetJoiner) && File.Exists(ParakeetTokens);

    /// <summary>Everything voice commands need.</summary>
    public static bool Ready => HasVad && HasSpeechToText;
}
