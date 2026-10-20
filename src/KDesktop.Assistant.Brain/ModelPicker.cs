using System.Globalization;
using System.Text.RegularExpressions;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Core.Assistant.Brain;

namespace KDesktop.Assistant.Brain;

/// <summary>Chooses the model the assistants talk with (Kobii's rule, plan §5.1).</summary>
public static partial class ModelPicker
{
    /// <summary>Ideal size in billions of parameters: quick, and enough to follow instructions and call tools.</summary>
    private const double IdealBillions = 4.5;

    /// <summary>
    /// The preferred one when the server has it; otherwise a chat model with tools whose size is nearest the ideal (huge
    /// models are slow and take a lot of video memory; tiny ones do not follow instructions).
    /// </summary>
    public static ModelInfo? Choose(IReadOnlyList<ModelInfo> models, string? preferred)
    {
        if (Find(models, preferred) is { } match) return match;
        return Chat(models)
            .OrderByDescending(m => m.SupportsTools)
            .ThenBy(m => SizeOf(m) is var size && size <= 0 ? 10 : Math.Abs(Math.Log(size / IdealBillions)))
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>The model of that name («qwen3.5:4b» also finds «qwen3.5:4b:latest»…), or null.</summary>
    public static ModelInfo? Find(IReadOnlyList<ModelInfo> models, string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
        : models.FirstOrDefault(m => string.Equals(m.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
          ?? models.FirstOrDefault(m => BrainProviders.SameModel(m.Name, name));

    /// <summary>The models one can talk with (not those that only make vectors).</summary>
    public static IEnumerable<ModelInfo> Chat(IEnumerable<ModelInfo> models) => models.Where(m => !m.IsEmbeddingOnly);

    /// <summary>
    /// Its size; when the server does not say (llama-swap, LM Studio), guessed from the name: «qwen3.5-4b» → 4,
    /// «gemma4-12b-vision» → 12, «qwen3.6-35b-a3b» → 35.
    /// </summary>
    public static double SizeOf(ModelInfo model)
    {
        if (model.ParameterBillions > 0) return model.ParameterBillions;
        var match = SizeInName().Match(model.Name);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? size : 0;
    }

    [GeneratedRegex(@"(?<![\d.])(\d+(?:\.\d+)?)b(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex SizeInName();
}
