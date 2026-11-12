namespace Aras.SreHub.Models;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    // AzureOpenAI, or OpenAI for any OpenAI-compatible endpoint (OpenAI, Ollama, GitHub Models).
    public string Provider { get; set; } = "AzureOpenAI";
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2024-10-21";

    public bool IsAzure => string.Equals(Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Model)
        && (IsAzure
            ? !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey)
            : !string.IsNullOrWhiteSpace(Endpoint) || !string.IsNullOrWhiteSpace(ApiKey));
}

public sealed record AssistantChatRequest(string? ConversationId, string Message);

public sealed record AssistantResolveRequest(string ConversationId, bool Approve);

public sealed record AssistantResetRequest(string? ConversationId);

public sealed record AssistantStep(string Tool, string Summary);

public sealed record AssistantPending(string Tool, string Summary, string Details);

public sealed record AssistantReply(string ConversationId, string? Reply, IReadOnlyList<AssistantStep> Steps, AssistantPending? Pending);
