using SongSense.Core;
namespace SongSense.App;

public sealed partial class MainViewModel
{
    public string HeaderTitle => CurrentTrack is null ? "Song Sense" : TrackTitle;
    public string LyricsEmptyTitle => CurrentTrack is null ? "Esperando una canción" : PreparedLyrics?.IsInstrumental == true ? "Pieza instrumental" : Preparation.Cause == PreparationCause.NotFound ? "Letra no disponible" : "Prepara la letra";
    public string LyricsEmptyExplanation => CurrentTrack is null ? WelcomeInstruction : PreparedLyrics?.IsInstrumental == true ? "Esta versión no contiene texto. Puedes revisar otra versión." : Preparation.Message;
    private string WelcomeInstruction => !AutomaticUpdatesEnabled
        ? "Reanuda el seguimiento para recuperar la última observación."
        : Preparation.Cause == PreparationCause.NoSpotify
            ? "Abre Spotify y reproduce una canción."
            : "Reproduce una canción en Spotify.";
    public string MeaningEmptyTitle => CurrentTrack is null ? "Esperando una canción" : PreparedLyrics?.IsInstrumental == true ? "Pieza instrumental" : HasPreparedLyrics ? "Todavía no hay análisis" : "Primero, la letra";
    public string MeaningEmptyExplanation => CurrentTrack is null ? LyricsEmptyExplanation : PreparedLyrics?.IsInstrumental == true ? LyricsEmptyExplanation : HasPreparedLyrics ? "Puedes leer la letra original o pedir una interpretación con una acción expresa." : "Busca o confirma la letra para poder interpretarla.";
    public bool HasReadableOriginal => HasPreparedLyrics && PreparedLyrics?.IsInstrumental != true;
    public bool HasFailedLocalSave => cache is not null && (lyricsSaveState == ContentSaveState.Failed || insightSaveState == ContentSaveState.Failed);
    public string SaveRecoveryMessage => HasFailedLocalSave ? "El contenido sigue disponible en esta sesión, pero su guardado local falló. Puedes reintentar guardarlo sin volver a consultar al proveedor de IA." : "";
    private bool retryingLocalSave;
    public AsyncCommand RetryLocalSaveCommand { get; }
    public bool CanRetryLocalSave => HasFailedLocalSave && storageReady && !StorageError && !deletingCache && !searching && !analysing && !retryingLocalSave && !automaticStopped && !lifetime.IsCancellationRequested && (lyricsSaveState == ContentSaveState.Failed || Insight is not null && AiSettingsRules.IsValidModel(EffectiveModel));
    public Task RetryLocalSaveAsync()
    {
        var task = RetryLocalSaveCoreAsync(); TrackCacheTask(task); return task;
    }
    private async Task RetryLocalSaveCoreAsync()
    {
        if (!CanRetryLocalSave || cache is null || CurrentTrack is not { } track || PreparedLyrics is not { } lyrics) return;
        var result = Insight; var revision = LyricsRevision; var epoch = cacheEpoch; var configuration = configurationRevision; var model = EffectiveModel; var scope = CacheScope; var language = targetLanguage;
        var saveLyrics = lyricsSaveState == ContentSaveState.Failed; var saveInsight = insightSaveState == ContentSaveState.Failed && result is not null && !string.IsNullOrWhiteSpace(model);
        bool Current() => CurrentTrack?.Revision == track.Revision && LyricsRevision == revision && cacheEpoch == epoch && configurationRevision == configuration && ReferenceEquals(PreparedLyrics, lyrics) && ReferenceEquals(Insight, result) && !automaticStopped && !lifetime.IsCancellationRequested;
        retryingLocalSave = true; NotifyProvenance();
        try
        {
            var written = await CacheIoAsync(async () =>
            {
                if (!Current()) return false;
                if (saveLyrics) await cache.SaveLyricsAsync(track, lyrics, lifetime);
                if (saveInsight) await cache.SaveInsightAsync(track, lyrics, model!, CacheIdentity.Version, result!, lifetime, scope, language);
                return true;
            }, lifetime);
            if (written && Current())
            {
                if (saveLyrics) lyricsSaveState = ContentSaveState.Saved;
                if (saveInsight) insightSaveState = ContentSaveState.Saved;
                cacheMessage = "Guardado local completado. No se ha enviado ninguna consulta de IA.";
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { if (Current()) cacheMessage = "No se pudo guardar todavía. Conservas el contenido en esta sesión y puedes reintentar."; }
        finally { retryingLocalSave = false; NotifyProvenance(); OnChanged(nameof(CacheMessage)); }
    }
    private void NotifyMainComposition()
    {
        foreach (var name in new[] { nameof(HeaderTitle), nameof(LyricsEmptyTitle), nameof(LyricsEmptyExplanation), nameof(MeaningEmptyTitle), nameof(MeaningEmptyExplanation), nameof(HasReadableOriginal), nameof(PreparationPresentation), nameof(OperationFeedback) }) OnChanged(name);
        RetryLocalSaveCommand?.Refresh();
    }
}
