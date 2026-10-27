using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;

public static class Preview
{
    static string output = "", data = "";
    static readonly List<string> report = [];
    static readonly List<MainViewModel> models = [];
    static readonly HttpClient http = new(new NoNetwork());
    static readonly Provider provider = new();
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        data = Path.Combine(output, "data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var dictionary = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (args.Contains("--interactive")) await Interactive(); else await Run();
                foreach (var vm in models) await vm.StopAsync();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
            app.Shutdown(Environment.ExitCode);
        }); app.Run();
    }
    static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static async Task Flush(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    static async Task<MainWindow> Main(string scenario)
    {
        var path = Path.Combine(data, Guid.NewGuid() + ".db"); var store = new SqliteSettingsStore(path); await store.InitializeAsync(); await store.SaveSettingsAsync(new(true, "fixture-model", 20));
        var vm = new MainViewModel(scenario == "storage" ? new FailedStore() : store, provider, insightProvider: provider, cache: new SqliteCacheStore(path)); models.Add(vm); await vm.InitializeAsync();
        if (scenario is not ("empty" or "storage"))
        {
            vm.ApplyDetection(new(ApplicationState.Playing, new(1, "Ventana propia", "Artista sintético", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null)); await vm.WaitForCacheAsync();
            if (scenario == "offline") await vm.SearchLyricsAsync();
            else { vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz"); await vm.WaitForCacheAsync(); if (scenario == "ready") await vm.GenerateInsightAsync(); }
        }
        return new MainWindow { DataContext = vm, ShowActivated = false, Title = "A2-04 · Principal · " + scenario };
    }
    static async Task<Window> Create(string family)
    {
        if (family.StartsWith("main-")) return await Main(family[5..]);
        var main = await Main("lyrics"); var vm = (MainViewModel)main.DataContext;
        if (family == "preferences")
        {
            main.Show();
            var window = new PreferencesWindow(main, vm) { Title = "A2-04 · Preferencias", ShowActivated = false };
            window.Loaded += (_, _) => { foreach (var e in Find<Expander>(window).Where(e => Equals(e.Header, "Contenido guardado"))) e.IsExpanded = true; };
            return window;
        }
        if (family == "editor")
        {
            var editor = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!);
            vm.ApplyDetection(new(ApplicationState.Playing, new(2, "Otra canción propia", "Artista", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
            return new LyricsEditorWindow(editor, true) { ShowActivated = false, Title = "A2-04 · Editor · borrador anterior" };
        }
        if (family == "api")
        {
            var store = new SqliteSettingsStore(Path.Combine(data, Guid.NewGuid() + ".db")); await store.InitializeAsync();
            return new AiSettingsWindow(new(new AiConfigurationService(store, new DpapiSecretStore(Path.Combine(data, Guid.NewGuid() + ".dpapi")), store, new OpenAiConnectionProbe(http)), CancellationToken.None)) { ShowActivated = false, Title = "A2-04 · API" };
        }
        var credentials = new ChatGptCredentialStore(Path.Combine(data, Guid.NewGuid() + ".dpapi"));
        await credentials.SaveAsync(new() { ActiveClientId = "oaiapp_fixture", Registrations = [new() { ClientId = "oaiapp_fixture", Subject = "subject", Email = "fixture@example.invalid", Model = "fixture-model", AccessToken = "fixture-access", RefreshToken = "fixture-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Scopes = [ChatGptOAuthClient.PlanScope] }] }, CancellationToken.None);
        var connection = new ChatGptConnection(credentials, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
        return new ConnectionWindow(new(connection, CancellationToken.None)) { ShowActivated = false, Title = "A2-04 · Cuenta" };
    }
    static async Task Run()
    {
        report.Add("Synthetic content and stores; no HTTP. SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))));
        foreach (double scale in new[] { 1d, 1.57 })
        {
            UiTypography.Apply(Application.Current.Resources, scale);
            foreach (var family in new[] { "main-ready", "main-empty", "main-offline", "main-storage", "preferences", "account", "api", "editor" })
            {
                var window = await Create(family); window.Show(); await Flush(window);
                foreach (var size in new[] { (860, 780), (360, 480) })
                {
                    window.Width = size.Item1; window.Height = size.Item2; await Flush(window);
                    if (family == "preferences") { ((Button)window.FindName("DeleteSongButton")).BringIntoView(); await Flush(window); }
                    foreach (var button in Find<Button>(window).Where(b => b.IsVisible))
                    {
                        var name = new ButtonAutomationPeer(button).GetName();
                        if (name.Length > 25 && button.Template.FindName("Body", button) is not null)
                        {
                            foreach (var text in Find<AccessText>(button))
                                Check(text.TextWrapping == TextWrapping.Wrap && text.ActualWidth <= button.ActualWidth, "Unwrapped button: " + name);
                        }
                    }
                    foreach (var notice in Find<StatusNotice>(window).Where(n => n.IsVisible))
                    {
                        var text = (TextBlock)notice.Template.FindName("NoticeText", notice);
                        Check(text.TextWrapping == TextWrapping.Wrap && text.TextTrimming == TextTrimming.None, "Notice lost cause/action");
                        Check(UIElementAutomationPeer.CreatePeerForElement(notice)!.GetName().Contains(notice.Message), "Notice lost accessible detail");
                    }
                    if (family == "main-ready") Check(!Find<StatusNotice>(window).Any(n => n.IsVisible), "Ready repeats status");
                    await Capture(window, $"{family}-{size.Item1}-{scale:0.00}");
                }
                // No edited drafts or real secrets exist in these fixtures.
                window.Close(); await Flush(window);
            }
        }
        report.Add("PASS: five window families, long labels, untrimmed notices, accessible names; scales are synthetic UI text factors, render DPI is recorded.");
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), report);
    }
    static async Task Interactive()
    {
        using var typography = UiTypography.FollowSystem(Application.Current);
        var panel = new StackPanel { Margin = new Thickness(16) };
        var controller = new Window { Title = "A2-04 · Pruebas locales", Width = 410, Height = 600, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        double lastScale = -1; int lastWindows = -1; bool recording = false;
        var recorder = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        recorder.Tick += async (_, _) =>
        {
            if (recording) return;
            var scale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
            var windows = Application.Current.Windows.Cast<Window>().Where(w => w != controller && w.IsLoaded && w.IsVisible && w.ActualWidth > 0).ToArray();
            if (scale == lastScale && windows.Length == lastWindows) return;
            recording = true;
            try
            {
                lastScale = scale; lastWindows = windows.Length;
                foreach (var window in windows) await Capture(window, "auto-" + DateTime.Now.ToString("HHmmss") + "-" + window.Title.Replace('·', '-'));
                File.WriteAllLines(Path.Combine(output, "native-manifest.txt"), report);
            }
            catch (Exception ex) { File.AppendAllText(Path.Combine(output, "recorder-error.txt"), ex.ToString()); }
            finally { recording = false; }
        };
        controller.Loaded += (_, _) => recorder.Start(); controller.Closed += (_, _) => recorder.Stop();
        foreach (var family in new[] { "main-ready", "main-empty", "main-offline", "main-storage", "preferences", "account", "api", "editor" })
        {
            var button = new Button { Content = family, Margin = new Thickness(0, 3, 0, 3) }; panel.Children.Add(button);
            button.Click += async (_, _) => { var window = await Create(family); window.Width = 360; window.Height = 480; window.ShowActivated = true; window.Show(); };
        }
        var capture = new Button { Content = "Registrar ventanas y texto de Windows" }; panel.Children.Add(capture);
        capture.Click += async (_, _) =>
        {
            foreach (Window window in Application.Current.Windows.Cast<Window>().Where(w => w != controller).ToArray()) await Capture(window, "native-" + DateTime.Now.ToString("HHmmss") + "-" + window.Title.Replace('·', '-'));
            File.WriteAllLines(Path.Combine(output, "native-manifest.txt"), report);
        };
        var done = new TaskCompletionSource(); controller.Closed += (_, _) => done.TrySetResult(); controller.Show();
        foreach (var family in new[] { "main-ready", "preferences", "account", "api", "editor" })
        { var window = await Create(family); window.Width = 360; window.Height = 480; window.Show(); }
        await done.Task;
    }
    static async Task Capture(Window window, string name)
    {
        await Flush(window); var content = (FrameworkElement)window.Content;
        if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize)); bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        report.Add(JsonSerializer.Serialize(new { name, version = BuildIdentity.ProductVersion, hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))), width = window.ActualWidth, height = window.ActualHeight, clientWidth = content.ActualWidth, clientHeight = content.ActualHeight, dpi = dpi.PixelsPerInchX, uiFont = window.FindResource("UiControlFont"), systemTextFactor = new Windows.UI.ViewManagement.UISettings().TextScaleFactor, origin = "synthetic contents; native WPF", readingViewport = window is MainWindow m ? ((FrameworkElement)m.FindName("TranslationScroll")).ActualHeight : (double?)null }));
    }
    static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T t) yield return t; foreach (var x in Find<T>(child)) yield return x; } }
    sealed class NoNetwork : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) => throw new InvalidOperationException("No HTTP allowed"); }
    sealed class FailedStore : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t = default) => Task.FromException(new IOException("synthetic"));
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromException<AppSettings>(new IOException("synthetic"));
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(0);
    }
    sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => throw new LyricsProviderException(LyricsFailure.Offline);
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) => Task.FromResult(new SongInsight(track.Revision, "es", [], string.Join(' ', Enumerable.Repeat("La voz narrativa podría observar una oportunidad de cambio mientras contempla la luz que entra por la ventana.", 5)), ["Cambio"], [], [], []));
    }
}
