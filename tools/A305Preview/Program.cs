using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
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
            try { await Run(); report.Add("PASS A3-05: four actual auxiliary routes and long-content stress; full action bounds/labels, focus, copy, binding, scroll and Back."); }
            catch (Exception ex) { report.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(output, "manifest.txt"), report); app.Shutdown(Environment.ExitCode); }
        }); app.Run();
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static async Task Flush(Window window)
    { for (var i = 0; i < 3; i++) await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); }
    static async Task<Window> NewAuxiliary(HashSet<Window> before)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var window = Application.Current.Windows.Cast<Window>().FirstOrDefault(w => !before.Contains(w));
            if (window is not null) { await Flush(window); return window; }
            if (DateTime.UtcNow > until) throw new TimeoutException("Auxiliary did not open");
            await Task.Delay(10);
        }
    }
    static async Task Run()
    {
        report.Add("Actual menu/button routes, synthetic local data. No HTTP, browser, personal credentials or Windows scale changes. Client WPF renders.");
        report.Add($"Version={BuildIdentity.ProductVersion}; build={BuildIdentity.FileVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}");
        var previousClipboard = Clipboard.GetDataObject();
        using var http = new HttpClient(new NoNetwork());
        var connection = new ChatGptConnection(new ChatGptCredentialStore(Path.Combine(output, "data", Guid.NewGuid() + ".dpapi")), new ChatGptOAuthClient(http));
        await connection.InitializeAsync(CancellationToken.None);
        var vm = new MainViewModel(new Store(), new LyricsProvider()); await vm.InitializeAsync();
        vm.ApplyDetection(new(ApplicationState.Playing, new(1, "Ventana propia", "Artista sintético", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
        vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana\nY dejo entrar la luz");
        var main = new MainWindow { DataContext = vm }; main.Show();
        try
        {
            foreach (var scale in new[] { 1d, 1.57d, 2d })
            foreach (var geometry in new[] { "normal", "minimum" })
            foreach (var family in new[] { "details", "diagnostic", "connection", "about" })
            {
                UiTypography.Apply(Application.Current.Resources, scale);
                ConnectionWindow? account = null;
                if (family == "connection")
                {
                    account = new ConnectionWindow(new(connection, CancellationToken.None, _ => throw new InvalidOperationException("Browser forbidden"))) { Owner = main };
                    account.Show(); await Flush(account);
                }
                var before = Application.Current.Windows.Cast<Window>().ToHashSet();
                if (family == "connection")
                {
                    ((Expander)account!.FindName("ActivityHelpGroup")).IsExpanded = true; await Flush(account);
                    var diagnostic = Find<Button>(account).Single(b => Equals(b.Content, "Diagnóstico")); diagnostic.BringIntoView(); await Flush(account);
                    diagnostic.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else
                {
                    var menu = ((Button)main.FindName("ApplicationMenuButton")).ContextMenu;
                    var label = family switch { "details" => "Detalles de la canción…", "diagnostic" => "Ver diagnóstico", _ => "Acerca de Song Sense" };
                    var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, label));
                    if (family == "about") _ = main.Dispatcher.BeginInvoke(() => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
                    else item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                }
                var window = await NewAuxiliary(before);
                try
                {
                    if (geometry == "minimum") { window.Width = 360; window.Height = 480; }
                    await Exercise(window, $"{family}-{geometry}-scale{scale:0.00}", scale, family is "diagnostic" or "connection");
                }
                finally { if (window.IsVisible) window.Close(); account?.Close(); }
            }
            foreach (var scale in new[] { 1d, 1.57d, 2d })
            foreach (var sanitize in new[] { false, true })
            {
                UiTypography.Apply(Application.Current.Resources, scale);
                var data = new LongData();
                var window = new Window { Width = 360, Height = 480, Content = AuxiliaryContent.Create(data, nameof(LongData.Body), "Diagnóstico de conexión", data.Summary, sanitize, nameof(LongData.Summary)) };
                window.Show();
                try
                {
                    data.Update(); await Flush(window);
                    Check(Find<TextBox>(window).Single().Text == data.Body, "Body binding lost after update");
                    Check(Find<TextBlock>(window).Any(t => t.Text == data.Summary), "Summary binding lost after update");
                    // Resize and update resources while already open, then restore the requested geometry.
                    window.Width = 600; UiTypography.Apply(Application.Current.Resources, scale == 2 ? 1 : 2); await Flush(window);
                    window.Width = 360; UiTypography.Apply(Application.Current.Resources, scale); await Flush(window);
                    await Exercise(window, $"long-{sanitize}-minimum-scale{scale:0.00}", scale, sanitize, true);
                }
                finally { if (window.IsVisible) window.Close(); }
            }
        }
        finally
        {
            main.Close(); await vm.StopAsync();
            if (previousClipboard is null) Clipboard.Clear(); else Clipboard.SetDataObject(previousClipboard, true);
        }
    }
    static async Task Exercise(Window window, string name, double scale, bool sanitize, bool stress = false)
    {
        await Flush(window); window.Activate();
        var copy = Find<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == "AuxiliaryCopy");
        var back = Find<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == "AuxiliaryBack");
        var body = Find<TextBox>(window).Single();
        Bounds();
        Check(body.IsReadOnly && body.ActualHeight > body.FontSize * 1.1, "Body is not readable/selectable");
        Check(Math.Abs(body.FontSize - 14 * scale) < .001, "Body did not follow UI scale");
        var texts = Find<TextBlock>(window).Where(t => t.Parent is StackPanel).ToArray();
        Check(texts.Any(t => Math.Abs(t.FontSize - 20 * scale) < .001) && texts.Any(t => Math.Abs(t.FontSize - 13 * scale) < .001), "Heading/summary did not follow UI scale");
        Check(copy.Focus(), "Copy cannot receive focus");
        Check(copy.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && back.IsKeyboardFocused, "Tab did not reach Back");
        Check(back.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)) && copy.IsKeyboardFocused, "Shift+Tab did not return to Copy");
        back.Focus(); Check(back.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && body.IsKeyboardFocusWithin, "Body focus order changed");
        var header = Find<ScrollViewer>(window).First(s => s.Content is StackPanel);
        Check(body.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && header.IsKeyboardFocused, "Long heading/description cannot receive keyboard focus");
        Check(header.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)) && body.IsKeyboardFocusWithin, "Shift+Tab from heading did not return to body");
        if (stress) { header.PageDown(); await Flush(window); Check(header.VerticalOffset > 0, "Long description cannot scroll"); }
        var selection = body.Text[..Math.Min(24, body.Text.Length)]; body.Select(0, selection.Length); Check(body.SelectedText == selection && selection.Length > 0, "Body selection failed");
        if (!sanitize) { body.Copy(); Check(Clipboard.GetText() == selection, "Native selected Copy failed"); }
        copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Clipboard.GetText() == (sanitize ? AuxiliaryContent.Sanitize(body.Text) : body.Text), "Copy action returned stale or incorrect content");
        if (sanitize && stress)
        {
            var copied = Clipboard.GetText(); Check(!copied.Contains("fixture-secret") && !copied.Contains("fixture@example.invalid") && copied.Contains("[credencial omitida]") && copied.Contains("[cuenta omitida]"), "Sanitized copy retained synthetic sensitive values");
        }
        Render(window, name + "-top");
        body.ScrollToEnd(); await Flush(window); Bounds();
        if (stress) Check(body.VerticalOffset > 0, "Long body cannot scroll");
        Render(window, name + "-end");
        report.Add($"{name}: window={window.ActualWidth}x{window.ActualHeight}; heading={20 * scale}; summary={13 * scale}; actions={copy.FontSize}; body={body.ActualWidth}x{body.ActualHeight}; readonly/selection/focus/copy=PASS; sanitized={sanitize}; scroll={body.VerticalOffset}");
        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush(window);
        Check(!window.IsVisible, "Back did not close the auxiliary");
        void Bounds()
        {
            var root = (FrameworkElement)window.Content;
            var rectangles = new List<Rect>();
            foreach (var button in new[] { copy, back })
            {
                var bounds = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize)); rectangles.Add(bounds);
                Check(button.IsVisible && button.IsEnabled && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1, $"{name}: button outside viewport {bounds}");
                var label = Find<AccessText>(button).Single();
                Check(label.Text == (string)button.Content && label.DesiredSize.Width <= label.ActualWidth + 1 && label.DesiredSize.Height <= label.ActualHeight + 1, "Action label truncated");
                Check(Math.Abs(button.FontSize - 14 * scale) < .001, "Action font reduced or stale");
                report.Add($"{name}: action={button.Content}; bounds={bounds}; label={label.ActualWidth}x{label.ActualHeight}; desired={label.DesiredSize}");
            }
            Check(!rectangles[0].IntersectsWith(rectangles[1]), "Actions overlap");
        }
    }
    static void Render(Window window, string name)
    {
        report.Add($"capture={name}; window={window.ActualWidth}x{window.ActualHeight}; observedDpi={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; uiFactor={(double)window.FindResource("UiControlFont")/14}; renderDpi=96");
        var content = (FrameworkElement)window.Content; if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T typed) yield return typed; foreach (var nested in Find<T>(child)) yield return nested; } }
    sealed class LongData : INotifyPropertyChanged
    {
        public string Body { get; private set; } = "Initial synthetic text";
        public string Summary { get; private set; } = "Initial description";
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Update()
        {
            Body = string.Join('\n', Enumerable.Repeat("Diagnóstico propio: HTTP401. Bearer fixture-secret sk-fixture-secret fixture@example.invalid", 80));
            Summary = string.Join(' ', Enumerable.Repeat("Descripción larga de un estado sintético que puede desplazarse para leerlo completo.", 10));
            PropertyChanged?.Invoke(this, new(nameof(Body))); PropertyChanged?.Invoke(this, new(nameof(Summary)));
        }
    }
    sealed class Store : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings());
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(0);
    }
    sealed class LyricsProvider : ILyricsProvider
    { public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken t) => throw new InvalidOperationException("No lyrics search expected"); }
    sealed class NoNetwork : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) => throw new InvalidOperationException("HTTP forbidden"); }
}
