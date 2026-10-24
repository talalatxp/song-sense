using SongSense.Core;
namespace SongSense.App;
public enum ContentSaveState { SessionOnly, Pending, Saved, Failed }
public sealed partial class MainViewModel
{
    private ContentSaveState lyricsSaveState, insightSaveState;
    public ContentSaveState LyricsSaveState => lyricsSaveState;
    public ContentSaveState InsightSaveState => insightSaveState;
    private static string SaveLabel(ContentSaveState state) => state switch
    {
        ContentSaveState.Saved => "Guardado en este dispositivo",
        ContentSaveState.Pending => "Guardado local pendiente",
        ContentSaveState.Failed => "Guardado local fallido · disponible en esta sesión",
        _ => "Solo en esta sesión"
    };
    public string ReadingSource => HasPreparedLyrics ? $"{LyricsSource} · {SaveLabel(lyricsSaveState)}" : "";
    public string AnalysisDetails => Insight is not { } result ? "No hay análisis para el idioma seleccionado." :
        $"Destino: {OutputLanguages.Get(result.TargetLanguage).Name}\nIdioma original: {LanguageLabel(result.Language)}\nAnálisis: {SaveLabel(insightSaveState)}\n" +
        (result.Provenance is { } source
            ? $"Proveedor: {(source.Mode == AiConnectionMode.ApiKey ? "OpenAI · API" : "ChatGPT · sesión")}\nModelo: {source.Model}\nFecha del análisis: {source.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm:ss} · hora local de Windows"
            : "Proveedor, modelo y fecha: no registrados para este resultado antiguo o sin procedencia comunicada.");
    private static string LanguageLabel(string language) => language == "mixed" ? "Mixto" : OutputLanguages.All.FirstOrDefault(l => l.Code == language)?.Name ?? language;
    private void NotifyProvenance()
    {
        OnChanged(nameof(LyricsSaveState)); OnChanged(nameof(InsightSaveState)); OnChanged(nameof(ReadingSource)); OnChanged(nameof(AnalysisDetails)); OnChanged(nameof(SongDetails));
    }
}
