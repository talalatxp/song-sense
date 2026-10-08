using System.Windows;
using System.Net.Http;
using SongSense.Infrastructure;
using SongSense.Core;

namespace SongSense.App;

public partial class App : Application
{
    private readonly CancellationTokenSource lifetime = new();
    private ITrackDetector? detector;
    private MainViewModel? viewModel;
    private bool closing;
    private bool canClose;
    private HttpClient? lyricsHttp;
    private LrclibLyricsProvider? lyricsProvider;
    private HttpClient? aiHttp;
    private ChatGptConnection? connection;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        lyricsHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        lyricsProvider = new LrclibLyricsProvider(lyricsHttp);
        var settingsStore = new SqliteSettingsStore(SqliteSettingsStore.DefaultDatabasePath);
        aiHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var chatGptStore = new ChatGptCredentialStore(ChatGptCredentialStore.DefaultPath);
        connection = new ChatGptConnection(chatGptStore, new ChatGptOAuthClient(aiHttp));
        var aiConfiguration = new AiConfigurationService(settingsStore, new DpapiSecretStore(DpapiSecretStore.DefaultPath), settingsStore, new OpenAiConnectionProbe(aiHttp),
            operationLockPath: chatGptStore.LockPath,
            allowPaidRequests: async token => connection.Mode == AiConnectionMode.ApiKey && connection.CanGenerate && (await chatGptStore.ReadAsync(token)).Mode == AiConnectionMode.ApiKey,
            authorizationCancellation: () => connection.AuthorizationCancellation);
        viewModel = new MainViewModel(settingsStore, lyricsProvider, lifetime.Token,
            new ConnectionInsightProvider(connection, new OpenAiInsightProvider(aiConfiguration, new OpenAiResponsesClient(aiHttp))),
            cache: new SqliteCacheStore(SqliteSettingsStore.DefaultDatabasePath), connection: connection);
        connection.Changed += (_, _) =>
        {
            if (closing || Dispatcher.HasShutdownStarted) return;
            if (Dispatcher.CheckAccess()) viewModel.ApplyConnectionChange();
            else Dispatcher.BeginInvoke(viewModel.ApplyConnectionChange);
        };
        detector = new SpotifyTrackDetector(new WindowsMediaSessionSource());
        detector.TrackChanged += TrackChanged;
        var mainWindow = new MainWindow { DataContext = viewModel,
            CreateConnectionWindow = () => new ConnectionWindow(new ConnectionViewModel(connection, lifetime.Token))
            { OpenApiSettingsAction = () => { if (connection.Mode == AiConnectionMode.ApiKey && MainWindow is MainWindow window) window.ShowApiSettings(); } },
            CreateSettingsWindow = () =>
            {
                var settingsVm = new AiSettingsViewModel(aiConfiguration, lifetime.Token);
                settingsVm.SettingsLoaded += async (_, settings) => { viewModel.ApplyAiSettings(settings); await viewModel.RefreshAiUsageAsync(); };
                return new AiSettingsWindow(settingsVm);
            } };
        MainWindow = mainWindow;
        MainWindow.Closing += async (_, args) =>
        {
            if (canClose) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            detector.TrackChanged -= TrackChanged;
            lifetime.Cancel();
            await detector.DisposeAsync();
            await viewModel.StopAsync();
            foreach (var window in Windows.OfType<AiSettingsWindow>().ToArray())
                if (window.DataContext is AiSettingsViewModel settingsVm) await settingsVm.StopAsync();
            foreach (var window in Windows.OfType<ConnectionWindow>().ToArray())
                if (window.DataContext is ConnectionViewModel connectionVm) await connectionVm.StopAsync();
            lyricsProvider.Dispose();
            lyricsHttp.Dispose();
            aiHttp.Dispose();
            canClose = true;
            await Dispatcher.InvokeAsync(MainWindow.Close);
        };
        MainWindow.Show();
        viewModel.StartAutomaticUpdates();
        _ = viewModel.InitializeAsync();
        _ = InitializeConnectionAsync();
        _ = detector.StartAsync(lifetime.Token);
    }
    private async Task InitializeConnectionAsync()
    {
        try { await connection!.InitializeAsync(lifetime.Token); }
        catch (Exception) { viewModel?.ApplyConnectionChange(); }
    }

    private void TrackChanged(object? sender, TrackDetection observation)
    {
        if (closing || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() => { if (!closing) viewModel?.ApplyDetection(observation); });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        lifetime.Cancel();
        lifetime.Dispose();
        base.OnExit(e);
    }
}
