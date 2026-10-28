using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await Run(); report.Add("PASS A3-06: five candidates, arrows/click/Enter/double-click/button, focus return, resizing, explicit confirmation and stale draft protection."); }
            catch (Exception ex) { report.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(output, "manifest.txt"), report); app.Shutdown(Environment.ExitCode); }
        }); app.Run();
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static async Task Flush(Window window)
    { for (var i = 0; i < 3; i++) await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); }
    static async Task Run()
    {
        report.Add("Synthetic five-candidate result loaded once per scenario. WPF input events, no OS mouse/keyboard injection. No real LRCLIB, AI, HTTP or personal settings.");
        report.Add($"Version={BuildIdentity.ProductVersion}; build={BuildIdentity.FileVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LyricsEditorWindow).Assembly.Location)))}");
        var clipboard = Clipboard.GetDataObject();
        try
        {
            foreach (var scale in new[] { 1d, 1.57d })
            foreach (var width in new[] { 360, 600, 860 })
            {
                foreach (var method in new[] { "button", "enter", "double" }) await Scenario(width, scale, method, false);
                await Scenario(width, scale, "enter", true);
            }
        }
        finally { if (clipboard is null) Clipboard.Clear(); else Clipboard.SetDataObject(clipboard, true); }
    }
    static async Task Scenario(int width, double scale, string method, bool stale)
    {
        UiTypography.Apply(Application.Current.Resources, scale);
        var provider = new Provider(); var owner = new MainViewModel(new Store(), provider, insightProvider: provider);
        await owner.InitializeAsync(); owner.ApplyDetection(Track(1)); await owner.SearchLyricsAsync();
        var editor = new LyricsEditorViewModel(owner, owner.BeginLyricsEdit()!);
        var window = new LyricsEditorWindow(editor, false) { Width = width, Height = width == 360 ? 480 : 700 };
        var name = $"{(stale ? "stale" : method)}-{width}-scale{scale:0.00}";
        try
        {
            window.Show(); window.Activate(); await Flush(window);
            var list = (ListBox)window.FindName("VersionsList"); var preview = (ScrollViewer)window.FindName("VersionPreview");
            var heading = (TextBlock)window.FindName("VersionsHeading");
            var next = (Button)window.FindName("PreviewNext"); var back = (Button)window.FindName("VersionBack");
            Check(list.IsVisible && editor.VersionsStep == LyricsVersionsStep.List && !next.IsEnabled, "Initial list/selection state is wrong");
            if (!stale)
            {
                await SelectByClick(0);
                for (var index = 0; index < 5; index++)
                {
                    if (index > 0) { SendKey(window, Key.Down); await Flush(window); }
                    Check(list.SelectedIndex == index && Item(index).IsKeyboardFocused, "Arrow lost candidate selection or focus");
                    Check(editor.VersionsStep == LyricsVersionsStep.List && list.IsVisible, "Arrow opened preview");
                    NoRequests(); Check(owner.PreparedLyrics is null, "Navigation applied candidate");
                }
                Render(window, name + "-all-five-list");
            }
            else editor.ManualText = "Borrador propio anterior que se conserva para copiar.";
            await SelectByClick(1); await Open(); Render(window, name + "-preview");
            Check(editor.VersionsStep == LyricsVersionsStep.Preview && preview.IsVisible && preview.IsKeyboardFocused, "Explicit opening did not focus preview");
            foreach (var nextWidth in new[] { 360, 600, 860, 600, width })
            {
                window.Width = nextWidth; await Flush(window);
                Check(ReferenceEquals(editor.Selected, editor.Options[1]) && editor.VersionsStep == LyricsVersionsStep.Preview, "Resize lost selection/step");
                Check(preview.IsVisible && (nextWidth < 640 ? !list.IsVisible : list.IsVisible), "Resize does not match preview/compare surfaces");
                Check(heading.Text.Contains(nextWidth < 640 ? "Vista previa" : "compara"), "Heading does not match visible step");
                Check(owner.PreparedLyrics is null, "Resize confirmed lyrics"); NoRequests();
            }
            window.Width = Math.Min(width, 600); await Flush(window);
            back.BringIntoView(); await Flush(window); back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush(window);
            Check(editor.VersionsStep == LyricsVersionsStep.List && list.IsVisible && !preview.IsVisible && heading.Text.Contains("Selecciona"), "Back left stale preview state/heading");
            var returned = Item(1);
            Check(returned.IsKeyboardFocused && returned.IsSelected, "Back focused list container instead of selected candidate");
            var viewport = Find<ScrollContentPresenter>(list).First();
            var returnedBounds = returned.TransformToAncestor(viewport).TransformBounds(new Rect(returned.RenderSize));
            Check(returnedBounds.Top >= -1 && returnedBounds.Bottom <= viewport.ActualHeight + 1, "Returned candidate is outside the list viewport");
            OnScreen(returned);
            Render(window, name + "-back-focused");
            // Resize while in List: neither the selection nor the explicit step may change.
            foreach (var nextWidth in new[] { 860, 360, 600 })
            {
                window.Width = nextWidth; await Flush(window);
                Check(editor.VersionsStep == LyricsVersionsStep.List && list.IsVisible && editor.Selected == editor.Options[1], "Resize changed List step");
                Check(nextWidth < 640 ? !preview.IsVisible : preview.IsVisible, "List/compare adaptation is wrong");
            }
            if (stale)
            {
                var oldPreview = editor.Preview; var oldDraft = editor.ManualText;
                owner.ApplyDetection(Track(2)); await Flush(window);
                Check(!editor.IsCurrent && !editor.ConfirmCommand.CanExecute(null) && !editor.SaveCommand.CanExecute(null), "Stale editor can apply to new song");
                Check(!((Button)window.FindName("ConfirmVersionButton")).IsEnabled, "Stale confirmation remains enabled in the window");
                var copy = Find<Button>(window).Single(b => Equals(b.Content, "Copiar borrador"));
                Check(copy.IsEnabled, "Stale draft cannot be copied");
                copy.BringIntoView(); await Flush(window); OnScreen(copy); copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Clipboard.GetText() == oldDraft && editor.Preview == oldPreview, "Context change lost draft/preview");
                Render(window, name + "-stale-blocked-copy");
                await Open(); await editor.SaveAsync(true); await editor.SaveAsync();
                Check(owner.PreparedLyrics is null && editor.ManualText == oldDraft, "Stale save changed content");
                Render(window, name + "-stale-copy-read"); NoRequests();
            }
            else
            {
                await SelectByClick(3); SendKey(window, Key.Down); await Flush(window);
                Check(list.SelectedIndex == 4 && editor.VersionsStep == LyricsVersionsStep.List, "Second arrow traversal changed surface");
                await Open(); window.Width = width; await Flush(window);
                var confirm = (Button)window.FindName("ConfirmVersionButton");
                Check(confirm.IsVisible && confirm.IsEnabled && owner.PreparedLyrics is null, "Version applied before explicit confirmation");
                confirm.BringIntoView(); await Flush(window); OnScreen(confirm);
                Render(window, name + "-ready-to-confirm");
                ((IInvokeProvider)new ButtonAutomationPeer(confirm).GetPattern(PatternInterface.Invoke)).Invoke(); await Flush(window);
                Check(!window.IsVisible && owner.PreparedLyrics?.LrclibId == 5 && owner.OriginalLyrics.StartsWith("Texto propio de la versión 5"), "Explicit confirmation did not apply chosen version");
                NoRequests();
            }
            report.Add($"{name}: five options; returned-item={returnedBounds}; list-viewport={viewport.ActualHeight}; method={method}; search calls={provider.Searches}; AI calls={provider.Generations}; original-context-current={editor.IsCurrent}; PASS");
            async Task SelectByClick(int index)
            {
                list.ScrollIntoView(editor.Options[index]); await Flush(window);
                var item = Item(index);
                item.BringIntoView(); await Flush(window); OnScreen(item);
                item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                await Flush(window);
                Check(list.SelectedIndex == index && item.IsKeyboardFocused && editor.VersionsStep == LyricsVersionsStep.List, "Single click opened preview or lost item focus");
            }
            ListBoxItem Item(int index) => (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(editor.Options[index]);
            async Task Open()
            {
                if (method == "button")
                {
                    if (window.Width >= 640) { window.Width = 600; await Flush(window); }
                    Check(next.IsVisible && next.IsEnabled, "Preview button is unavailable");
                    next.BringIntoView(); await Flush(window); OnScreen(next); next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else
                {
                    var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(editor.Selected!); item.Focus();
                    if (method == "enter") SendKey(window, Key.Enter);
                    else list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = item });
                }
                await Flush(window); NoRequests();
            }
            void NoRequests() => Check(provider.Searches == 1 && provider.Generations == 0, "Browsing already loaded candidates made a provider request");
            void OnScreen(FrameworkElement element)
            {
                var outer = (ScrollViewer)window.Content;
                var bounds = element.TransformToAncestor(outer).TransformBounds(new Rect(element.RenderSize));
                Check(bounds.Top >= -1 && bounds.Bottom <= outer.ViewportHeight + 1 && bounds.Left >= -1 && bounds.Right <= outer.ViewportWidth + 1, $"{name}: selected item/action outside window viewport: {bounds}; viewport={outer.ViewportWidth}x{outer.ViewportHeight}");
            }
        }
        finally
        {
            if (window.IsVisible)
            {
                var dismissal = window.Dispatcher.BeginInvoke(() =>
                {
                    var prompt = Application.Current.Windows.OfType<ConfirmationWindow>().SingleOrDefault();
                    if (prompt is not null) Find<Button>(prompt).Single(b => Equals(b.Content, "Descartar borrador")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }, DispatcherPriority.ContextIdle);
                window.RequestClose(); await dismissal;
            }
            await owner.StopAsync();
        }
    }
    static void SendKey(Window window, Key key)
    {
        var target = Keyboard.FocusedElement as UIElement ?? throw new InvalidOperationException("No focused input target");
        var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(input);
        if (!input.Handled) { input.RoutedEvent = Keyboard.KeyDownEvent; target.RaiseEvent(input); }
    }
    static void Render(Window window, string name)
    {
        report.Add($"capture={name}; observedDpi={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; uiFactor={(double)window.FindResource("UiControlFont")/14}; renderDpi=96");
        report.Add($"capture={name}; actual-window={window.ActualWidth}x{window.ActualHeight}; ui-font={window.FindResource("UiControlFont")}; outer-offset={((ScrollViewer)window.Content).VerticalOffset}");
        var content = (FrameworkElement)window.Content; if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T t) yield return t; foreach (var nested in Find<T>(child)) yield return nested; } }
    static TrackDetection Track(long revision) => new(ApplicationState.Playing, new(revision, "Ventana propia " + revision, "Artista", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null);
    sealed class Store : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings());
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(0);
    }
    sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public int Searches, Generations;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken t)
        {
            Searches++;
            return Task.FromResult(new LyricsSearchResult(track.Revision, LyricsSearchKind.Candidates, null, Enumerable.Range(1, 5).Select(i => new LyricsCandidate(i, $"Ventana propia · versión {i} · grabación en directo y mezcla alternativa de título largo", "Artista sintético", $"Mismo álbum · edición {i}", TimeSpan.FromSeconds(180 + i), false, $"Texto propio de la versión {i}\n" + string.Join('\n', Enumerable.Repeat("Abro una ventana y dejo entrar la luz de la mañana", 40)), null)).ToArray()));
        }
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken t) { Generations++; throw new InvalidOperationException("AI forbidden"); }
    }
}
