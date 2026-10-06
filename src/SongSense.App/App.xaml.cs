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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        lyricsHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        lyricsProvider = new LrclibLyricsProvider(lyricsHttp);
        var settingsStore = new SqliteSettingsStore(SqliteSettingsStore.DefaultDatabasePath);
        aiHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var aiConfiguration = new AiConfigurationService(settingsStore, new DpapiSecretStore(DpapiSecretStore.DefaultPath), settingsStore, new OpenAiConnectionProbe(aiHttp),
            operationLockPath: System.IO.Path.Combine(System.IO.Path.GetDirectoryName(DpapiSecretStore.DefaultPath)!, "ai-operation.lock"));
        viewModel = new MainViewModel(settingsStore, lyricsProvider, lifetime.Token,
            new OpenAiInsightProvider(aiConfiguration, new OpenAiResponsesClient(aiHttp)));
        detector = new SpotifyTrackDetector(new WindowsMediaSessionSource());
        detector.TrackChanged += TrackChanged;
        MainWindow = new MainWindow { DataContext = viewModel,
            CreateSettingsWindow = () =>
            {
                var settingsVm = new AiSettingsViewModel(aiConfiguration, lifetime.Token);
                settingsVm.SettingsLoaded += async (_, settings) => { viewModel.ApplyAiSettings(settings); await viewModel.RefreshAiUsageAsync(); };
                return new AiSettingsWindow(settingsVm);
            } };
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
            lyricsProvider.Dispose();
            lyricsHttp.Dispose();
            aiHttp.Dispose();
            canClose = true;
            await Dispatcher.InvokeAsync(MainWindow.Close);
        };
        MainWindow.Show();
        _ = viewModel.InitializeAsync();
        _ = detector.StartAsync(lifetime.Token);
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
