using SongSense.Core;

namespace SongSense.App;

public sealed partial class MainViewModel
{
    public string DisplayStatus => StorageError ? "Error local" : analysing ? "Analizando…" : searching ? "Buscando…" :
        insightState is ApplicationState.Error or ApplicationState.Offline or ApplicationState.DailyLimit ? "Revisar" :
        HasInsight ? "Listo" : HasPreparedLyrics ? "Letra lista" : CurrentTrack is not null ? "Escuchando" : "Sin canción";
    public string DisplayNotice => languageSaveFailed ? languageMessage : StorageError ? "No se pudo abrir el almacenamiento. Revisa los detalles." :
        insightState is ApplicationState.Error or ApplicationState.Offline ? "No se pudo completar el análisis. Ver detalles en Ajustes." :
        insightState == ApplicationState.DailyLimit ? "Has alcanzado el límite diario." :
        HasPendingCachedLyrics || Candidates.Count > 0 && !HasPreparedLyrics ? "Elige una versión de la letra en Más." :
        lyricsState is ApplicationState.Error or ApplicationState.Offline or ApplicationState.NoLyrics ? "No se encontró la letra. Puedes buscarla o añadirla desde Más." : "";
    public string CompactUsage => usageMessage.StartsWith("Consumo de esta consulta:", StringComparison.Ordinal)
        ? usageMessage.Split('·')[0].Replace("Consumo de esta consulta:", "").Trim()
        : usageMessage.Contains("0 tokens", StringComparison.Ordinal) ? "Guardado · 0 tokens nuevos"
        : usageMessage.StartsWith("Consumo no comunicado", StringComparison.Ordinal) ? "Consumo no disponible" : "";
    public string ReadingHint => HasInsight && Insight?.Language == targetLanguage ? TranslationMessage :
        HasPreparedLyrics && !CanUseAi ? "Activa las consultas desde Cuenta." :
        HasPreparedLyrics ? "Pulsa Traducir y explicar para ver la traducción." : "Busca la letra para empezar.";
    public bool ShowGenerate => HasPreparedLyrics && !HasInsight && !analysing;
    public bool ShowSearch => !HasPreparedLyrics && !searching;
}
