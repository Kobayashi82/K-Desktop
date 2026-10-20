using KDesktop.Core.Assistant.Brain;

namespace KDesktop.Assistant.Brain.Models;

/// <summary>Makes the client of the provider chosen in Options → Assistant → Brain.</summary>
public static class ChatModels
{
    /// <summary>Null when the user chose no AI.</summary>
    public static IChatModel? Create(BrainConfig config, HttpClient? http = null) => config.Provider switch
    {
        BrainProvider.None => null,
        BrainProvider.Ollama => new OllamaChatModel(config.Address, http),
        BrainProvider.LlamaSwap => new OpenAICompatibleChatModel(config.Address, config.ApiKey, http) { UnloadPath = "unload", RunningPath = "running" },
        _ => new OpenAICompatibleChatModel(config.Address, config.ApiKey, http),
    };
}
