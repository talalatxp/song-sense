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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        lyricsHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        lyricsProvider = new LrclibLyricsProvider(lyricsHttp);
        viewModel = new MainViewModel(new SqliteSettingsStore(SqliteSettingsStore.DefaultDatabasePath), lyricsProvider, lifetime.Token);
        detector = new SpotifyTrackDetector(new WindowsMediaSessionSource());
        detector.TrackChanged += TrackChanged;
        MainWindow = new MainWindow { DataContext = viewModel };
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
            lyricsProvider.Dispose();
            lyricsHttp.Dispose();
            canClose = true;
            MainWindow.Close();
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
