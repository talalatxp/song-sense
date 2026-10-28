using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
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
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await Run(); report.Add("PASS A3-02: all language scenarios and both tabs; selection/retry made zero new generations."); }
            catch (Exception ex) { report.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(output, "manifest.txt"), report); app.Shutdown(Environment.ExitCode); }
        }); app.Run();
    }
    static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static async Task Flush(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    static async Task Run()
    {
        report.Add("Isolated synthetic stores/providers; no HTTP, personal credentials, system scale or installed app changes.");
        report.Add($"Version={BuildIdentity.ProductVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}");
        foreach (var scale in new[] { 1d, 1.57d })
        foreach (var width in new[] { 360, 860 })
        foreach (var tab in new[] { 0, 1 })
        foreach (var scenario in new[] { "empty", "saved", "session", "write-failed", "cached", "saving", "save-failed", "cancelled", "rapid", "generate-cancel" })
        {
            UiTypography.Apply(Application.Current.Resources, scale);
            var path = Path.Combine(output, "data", Guid.NewGuid() + ".db");
            var sqlite = new SqliteSettingsStore(path); await sqlite.InitializeAsync(); await sqlite.SaveSettingsAsync(new(true, "fixture-model", 20));
            var store = new Store(sqlite); var provider = new Provider();
            var vm = new MainViewModel(store, provider, insightProvider: provider, cache: scenario == "session" ? null : new SqliteCacheStore(path));
            var window = new MainWindow { DataContext = vm, Width = width, Height = width == 360 ? 480 : 780 };
            Task? operation = null;
            try
            {
                await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, new(1, "Ventana propia", "Artista sintético", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
                await vm.WaitForCacheAsync(); vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz"); await vm.WaitForCacheAsync();
                if (scenario == "write-failed")
                {
                    using var db = new SqliteConnection($"Data Source={path};Pooling=False"); await db.OpenAsync();
                    using var command = db.CreateCommand(); command.CommandText = "CREATE TRIGGER fail_insight BEFORE INSERT ON insight_cache BEGIN SELECT RAISE(ABORT,'synthetic'); END;"; command.ExecuteNonQuery();
                }
                if (scenario is "saved" or "session" or "write-failed" or "cached" or "cancelled") await vm.GenerateInsightAsync();
                if (scenario == "cached")
                {
                    vm.TargetLanguage = "en"; await vm.WaitForCacheAsync(); await vm.GenerateInsightAsync();
                    vm.TargetLanguage = "es"; await vm.WaitForCacheAsync();
                }
                if (scenario == "cancelled") { provider.Pending = new(); operation = vm.RegenerateInsightAsync(); }
                var calls = provider.Calls; var source = vm.IdentifiedSourceLanguage; var original = vm.PreparedLyrics!.Text;
                window.Show(); ((TabControl)window.FindName("ReadingTabs")).SelectedIndex = tab; await Flush(window);
                if (scenario == "saving") store.Gate = new();
                store.Fail = scenario == "save-failed";
                var selector = (ComboBox)window.FindName("ReadingLanguage");
                selector.SelectedValue = "en";
                if (scenario == "rapid") { selector.SelectedValue = "fr"; selector.SelectedValue = "pt"; }
                if (scenario == "saving") await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); else await vm.WaitForCacheAsync();
                await Flush(window);
                var notice = (StatusNotice)window.FindName("LanguageStatus");
                var primary = (Button)window.FindName("PrimaryActionButton");
                Check(vm.ShowLanguageMessage && notice.IsVisible && notice.Message == vm.LanguageMessage, "Language message absent or stale on reading tab");
                Check(provider.Calls == calls, "Selecting language generated AI");
                Check(vm.IdentifiedSourceLanguage == source && vm.PreparedLyrics!.Text == original, "Original identity changed");
                Check(vm.LanguageMessage.Contains(scenario == "rapid" ? "Portugués" : "Inglés"), "Wrong destination");
                Check(vm.ShowPreparationNotice == false, "Duplicated preparation notice for language state");
                if (scenario == "cached")
                { Check(vm.HasInsight && vm.Usage.State == AiUsageState.RecuperadoDeCache && vm.LanguageMessage.Contains("recuperado"), "Cache misrepresented as new generation"); Check(!vm.LanguageMessage.Contains("Aún no hay"), "Cache kept stale transition text"); }
                else if (scenario != "saving") Check(!vm.HasInsight, "Old result survived new destination");
                if (scenario == "saved") Check(vm.LanguageMessage.Contains("guardado confirmado"), "Confirmed prior save missing");
                if (scenario is "session" or "write-failed") Check(!vm.LanguageMessage.Contains("guardado confirmado"), "Unconfirmed prior save claimed");
                if (scenario is "saving" or "save-failed") Check(!vm.GenerateInsightCommand.CanExecute(null), "Generation allowed before saving");
                if (scenario == "save-failed") Check(vm.Preparation.Action == PreparationAction.RetryLanguage && primary.IsEnabled, "No retry available");
                if (scenario == "cancelled") Check(provider.Cancelled && vm.LanguageSummary.Contains("puede haber consumo"), "Missing cancellation or consumption warning");
                var viewport = (ScrollViewer)window.Content;
                foreach (var control in new FrameworkElement[] { selector, notice })
                {
                    var bounds = control.TransformToAncestor(viewport).TransformBounds(new Rect(control.RenderSize));
                    Check(bounds.Left >= 0 && bounds.Right <= viewport.ViewportWidth + 1, "Language status exceeds client width");
                    Check(bounds.Top >= 0 && bounds.Bottom <= viewport.ViewportHeight + 1, $"{scenario}: language status outside visible client: {bounds}; viewport={viewport.ViewportHeight}");
                }
                var name = $"{scenario}-{width}-tab{tab}-scale{scale:0.00}"; Capture(window, name);
                report.Add($"{name}: target={vm.TargetLanguage}; source={source}; calls={calls}; font={notice.FontSize}; summary={vm.LanguageSummary}; message={vm.LanguageMessage}");
                if (scenario == "saved")
                {
                    var detail = (Expander)notice.Template.FindName("NoticeDetail", notice);
                    detail.IsExpanded = true; await Flush(window);
                    var full = Find<TextBox>(detail).Single();
                    Check(full.IsReadOnly && full.Text == vm.LanguageMessage && full.Text.Contains("guardado confirmado"), "Full confirmed-save detail inaccessible");
                    full.BringIntoView(); await Flush(window); Capture(window, name + "-detail");
                    Check(provider.Calls == calls, "Opening details generated AI");
                }
                if (scenario == "saving") { store.Gate!.SetResult(); await vm.WaitForCacheAsync(); Check(vm.GenerateInsightCommand.CanExecute(null), "Save completion did not unblock generation"); }
                if (scenario == "save-failed")
                {
                    store.Fail = false; primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await vm.WaitForCacheAsync();
                    Check(provider.Calls == calls && vm.GenerateInsightCommand.CanExecute(null), "Retry generated or did not unblock");
                }
                if (scenario == "generate-cancel")
                {
                    provider.Pending = new(); operation = vm.GenerateInsightAsync(); await Flush(window);
                    Check(!vm.ShowLanguageMessage && vm.Preparation.Action == PreparationAction.CancelAnalysis && primary.IsVisible && primary.IsEnabled, "Language > generate did not expose cancellation");
                    primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); provider.Pending.SetResult(Provider.Result("en")); await operation;
                    Check(!vm.HasInsight && vm.TargetLanguage == "en" && provider.Cancelled && provider.Calls == calls + 1, "Language > generate > cancel failed");
                    report.Add($"combined-{name}: language change then explicit generate/cancel; late result discarded; usage={vm.Usage.State}");
                    operation = null;
                }
                if (operation is not null)
                {
                    provider.Pending!.SetResult(Provider.Result("es")); await operation;
                    Check(!vm.HasInsight && vm.TargetLanguage == "en" && vm.LanguageSummary.Contains("Inglés"), "Late old result changed current destination");
                }
            }
            finally { store.Gate?.TrySetResult(); provider.Pending?.TrySetResult(Provider.Result("es")); window.Close(); await vm.StopAsync(); }
        }
    }
    static void Capture(Window window, string name)
    {
        report.Add($"capture={name}; window={window.ActualWidth}x{window.ActualHeight}; observedDpi={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; uiFactor={(double)window.FindResource("UiControlFont")/14}; renderDpi=96; readingPoints={(window as MainWindow)?.ReadingFontSize}");
        var content = (FrameworkElement)window.Content;
        if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var nested in Find<T>(child)) yield return nested;
        }
    }
    sealed class Store(SqliteSettingsStore inner) : ISettingsStore, IOutputLanguageSettings
    {
        public bool Fail;
        public TaskCompletionSource? Gate;
        public TaskCompletionSource Started { get; } = new();
        public Task InitializeAsync(CancellationToken t = default) => inner.InitializeAsync(t);
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => inner.ReadSettingsAsync(t);
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => inner.SaveSettingsAsync(s, t);
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => inner.ReadRequestCountAsync(d, t);
        public Task<string> ReadOutputLanguageAsync(CancellationToken t = default) => inner.ReadOutputLanguageAsync(t);
        public async Task SaveOutputLanguageAsync(string language, CancellationToken t = default)
        { Started.TrySetResult(); if (Gate is not null) await Gate.Task; if (Fail) throw new IOException("Synthetic save failure"); await inner.SaveOutputLanguageAsync(language, t); }
    }
    sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public int Calls;
        public bool Cancelled;
        public TaskCompletionSource<SongInsight>? Pending;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken t) => throw new InvalidOperationException("No search expected");
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken t) => GenerateAsync(track, lyrics, "es", t);
        public async Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, string language, CancellationToken t)
        { Calls++; using var registration = t.Register(() => Cancelled = true); return Pending is null ? Result(language) : await Pending.Task; }
        public static SongInsight Result(string language) => new(1, "es", language == "es" ? [] : [new(1, "[synthetic translation]", "es"), new(2, "[synthetic translation]", "es")], string.Join(' ', Enumerable.Repeat("La ventana podría representar una oportunidad de cambio mientras la voz narrativa contempla la luz y se prepara para un nuevo comienzo.", 5)), ["Cambio"], [], [], []) { TargetLanguage = language };
    }
}
