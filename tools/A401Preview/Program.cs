using System.IO;
using System.Security.Cryptography;
using System.Windows;
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
            try { if (args.Contains("--interactive")) await Interactive(); else { await Run(); report.Add("PASS A4-01 pause notice, geometry, keyboard, resize and cancellation."); } }
            catch (Exception ex) { report.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "manifest.txt"), report);
                app.Shutdown(Environment.ExitCode);
            }
        });
        app.Run();
    }
    static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static async Task Flush(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    // Native input session for the same isolated fixture; never uses user stores/providers.
    static async Task Interactive()
    {
        UiTypography.Apply(Application.Current.Resources, 1);
        var store = new SqliteSettingsStore(Path.Combine(output, "native-fixture.db"));
        await store.InitializeAsync(); await store.SaveSettingsAsync(new(true, "fixture-model", 20));
        var provider = new Provider("native");
        var vm = new MainViewModel(store, provider, insightProvider: provider);
        await vm.InitializeAsync();
        vm.ApplyDetection(new(ApplicationState.Playing, new(1, "Ventana propia", "Artista sintético", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
        vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz"); await vm.WaitForCacheAsync();
        vm.AutomaticUpdatesEnabled = false;
        vm.ApplyDetection(new(ApplicationState.Playing, new(2, "Otra canción propia", "Otro artista", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
        var window = new MainWindow { DataContext = vm, Title = "A4 · Seguimiento sintético aislado", Width = 360, Height = 480 };
        var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult();
        window.ConfigureCloseBehavior(() => Task.CompletedTask, () => { });
        report.Add($"Native isolated host; App={typeof(MainWindow).Assembly.Location}; Version={BuildIdentity.ProductVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}");
        window.Show(); await Flush(window); Capture(window, "native-before");
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.ShowTrackingPause)) report.Add($"pause={vm.ShowTrackingPause}; currentRevision={vm.CurrentTrack?.Revision}; providerCalls={provider.Calls}");
        };
        await closed.Task; await vm.StopAsync();
        Check(provider.Calls == 0, "Native traversal generated AI");
        Check(vm.CurrentTrack?.Revision == 2 && vm.AutomaticUpdatesEnabled, "Native Resume must adopt the latest observation");
        report.Add("PASS native Resume, latest revision adopted; no AI calls; normal exit.");
    }
    static async Task Run()
    {
        report.Add("Synthetic data; isolated local stores; no HTTP providers, credentials or system settings changed.");
        report.Add($"Version={BuildIdentity.ProductVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}");
        foreach (var scale in new[] { 1d, 1.57d })
        foreach (var size in new[] { (360, 480), (600, 700), (860, 780) })
        foreach (var points in new[] { 12d, 24d })
        foreach (var scenario in new[] { "empty", "lyrics", "result", "other", "analysis", "search", "active" })
        {
            UiTypography.Apply(Application.Current.Resources, scale);
            var store = new SqliteSettingsStore(Path.Combine(output, "data", Guid.NewGuid() + ".db"));
            await store.InitializeAsync(); await store.SaveSettingsAsync(new(scenario != "disabled", "fixture-model", 20));
            var provider = new Provider(scenario);
            var vm = new MainViewModel(store, provider, insightProvider: provider);
            MainWindow? window = null;
            try
            {
                await vm.InitializeAsync();
                vm.ApplyDetection(new(ApplicationState.Playing, new(1, "Ventana propia", "Artista sintético", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
                await vm.WaitForCacheAsync();
                vm.AutomaticUpdatesEnabled = false;
                Task? operation = null;
                if (scenario == "search") operation = vm.SearchLyricsAsync();
                else if (scenario != "empty")
                {
                    vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz"); await vm.WaitForCacheAsync();
                    if (scenario is "result" or "other") await vm.GenerateInsightAsync();
                    if (scenario == "analysis") operation = vm.GenerateInsightAsync();
                }
                if (scenario == "other") vm.ApplyDetection(new(ApplicationState.Playing, new(2, "Ventana propia", "Otro artista", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
                if (scenario == "active") vm.AutomaticUpdatesEnabled = true;
                if (operation is not null) await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var expected = vm.Preparation.Action;
                window = new MainWindow { DataContext = vm, Width = size.Item1, Height = size.Item2 };
                window.Show(); window.ReadingFontSize = points; await Flush(window);
                var expander = (Expander)window.FindName("ReadingActions");
                Check(expander.IsExpanded == (size.Item1 >= 600), "Initial folding ignored opening width");
                var primary = (Button)window.FindName("PrimaryActionButton");
                var menu = (Button)window.FindName("ApplicationMenuButton");
                foreach (var expanded in new[] { false, true })
                {
                    expander.IsExpanded = expanded; await Flush(window);
                    var resume = (Button)window.FindName("ResumeTrackingButton");
                    var notice = (TextBlock)window.FindName("TrackingPauseNotice");
                    Check(notice.IsVisible == (scenario != "active"), "Pause visibility");
                    if (scenario != "active") Check(notice.Text == vm.TrackingPauseMessage && !string.IsNullOrEmpty(notice.Text), "Pause message binding");
                    if (scenario != "active")
                    {
                        var bounds = notice.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(notice.RenderSize));
                        Check(bounds.Bottom <= ((ScrollViewer)window.Content).ViewportHeight + 1, "Pause notice clipped");
                    }
                    foreach (var button in (scenario == "active" ? new[] { menu } : operation is not null ? new[] { resume, menu, primary } : new[] { resume, menu }))
                    {
                        var bounds = button.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(button.RenderSize));
                        var viewport = (ScrollViewer)window.Content;
                        Check(button.IsVisible && button.IsEnabled && bounds.Top >= 0 && bounds.Bottom <= viewport.ViewportHeight + 1 && bounds.Left >= 0 && bounds.Right <= viewport.ViewportWidth + 1, $"{scenario}: action clipped {bounds}");
                    }
                    window.Activate();
                    var decision = (Button)window.FindName("AiDecisionButton");
                    Check(decision.Focus(), "Cannot focus AI decision");
                    Check(decision.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && menu.IsKeyboardFocused, "Tab did not reach menu");
                    if (primary.IsVisible && primary.IsEnabled)
                        Check(menu.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && primary.IsKeyboardFocused, "Tab primary");
                    if (scenario != "active")
                    {
                        if (primary.IsVisible && primary.IsEnabled) Check(primary.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && resume.IsKeyboardFocused, "Tab resume");
                        else Check(menu.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && resume.IsKeyboardFocused, "Tab resume without primary");
                    }
                    if (operation is not null) Check(primary.IsVisible && primary.IsEnabled && vm.PrimaryActionLabel.Contains("Cancelar"), "Cancel lost priority");
                    var name = $"{scenario}-{size.Item1}-pt{points}-scale{scale:0.00}-{expanded}";
                    Capture(window, name);
                    report.Add($"{name}: action={vm.PrimaryActionLabel}; enabled={vm.CanPrimaryAction}; font={primary.FontSize}; keyboard=Tab decision > menu > optional primary > resume; activation=controlled WPF Enter; usage={vm.Usage.State}");
                }
                foreach (var expanded in new[] { false, true })
                {
                    expander.IsExpanded = expanded;
                    var focus = (Button)window.FindName(scenario == "active" ? "ApplicationMenuButton" : "ResumeTrackingButton");
                    focus.Focus();
                    foreach (var width in new[] { 599, 600, 599 })
                    {
                        window.Width = width; await Flush(window);
                        Check(expander.IsExpanded == expanded, "Resize overwrote explicit folding choice");
                        Check(focus.IsKeyboardFocused, "Resize lost keyboard focus");
                    }
                }
                expander.IsExpanded = false;
                if (operation is not null)
                {
                    primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await operation.WaitAsync(TimeSpan.FromSeconds(5)); await Flush(window);
                    Check(provider.Cancelled, "Essential Cancel did not cancel the provider token");
                    Check(vm.Preparation.Action != expected, "Cancellation did not leave operation state");
                    report.Add($"{scenario}: provider token cancelled via essential button; usage after={vm.Usage.State}; message={vm.UsageMessage}");
                }
                var calls = provider.Calls;
                if (scenario != "active")
                {
                    var resume = (Button)window.FindName("ResumeTrackingButton");
                    resume.Focus();
                    resume.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(resume), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                    await Flush(window); await vm.WaitForCacheAsync();
                    Check(vm.AutomaticUpdatesEnabled && !((FrameworkElement)window.FindName("TrackingPausePanel")).IsVisible, "Resume did not clear notice");
                    Check(provider.Calls == calls, "Resume generated AI");
                }
            }
            finally { window?.Close(); await vm.StopAsync(); }
        }
    }
    static void Capture(Window window, string name)
    {
        report.Add($"capture={name}; window={window.ActualWidth}x{window.ActualHeight}; observedDpi={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; uiFactor={(double)window.FindResource("UiControlFont")/14}; renderDpi=96; readingPoints={(window as MainWindow)?.ReadingFontSize}");
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    sealed class Provider(string scenario) : ILyricsProvider, IInsightProvider
    {
        public int Calls;
        public bool Cancelled;
        public TaskCompletionSource Started { get; } = new();
        async Task Wait(CancellationToken token)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
        public async Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token)
        {
            if (scenario == "search") await Wait(token);
            return new(track.Revision, LyricsSearchKind.Candidates, null, Enumerable.Range(1, 3).Select(i => new LyricsCandidate(i, "Versión " + i, "Autor", "Álbum", TimeSpan.FromMinutes(3), false, "Texto propio", null)).ToArray());
        }
        public async Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token)
        {
            Calls++;
            if (scenario == "analysis") await Wait(token);
            return new SongInsight(track.Revision, "es", [], "Una oportunidad de cambio.", ["Cambio"], [], [], []);
        }
    }
}
