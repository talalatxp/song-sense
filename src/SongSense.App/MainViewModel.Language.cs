using SongSense.Core;

namespace SongSense.App;

public sealed partial class MainViewModel
{
    private string targetLanguage = OutputLanguages.Default;
    private bool languageSaving, languageSaveFailed;
    private long languageRevision;
    private enum LanguageNotice { None, Selected, Cached }
    private LanguageNotice languageNotice;
    private LanguageContext? languageContext;
    private SavedLanguageResult? previousLanguageResult;
    private bool languageCancelledAnalysis;
    private sealed record LanguageContext(long? Track, long Lyrics, long Epoch, string Scope, string? Model);
    private LanguageContext CurrentLanguageContext => new(CurrentTrack?.Revision, LyricsRevision, cacheEpoch, CacheScope, EffectiveModel);
    private bool LanguageContextIsCurrent => languageContext == CurrentLanguageContext;
    public IReadOnlyList<OutputLanguage> AvailableLanguages => OutputLanguages.All;
    public bool CanChangeLanguage => storageReady && !StorageError && !lifetime.IsCancellationRequested;
    public string LanguageMessage
    {
        get
        {
            var destination = OutputLanguages.Get(targetLanguage).Name;
            var cancelled = LanguageContextIsCurrent && languageCancelledAnalysis
                ? " Se canceló el análisis en curso al cambiar el idioma; una solicitud ya enviada puede consumir tokens y cuenta para el límite local." : "";
            if (languageSaving) return $"Guardando idioma de lectura: {destination}… No se ha enviado una consulta nueva." + cancelled;
            if (languageSaveFailed) return $"No se pudo guardar el idioma {destination}. No se ha enviado una consulta nueva. Reintenta el guardado; la generación permanece bloqueada." + cancelled;
            if (!LanguageContextIsCurrent || languageNotice == LanguageNotice.None) return "";
            if (languageNotice == LanguageNotice.Cached)
                return $"Resultado en {destination} recuperado de este dispositivo. No se ha enviado una consulta nueva." + cancelled;
            var message = $"Idioma cambiado a {destination}. No se ha enviado una consulta nueva. Aún no hay un resultado en este idioma.";
            if (store is not IOutputLanguageSettings) message += " Selección solo para esta sesión.";
            if (previousLanguageResult is { } saved && saved.Context == CurrentLanguageContext)
                message += $" El resultado anterior en {saved.Language} tiene guardado confirmado en este dispositivo. Volver a ese idioma puede recuperarlo mientras siga en caché.";
            return message + cancelled;
        }
    }
    public bool ShowLanguageMessage => LanguageMessage.Length > 0;
    // The essential action already offers retry when preparation selects it.
    public bool ShowLanguageRetry => languageSaveFailed && RetryLanguageCommand.CanExecute(null) && Preparation.Action != PreparationAction.RetryLanguage;
    public string LanguageSummary
    {
        get
        {
            if (!ShowLanguageMessage) return "";
            var destination = OutputLanguages.Get(targetLanguage).Name;
            var summary = languageSaving ? $"Guardando idioma: {destination}… Sin consulta nueva."
                : languageSaveFailed ? $"No se pudo guardar {destination}. Reintenta el guardado antes de generar."
                : languageNotice == LanguageNotice.Cached ? $"{destination}: resultado recuperado del dispositivo. Sin consulta nueva."
                : $"{destination} seleccionado. Sin resultado nuevo ni consulta nueva.";
            if (LanguageContextIsCurrent && languageCancelledAnalysis)
            {
                var state = languageSaving ? "guardando selección" : languageSaveFailed ? "reintenta el guardado"
                    : languageNotice == LanguageNotice.Cached ? "resultado del dispositivo" : "sin resultado nuevo";
                return $"{destination}: {state}. Análisis cancelado; puede haber consumo. Sin consulta nueva.";
            }
            return summary;
        }
    }
    public string LanguagePreferenceStatus => languageSaving ? "Guardando selección…" : languageSaveFailed ? "Selección sin guardar." : "Destino seleccionado: " + OutputLanguages.Get(targetLanguage).Name;
    public NoticeKind LanguageNoticeKind => languageSaveFailed ? NoticeKind.Error : languageSaving ? NoticeKind.Progress : NoticeKind.Information;
    private sealed record SavedLanguageResult(LanguageContext Context, string Language);
    private void ClearLanguageNotice()
    {
        languageNotice = LanguageNotice.None; languageContext = null;
        previousLanguageResult = null; languageCancelledAnalysis = false;
        NotifyLanguageNotice();
    }
    private void NotifyLanguageNotice()
    {
        foreach (var property in new[] { nameof(LanguageMessage), nameof(ShowLanguageMessage), nameof(LanguageSummary), nameof(ShowLanguageRetry), nameof(LanguageNoticeKind), nameof(LanguagePreferenceStatus) }) OnChanged(property);
    }
    private void LanguageCacheRestored()
    {
        if (!LanguageContextIsCurrent || languageSaving || languageSaveFailed) return;
        languageNotice = LanguageNotice.Cached; previousLanguageResult = null; NotifyLanguageNotice();
    }
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
            var saved = HasInsight && insightSaveState == ContentSaveState.Saved ? new SavedLanguageResult(CurrentLanguageContext, OutputLanguages.Get(targetLanguage).Name) : null;
            var cancelled = analysing || LanguageContextIsCurrent && languageCancelledAnalysis;
            targetLanguage = value;
            configurationRevision++; InvalidateInsight();
            languageContext = CurrentLanguageContext; previousLanguageResult = saved; languageCancelledAnalysis = cancelled;
            languageSaving = true; languageSaveFailed = false;
            OnChanged(); NotifyLanguageNotice(); NotifyLyrics();
            TrackCacheTask(PersistLanguageAsync(value, ++languageRevision));
        }
    }
    private async Task PersistLanguageAsync(string language, long revision)
    {
        try
        {
            if (store is IOutputLanguageSettings preferences)
                await CacheIoAsync(async () => { await preferences.SaveOutputLanguageAsync(language, lifetime); return true; }, lifetime);
            if (revision != languageRevision || language != targetLanguage) return;
            languageSaving = false;
            languageNotice = LanguageContextIsCurrent ? LanguageNotice.Selected : LanguageNotice.None;
            QueueCacheRestore();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (revision != languageRevision || language != targetLanguage) return;
            languageSaving = false; languageSaveFailed = true;
        }
        finally { NotifyLanguageNotice(); NotifyLyrics(); }
    }
}
