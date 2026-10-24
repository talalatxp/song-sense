using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;

// Synthetic/offline fixture, compatible with the UX01 snapshot as well as UX02.
public static class Preview
{
    static string output = "";
    static bool interactive;
    static readonly List<string> report = [];
    static readonly Dictionary<string, FrameworkElement> samples = [];
    static RenderTargetBitmap? lastBitmap;
    static FrameworkElement? lastContent;
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        interactive = args.Contains("--interactive");
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First();
            app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(
                resources.Elements().First().Name.LocalName == "ResourceDictionary" ? resources.Elements().Single().ToString() :
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                string.Concat(resources.Nodes().Select(n => n.ToString())) + "</ResourceDictionary>");
            if (args.Contains("--system-palette"))
                typeof(MainWindow).Assembly.GetType("SongSense.App.ThemeResources")?.GetMethod("Apply")?.Invoke(null, [app.Resources, true]);
            report.Add($"SystemParameters.HighContrast={SystemParameters.HighContrast}; system-palette fixture={args.Contains("--system-palette")}; not an OS settings change.");
            app.Dispatcher.InvokeAsync(async () =>
            {
                try { await Run(app); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                app.Shutdown();
            });
            app.Run();
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
    }
    static async Task Run(Application app)
    {
        var gallery = Gallery(); gallery.Show();
        await Capture(gallery, "gallery-top");
        var primary = (Button)samples["primary"];
        primary.Focus(); await Capture(gallery, "gallery-focus");
        Simulate(primary, UIElement.IsMouseOverProperty, true);
        await Capture(gallery, "gallery-hover");
        Simulate(primary, ButtonBase.IsPressedProperty, true);
        await Capture(gallery, "gallery-pressed");
        Simulate(primary, ButtonBase.IsPressedProperty, false);
        Simulate(primary, UIElement.IsMouseOverProperty, false);
        var galleryScroll = (ScrollViewer)gallery.Content; galleryScroll.ScrollToEnd();
        await Capture(gallery, "gallery-bottom");
        var combo = (ComboBox)samples["combo"]; combo.IsDropDownOpen = true;
        await Capture(gallery, "gallery-selector"); combo.IsDropDownOpen = false;
        if (interactive)
        {
            gallery.Title = "UX02 · Galería sintética · teclado";
            var done = new TaskCompletionSource(); gallery.Closed += (_, _) => done.TrySetResult();
            await done.Task;
        }
        else gallery.Hide();
        var store = new Store(); var provider = new Provider();
        var vm = new MainViewModel(store, provider, insightProvider: provider);
        await vm.InitializeAsync();
        var track = new CurrentTrack(1, "Ventana de prueba", "Artista sintético", "Álbum propio", TimeSpan.FromMinutes(3), PlaybackState.Playing);
        vm.ApplyDetection(new(ApplicationState.Playing, track, null)); await vm.SearchLyricsAsync();
        var main = new MainWindow { DataContext = vm, ShowActivated = false };
        main.Show(); await Sizes(main, "main");
        ((Expander)main.FindName("OptionsPanel")).IsExpanded = true;
        await Capture(main, "main-options-minimum");
        main.Hide();
        using var http = new HttpClient(new NoNetwork());
        var connection = new ChatGptConnection(new ChatGptCredentialStore(Path.Combine(output, "unused-account.dpapi")), new ChatGptOAuthClient(http));
        var accountVm = new ConnectionViewModel(connection, CancellationToken.None);
        var account = new ConnectionWindow(accountVm) { ShowActivated = false };
        account.Show(); await Sizes(account, "account");
        var consent = Find<CheckBox>(account).Single();
        var scroller = (ScrollViewer)account.Content;
        foreach (var enabled in new[] { true, false })
        foreach (var selected in new[] { false, true })
        {
            consent.IsEnabled = enabled; consent.IsChecked = selected;
            account.Width = 640; account.Height = 800;
            scroller.ScrollToVerticalOffset(0); await Idle(account); consent.BringIntoView();
            await Capture(account, $"consent-{enabled}-{selected}");
            MeasureText(consent, $"consent enabled={enabled} checked={selected}");
        }
        account.Hide();
        var api = new AiSettingsWindow(new AiSettingsViewModel(new AiConfigurationService(store,
            new DpapiSecretStore(Path.Combine(output, "unused-api.dpapi")), store, new OpenAiConnectionProbe(http)), CancellationToken.None)) { ShowActivated = false };
        api.Show(); await Sizes(api, "api"); api.Hide();
        var editor = new LyricsEditorWindow(new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!)) { ShowActivated = false };
        editor.Show(); await Sizes(editor, "editor");
        Find<TabControl>(editor).Single().SelectedIndex = 1;
        await Capture(editor, "editor-manual-minimum"); editor.Hide();
        await vm.StopAsync();
        foreach (var name in new[] { "Ink", "Muted", "Outline", "ControlBorder", "Gold", "Information", "Success", "Warning", "Error", "Progress", "Focus" })
            MeasurePalette(app, name);
        report.Add("App SHA256=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))));
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), report);
    }
    static void Simulate(DependencyObject element, DependencyProperty property, bool value)
    {
        var owner = property.OwnerType;
        var key = owner.GetField(property.Name + "PropertyKey", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as DependencyPropertyKey;
        if (key is not null) element.SetValue(key, value);
        else report.Add("Cannot force fixture state " + property.Name);
    }
    static Window Gallery()
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        void Title(string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 20, Margin = new Thickness(0, 12, 0, 8) });
        void Add(FrameworkElement item, string? name = null)
        {
            item.Margin = new Thickness(4, 6, 8, 8); panel.Children.Add(item);
            if (name is not null) { samples[name] = item; AutomationProperties.SetName(item, name); }
        }
        Title("UX02 · Galería local · datos sintéticos");
        var row = new WrapPanel();
        foreach (var pair in new[] { (true, false), (true, true), (false, false), (false, true) })
            row.Children.Add(new CheckBox { Content = new TextBlock { Text = pair.Item1 ? "Consentimiento habilitado" : "Consentimiento deshabilitado", FontSize = 14 }, IsEnabled = pair.Item1, IsChecked = pair.Item2, Margin = new Thickness(6), Width = 300 });
        Add(row);
        var primary = new Button { Content = "Acción principal", HorizontalAlignment = HorizontalAlignment.Left };
        primary.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton"); Add(primary, "primary");
        Add(new Button { Content = "Acción secundaria", HorizontalAlignment = HorizontalAlignment.Left }, "secondary");
        var busy = new Button { Content = "Procesando", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left };
        var ui = typeof(MainWindow).Assembly.GetType("SongSense.App.UiState"); ui?.GetMethod("SetIsBusy")?.Invoke(null, [busy, true]); Add(busy);
        Title("Campos y selección");
        Add(new TextBox { Text = "Texto editable · foco con Tab", MaxWidth = 500, HorizontalAlignment = HorizontalAlignment.Left }, "input");
        Add(new PasswordBox { MaxWidth = 500, MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left }, "password");
        var invalid = new TextBox { MaxWidth = 500, MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left };
        invalid.SetBinding(TextBox.TextProperty, new Binding(nameof(Sample.Value)) { Source = new Sample(), UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Validation.MarkInvalid(invalid.GetBindingExpression(TextBox.TextProperty)!, new ValidationError(new ExceptionValidationRule(), invalid.GetBindingExpression(TextBox.TextProperty)!, "Campo de ejemplo inválido", null));
        Add(invalid, "invalid");
        Add(new ComboBox { ItemsSource = new[] { new Choice("Español"), new Choice("Inglés"), new Choice("Francés") }, DisplayMemberPath = nameof(Choice.Name), SelectedIndex = 0, Width = 300, HorizontalAlignment = HorizontalAlignment.Left }, "combo");
        Add(new ListBox { ItemsSource = new[] { "Primera versión", "Segunda versión", "Tercera versión" }, SelectedIndex = 1, MaxHeight = 150 }, "list");
        Add(new Slider { Minimum = 12, Maximum = 24, Value = 16, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 300, HorizontalAlignment = HorizontalAlignment.Left }, "slider");
        Add(new Expander { Header = "Detalles desplegables", Content = new TextBlock { Text = "Información de ejemplo conservando el contexto.", Margin = new Thickness(8) }, IsExpanded = true }, "expander");
        var menuButton = new Button { Content = "Abrir menú local", HorizontalAlignment = HorizontalAlignment.Left };
        var menu = new ContextMenu(); menu.Items.Add(new MenuItem { Header = "Acción local de ejemplo" });
        menu.Items.Add(new MenuItem { Header = "Opción seleccionada", IsCheckable = true, IsChecked = true });
        var submenu = new MenuItem { Header = "Más opciones" }; submenu.Items.Add(new MenuItem { Header = "Detalle local" }); menu.Items.Add(submenu);
        menu.Items.Add(new MenuItem { Header = "Acción deshabilitada", IsEnabled = false });
        menuButton.ContextMenu = menu; menuButton.Click += (_, _) => { menu.PlacementTarget = menuButton; menu.IsOpen = true; }; Add(menuButton, "menu");
        Title("Mensajes: texto y símbolo, además de color");
        foreach (var kind in new[] { "Information", "Success", "Warning", "Error", "Progress" })
        {
            var type = typeof(MainWindow).Assembly.GetType("SongSense.App.StatusNotice");
            if (type is null) { Add(new TextBlock { Text = kind + " · Mensaje de ejemplo" }); continue; }
            var notice = (Control)Activator.CreateInstance(type)!;
            type.GetProperty("Message")!.SetValue(notice, "Mensaje de ejemplo");
            var kindProperty = type.GetProperty("Kind")!;
            kindProperty.SetValue(notice, Enum.Parse(kindProperty.PropertyType, kind)); Add(notice, kind);
        }
        var window = new Window { Title = "UX02 · Galería sintética", Width = 860, Height = 780, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, ShowActivated = interactive };
        window.SetResourceReference(Control.BackgroundProperty, "Canvas"); window.SetResourceReference(Control.ForegroundProperty, "Ink");
        return window;
    }
    static async Task Sizes(Window window, string name)
    {
        await Capture(window, name + "-normal");
        window.Width = 360; window.Height = 480; await Capture(window, name + "-minimum");
        if (window.Content is ScrollViewer scroll) { scroll.ScrollToEnd(); await Capture(window, name + "-minimum-footer"); }
    }
    static Task Idle(Window window) => window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle).Task;
    static async Task Capture(Window window, string name)
    {
        await Idle(window);
        var content = (FrameworkElement)window.Content;
        // Include the content presenter so a root Grid's margin is part of the
        // client capture instead of clipping its right/bottom edges.
        if (VisualTreeHelper.GetParent(content) is FrameworkElement presenter) content = presenter;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content);
        lastBitmap = bitmap; lastContent = content;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
        report.Add($"{name}: {window.Width}x{window.Height} DIP; client {content.ActualWidth}x{content.ActualHeight}; Windows {VisualTreeHelper.GetDpi(window).PixelsPerInchX} DPI; render 96 DPI; Segoe UI.");
        if (name.StartsWith("gallery")) foreach (var item in samples) MeasureText(item.Value, name + "/" + item.Key);
    }
    static void MeasureText(FrameworkElement sample, string label)
    {
        var text = sample is TextBlock block ? block : Find<TextBlock>(sample).FirstOrDefault(t => t.ActualWidth > 0 && t.ActualHeight > 0);
        if (text?.Foreground is not SolidColorBrush foreground || lastBitmap is null || lastContent is null || !lastContent.IsAncestorOf(text)) return;
        var bounds = text.TransformToAncestor(lastContent).TransformBounds(new Rect(text.RenderSize));
        var x = Math.Max(0, (int)bounds.X); var y = Math.Max(0, (int)bounds.Y);
        var width = Math.Min(lastBitmap.PixelWidth - x, (int)bounds.Width); var height = Math.Min(lastBitmap.PixelHeight - y, (int)bounds.Height);
        if (width <= 0 || height <= 0 || bounds.X + bounds.Width < 0 || bounds.Y + bounds.Height < 0) return;
        var pixels = new byte[width * height * 4]; lastBitmap.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
        var matched = 0;
        var colors = new Dictionary<uint, int>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] == foreground.Color.B && pixels[i + 1] == foreground.Color.G && pixels[i + 2] == foreground.Color.R && pixels[i + 3] == 255) matched++;
            var pixel = BitConverter.ToUInt32(pixels, i); colors[pixel] = colors.GetValueOrDefault(pixel) + 1;
        }
        var common = colors.MaxBy(pair => pair.Value).Key;
        var backdrop = Color.FromArgb((byte)(common >> 24), (byte)(common >> 16), (byte)(common >> 8), (byte)common);
        DependencyObject? ancestor = text; var opacity = 1.0;
        while (ancestor is not null)
        {
            if (ancestor is UIElement ui) opacity *= ui.Opacity;
            ancestor = VisualTreeHelper.GetParent(ancestor);
        }
        report.Add($"PIXELS {label}: foreground={foreground.Color}; solid glyph pixels={matched}; ancestor opacity={opacity:0.00}; rendered dominant background={backdrop}; ratio={Ratio(foreground.Color, backdrop):0.00}:1. Source-foreground ratio applies when solid glyph pixels are present and opacity=1; disabled samples evaluated separately.");
    }
    static void MeasurePalette(Application app, string key)
    {
        if (app.TryFindResource(key) is not SolidColorBrush ink || app.TryFindResource("Surface") is not SolidColorBrush surface) return;
        // Render opaque swatches before measuring, rather than relying only on source hex strings.
        var visual = new DrawingVisual(); using (var draw = visual.RenderOpen()) { draw.DrawRectangle(ink, null, new Rect(0, 0, 10, 10)); draw.DrawRectangle(surface, null, new Rect(10, 0, 10, 10)); }
        var bitmap = new RenderTargetBitmap(20, 10, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var data = new byte[20 * 10 * 4]; bitmap.CopyPixels(data, 80, 0);
        Color Read(int offset) => Color.FromArgb(data[offset + 3], data[offset + 2], data[offset + 1], data[offset]);
        report.Add($"SWATCH {key}/Surface: {Read(0)} / {Read(40)} = {Ratio(Read(0), Read(40)):0.00}:1; opacity {ink.Opacity:0.00}/{surface.Opacity:0.00}.");
    }
    static double Ratio(Color a, Color b)
    {
        static double L(Color c) { static double Channel(byte value) { var x = value / 255.0; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); } return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B); }
        var x = L(a); var y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match;
            foreach (var descendant in Find<T>(child)) yield return descendant;
        }
    }
    public sealed record Choice(string Name);
    public sealed class Sample { public string Value { get; set; } = "Valor inválido de ejemplo"; }
    sealed class Store : ISettingsStore, IRequestBudget
    {
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken token = default) => Task.FromResult(new AppSettings(false, "fixture-model"));
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken token = default) => Task.FromResult(3);
        public Task<bool> TryReserveRequestAsync(DateOnly date, CancellationToken token = default) => throw new InvalidOperationException("No inference in UX02 fixture");
    }
    sealed class NoNetwork : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("No network in UX02 fixture"); }
    sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) => throw new InvalidOperationException("No inference in UX02 fixture");
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => Task.FromResult(new LyricsSearchResult(track.Revision, LyricsSearchKind.Candidates, null,
            [new(1, "Ventana original", "Artista sintético", "Álbum propio", TimeSpan.FromMinutes(3), false, "Abro una ventana\nY dejo entrar la luz", null), new(2, "Ventana alternativa", "Artista sintético", "Versión acústica", TimeSpan.FromMinutes(4), false, "Busco otra puerta\nY encuentro mi voz", null)]));
    }
}
