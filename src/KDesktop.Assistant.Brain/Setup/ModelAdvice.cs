using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Brain.Setup;

/// <summary>Which model to download when there is none.</summary>
public static class ModelAdvice
{
    /// <summary>The usual one: about 4 B parameters, with tools. Quick on a normal graphics card.</summary>
    public static readonly BrainSuggestion Standard = new("qwen3.5:4b", 3.3);

    /// <summary>For little video memory or no graphics card: smaller, also with tools.</summary>
    public static readonly BrainSuggestion Light = new("qwen3.5:2b", 2.7);

    /// <summary>Video memory with which the usual model runs with room to spare.</summary>
    public const double StandardVideoMemoryGb = 6;

    /// <summary>The one for this video memory (unknown: the usual one).</summary>
    public static BrainSuggestion Recommend(double? videoMemoryGb) =>
        videoMemoryGb is { } gb && gb < StandardVideoMemoryGb ? Light : Standard;
}
