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
            targetLanguage = value;
            configurationRevision++; InvalidateInsight();
            languageSaving = true; languageSaveFailed = false; languageMessage = "Guardando…";
            OnChanged(); OnChanged(nameof(LanguageMessage)); NotifyLyrics();
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
            languageSaving = false; languageMessage = "";
            QueueCacheRestore();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (revision != languageRevision || language != targetLanguage) return;
            languageSaving = false; languageSaveFailed = true;
            languageMessage = "No se pudo guardar el idioma. Selecciónalo de nuevo.";
        }
        finally { OnChanged(nameof(LanguageMessage)); NotifyLyrics(); }
    }
}
