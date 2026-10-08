namespace SongSense.Core;

// Provider-reported tokens for one response, never a conversion to plan allowance.
public sealed record AiUsage(long InputTokens, long OutputTokens, long TotalTokens)
{
    public string Describe() => $"Consumo de esta consulta: {TotalTokens:N0} tokens · Entrada: {InputTokens:N0} · Salida: {OutputTokens:N0}";
}

public enum AiConnectionMode { ChatGptIncluded, ApiKey }

public interface IAnalysisSession
{
    AiConnectionMode Mode { get; }
    string? Model { get; }
    string CacheScope { get; }
    bool CanGenerate { get; }
    string Status { get; }
    event EventHandler? Changed;
}
