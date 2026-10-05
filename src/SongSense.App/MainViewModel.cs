using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SongSense.Core;

namespace SongSense.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore store;
    private readonly ILyricsProvider lyricsProvider;
    private readonly CancellationToken lifetime;
    private bool initializing;
    private TrackDetection detection = new(ApplicationState.NoSpotify, null, null);
    private string storageMessage = "Preparando almacenamiento local…";
    private bool storageError;
    private CancellationTokenSource? lyricsRequest;
    private readonly List<Task> searches = [];
    private bool searching;
    private ApplicationState? lyricsState;
    private string lyricsMessage = "";
    public ResolvedLyrics? PreparedLyrics { get; private set; }
    public IReadOnlyList<LyricsCandidate> Candidates { get; private set; } = [];

    public MainViewModel(ISettingsStore store, ILyricsProvider lyricsProvider, CancellationToken lifetime = default)
    {
        this.store = store;
        this.lyricsProvider = lyricsProvider;
        this.lifetime = lifetime;
        RetryStorageCommand = new AsyncCommand(InitializeAsync, () => !initializing && StorageError);
        SearchLyricsCommand = new AsyncCommand(SearchLyricsAsync, () => CurrentTrack is not null && !searching && !StorageError);
        CancelLyricsCommand = new ActionCommand(CancelLyrics, () => searching);
    }

    public CurrentTrack? CurrentTrack => detection.Track;
    public string TrackTitle => CurrentTrack?.Title ?? "Todavía no hay una canción";
    public string Artist => CurrentTrack?.Artist ?? "El título y el artista aparecerán aquí";
    public string Album => CurrentTrack?.Album ?? "Álbum no disponible";
    public string Duration => CurrentTrack?.Duration is { } duration
        ? $"Duración: {(int)duration.TotalMinutes}:{duration.Seconds:00}" : "Duración desconocida";
    public string Status => (StorageError ? ApplicationState.Error :
        detection.State == ApplicationState.Paused && !searching ? ApplicationState.Paused :
        lyricsState ?? detection.State).InSpanish();
    public string LyricsMessage => lyricsMessage;
    public string OriginalLyrics => PreparedLyrics?.Text ?? "";
    public string DetectionMessage => detection.State switch
    {
        ApplicationState.NoSpotify => "Abre Spotify y reproduce una canción para verla aquí.",
        ApplicationState.NoTrack => "Spotify está disponible, pero no ofrece una canción identificable. Reproduce una canción con título y artista.",
        ApplicationState.Error => "No se pudieron leer las sesiones de Windows. Se reintentará cada 5 segundos; si persiste, reinicia Spotify.",
        _ => "Pulsa Buscar letra para consultar LRCLIB. La traducción y la IA llegarán en sus próximas features."
    };
    public string StorageMessage { get => storageMessage; private set { storageMessage = value; OnChanged(); } }
    public bool StorageError { get => storageError; private set { storageError = value; OnChanged(); RetryStorageCommand.Refresh(); SearchLyricsCommand.Refresh(); } }
    public AsyncCommand RetryStorageCommand { get; }
    public AsyncCommand SearchLyricsCommand { get; }
    public ActionCommand CancelLyricsCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public void ApplyDetection(TrackDetection observation)
    {
        if (CurrentTrack?.Revision != observation.Track?.Revision)
        {
            lyricsRequest?.Cancel();
            lyricsRequest = null;
            searching = false;
            PreparedLyrics = null;
            Candidates = [];
            lyricsState = null;
            lyricsMessage = "";
        }
        detection = observation;
        foreach (var property in new[] { nameof(CurrentTrack), nameof(TrackTitle), nameof(Artist), nameof(Album), nameof(Duration), nameof(Status), nameof(DetectionMessage) })
            OnChanged(property);
        NotifyLyrics();
    }

    private void NotifyLyrics()
    {
        foreach (var property in new[] { nameof(Status), nameof(LyricsMessage), nameof(OriginalLyrics), nameof(PreparedLyrics), nameof(Candidates) }) OnChanged(property);
        SearchLyricsCommand.Refresh();
        CancelLyricsCommand.Refresh();
    }

    public Task SearchLyricsAsync()
    {
        if (!SearchLyricsCommand.CanExecute(null) || CurrentTrack is not { } track || lifetime.IsCancellationRequested) return Task.CompletedTask;
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        lyricsRequest = request;
        searching = true;
        PreparedLyrics = null;
        Candidates = [];
        lyricsState = ApplicationState.SearchingLyrics;
        lyricsMessage = "Buscando letra en LRCLIB…";
        NotifyLyrics();
        searches.RemoveAll(task => task.IsCompleted);
        var task = SearchCoreAsync(track, request);
        searches.Add(task);
        return task;
    }

    private bool IsCurrent(CurrentTrack track, CancellationTokenSource request) =>
        !request.IsCancellationRequested && ReferenceEquals(lyricsRequest, request) && CurrentTrack?.Revision == track.Revision;

    private async Task SearchCoreAsync(CurrentTrack track, CancellationTokenSource request)
    {
        try
        {
            var result = await lyricsProvider.FindAsync(track, request.Token);
            if (!IsCurrent(track, request) || result.TrackRevision != track.Revision) return;
            PreparedLyrics = result.Lyrics;
            Candidates = result.Candidates;
            (lyricsState, lyricsMessage) = result.Kind switch
            {
                LyricsSearchKind.Matched => (ApplicationState.AiNotConfigured, "Letra encontrada en LRCLIB. La traducción y la explicación aún no están disponibles."),
                LyricsSearchKind.Instrumental => (ApplicationState.Ready, "Esta canción es instrumental"),
                LyricsSearchKind.Candidates => (ApplicationState.ChoosingVersion, $"Hay {Candidates.Count} candidato(s) que necesitan confirmación. La selección de versión estará disponible en F04; todavía no se ha preparado una letra."),
                _ => (ApplicationState.NoLyrics, "No se encontró una letra disponible. Puedes volver a buscar; la entrada manual llegará en F04.")
            };
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (LyricsProviderException error)
        {
            if (!IsCurrent(track, request)) return;
            lyricsState = error.Failure == LyricsFailure.Offline ? ApplicationState.Offline : ApplicationState.Error;
            lyricsMessage = error.Failure switch
            {
                LyricsFailure.RateLimited => error.RetryAt is { } until
                    ? $"LRCLIB ha limitado las consultas. Espera hasta {until.ToLocalTime():HH:mm:ss} y pulsa Buscar letra."
                    : "LRCLIB ha limitado las consultas. Espera 60 segundos y pulsa Buscar letra.",
                LyricsFailure.Offline => "No se pudo conectar con LRCLIB. Comprueba tu conexión y pulsa Buscar letra.",
                LyricsFailure.Timeout => "LRCLIB no respondió en 15 segundos. Pulsa Buscar letra para reintentar.",
                LyricsFailure.InvalidResponse => "LRCLIB devolvió un formato inválido. No se ha preparado ninguna letra. Pulsa Buscar letra para reintentar.",
                _ => "LRCLIB devolvió un error. Pulsa Buscar letra para reintentar más tarde."
            };
        }
        catch (Exception)
        {
            if (!IsCurrent(track, request)) return;
            lyricsState = ApplicationState.Error;
            lyricsMessage = "No se pudo buscar la letra. Pulsa Buscar letra para reintentar.";
        }
        finally
        {
            if (ReferenceEquals(lyricsRequest, request))
            {
                lyricsRequest = null;
                searching = false;
                NotifyLyrics();
            }
            request.Dispose();
        }
    }

    public void CancelLyrics()
    {
        if (!searching) return;
        lyricsRequest?.Cancel();
        lyricsRequest = null;
        searching = false;
        lyricsState = null;
        lyricsMessage = "Búsqueda cancelada. Puedes volver a intentarlo.";
        NotifyLyrics();
    }

    public async Task StopAsync()
    {
        CancelLyrics();
        await Task.WhenAll(searches);
    }

    public async Task InitializeAsync()
    {
        if (initializing) return;
        initializing = true;
        RetryStorageCommand.Refresh();
        StorageMessage = "Preparando almacenamiento local…";
        try
        {
            // Microsoft.Data.Sqlite performs local I/O synchronously; keep it off the UI thread.
            await Task.Run(async () =>
            {
                await store.InitializeAsync(lifetime);
                await store.ReadSettingsAsync(lifetime);
            }, lifetime);
            StorageError = false;
            StorageMessage = "Almacenamiento local listo";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            StorageError = true;
            StorageMessage = "No se pudo abrir la base local. Comprueba el acceso a %LOCALAPPDATA%\\SongSense y usa una versión compatible con la base. Después, pulsa Reintentar. Tus datos se conservan.";
        }
        finally
        {
            initializing = false;
            OnChanged(nameof(Status));
            RetryStorageCommand.Refresh();
        }
    }
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await execute(); }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class ActionCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
