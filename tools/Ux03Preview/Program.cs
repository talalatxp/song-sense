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

public static class Preview
{
    static string output = "";
    static bool interactive;
    static readonly List<string> report = [];
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output); interactive = args.Contains("--interactive");
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
            app.Dispatcher.InvokeAsync(async () =>
            {
                try { await Run(); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                app.Shutdown();
            }); app.Run();
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
    }
    static async Task Run()
    {
        foreach (var scenario in new[] { "empty", "no-track", "detection", "versions", "notfound", "offline", "timeout", "limited", "invalid", "instrumental", "disabled", "no-model", "session-pending", "plan-paused", "quota", "language-saving", "language-error", "ready", "analysing", "cancelled", "ai-error", "result-paused" })
        {
            if (interactive && scenario != "versions") continue;
            var store = new Store(scenario); var provider = new Provider(scenario); var session = new Session(scenario);
            var vm = new MainViewModel(store, provider, insightProvider: provider, connection: session);
            await vm.InitializeAsync();
            var track = new CurrentTrack(1, "Ventana de prueba", "Artista sintético", "Álbum propio", TimeSpan.FromMinutes(3), PlaybackState.Playing);
            if (scenario is "no-track" or "detection") vm.ApplyDetection(new(scenario == "no-track" ? ApplicationState.NoTrack : ApplicationState.Error, null, null));
            else if (scenario != "empty")
            {
                vm.ApplyDetection(new(ApplicationState.Playing, track, null));
                if (scenario is "versions" or "notfound" or "offline" or "timeout" or "limited" or "invalid" or "instrumental") await vm.SearchLyricsAsync();
                else vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz");
                if (scenario is "language-saving" or "language-error")
                {
                    vm.TargetLanguage = "en";
                    if (scenario == "language-error") await vm.WaitForCacheAsync();
                }
                if (scenario is "analysing" or "cancelled")
                {
                    _ = vm.GenerateInsightAsync(); if (scenario == "cancelled") vm.CancelInsight();
                }
                if (scenario == "ai-error") await vm.GenerateInsightAsync();
                if (scenario == "result-paused") { await vm.GenerateInsightAsync(); vm.ApplyAiSettings(new(false, "fixture-model", 3)); }
            }
            using var http = new HttpClient(new NoNetwork());
            var main = new MainWindow
            {
                DataContext = vm, Title = "UX03 · " + scenario, ShowActivated = interactive,
                CreateConnectionWindow = () => new ConnectionWindow(new ConnectionViewModel(new ChatGptConnection(new ChatGptCredentialStore(Path.Combine(output, "unused-session.dpapi")), new ChatGptOAuthClient(http)), CancellationToken.None)),
                CreateSettingsWindow = () => new AiSettingsWindow(new AiSettingsViewModel(new AiConfigurationService(store, new DpapiSecretStore(Path.Combine(output, "unused-api.dpapi")), store, new OpenAiConnectionProbe(http)), CancellationToken.None))
            };
            main.Show(); await Capture(main, scenario + "-normal");
            ((TabControl)main.FindName("ReadingTabs")).SelectedIndex = 1; await Capture(main, scenario + "-meaning");
            main.Width = 360; main.Height = 480; await Capture(main, scenario + "-minimum");
            report.Add($"{scenario}: status={vm.DisplayStatus}; notice={vm.DisplayNotice}; hint={vm.ReadingHint}; inference={provider.Calls}; searches={provider.Searches}");
#if CURRENT_PREPARATION
            if (scenario is "versions" or "notfound" or "disabled" or "session-pending" or "plan-paused" or "quota")
            {
                var before = provider.Calls;
                var button = Find<Button>(main).Single(item => System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "PrimaryAction");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                foreach (Window secondary in Application.Current.Windows.Cast<Window>().Where(w => w != main && w.Owner == main).ToArray())
                {
                    await Capture(secondary, scenario + "-action");
                    if (secondary is LyricsEditorWindow) report.Add($"{scenario}: editor-tab={Find<TabControl>(secondary).Single().SelectedIndex}");
                    secondary.Close();
                }
                if (provider.Calls != before) throw new InvalidOperationException("Configuration/navigation sent inference");
            }
#endif
            if (interactive)
            {
                main.Title = "UX03 · Versiones sintéticas · teclado";
                var done = new TaskCompletionSource(); main.Closed += (_, _) => done.TrySetResult(); await done.Task;
            }
            else main.Close();
            store.Release(); await vm.StopAsync();
        }
        report.Add("App SHA256=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))));
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), report);
    }
    static async Task Capture(Window window, string name)
    {
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)window.Content;
        if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        report.Add($"{name}: {window.Width}x{window.Height} DIP; client {content.ActualWidth}x{content.ActualHeight}; Windows {VisualTreeHelper.GetDpi(window).PixelsPerInchX} DPI; render 96 DPI; Segoe UI, reading 16 pt.");
    }
    static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T item) yield return item; foreach (var item2 in Find<T>(child)) yield return item2; }
    }
    sealed class Session(string scenario) : IAnalysisSession
    {
        public AiConnectionMode Mode => scenario is "session-pending" or "plan-paused" ? AiConnectionMode.ChatGptIncluded : AiConnectionMode.ApiKey;
        public string? Model => "fixture-model";
        public string CacheScope => "synthetic";
        public bool CanGenerate => scenario is not ("session-pending" or "plan-paused");
        public string Status => "Sesión sintética";
        public event EventHandler? Changed { add { } remove { } }
#if CURRENT_PREPARATION
        public AnalysisSessionState Readiness => scenario == "session-pending" ? AnalysisSessionState.PendingRegistration : scenario == "plan-paused" ? AnalysisSessionState.ConfirmationRequired : AnalysisSessionState.Ready;
#endif
    }
    sealed class Store(string scenario) : ISettingsStore, IRequestBudget, IOutputLanguageSettings
    {
        readonly TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => pending.TrySetResult();
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings(scenario != "disabled", scenario == "no-model" ? null : "fixture-model", 3));
        public Task SaveSettingsAsync(AppSettings value, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken t = default) => Task.FromResult(scenario == "quota" ? 3 : 0);
        public Task<bool> TryReserveRequestAsync(DateOnly date, CancellationToken t = default) => throw new InvalidOperationException("No paid requests in fixture");
        public Task<string> ReadOutputLanguageAsync(CancellationToken t = default) => Task.FromResult("es");
        public Task SaveOutputLanguageAsync(string language, CancellationToken t = default) => scenario == "language-saving" ? pending.Task : scenario == "language-error" ? Task.FromException(new IOException("synthetic")) : Task.CompletedTask;
    }
    sealed class Provider(string scenario) : ILyricsProvider, IInsightProvider
    {
        public int Calls, Searches;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token)
        {
            Searches++;
            var failure = scenario switch { "offline" => LyricsFailure.Offline, "timeout" => LyricsFailure.Timeout, "limited" => LyricsFailure.RateLimited, "invalid" => LyricsFailure.InvalidResponse, _ => (LyricsFailure?)null };
            if (failure is { } value) throw new LyricsProviderException(value, value == LyricsFailure.RateLimited ? DateTimeOffset.UtcNow.AddMinutes(1) : null);
            return Task.FromResult(scenario == "instrumental" ? new LyricsSearchResult(track.Revision, LyricsSearchKind.Instrumental, new(track.Revision, "", LyricsOrigin.Lrclib, 1, true), []) :
                scenario == "notfound" ? new LyricsSearchResult(track.Revision, LyricsSearchKind.NotFound, null, []) :
                new LyricsSearchResult(track.Revision, LyricsSearchKind.Candidates, null, [new(1, "Ventana original", "Artista", "Álbum", TimeSpan.FromMinutes(3), false, "Abro una ventana", null), new(2, "Ventana acústica", "Artista", "Otro álbum", TimeSpan.FromMinutes(4), false, "Busco una puerta", null)]));
        }
        public async Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token)
        {
            Calls++; if (scenario is "analysing" or "cancelled") await Task.Delay(Timeout.Infinite, token);
            if (scenario == "ai-error") throw new AiException(AiFailure.Offline);
            return new SongInsight(track.Revision, "es", [], "La voz narrativa busca una oportunidad de cambio.", ["Cambio"], [], [], []) { Usage = new(100, 50, 150) };
        }
    }
    sealed class NoNetwork : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("No network in UX03 fixture"); }
}
