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
    private string automaticMessage = "Actualización automática activada · IA solo al pulsar Traducir y explicar.";
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
            NotifyInsight();
            if (storageReady && store is IAutomaticUpdateSettings) TrackCacheTask(PersistAutomaticAsync(value));
        }
    }
    public AsyncCommand RetryAutomaticCommand { get; }
    public ActionCommand ResumeTrackingCommand { get; }
    public bool CanResumeTracking => !AutomaticUpdatesEnabled && !automaticStopped && !lifetime.IsCancellationRequested;
    public string TrackContextLabel => AutomaticUpdatesEnabled && CurrentTrack is not null && latestObservation.State == ApplicationState.Playing
        && latestObservation.Track?.Playback == PlaybackState.Playing ? "AHORA SUENA" : "CANCIÓN MOSTRADA";
    // Playback describes the latest observation, even when the displayed content is frozen.
    public string PlaybackStatus => (latestObservation.State switch
    {
        ApplicationState.NoSpotify => "Spotify no disponible",
        ApplicationState.NoTrack => "Spotify sin canción identificable",
        ApplicationState.Error => "Reproducción no disponible",
        _ => latestObservation.Track?.Playback switch
        {
            PlaybackState.Playing => "Spotify reproduciendo",
            PlaybackState.Paused => "Spotify en pausa",
            PlaybackState.Stopped => "Spotify detenido",
            _ => "Estado de reproducción no disponible"
        }
    }) + (!AutomaticUpdatesEnabled && CurrentTrack is not null && latestObservation.Track is { } observed && observed.Revision != CurrentTrack.Revision
        ? " · otra canción" : "");
    public string TrackingStatus => AutomaticUpdatesEnabled ? "Seguimiento activo" : CurrentTrack is null
        ? "Seguimiento pausado · sin canción mostrada" : "Seguimiento pausado · mostrando esta canción";
    private void NotifyPlaybackContext()
    {
        OnChanged(nameof(TrackContextLabel)); OnChanged(nameof(PlaybackStatus)); OnChanged(nameof(TrackingStatus));
        OnChanged(nameof(CanResumeTracking)); ResumeTrackingCommand.Refresh();
    }
    private void NotifyAutomatic()
    {
        OnChanged(nameof(AutomaticUpdatesEnabled)); OnChanged(nameof(AutomaticMessage)); RetryAutomaticCommand.Refresh();
        NotifyPlaybackContext();
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
                        automaticMessage = "El análisis anterior falló. Pulsa Traducir y explicar para volver a consultar IA.";
                    else if (!CanUseAi)
                        automaticMessage = connection?.Mode == AiConnectionMode.ChatGptIncluded ? connection.Status : "Letra lista · IA desactivada o conexión no disponible.";
                    else if (!AiSettingsRules.IsValidModel(EffectiveModel))
                        automaticMessage = "Letra lista · Selecciona un modelo válido.";
                    else
                    {
                        await RefreshAiUsageAsync();
                        if (!AutomaticContextCurrent(track, sequence, request.Token)) continue;
                        if (!HasQuota) automaticMessage = "Letra lista · Límite diario alcanzado. No se ha enviado una consulta.";
                        else automaticMessage = "Letra lista · Pulsa Traducir y explicar para consultar IA.";
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
