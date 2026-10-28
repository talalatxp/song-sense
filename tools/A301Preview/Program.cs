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
            try { await Run(); report.Add("PASS A3-01 matrix, keyboard traversal, resize choice and cancellation."); }
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
    static async Task Run()
    {
        report.Add("Synthetic data; isolated local stores; no HTTP providers, credentials or system settings changed.");
        report.Add($"Version={BuildIdentity.ProductVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}");
        foreach (var scale in new[] { 1d, 1.57d })
        foreach (var size in new[] { (360, 480), (600, 700), (860, 780) })
        foreach (var scenario in new[] { "versions", "disabled", "error", "ready", "analysis", "search" })
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
                Task? operation = null;
                if (scenario is "versions") await vm.SearchLyricsAsync();
                else if (scenario == "search") operation = vm.SearchLyricsAsync();
                else
                {
                    vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz"); await vm.WaitForCacheAsync();
                    if (scenario == "error") await vm.GenerateInsightAsync();
                    if (scenario == "analysis") operation = vm.GenerateInsightAsync();
                }
                if (operation is not null) await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var expected = scenario switch
                {
                    "versions" => PreparationAction.ChooseVersion, "disabled" => PreparationAction.OpenApi,
                    "analysis" => PreparationAction.CancelAnalysis, "search" => PreparationAction.CancelSearch,
                    _ => PreparationAction.Generate
                };
                Check(vm.Preparation.Action == expected, $"{scenario}: unexpected preparation {vm.Preparation.Action}");
                window = new MainWindow { DataContext = vm, Width = size.Item1, Height = size.Item2 };
                window.Show(); await Flush(window);
                var expander = (Expander)window.FindName("ReadingActions");
                Check(expander.IsExpanded == (size.Item1 >= 600), "Initial folding ignored opening width");
                var primary = (Button)window.FindName("PrimaryActionButton");
                var menu = (Button)window.FindName("ApplicationMenuButton");
                foreach (var expanded in new[] { false, true })
                {
                    expander.IsExpanded = expanded; await Flush(window);
                    foreach (var button in new[] { primary, menu })
                    {
                        var bounds = button.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(button.RenderSize));
                        var viewport = (ScrollViewer)window.Content;
                        Check(button.IsVisible && button.IsEnabled && bounds.Top >= 0 && bounds.Bottom <= viewport.ViewportHeight + 1 && bounds.Left >= 0 && bounds.Right <= viewport.ViewportWidth + 1, $"{scenario}: action clipped {bounds}");
                    }
                    window.Activate();
                    var decision = (Button)window.FindName("AiDecisionButton");
                    Check(decision.Focus(), "Cannot focus AI decision");
                    Check(decision.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && menu.IsKeyboardFocused, "Tab did not reach menu");
                    Check(menu.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && primary.IsKeyboardFocused, "Tab did not reach essential action");
                    Check(primary.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)) && menu.IsKeyboardFocused, "Shift+Tab did not return to menu");
                    var name = $"{scenario}-{size.Item1}-scale{scale:0.00}-{expanded}";
                    Capture(window, name);
                    report.Add($"{name}: action={vm.PrimaryActionLabel}; enabled={vm.CanPrimaryAction}; font={primary.FontSize}; keyboard=decision > menu > primary > menu; usage={vm.Usage.State}");
                }
                foreach (var expanded in new[] { false, true })
                {
                    expander.IsExpanded = expanded;
                    foreach (var width in new[] { 599, 600, 599 })
                    {
                        window.Width = width; await Flush(window);
                        Check(expander.IsExpanded == expanded, "Resize overwrote explicit folding choice");
                    }
                }
                expander.IsExpanded = false;
                menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush(window);
                Check(menu.ContextMenu.IsOpen, "Menu inaccessible when collapsed");
                foreach (var label in new[] { "Salir de Song Sense", "Acerca de Song Sense", "Ver diagnóstico" })
                    Check(menu.ContextMenu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, label) && i.IsEnabled), "Missing menu action " + label);
                menu.ContextMenu.IsOpen = false;
                if (operation is not null)
                {
                    primary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await operation.WaitAsync(TimeSpan.FromSeconds(5)); await Flush(window);
                    Check(provider.Cancelled, "Essential Cancel did not cancel the provider token");
                    Check(vm.Preparation.Action != expected, "Cancellation did not leave operation state");
                    report.Add($"{scenario}: provider token cancelled via essential button; usage after={vm.Usage.State}; message={vm.UsageMessage}");
                }
                else Check(provider.Calls == (scenario == "error" ? 1 : 0), "UI navigation generated AI");
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
            throw new AiException(AiFailure.Offline);
        }
    }
}
