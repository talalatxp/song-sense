using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    static readonly List<string> report = [];
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await Run(); }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); Environment.ExitCode = 1; }
            app.Shutdown(Environment.ExitCode);
        });
        app.Run();
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static async Task Flush(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    static async Task Run()
    {
        report.Add("Synthetic data and providers; HTTP/browser forbidden. No personal database or credentials.");
        report.Add($"Version={BuildIdentity.ProductVersion}; build={BuildIdentity.FileVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}");
        var runData = Path.Combine(output, "data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(runData);
        foreach (var scenario in new[] { "no-spotify", "no-track", "no-lyrics", "not-found", "instrumental", "api-ready", "chatgpt-ready", "chatgpt-model-missing", "api-model-missing", "chatgpt-paused", "chatgpt-disconnected", "unconfigured", "disabled", "error", "cached", "quota", "long-model" })
        {
            var data = Path.Combine(runData, scenario); Directory.CreateDirectory(data);
            var chat = scenario.StartsWith("chatgpt");
            var model = scenario.EndsWith("model-missing") || scenario == "unconfigured" ? null : scenario == "long-model" ? new string('m', 120) : "fixture-model";
            using var http = new HttpClient(new NoNetwork());
            var credentials = new ChatGptCredentialStore(Path.Combine(data, "synthetic.dpapi"));
            await credentials.SaveAsync(new ChatGptCredentials
            {
                Mode = chat ? AiConnectionMode.ChatGptIncluded : AiConnectionMode.ApiKey,
                ActiveClientId = "oaiapp_fixture_client",
                Registrations = [new ChatGptRegistration { ClientId = "oaiapp_fixture_client", Subject = "fixture-subject", Model = model,
                    AccessToken = scenario == "chatgpt-disconnected" ? null : "synthetic-invalid-access", RefreshToken = scenario == "chatgpt-disconnected" ? null : "synthetic-invalid-refresh", Scopes = [ChatGptOAuthClient.PlanScope], ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }]
            }, CancellationToken.None);
            var connection = new ChatGptConnection(credentials, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
            if (scenario == "chatgpt-ready") await connection.SetPlanUseAsync(true, CancellationToken.None);
            var store = new SqliteSettingsStore(Path.Combine(data, "synthetic.db")); await store.InitializeAsync();
            await store.SaveSettingsAsync(new(scenario != "disabled" && model is not null, model, 1));
            if (scenario == "quota") await store.TryReserveRequestAsync(DateOnly.FromDateTime(DateTime.Now));
            var cache = new SqliteCacheStore(Path.Combine(data, "synthetic.db"));
            var track = new CurrentTrack(1, "Ventana propia", "Artista sintético", "Álbum propio", TimeSpan.FromMinutes(3), PlaybackState.Playing);
            var lyrics = new ResolvedLyrics(1, "Abro una ventana\nY dejo entrar la luz", LyricsOrigin.Manual, null, false);
            if (scenario == "cached")
            {
                await cache.SaveLyricsAsync(track, lyrics);
                var summary = string.Join(' ', Enumerable.Repeat("La ventana podría representar una oportunidad de cambio mientras la voz narrativa observa la luz y se prepara para tomar una decisión.", 4));
                await cache.SaveInsightAsync(track, lyrics, model!, CacheIdentity.Version, new(1, "es", [], summary, ["Cambio"], [], [], [])
                { Provenance = new(AiConnectionMode.ApiKey, model!, DateTimeOffset.UtcNow.AddDays(-1)) });
            }
            var provider = new Provider(scenario);
            var vm = new MainViewModel(store, provider, insightProvider: provider, connection: scenario == "unconfigured" ? null : connection, cache: cache);
            await vm.InitializeAsync();
            if (scenario == "no-track") vm.ApplyDetection(new(ApplicationState.NoTrack, null, null));
            else if (scenario != "no-spotify")
            {
                vm.ApplyDetection(new(ApplicationState.Playing, track, null)); await vm.WaitForCacheAsync();
                if (scenario is "not-found" or "instrumental") await vm.SearchLyricsAsync();
                else if (scenario is not ("no-lyrics" or "cached")) { vm.SaveManual(vm.BeginLyricsEdit()!, lyrics.Text); await vm.WaitForCacheAsync(); }
                if (scenario == "error") await vm.GenerateInsightAsync();
            }
            ConnectionViewModel? account = null;
            var main = new MainWindow { DataContext = vm, ShowActivated = false, CreateConnectionWindow = () => new ConnectionWindow(account = new(connection, CancellationToken.None, _ => throw new InvalidOperationException("Browser forbidden"))) };
            main.Show();
            foreach (var size in new[] { (860, 780), (360, 480) })
            {
                main.Width = size.Item1; main.Height = size.Item2;
                foreach (var tab in new[] { 0, 1 })
                {
                    ((TabControl)main.FindName("ReadingTabs")).SelectedIndex = tab; await Flush(main);
                    var button = (Button)main.FindName("AiDecisionButton");
                    Check(button.IsVisible && button.IsEnabled, "Decision must remain visible and actionable");
                    var rect = button.TransformToAncestor((Visual)main.Content).TransformBounds(new Rect(button.RenderSize));
                    Check(rect.Right <= ((FrameworkElement)main.Content).ActualWidth + 1, "Decision exceeds client width");
                    var document = (ReadingDocument)main.FindName(tab == 0 ? "TranslationScroll" : "MeaningScroll");
                    var text = new TextRange(document.Document.ContentStart, document.Document.ContentEnd).Text;
                    Check(!text.Contains("Usa la acción principal"), "Generic instruction survived");
                    if (!vm.HasInsight) Check(text.Contains(vm.Preparation.Message), "Empty state must follow preparation");
                    await Capture(main, $"{scenario}-{size.Item1}-{tab}", document);
                }
            }
            var before = provider.Calls;
            ((Button)main.FindName("AiDecisionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush(main);
            Check(account is not null, "Decision must open Cuenta");
            Check(account!.Step == (scenario == "chatgpt-paused" ? 3 : scenario == "chatgpt-disconnected" ? 1 : chat ? 2 : 5), "Unexpected configuration step");
            Check(provider.Calls == before, "Opening configuration generated an analysis");
            report.Add($"{scenario}: label={vm.AiDecisionLabel}; cause={vm.Preparation.Cause}; action={vm.Preparation.Action}; account-step={account.Step}; synthetic analyses={provider.Calls}; usage={vm.Usage.State}");
            if (scenario == "cached") Check(vm.Usage.State == AiUsageState.RecuperadoDeCache && provider.Calls == 0, "Cache must not infer");
            if (scenario != "error") Check(provider.Calls == 0, "Unexpected analysis");
            foreach (Window child in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == main).ToArray()) child.Close();
            await Flush(main); main.Close(); await vm.StopAsync();
        }
        report.Add("PASS: all scenarios, both tabs and sizes, decision button bounds and Cuenta routing; no HTTP requests.");
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), report);
    }
    static async Task Capture(Window window, string name, ReadingDocument reading)
    {
        await Flush(window);
        var content = (FrameworkElement)window.Content;
        if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        report.Add($"{name}: window={window.Width}x{window.Height} DIP; client={content.ActualWidth}x{content.ActualHeight}; reading={reading.ActualWidth}x{reading.ActualHeight}; observed DPI={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; render=96 DPI; reading=16 pt; first point=top; origin=synthetic");
    }
    sealed class NoNetwork : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) => throw new InvalidOperationException("HTTP forbidden in A2-01 fixture"); }
    sealed class Provider(string scenario) : ILyricsProvider, IInsightProvider
    {
        public int Calls;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => Task.FromResult(scenario == "instrumental"
            ? new LyricsSearchResult(track.Revision, LyricsSearchKind.Instrumental, new(track.Revision, "", LyricsOrigin.Lrclib, 1, true), [])
            : new LyricsSearchResult(track.Revision, LyricsSearchKind.NotFound, null, []));
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token)
        { Calls++; throw new AiException(AiFailure.Offline); }
    }
}
