using KDesktop.Assistant.Sdk;
using KDesktop.Core;

namespace KDesktop.Assistant.Host.Audio;

/// <summary>
/// The sound of the process: one mixer and one output for every assistant, under the module's settings (Options →
/// Assistant: sound on or off and its volume, the <c>general</c> section of <c>config</c>).
/// </summary>
internal sealed class HostAudio : IDisposable
{
    private readonly Mixer _mixer = new();
    private readonly SoundOutput _output;

    public HostAudio() => _output = new SoundOutput(_mixer);

    public bool Enabled => !_mixer.Muted;

    /// <summary>The module's settings changed (or came with the welcome).</summary>
    public void Apply(bool enabled, double volume)
    {
        if (Enabled != enabled) Log.Info(enabled ? "Asistente: sonido activado" : "Asistente: sonido desactivado");
        _mixer.Volume = volume;
        _mixer.Muted = !enabled;
        // Loops that were up sound again.
        if (enabled && _mixer.HasSound) _output.Wake();
    }

    public void Play(HostSound sound, double gain, double pan)
    {
        if (!Enabled) return;
        _mixer.Play(sound, gain, pan);
        _output.Wake();
    }

    public HostLoop Loop(HostSound sound) => _mixer.Loop(sound);

    /// <summary>Starts a sentence of an assistant's voice (it ends at once with the sound off).</summary>
    public void Speak(SpeechLine line, double gain, double pan)
    {
        _mixer.Speak(line, gain, pan);
        if (Enabled) _output.Wake();
    }

    /// <summary>A loop went up: the output must be running.</summary>
    public void Wake()
    {
        if (Enabled) _output.Wake();
    }

    public void Dispose() => _output.Dispose();
}

/// <summary>
/// <see cref="IAudio"/> for one assistant: its loops are remembered so they stop when it is unloaded (even if it did
/// not dispose them), and its own volume (stage 13.13) goes over everything it plays. Muted, everything loads and plays
/// as silence.
/// </summary>
internal sealed class AssistantAudio(HostAudio audio, string packageDirectory) : IAudio
{
    private readonly List<AssistantLoop> _loops = [];
    private double _volume = 1;

    public bool Enabled => audio.Enabled;

    /// <summary>Its own volume (0…1, the «Basics» tab of its card), with the same curve as the one of all the assistants.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            Factor = _volume * _volume;
            foreach (var loop in _loops) loop.Apply();
        }
    }

    /// <summary>What its gains are multiplied by.</summary>
    private double Factor { get; set; } = 1;

    public ISound Load(float[] samples, int sampleRate, int channels = 1)
    {
        ArgumentNullException.ThrowIfNull(samples);
        // A copy: what it does with its array afterwards does not reach the mixer thread.
        return new HostSound((float[])samples.Clone(), sampleRate, channels);
    }

    public ISound LoadWav(string path) => WavFile.Read(Path.Combine(packageDirectory, path));

    public void Play(ISound sound, double gain = 1, double pan = 0)
    {
        if (Factor > 0) audio.Play(Own(sound), gain * Factor, pan);
    }

    public ILoop Loop(ISound sound)
    {
        _loops.RemoveAll(l => l.Disposed);
        var loop = new AssistantLoop(this, audio, audio.Loop(Own(sound)));
        _loops.Add(loop);
        return loop;
    }

    /// <summary>Stops what it left playing.</summary>
    public void DisposeAll()
    {
        foreach (var loop in _loops) loop.Dispose();
        _loops.Clear();
    }

    private static HostSound Own(ISound sound) =>
        sound as HostSound ?? throw new ArgumentException("El sonido no es de este host (usa Load o LoadWav)", nameof(sound));

    private sealed class AssistantLoop(AssistantAudio owner, HostAudio audio, HostLoop loop) : ILoop
    {
        /// <summary>The gain it asked for (the loop sounds at it times its assistant's volume).</summary>
        private double _gain = loop.Gain;

        public bool Disposed { get; private set; }

        public double Gain
        {
            get => _gain;
            set
            {
                _gain = value;
                Apply();
            }
        }

        /// <summary>Its gain, with its assistant's volume.</summary>
        public void Apply()
        {
            if (Disposed) return;
            var gain = _gain * owner.Factor;
            var rising = gain > 0 && loop.Gain <= 0;
            loop.Gain = gain;
            if (rising) audio.Wake();
        }

        public double Pitch { get => loop.Pitch; set => loop.Pitch = value; }

        public double Pan { get => loop.Pan; set => loop.Pan = value; }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            loop.Dispose();
        }
    }
}
