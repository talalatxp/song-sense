using SongSense.Core;

namespace SongSense.App;

public sealed partial class MainViewModel
{
    private bool automaticUpdatesEnabled = true;
    private bool automaticStarted, automaticStopped, automaticPending, automaticRunning;
    private long automaticSequence;
    private long? automaticAttemptedRevision;
    private CancellationTokenSource? automaticRequest;
    private Task automaticWorker = Task.CompletedTask;
    private TrackDetection latestObservation = new(ApplicationState.NoSpotify, null, null);
    private string automaticMessage = "Actualización automática activada · IA sujeta a configuración y límites.";
    public string AutomaticMessage => !AutomaticUpdatesEnabled ? "Actualización pausada · Contenido congelado" : automaticMessage;
    public bool AutomaticUpdatesEnabled
    {
        get => automaticUpdatesEnabled;
        set
        {
            if (automaticUpdatesEnabled == value || automaticStopped) return;
            automaticUpdatesEnabled = value;
            automaticSequence++; automaticRequest?.Cancel(); automaticPending = false;
            if (!value)
            {
                cacheEpoch++; CancelLyrics(); CancelInsight();
            }
            else
            {
                ApplyActiveDetection(latestObservation);
                ScheduleAutomatic(true);
            }
            NotifyAutomatic();
            if (storageReady && store is IAutomaticUpdateSettings) TrackCacheTask(PersistAutomaticAsync(value));
        }
    }
    public AsyncCommand RetryAutomaticCommand { get; }
    private void NotifyAutomatic()
    {
        OnChanged(nameof(AutomaticUpdatesEnabled)); OnChanged(nameof(AutomaticMessage)); RetryAutomaticCommand.Refresh();
    }
    private async Task PersistAutomaticAsync(bool enabled)
    {
        try
        {
            await CacheIoAsync(async () => { await ((IAutomaticUpdateSettings)store).SaveAutomaticUpdatesAsync(enabled, lifetime); return true; }, lifetime);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { cacheMessage = "No se pudo guardar la preferencia de actualización automática. Comprueba el ajuste al reabrir."; OnChanged(nameof(CacheMessage)); }
    }
    // Explicit startup keeps the automatic worker owned by the application's lifecycle.
    public void StartAutomaticUpdates()
    {
        if (automaticStarted || automaticStopped) return;
        automaticStarted = true; ScheduleAutomatic();
    }
    private Task RetryAutomaticAsync()
    {
        if (PreparedLyrics is null) lyricsState = null;
        if (Insight is null) insightState = null;
        ScheduleAutomatic(true);
        return automaticWorker;
    }
    private void ScheduleAutomatic(bool allowSameTrack = false)
    {
        if (!automaticStarted || automaticStopped || !AutomaticUpdatesEnabled || !storageReady || StorageError || lifetime.IsCancellationRequested) return;
        if (CurrentTrack is null)
        {
            automaticSequence++; automaticPending = false; automaticRequest?.Cancel();
            automaticMessage = "Esperando una canción de Spotify."; NotifyAutomatic(); return;
        }
        if (!allowSameTrack && automaticAttemptedRevision == CurrentTrack.Revision) return;
        if (allowSameTrack) automaticAttemptedRevision = null;
        automaticSequence++; automaticPending = true; automaticRequest?.Cancel();
        if (!automaticRunning)
        {
            automaticRunning = true;
            automaticWorker = RunAutomaticAsync();
        }
        NotifyAutomatic();
    }
    private bool AutomaticContextCurrent(CurrentTrack track, long sequence, CancellationToken token) =>
        !token.IsCancellationRequested && !automaticStopped && AutomaticUpdatesEnabled &&
        CurrentTrack?.Revision == track.Revision && automaticSequence == sequence;
    private async Task RunAutomaticAsync()
    {
        try
        {
            while (automaticPending && AutomaticUpdatesEnabled && !automaticStopped && !lifetime.IsCancellationRequested)
            {
                automaticPending = false;
                if (CurrentTrack is not { } track) continue;
                var sequence = automaticSequence;
                using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
                automaticRequest = request;
                try
                {
                    automaticMessage = "Esperando 1,5 segundos de estabilidad…"; NotifyAutomatic();
                    await Task.Delay(TimeSpan.FromMilliseconds(1500), clock, request.Token);
                    // A provider can ignore cancellation. Drain actual calls before starting the latest track.
                    await Task.WhenAll(searches.Concat(generations).Where(task => !task.IsCompleted).ToArray());
                    if (!AutomaticContextCurrent(track, sequence, request.Token)) continue;
                    automaticAttemptedRevision = track.Revision;
                    QueueCacheRestore();
                    await WaitForCacheAsync();
                    if (!AutomaticContextCurrent(track, sequence, request.Token)) continue;
                    if (PreparedLyrics is null && pendingCachedLyrics is null && Candidates.Count == 0 && lyricsState is not (ApplicationState.Error or ApplicationState.Offline or ApplicationState.NoLyrics))
                        await SearchLyricsAsync();
                    await WaitForCacheAsync();
                    if (!AutomaticContextCurrent(track, sequence, request.Token)) continue;
                    if (pendingCachedLyrics is not null || Candidates.Count > 0 && PreparedLyrics is null)
                        automaticMessage = "Elige o confirma una versión para continuar.";
                    else if (PreparedLyrics is { IsInstrumental: true })
                        automaticMessage = "Instrumental · Flujo completado sin consultar IA.";
                    else if (PreparedLyrics is null)
                        automaticMessage = "Flujo detenido: no hay letra confirmada. Reintentar flujo vuelve a comprobarla.";
                    else if (Insight is not null)
                        automaticMessage = "Resultado recuperado de caché · 0 tokens nuevos.";
                    else if (insightState is ApplicationState.Error or ApplicationState.Offline)
                        automaticMessage = "El análisis anterior falló. Reintentar flujo es una acción explícita.";
                    else if (!CanUseAi)
                        automaticMessage = connection?.Mode == AiConnectionMode.ChatGptIncluded ? connection.Status : "Letra lista · IA desactivada o conexión no disponible.";
                    else if (!AiSettingsRules.IsValidModel(EffectiveModel))
                        automaticMessage = "Letra lista · Selecciona un modelo válido.";
                    else
                    {
                        await RefreshAiUsageAsync();
                        if (!AutomaticContextCurrent(track, sequence, request.Token)) continue;
                        if (!HasQuota) automaticMessage = "Letra lista · Límite diario alcanzado. No se ha enviado una consulta.";
                        else
                        {
                            await GenerateInsightAsync();
                            if (!AutomaticContextCurrent(track, sequence, request.Token)) continue;
                            automaticMessage = Insight is not null ? "Flujo completado para la canción actual." : "Flujo detenido. Reintentar flujo es una acción explícita; no se repite automáticamente.";
                        }
                    }
                    NotifyAutomatic();
                }
                catch (OperationCanceledException) when (request.IsCancellationRequested) { }
                catch (Exception)
                {
                    if (AutomaticContextCurrent(track, sequence, request.Token))
                    { automaticAttemptedRevision = track.Revision; automaticMessage = "No se pudo completar el flujo. Reintenta explícitamente; no se muestran datos privados en el diagnóstico."; NotifyAutomatic(); }
                }
                finally { if (ReferenceEquals(automaticRequest, request)) automaticRequest = null; }
            }
        }
        finally { automaticRunning = false; NotifyAutomatic(); }
    }
    public Task WaitForAutomaticAsync() => automaticWorker;
}
