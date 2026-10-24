namespace SongSense.Core;

public enum AiUsageState { NoSolicitado, EnCurso, Comunicado, NoComunicado, RecuperadoDeCache }

// Captured when the operation starts; never reconstructed from the active song or UI text.
public sealed record AiUsageContext(string Operation, string Subject, long? TrackRevision,
    long? LyricsRevision, string Language, AiConnectionMode Mode, string? Model, string Scope);

public sealed record AiUsagePresentation(AiUsageState State, AiUsage? Tokens = null,
    AiUsageContext? Context = null, DateTimeOffset? At = null)
{
    private static long nextSequence;
    public long Sequence { get; init; } = Interlocked.Increment(ref nextSequence);
    public string Compact => State switch
    {
        AiUsageState.NoSolicitado => "",
        AiUsageState.EnCurso => "Consumo pendiente",
        AiUsageState.Comunicado when Tokens is { } usage => $"{usage.TotalTokens:N0} tokens · OpenAI",
        AiUsageState.RecuperadoDeCache => "Guardado en este dispositivo · 0 tokens nuevos",
        _ => "Consumo no disponible"
    };
    public string Description => State switch
    {
        AiUsageState.NoSolicitado => "Todavía no se ha solicitado IA en este contexto.",
        AiUsageState.EnCurso => "Operación en curso · Consumo pendiente de comunicar por OpenAI.",
        AiUsageState.Comunicado when Tokens is { } usage => usage.Describe(),
        AiUsageState.RecuperadoDeCache => "Guardado en este dispositivo · 0 tokens nuevos; no se ha enviado una consulta.",
        _ => "Consumo no disponible · Consumo no comunicado por OpenAI; no equivale a 0 tokens."
    };
}
