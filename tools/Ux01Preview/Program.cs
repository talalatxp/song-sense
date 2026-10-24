using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;

// Offline, synthetic WPF fixture; also compiles against the pre-UX01 commit for comparison.
public static class Preview
{
    private static string output = "";
    private static bool interactive;
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        interactive = args.Length > 2 && args[2] == "--interactive";
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var source = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml"));
            var resources = source.Root!.Elements().First();
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
                resources.Elements().FirstOrDefault()?.Name.LocalName == "ResourceDictionary" ? resources.Elements().Single().ToString() :
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                string.Concat(resources.Nodes().Select(n => n.ToString())) + "</ResourceDictionary>");
            app.Dispatcher.InvokeAsync(async () =>
            {
                try { await Run(); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                app.Shutdown();
            });
            app.Run();
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
    }

    private static async Task Run()
    {
        var track = new CurrentTrack(1, "Ventana de prueba", "Artista sintético", "Álbum de prueba", TimeSpan.FromMinutes(3), PlaybackState.Playing);
        var metadata = new List<string>();
        foreach (var scenario in new[] { "unrequested", "pending", "reported", "zero", "unknown", "error", "error-reported", "cancel", "cache" })
        {
            if (interactive && scenario != "unknown") continue;
            var provider = new Provider(scenario);
            var vm = new MainViewModel(new Store(), provider, insightProvider: provider,
                cache: scenario == "cache" ? new MemoryCache(Provider.Result(track)) : null);
            await vm.InitializeAsync();
            vm.ApplyDetection(new(ApplicationState.Playing, track, null));
            vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz");
            await vm.WaitForCacheAsync();
            if (scenario != "unrequested" && scenario != "cache")
            {
                var work = vm.GenerateInsightAsync();
                if (scenario == "cancel") vm.CancelInsight();
                if (scenario != "pending") await work;
            }
            var window = new MainWindow { DataContext = vm, Width = 860, Height = 780, ShowActivated = false };
            window.Show();
            await Capture(window, scenario + "-normal", metadata);
            window.Width = 360; window.Height = 480;
            await Capture(window, scenario + "-minimum", metadata);
            FindScroll(window)?.ScrollToEnd();
            await Capture(window, scenario + "-minimum-footer", metadata);
            window.Hide();
            if (scenario == "unknown")
            {
                using var http = new HttpClient(new NoNetwork());
                var connection = new ChatGptConnection(new ChatGptCredentialStore(Path.Combine(output, "unused-session.dpapi")), new ChatGptOAuthClient(http));
                // Share the activity when supported; reflection keeps the baseline fixture identical.
                var activity = typeof(MainViewModel).GetProperty("Activity")?.GetValue(vm);
                var constructor = typeof(ConnectionViewModel).GetConstructors().Single();
                var connectionVm = (ConnectionViewModel)constructor.Invoke(constructor.GetParameters().Length == 4
                    ? [connection, CancellationToken.None, null, activity] : [connection, CancellationToken.None, null]);
                var account = new ConnectionWindow(connectionVm) { ShowActivated = false };
                account.Show();
                var scroll = (ScrollViewer)account.Content;
                scroll.ScrollToEnd();
                await Capture(account, "account-normal", metadata);
                account.Width = 360; account.Height = 480; scroll.ScrollToEnd();
                await Capture(account, "account-minimum", metadata);
                if (interactive)
                {
                    account.Title = "UX01 · Cuenta sintética · prueba de teclado";
                    var closed = new TaskCompletionSource();
                    account.Closed += (_, _) => closed.SetResult();
                    await closed.Task;
                }
                if (!interactive) account.Hide();
                var store = new Store();
                var service = new AiConfigurationService(store, new DpapiSecretStore(Path.Combine(output, "unused-api.dpapi")), store, new OpenAiConnectionProbe(http));
                var apiConstructor = typeof(AiSettingsViewModel).GetConstructors().Single();
                var apiVm = (AiSettingsViewModel)apiConstructor.Invoke(apiConstructor.GetParameters().Length == 3
                    ? [service, CancellationToken.None, activity] : [service, CancellationToken.None]);
                var api = new AiSettingsWindow(apiVm) { ShowActivated = false };
                api.Show();
                ((ScrollViewer)api.Content).ScrollToEnd();
                await Capture(api, "api-normal", metadata);
                api.Width = 360; api.Height = 480; ((ScrollViewer)api.Content).ScrollToEnd();
                await Capture(api, "api-minimum", metadata);
                api.Hide();
            }
            await vm.StopAsync();
        }
        metadata.Add("App SHA256: " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainViewModel).Assembly.Location))));
        metadata.Add("Core SHA256: " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(AiUsage).Assembly.Location))));
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), metadata);
    }
    private static async Task Capture(Window window, string name, List<string> metadata)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            drawing.DrawRectangle(window.Background, null, rect);
        }
        bitmap.Render(visual);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        var dpi = VisualTreeHelper.GetDpi(window);
        metadata.Add($"{name}: window {window.Width}x{window.Height} DIP; client {content.ActualWidth}x{content.ActualHeight}; display DPI {dpi.PixelsPerInchX}; render 96 DPI; Segoe UI; reading 16 pt; compact={((window.DataContext as MainViewModel)?.CompactUsage)}");
    }
    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("Network forbidden in UX01 preview");
    }
    private sealed class Store : ISettingsStore, IRequestBudget
    {
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken token = default) => Task.FromResult(new AppSettings(true, "fixture-model"));
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken token = default) => Task.FromResult(3);
        public Task<bool> TryReserveRequestAsync(DateOnly date, CancellationToken token = default) => throw new InvalidOperationException("No budget reservation in preview");
    }
    private sealed class Provider(string scenario) : ILyricsProvider, IInsightProvider
    {
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => throw new InvalidOperationException("No lyrics requests in preview");
        public static SongInsight Result(CurrentTrack track) => new(track.Revision, "es", [], "La ventana representa una posibilidad de cambio.", ["Cambio"], [], [], []);
        public async Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token)
        {
            if (scenario is "pending" or "cancel") await Task.Delay(Timeout.Infinite, token);
            if (scenario.StartsWith("error")) throw new AiException(AiFailure.Offline) { Usage = scenario == "error-reported" ? new(100, 50, 150) : null };
            return Result(track) with { Usage = scenario == "zero" ? new(0, 0, 0) : scenario == "reported" ? new(100, 50, 150) : null };
        }
    }
    private sealed class MemoryCache(SongInsight insight) : ICacheStore
    {
        public Task<CachedSong?> ReadSongAsync(CurrentTrack song, CancellationToken token = default) => Task.FromResult<CachedSong?>(null);
        public Task<SongInsight?> ReadInsightAsync(ResolvedLyrics lyrics, string model, string version, CancellationToken token = default, CurrentTrack? song = null, string scope = "api-key", string targetLanguage = "es") => Task.FromResult<SongInsight?>(insight);
        public Task SaveLyricsAsync(CurrentTrack song, ResolvedLyrics? lyrics, CancellationToken token = default) => Task.CompletedTask;
        public Task SaveInsightAsync(CurrentTrack song, ResolvedLyrics lyrics, string model, string version, SongInsight result, CancellationToken token = default, string scope = "api-key", string targetLanguage = "es") => Task.CompletedTask;
        public Task DeleteSongAsync(CurrentTrack song, CancellationToken token = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken token = default) => Task.CompletedTask;
    }
}
