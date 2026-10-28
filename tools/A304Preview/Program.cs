using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
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
    static readonly string summary = string.Join(' ', Enumerable.Repeat("La ventana podría representar una oportunidad de cambio mientras la voz narrativa observa la luz y considera un nuevo comienzo con cautela.", 5));
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await Run(); report.Add("PASS A3-04 matrix: content/hierarchy sizes, summary budget, copy, expansion and logical reading position."); }
            catch (Exception ex) { report.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(output, "manifest.txt"), report); app.Shutdown(Environment.ExitCode); }
        }); app.Run();
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static async Task Flush(Window window)
    {
        for (var i = 0; i < 3; i++) await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    }
    static async Task Run()
    {
        report.Add("Same complete synthetic insight throughout; no HTTP, credentials, personal stores or Windows scale changes. Client WPF renders at 96 DPI.");
        report.Add($"Version={BuildIdentity.ProductVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}");
        var previousClipboard = Clipboard.GetDataObject();
        try
        {
            foreach (var scale in new[] { 1d, 1.57d })
            foreach (var size in new[] { (360, 480), (600, 700), (860, 780) })
            foreach (var points in new[] { 12d, 16d, 20d, 24d }) await Scenario(scale, size, points);
        }
        finally { if (previousClipboard is null) Clipboard.Clear(); else Clipboard.SetDataObject(previousClipboard, true); }
    }
    static async Task Scenario(double scale, (int Width, int Height) size, double points)
    {
        UiTypography.Apply(Application.Current.Resources, scale);
        var provider = new Provider(); var vm = new MainViewModel(new Store(), provider, insightProvider: provider);
        var window = new MainWindow { DataContext = vm, Width = size.Width, Height = size.Height, ReadingFontSize = points };
        try
        {
            await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, new(1, "Ventana propia", "Artista sintético", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
            vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz"); await vm.GenerateInsightAsync();
            window.Show(); var tabs = (TabControl)window.FindName("ReadingTabs"); tabs.SelectedIndex = 1; await Flush(window);
            var view = (ReadingDocument)window.FindName("MeaningScroll"); var document = view.Document;
            var name = $"{size.Width}-pt{points}-scale{scale:0.00}";
            var menu = (Button)window.FindName("ApplicationMenuButton");
            Check(menu.IsVisible && menu.IsEnabled, "A3-01 application menu hidden");
            var menuBounds = menu.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(menu.RenderSize));
            Check(menuBounds.Top >= 0 && menuBounds.Bottom <= ((ScrollViewer)window.Content).ViewportHeight, "Essential area outside opening viewport");
            // An explicit operation swaps the same essential action to Cancel, even with the group closed.
            provider.Pending = new(); var operation = vm.RegenerateInsightAsync(); await Flush(window);
            var action = (Button)window.FindName("PrimaryActionButton");
            Check(action.IsVisible && action.IsEnabled && vm.Preparation.Action == PreparationAction.CancelAnalysis, "A3-01 cancellation hidden");
            var actionBounds = action.TransformToAncestor((Visual)window.Content).TransformBounds(new Rect(action.RenderSize));
            Check(actionBounds.Top >= 0 && actionBounds.Bottom <= ((ScrollViewer)window.Content).ViewportHeight, "Essential cancel outside opening viewport");
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); provider.Pending.SetResult(Provider.Result()); await operation;
            provider.Pending = null; await Flush(window);
            // A new explicit generation restores the same complete fixture after cancellation.
            if (!vm.HasInsight) { await vm.GenerateInsightAsync(); await Flush(window); document = view.Document; }
            CheckTypography(view, points, scale);
            await Capture("collapsed");
            var heading = view.Document.Blocks.OfType<Paragraph>().Single(p => ReadingAnchors.GetKey(p) == "meaning:summary-heading");
            var preview = view.Document.Blocks.OfType<Paragraph>().SingleOrDefault(p => ReadingAnchors.GetKey(p) == "meaning:summary-text");
            var toggle = view.Document.Blocks.OfType<Paragraph>().SingleOrDefault(p => ReadingAnchors.GetKey(p) == "meaning:summary-toggle");
            Check(toggle is not null, "Long summary needs a reveal link");
            if (preview is not null)
            {
                var bottom = toggle!.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom;
                var top = heading.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top;
                Check(bottom - top <= view.ViewportHeight * .35 + 1, $"Summary group exceeds budget: {bottom - top} / {view.ViewportHeight * .35}");
            }
            Click(toggle!.Inlines.OfType<Hyperlink>().Single()); await Flush(window);
            Check(Text(view).Contains(summary), "Expansion lost full stored summary"); await Capture("summary-expanded");
            Click(toggle.Inlines.OfType<Hyperlink>().Single()); await Flush(window);
            foreach (var id in new[] { "MeaningMetaphors", "MeaningAlternatives" })
            {
                var link = Links(view).Single(l => AutomationProperties.GetAutomationId(l) == id);
                Click(link); await Flush(window);
            }
            CheckTypography(view, points, scale);
            foreach (var (label, text) in new[] { ("themes", "Cambio"), ("metaphors", "La luz puede sugerir"), ("alternatives", "Otra lectura posible"), ("warnings", "No es una explicación definitiva") })
            {
                var p = Paragraphs(view.Document.Blocks).First(p => ReadingDocument.GetTextRole(p) != ReadingTextRole.Secondary && new TextRange(p.ContentStart, p.ContentEnd).Text.Contains(text));
                p.BringIntoView(); await Flush(window); await Capture(label);
            }
            var target = Paragraphs(view.Document.Blocks).First(p => new TextRange(p.ContentStart, p.ContentEnd).Text.StartsWith("La luz puede sugerir"));
            target.BringIntoView(); await Flush(window);
            window.Activate(); view.Focus(); await Flush(window);
            var start = target.ContentStart.GetNextInsertionPosition(LogicalDirection.Forward)!;
            view.Selection.Select(start, start.GetPositionAtOffset(24)!); await Flush(window); var selection = view.Selection.Text;
            Check(selection.Length > 0 && view.IsReadOnly, "No native read-only selection");
            view.Copy(); Check(Clipboard.GetText() == selection, "Native Copy did not copy selection");
            view.RememberPosition(); var saved = view.Bookmark!;
            tabs.SelectedIndex = 0; await Flush(window);
            UiTypography.Apply(Application.Current.Resources, scale == 1 ? 1.57 : 1);
            window.ReadingFontSize = points == 24 ? 12 : 24; window.Width = size.Width == 360 ? 860 : 360;
            await Flush(window); tabs.SelectedIndex = 1; await Flush(window);
            Check(view.Selection.Text == selection, "Hidden meaning tab lost selection on reflow"); Visible(view, saved);
            CheckTypography(view, window.ReadingFontSize, scale == 1 ? 1.57 : 1);
            window.Width = size.Width; window.ReadingFontSize = points; UiTypography.Apply(Application.Current.Resources, scale); await Flush(window);
            Check(ReferenceEquals(document, view.Document) && view.Selection.Text == selection, "Typography replaced document or selection"); Visible(view, saved);
            await Capture("anchor-restored");
            Check(vm.Insight!.Summary == summary, "Presentation changed stored summary");
            report.Add($"{name}: content={view.FontSize}; heading={Math.Max(view.UiControlSize, .9 * view.FontSize)}; navigation={Math.Max(view.UiControlSize, .85 * view.FontSize)}; secondary={view.UiSecondarySize}; viewport={view.ViewportWidth}x{view.ViewportHeight}; preview-present={preview is not null}; anchor={saved.View.Key}; selection/copy/reflow=PASS");
            async Task Capture(string state) { await Flush(window); Render(window, name + "-" + state); }
        }
        finally { window.Close(); await vm.StopAsync(); }
    }
    static void CheckTypography(ReadingDocument view, double points, double scale)
    {
        Check(Math.Abs(view.FontSize - points * 4 / 3) < .001, "Reading size converted twice or multiplied by UI scale");
        foreach (var p in Paragraphs(view.Document.Blocks))
        {
            var role = ReadingDocument.GetTextRole(p);
            var expected = role switch
            {
                ReadingTextRole.Heading => Math.Max(14 * scale, .9 * points * 4 / 3),
                ReadingTextRole.Navigation => Math.Max(14 * scale, .85 * points * 4 / 3),
                ReadingTextRole.Secondary => 12 * scale, _ => points * 4 / 3
            };
            Check(Math.Abs(p.FontSize - expected) < .001 && Math.Abs(p.LineHeight - 1.5 * expected) < .001, $"Wrong effective typography for {role}: {p.FontSize}/{p.LineHeight}, expected {expected}");
        }
    }
    static void Visible(ReadingDocument view, ReadingBookmark saved)
    {
        var pointer = ReadingAnchors.ResolveView(saved, ReadingAnchors.Index(view.Document));
        Check(pointer is { HasValidLayout: true }, "Reading anchor no longer resolves");
        var bounds = pointer!.GetCharacterRect(LogicalDirection.Forward);
        Check(bounds.Bottom > 0 && bounds.Top < view.ViewportHeight, "Logical section lost after reflow");
    }
    static void Render(Window window, string name)
    {
        report.Add($"capture={name}; window={window.ActualWidth}x{window.ActualHeight}; observedDpi={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; uiFactor={(double)window.FindResource("UiControlFont")/14}; renderDpi=96; readingPoints={(window as MainWindow)?.ReadingFontSize}");
        var content = (FrameworkElement)window.Content; if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
    { foreach (var b in blocks) { if (b is Paragraph p) yield return p; if (b is Section s) foreach (var p2 in Paragraphs(s.Blocks)) yield return p2; if (b is System.Windows.Documents.List l) foreach (var i in l.ListItems) foreach (var p3 in Paragraphs(i.Blocks)) yield return p3; } }
    static IEnumerable<Hyperlink> Links(ReadingDocument view) => view.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>());
    static void Click(Hyperlink link) => link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
    static string Text(ReadingDocument view) => new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
    sealed class Store : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings(true, "fixture-model", 100));
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(0);
    }
    sealed class Provider : IInsightProvider, ILyricsProvider
    {
        public TaskCompletionSource<SongInsight>? Pending;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken t) => throw new InvalidOperationException("No lyrics request expected");
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken t) => Pending?.Task ?? Task.FromResult(Result());
        public static SongInsight Result() => new(1, "es", [], summary, ["Cambio", "Esperanza", "Una oportunidad incierta"],
            [new("luz", string.Join(' ', Enumerable.Repeat("La luz puede sugerir una oportunidad de cambio que la voz narrativa aún contempla con incertidumbre.", 5))), new("ventana", "La ventana podría representar el límite entre una experiencia conocida y una posibilidad nueva.")],
            [string.Join(' ', Enumerable.Repeat("Otra lectura posible relaciona la imagen con una pausa para observar el entorno sin decidir todavía.", 5)), "También podría ser una descripción cotidiana sin simbolismo deliberado."],
            ["No es una explicación definitiva de la intención del artista.", "Las imágenes admiten interpretaciones distintas según el contexto."]);
    }
}
