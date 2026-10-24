using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using SongSense.App;
using SongSense.Core;

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
            try { await Run(args.Contains("--interactive")); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
            app.Shutdown();
        }); app.Run();
    }
    static TrackDetection Observe(long id, PlaybackState playback = PlaybackState.Playing) =>
        new(playback == PlaybackState.Paused ? ApplicationState.Paused : ApplicationState.Playing,
            new(id, "Ventana de prueba " + id, "Artista sintético", "Álbum propio", TimeSpan.FromMinutes(3), playback), "Spotify.exe");
    static async Task Run(bool interactive)
    {
        foreach (var scenario in new[] { "playing-ready", "spotify-paused", "tracking-paused", "both-paused", "other-song", "spotify-closed", "reopened", "startup-paused" })
        {
            if (interactive && scenario != "other-song") continue;
            var store = new Store { Enabled = scenario != "startup-paused" }; var provider = new Provider();
            var vm = new MainViewModel(store, provider, insightProvider: provider);
            await vm.InitializeAsync();
            if (scenario != "startup-paused")
            {
                vm.ApplyDetection(Observe(1)); vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz");
                await vm.GenerateInsightAsync();
            }
            vm.StartAutomaticUpdates();
            if (scenario is not ("playing-ready" or "spotify-paused" or "startup-paused")) vm.AutomaticUpdatesEnabled = false;
            if (scenario is "spotify-paused" or "both-paused") vm.ApplyDetection(Observe(1, PlaybackState.Paused));
            if (scenario is "other-song" or "startup-paused") { vm.ApplyDetection(Observe(2)); vm.ApplyDetection(Observe(3, PlaybackState.Paused)); }
            if (scenario == "spotify-closed") vm.ApplyDetection(new(ApplicationState.NoSpotify, null, null));
            var main = new MainWindow { DataContext = vm, Title = "UX04 · " + scenario, ShowActivated = interactive };
            main.Show();
            if (scenario == "reopened")
            {
                main.Hide(); vm.ApplyDetection(Observe(2)); vm.ApplyDetection(Observe(4, PlaybackState.Paused)); main.OpenFromTray();
                if (vm.CurrentTrack?.Revision != 1 || vm.Insight is null || vm.AutomaticUpdatesEnabled) throw new InvalidOperationException("Reopening changed frozen content");
            }
            await Capture(main, scenario + "-normal");
            ((TabControl)main.FindName("ReadingTabs")).SelectedIndex = 1; await Capture(main, scenario + "-meaning");
            main.Width = 360; main.Height = 480; await Capture(main, scenario + "-minimum");
            report.Add($"{scenario}: shown={vm.CurrentTrack?.Revision}; ready={vm.HasInsight}; preparation={vm.DisplayStatus}; tracking={vm.AutomaticUpdatesEnabled}; inference={provider.Inferences}; settings-open={((Expander)main.FindName("OptionsPanel")).IsExpanded}");
#if CURRENT_TRACKING
            report.Add($"{scenario}: label={vm.TrackContextLabel}; playback={vm.PlaybackStatus}; tracking-label={vm.TrackingStatus}");
            foreach (var id in new[] { "PlaybackStatus", "TrackingStatus", "ResumeTracking" })
            {
                var element = Find<FrameworkElement>(main).Single(e => AutomationProperties.GetAutomationId(e) == id);
                var position = element.TransformToAncestor(main).Transform(new Point());
                report.Add($"{scenario}: {id} visible={element.IsVisible}; bounds={position.X},{position.Y},{element.ActualWidth},{element.ActualHeight}");
                if (element.IsVisible && (position.Y + element.ActualHeight > main.ActualHeight || element.ActualWidth <= 0)) throw new InvalidOperationException("Playback context outside minimum viewport");
            }
            if (vm.CanResumeTracking && !interactive)
            {
                var expected = scenario is "other-song" or "startup-paused" ? 3 : scenario == "reopened" ? 4 : scenario == "spotify-closed" ? (long?)null : 1;
                var calls = provider.Inferences;
                var resume = Find<Button>(main).Single(b => AutomationProperties.GetAutomationId(b) == "ResumeTracking");
                var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(resume);
                ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await vm.WaitForAutomaticAsync();
                if (provider.Inferences != calls || vm.CurrentTrack?.Revision != expected || !vm.AutomaticUpdatesEnabled) throw new InvalidOperationException("Resume did not use latest observation without inference");
                report.Add($"{scenario}: resumed={vm.CurrentTrack?.Revision}; playback={vm.PlaybackStatus}; inference={provider.Inferences}");
            }
#endif
            if (interactive)
            {
                main.Title = "UX04 · Seguimiento sintético · teclado";
                var done = new TaskCompletionSource(); main.Closed += (_, _) => done.TrySetResult(); await done.Task;
                report.Add($"keyboard: shown={vm.CurrentTrack?.Revision}; tracking={vm.AutomaticUpdatesEnabled}; inference={provider.Inferences}");
            }
            else main.Close();
            await vm.StopAsync();
        }
        report.Add("App SHA256=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))));
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), report);
    }
    static async Task Capture(Window window, string name)
    {
        foreach (var text in Find<TextBlock>(window)) { text.InvalidateMeasure(); text.InvalidateVisual(); }
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        await Task.Delay(30);
        var content = (FrameworkElement)window.Content;
        if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        report.Add($"{name}: {window.Width}x{window.Height} DIP; client {content.ActualWidth}x{content.ActualHeight}; Windows {VisualTreeHelper.GetDpi(window).PixelsPerInchX} DPI; render 96 DPI; Segoe UI, reading 16 pt.");
    }
    static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T item) yield return item; foreach (var nested in Find<T>(child)) yield return nested; }
    }
    sealed class Store : ISettingsStore, IAutomaticUpdateSettings
    {
        public bool Enabled = true;
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings(true, "fixture-model"));
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken t = default) => Task.FromResult(0);
        public Task<bool> ReadAutomaticUpdatesAsync(CancellationToken t = default) => Task.FromResult(Enabled);
        public Task SaveAutomaticUpdatesAsync(bool enabled, CancellationToken t = default) { Enabled = enabled; return Task.CompletedTask; }
    }
    sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public int Inferences;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken t) => Task.FromResult(new LyricsSearchResult(track.Revision, LyricsSearchKind.Matched,
            new(track.Revision, "Abro una ventana\nY dejo entrar la luz", LyricsOrigin.Lrclib, 1, false), []));
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken t)
        {
            Inferences++;
            return Task.FromResult(new SongInsight(track.Revision, "es", [], "La voz narrativa busca una oportunidad de cambio.", ["Cambio"], [], [], []) { Usage = new(100, 50, 150) });
        }
    }
}
