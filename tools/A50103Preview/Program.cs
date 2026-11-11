using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SongSense.App;
using Path = System.IO.Path;

public static class Preview
{
    static string root = "", output = ""; static bool before, highContrast;
    static readonly List<string> report = [];
    [STAThread] public static void Main(string[] args)
    {
        root = args[0]; output = args[1]; before = args.Contains("--before"); Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = new ResourceDictionary();
        if (before)
        {
            foreach (var name in new[] { "Dark", "Controls" })
                resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(root, "artifacts/uiux-visual-fixes/baseline", name + ".xaml")).Replace("clr-namespace:SongSense.App", "clr-namespace:SongSense.App;assembly=SongSense")));
        }
        else
        {
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = ThemeResources.PaletteUri(false) });
            resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SongSense;component/Themes/Controls.xaml") });
        }
        app.Resources = resources;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await Run(); report.Add("PASS shared foundations, expansion, notices and keyboard contracts."); }
            catch (Exception ex) { report.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(output, "manifest.txt"), report); app.Shutdown(Environment.ExitCode); }
        }); app.Run();
    }
    static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    static Task Flush(Window w) => w.Dispatcher.InvokeAsync(w.UpdateLayout, DispatcherPriority.ApplicationIdle).Task;
    static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 8) };
    static Button Button(string label, string style = "QuietButton") => new() { HorizontalAlignment = HorizontalAlignment.Left, Content = label, Style = (Style)(Application.Current.TryFindResource(style) ?? Application.Current.FindResource("QuietButton")), MaxWidth = 240 };
    static Panel Actions(bool mixed = false)
    {
        if (before) return new WrapPanel();
        var row = new ActionPanel(); row.SetResourceReference(ActionPanel.GapProperty, mixed ? "Space12" : "Space8"); return row;
    }
    static (Window Window, StackPanel Page, ScrollViewer Scroll) Page(double width, string label)
    {
        var page = new StackPanel { Margin = new Thickness(width < 600 ? 12 : 24) };
        page.Children.Add(new TextBlock { Text = "Song Sense · " + label, FontSize = 22, Foreground = (Brush)Application.Current.FindResource("Gold"), Margin = new Thickness(0, 0, 0, 24) });
        var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var window = new Window { Width = width, Height = 900, Content = scroll, Title = "A5 · " + label };
        UiLayout.SetIsCompact(window, width < 600); return (window, page, scroll);
    }
    static async Task Run()
    {
        report.Add($"Mode={(before ? "before: reconstructed saved A4 resources on gallery host" : "after")}; Version={BuildIdentity.ProductVersion}; AppSha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}; render=96 DPI; resource factors are not Windows DPI acceptance.");
        foreach (var scale in new[] { 1d, 1.57d, 2d })
        foreach (var width in new[] { 420d, 960d })
        {
            UiTypography.Apply(Application.Current.Resources, scale);
            await Controls(width, scale); await Expanders(width, scale); await Pair(width, scale);
            foreach (NoticeKind kind in Enum.GetValues<NoticeKind>()) await Notices(width, scale, kind);
        }
        if (!before)
        {
            highContrast = true; UiTypography.Apply(Application.Current.Resources, 1);
            ThemeResources.Apply(Application.Current.Resources, true);
            await Controls(960, 1); await Notices(960, 1, NoticeKind.Warning);
            ThemeResources.Apply(Application.Current.Resources, false);
        }
    }
    static async Task Controls(double width, double scale)
    {
        var (w, page, scroll) = Page(width, "A5-01 · Controles");
        var actions = Actions(true); var primary = Button("Continuar", "PrimaryButton"); var quiet = Button("Volver"); var danger = Button("Eliminar contenido guardado", "DangerButton"); var icon = Button("⋯", "IconButton");
        foreach (var b in new[] { primary, quiet, danger, icon }) actions.Children.Add(b);
        page.Children.Add(Label("Acciones independientes · la acción de peligro conserva su verbo")); page.Children.Add(actions);
        var longActions = Actions(); foreach (var text in new[] { "Guardar esta configuración sin perder el borrador", "Consultar de nuevo los modelos disponibles", "Cancelar la operación en curso" }) longActions.Children.Add(Button(text));
        page.Children.Add(Label("Etiquetas largas · filas con envoltura")); page.Children.Add(longActions);
        var field = new TextBox { Text = "Texto de prueba seleccionable", TextWrapping = TextWrapping.Wrap }; page.Children.Add(Label("Campo editable")); page.Children.Add(field);
        var combo = new ComboBox { ItemsSource = new[] { "Español", "Portugués · destino de traducción y explicación" }, SelectedIndex = 1 }; page.Children.Add(Label("Selector")); page.Children.Add(combo);
        var tabs = new TabControl(); foreach (var text in new[] { "Letra", "Significado", "Nombre de pestaña más largo" }) tabs.Items.Add(new TabItem { Header = text, Content = Label("Contenido de la pestaña; la selección conserva su patrón nativo.") });
        page.Children.Add(Label("Pestañas · selección diferenciada")); page.Children.Add(tabs);
        w.Show(); await Flush(w);
        var origins = new Dictionary<Button, Point>();
        foreach (var state in new[] { "rest", "hover", "focus", "pressed", "disabled" })
        {
            foreach (var b in new[] { primary, quiet, danger, icon }) { b.IsEnabled = state != "disabled"; MouseState(b, state == "hover"); }
            field.IsEnabled = combo.IsEnabled = tabs.IsEnabled = state != "disabled";
            MouseState(field, state == "hover"); MouseState(combo, state == "hover");
            foreach (TabItem tab in tabs.Items) MouseState(tab, state == "hover");
            if (state is "focus" or "pressed") { w.Activate(); primary.Focus(); }
            if (state == "pressed") primary.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(primary), 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            await Flush(w);
            if (!before)
            {
                var buttons = actions.Children.OfType<Button>().ToArray();
                for (int i = 1; i < buttons.Length; i++)
                {
                    var a = buttons[i - 1].TransformToAncestor(actions).TransformBounds(new Rect(buttons[i - 1].RenderSize)); var b = buttons[i].TransformToAncestor(actions).TransformBounds(new Rect(buttons[i].RenderSize));
                    Check(b.Left >= a.Right + 7.9 || b.Top >= a.Bottom + 7.9, "Independent action borders touch after wrap");
                }
                Check(icon.ActualWidth >= icon.ActualHeight - 1, "Icon is not square");
                foreach (var button in new[] { primary, quiet, danger, icon })
                {
                    var cp = Descendants(button).OfType<ContentPresenter>().First(); var origin = cp.TranslatePoint(new Point(), button);
                    if (state == "rest") origins[button] = origin;
                    else Check((origin - origins[button]).Length < .1, "Button state moves its text");
                }
            }
            await Capture(w, scroll, $"controls-{width}-{scale:0.00}-{state}");
            if (state == "focus")
            {
                foreach (var (control, role) in new (Control, string)[] { (field, "field"), (combo, "selector"), ((TabItem)tabs.Items[0], "tab") })
                {
                    control.Focus(); await Flush(w); await Capture(w, scroll, $"controls-{width}-{scale:0.00}-focus-{role}");
                }
            }
            if (state == "pressed") primary.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(primary), 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
        }
        w.Close();
    }
    static void MouseState(UIElement element, bool value)
    {
        var key = (DependencyPropertyKey?)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        Check(key is not null, "Hover fixture cannot locate WPF state key"); element.SetValue(key!, value);
    }
    static async Task Expanders(double width, double scale)
    {
        foreach (var group in new[] { new[] { "Estado y consumo", "Datos locales", "Avanzado" }, new[] { "Configuración", "Actividad", "Datos y almacenamiento" } })
        {
            var (w, page, scroll) = Page(width, "A5-02 · " + group[0]); var sections = new List<Expander>();
            foreach (var label in group)
            {
                var body = new StackPanel(); body.Children.Add(Label("Contenido de la sección. Se conserva todo el texto y su agrupación."));
                var nested = new Expander { Header = "Avanzado · opciones adicionales", Content = Button("Revisar configuración") }; body.Children.Add(nested);
                var e = new Expander { Header = label + (label == group[1] ? " · encabezado largo que debe envolver y conservar el centro de la flecha en su primera línea" : ""), Content = body, Margin = new Thickness(0, 0, 0, 16) };
                page.Children.Add(e); sections.Add(e);
            }
            page.Children.Add(Label("Siguiente grupo · separado del contenido y del foco anterior")); w.Show(); await Flush(w);
            foreach (var expanded in new[] { false, true })
            {
                foreach (var e in sections) { e.IsExpanded = expanded; ((Expander)((StackPanel)e.Content).Children[1]).IsExpanded = expanded; }
                await Flush(w);
                var header = Descendants(sections[0]).OfType<ToggleButton>().First(); w.Activate(); header.Focus();
                if (!before)
                {
                    var path = Descendants(header).OfType<System.Windows.Shapes.Path>().Single();
                    Check(path.RenderTransform is RotateTransform r && Math.Abs(r.Angle - (expanded ? 90 : 0)) < .1, "Chevron must rotate the same geometry");
                    var origin = path.TranslatePoint(new Point(), header); report.Add($"chevron {width}/{scale}/{expanded}: {origin}; box={path.ActualWidth}x{path.ActualHeight}");
                    header.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(header), 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
                    header.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(header), 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
                    Check(sections[0].IsExpanded != expanded, "Space did not toggle expansion");
                    header.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(header), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
                    Check(sections[0].IsExpanded == expanded, "Enter did not restore expansion");
                    Check(header.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "Tab cannot leave expander"); header.Focus();
                }
                await Capture(w, scroll, $"expander-{group[0]}-{width}-{scale:0.00}-{expanded}");
            }
            w.Close();
        }
    }
    static async Task Pair(double width, double scale)
    {
        var (w, page, scroll) = Page(width, "A5-03 · Dos causas distintas");
        page.Children.Add(new StatusNotice { Kind = NoticeKind.Information, Message = "El borrador permanece en este dispositivo.", Summary = "El borrador permanece en este dispositivo." });
        page.Children.Add(new StatusNotice { Kind = NoticeKind.Warning, Summary = "El idioma aún no se ha guardado.", Message = "El idioma aún no se ha guardado. Conservas la selección y puedes reintentar.", Explanation = "La siguiente acción requiere que termine el guardado." });
        page.Children.Add(Label("Siguiente grupo · los dos avisos pertenecen a causas diferentes."));
        w.Show(); await Flush(w); await Capture(w, scroll, $"notice-pair-{width}-{scale:0.00}"); w.Close();
    }
    static async Task Notices(double width, double scale, NoticeKind kind)
    {
        var (w, page, scroll) = Page(width, "A5-03 · " + kind); var notices = new List<StatusNotice>();
        foreach (var lines in new[] { 1, 2, 4 })
        {
            var summary = string.Join("\n", Enumerable.Repeat("Conservas el borrador para revisarlo.", lines));
            var message = summary + "\nDetalle completo: los datos permanecen en este dispositivo. Puedes seleccionar y copiar esta explicación sin modificar el contenido.";
            var notice = new StatusNotice { Kind = kind, Summary = summary, Message = message };
            if (lines == 2) { notice.Explanation = "La acción está disponible sin salir de este aviso."; if (!before) notice.ActionContent = Button("Revisar el borrador"); }
            notices.Add(notice); page.Children.Add(notice);
        }
        page.Children.Add(Label("Siguiente grupo · los avisos mantienen separación incluso al cerrar su detalle"));
        w.Show(); await Flush(w);
        foreach (var expanded in new[] { false, true })
        {
            foreach (var n in notices)
            {
                var detail = (Expander)n.Template.FindName("NoticeDetail", n); detail.IsExpanded = expanded;
                Check(new FrameworkElementAutomationPeer(n).GetName() is not null, "Notice accessible");
                if (!before)
                {
                    Check(!((System.Windows.Shapes.Path)n.Template.FindName("Symbol", n)).Focusable, "Icon must not get keyboard focus");
                    Check(UIElementAutomationPeer.CreatePeerForElement(n)!.GetName().Contains(n.Message), "Full accessible message lost");
                }
            }
            await Flush(w);
            if (!before && expanded && width == 420 && scale == 1)
            {
                var box = Descendants(notices[0]).OfType<TextBox>().Single(); box.SelectAll();
                Check(box.SelectedText == notices[0].Message && box.IsReadOnly, "Selectable notice detail lost text");
                Check(ApplicationCommands.Copy.CanExecute(null, box), "Notice detail lost Copy command");
                report.Add($"{kind}: full read-only detail selection and Copy availability checked; no clipboard writes.");
            }
            await Capture(w, scroll, $"notice-{kind}-{width}-{scale:0.00}-{expanded}");
        }
        w.Close();
    }
    static async Task Capture(Window window, ScrollViewer scroll, string name)
    {
        scroll.ScrollToTop(); await Flush(window); Render(window, name + "-top");
        if (scroll.ScrollableHeight > 1) { scroll.ScrollToBottom(); await Flush(window); Render(window, name + "-bottom"); }
    }
    static void Render(Window window, string name)
    {
        if (highContrast) name = "hc-" + name;
        var content = (FrameworkElement)window.Content; var bmp = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual(); using (var d = bg.RenderOpen()) d.DrawRectangle(window.Background, null, new Rect(content.RenderSize)); bmp.Render(bg); bmp.Render(content);
        Save(bmp, name + ".png");
        if (name.StartsWith("controls") && name.Contains("rest")) Save(new FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0), name + "-gray.png");
        if (name.EndsWith("top")) Save(new CroppedBitmap(bmp, new Int32Rect(0, 60, (int)bmp.Width, Math.Min(280, (int)bmp.Height - 60))), name + "-crop.png");
        report.Add($"capture={name}; window={window.ActualWidth}x{window.ActualHeight}; factor={(double)window.FindResource("UiControlFont")/14}; observedDpi={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; origin={(before ? "reconstructed original resources" : "current shared templates")}");
    }
    static void Save(BitmapSource bitmap, string name) { var e = new PngBitmapEncoder(); e.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, name)); e.Save(stream); }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent) { for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var c=VisualTreeHelper.GetChild(parent,i);yield return c;foreach(var d in Descendants(c))yield return d;} }
}
