using SongSense.Core;

namespace SongSense.App;

public sealed partial class MainViewModel
{
    private string targetLanguage = OutputLanguages.Default;
    private bool languageSaving, languageSaveFailed;
    private long languageRevision;
    private string languageMessage = "";
    public IReadOnlyList<OutputLanguage> AvailableLanguages => OutputLanguages.All;
    public bool CanChangeLanguage => storageReady && !StorageError && !lifetime.IsCancellationRequested;
    public string LanguageMessage => languageMessage;
    public NoticeKind LanguageNoticeKind => languageSaveFailed ? NoticeKind.Error : languageSaving ? NoticeKind.Progress : NoticeKind.Information;
    private sealed record SavedLanguageResult(long? Track, long Lyrics, long Epoch, string Scope, string? Model, string Language);
    public AsyncCommand RetryLanguageCommand { get; }
    public Task RetryLanguageAsync()
    {
        if (!languageSaveFailed || !CanChangeLanguage || languageSaving) return Task.CompletedTask;
        TargetLanguage = targetLanguage;
        return WaitForCacheAsync();
    }
    public string TargetLanguage
    {
        get => targetLanguage;
        set
        {
            if (!CanChangeLanguage || !OutputLanguages.IsSupported(value) || value == targetLanguage && !languageSaveFailed) return;
            var saved = HasInsight && insightSaveState == ContentSaveState.Saved ? new SavedLanguageResult(CurrentTrack?.Revision, LyricsRevision, cacheEpoch, CacheScope, EffectiveModel, OutputLanguages.Get(targetLanguage).Name) : null;
            targetLanguage = value;
            configurationRevision++; InvalidateInsight();
            languageSaving = true; languageSaveFailed = false; languageMessage = $"Idioma cambiado a {OutputLanguages.Get(value).Name}. No se ha enviado una consulta. Guardando selección…";
            OnChanged(); OnChanged(nameof(LanguageMessage)); NotifyLyrics();
            TrackCacheTask(PersistLanguageAsync(value, ++languageRevision, saved));
        }
    }
    private async Task PersistLanguageAsync(string language, long revision, SavedLanguageResult? saved)
    {
        try
        {
            if (store is IOutputLanguageSettings preferences)
                await CacheIoAsync(async () => { await preferences.SaveOutputLanguageAsync(language, lifetime); return true; }, lifetime);
            if (revision != languageRevision || language != targetLanguage) return;
            languageSaving = false; languageMessage = $"Idioma cambiado a {OutputLanguages.Get(language).Name}. No se ha enviado una consulta.";
            if (store is not IOutputLanguageSettings) languageMessage += " Selección solo para esta sesión.";
            if (saved is not null && saved.Track == CurrentTrack?.Revision && saved.Lyrics == LyricsRevision && saved.Epoch == cacheEpoch && saved.Scope == CacheScope && saved.Model == EffectiveModel)
                languageMessage += $" El resultado anterior en {saved.Language} tiene guardado confirmado en este dispositivo. Volver a ese idioma puede recuperarlo mientras siga en caché.";
            QueueCacheRestore();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (revision != languageRevision || language != targetLanguage) return;
            languageSaving = false; languageSaveFailed = true;
            languageMessage = "No se pudo guardar el idioma. No se ha enviado una consulta. Reintenta el guardado; la generación permanece bloqueada.";
        }
        finally { OnChanged(nameof(LanguageMessage)); OnChanged(nameof(LanguageNoticeKind)); NotifyLyrics(); }
    }
}
