using KDesktop.Assistant.Brain.Models;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Brain.Setup;

/// <summary>Looks for a brain on this PC: Ollama, llama-swap and LM Studio at their usual addresses.</summary>
public static class BrainScout
{
    /// <summary>Where each server usually is, best first.</summary>
    public static readonly IReadOnlyList<BrainProvider> Usual = [BrainProvider.Ollama, BrainProvider.LlamaSwap, BrainProvider.OpenAICompatible];

    /// <summary>Longest wait per server (a local one that is not there says no at once).</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(4);

    /// <summary>Asks all the usual places at once (but the one in use) and gives those that answer, with how many chat models they have.</summary>
    public static async Task<IReadOnlyList<BrainFound>> ScanAsync(BrainConfig current, Func<BrainConfig, IChatModel?> create, CancellationToken cancellationToken)
    {
        var places = Usual
            .Select(p => new BrainConfig(p, ApiKey: current.ApiKey))
            .Where(p => !(p.Provider == current.Provider && SameEndpoint(p.Address, current.Address)));
        var results = await Task.WhenAll(places.Select(p => ProbeAsync(p, create, cancellationToken))).ConfigureAwait(false);
        return [.. results.OfType<BrainFound>().OrderByDescending(f => f.Models > 0)];
    }

    public static bool SameEndpoint(string a, string b) => string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static async Task<BrainFound?> ProbeAsync(BrainConfig place, Func<BrainConfig, IChatModel?> create, CancellationToken cancellationToken)
    {
        using var model = create(place);
        if (model is null) return null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Patience);
            var models = await model.ListModelsAsync(timeout.Token).ConfigureAwait(false);
            return new BrainFound(place.Provider.Key(), place.Address, ModelPicker.Chat(models).Count());
        }
        catch (ChatModelException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
