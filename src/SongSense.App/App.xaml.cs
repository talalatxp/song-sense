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
    private HttpClient? lyricsHttp;
    private LrclibLyricsProvider? lyricsProvider;
    private HttpClient? aiHttp;
    private ChatGptConnection? connection;
    private SingleInstance? instance;
    private DesktopTray? tray;
    private IDisposable? themeSubscription;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        themeSubscription = ThemeResources.FollowSystem(this);
        instance = new SingleInstance();
        if (!instance.IsFirst) { _ = ActivateExistingAsync(); return; }
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
            new ConnectionInsightProvider(connection, new OpenAiInsightProvider(aiConfiguration, new OpenAiResponsesClient(aiHttp)), new ChatGptInsightProvider(connection, aiHttp, settingsStore, new ChatGptRequestBudget(settingsStore))),
            cache: new SqliteCacheStore(SqliteSettingsStore.DefaultDatabasePath), connection: connection);
        detector = new SpotifyTrackDetector(new WindowsMediaSessionSource());
        detector.TrackChanged += TrackChanged;
        var mainWindow = new MainWindow { DataContext = viewModel,
            CreateConnectionWindow = () => new ConnectionWindow(new ConnectionViewModel(connection, lifetime.Token, activity: viewModel.Activity))
            { OpenApiSettingsAction = () => { if (connection.Mode == AiConnectionMode.ApiKey && MainWindow is MainWindow window) window.ShowApiSettings(); } },
            CreateSettingsWindow = () =>
            {
                var settingsVm = new AiSettingsViewModel(aiConfiguration, lifetime.Token, viewModel.Activity, connection);
                settingsVm.SettingsLoaded += async (_, settings) => { viewModel.ApplyAiSettings(settings); await viewModel.RefreshAiUsageAsync(); };
                return new AiSettingsWindow(settingsVm);
            } };
        MainWindow = mainWindow;
        mainWindow.InitializePreferences(new WindowPreferenceStore(WindowPreferenceStore.DefaultPath));
        tray = new DesktopTray(viewModel, mainWindow.OpenFromTray, mainWindow.RequestExit);
        instance.Listen(() => Dispatcher.BeginInvoke(() => { if (!closing) mainWindow.OpenFromTray(); }));
        mainWindow.ConfigureCloseBehavior(async () =>
        {
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
        }, tray.ShowFirstHideHint);
        mainWindow.Closed += (_, _) => Shutdown();
        MainWindow.Show();
        viewModel.StartAutomaticUpdates();
        _ = viewModel.InitializeAsync();
        _ = InitializeConnectionAsync();
        _ = detector.StartAsync(lifetime.Token);
    }
    private async Task ActivateExistingAsync()
    {
        if (!await instance!.ActivateFirstAsync())
            MessageBox.Show("Song Sense ya está abierto. Puedes abrirlo desde su icono de la bandeja.", "Song Sense", MessageBoxButton.OK, MessageBoxImage.Information);
        Shutdown();
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
        themeSubscription?.Dispose();
        lifetime.Cancel();
        tray?.Dispose(); instance?.Dispose();
        lifetime.Dispose();
        base.OnExit(e);
    }
}
