#if CURRENT_LIFETIME
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SongSense.App;
using Forms = System.Windows.Forms;

public static partial class Preview
{
    // Isolated synthetic session. Only the application under test is controlled here.
    static async Task Acceptance()
    {
        using var theme = ThemeResources.FollowSystem(Application.Current);
        var provider = new Provider();
        var model = new MainViewModel(new Store(), provider, insightProvider: provider);
        await model.InitializeAsync(); model.ApplyDetection(Track(1));
        model.SaveManual(model.BeginLyricsEdit()!, Lyrics); await model.GenerateInsightAsync();
        var window = new MainWindow { DataContext = model, Title = "Song Sense · Aceptación local aislada" };
        window.InitializePreferences(new SongSense.Infrastructure.WindowPreferenceStore(Path.Combine(output, "acceptance-window.json")));
        var ended = new TaskCompletionSource(); window.Closed += (_, _) => ended.TrySetResult();
        using var tray = new DesktopTray(model, window.OpenFromTray, window.RequestExit);
        window.ConfigureCloseBehavior(model.StopAsync, tray.ShowFirstHideHint);
        window.Show();
        int sequence = 0;
        var stack = new StackPanel { Margin = new Thickness(12) };
        var status = new TextBlock { Text = "Datos propios sintéticos; sin red, cuentas ni credenciales.", TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(status);
        void Record(string action)
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var screen = Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle);
            var state = new { action, time = DateTimeOffset.Now, sequence = ++sequence,
                version = BuildIdentity.ProductVersion,
                appSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),
                dpiX = dpi.PixelsPerInchX, dpiY = dpi.PixelsPerInchY, highContrast = SystemParameters.HighContrast,
                width = window.ActualWidth, height = window.ActualHeight, font = window.ReadingFontSize,
                left = window.Left, top = window.Top, visible = window.IsVisible, tracking = model.AutomaticUpdatesEnabled,
                screen = screen.DeviceName, area = new { screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height },
                screens = Forms.Screen.AllScreens.Select(s => new { name = s.DeviceName, s.Primary, s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height }).ToArray() };
            File.AppendAllText(Path.Combine(output, "observations.jsonl"), JsonSerializer.Serialize(state) + Environment.NewLine);
            status.Text = $"{action} · {dpi.PixelsPerInchX} DPI · contraste {SystemParameters.HighContrast} · seguimiento {model.AutomaticUpdatesEnabled}";
        }
        async Task CaptureActual(string name)
        {
            await Flush(window); var content = (FrameworkElement)window.Content;
            if(VisualTreeHelper.GetParent(content) is FrameworkElement parent) content=parent;
            var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var background=new DrawingVisual(); using(var draw=background.RenderOpen()) draw.DrawRectangle(window.Background,null,new Rect(content.RenderSize));
            bitmap.Render(background); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"{sequence + 1:D3}-{name}.png")); encoder.Save(file); Record(name);
        }
        void Button(string title, Func<Task> action)
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(6) };
            button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch(Exception e) { File.AppendAllText(Path.Combine(output,"error.txt"), e.ToString()); status.Text = "Error en la prueba; consultar error.txt"; } finally { button.IsEnabled = true; } };
            stack.Children.Add(button);
        }
        Button("Registrar matriz a la escala real actual", async () =>
        {
            window.OpenFromTray(); window.WindowState = WindowState.Normal;
            ((TabControl)window.FindName("ReadingTabs")).SelectedIndex = 0;
            foreach(var size in new[]{12,16,20,24}) foreach(var (w,h) in new[]{(860,780),(600,700),(360,480)})
            { window.Width=w; window.Height=h; window.ReadingFontSize=size; await CaptureActual($"reading-{w}-{size}"); }
            ((TabControl)window.FindName("ReadingTabs")).SelectedIndex=1;
            await CaptureActual("meaning-min"); window.Width=860; window.Height=780; window.ReadingFontSize=16;
            await CaptureActual("meaning-normal");
        });
        for(int i=0;i<Forms.Screen.AllScreens.Length;i++)
        {
            var target = Forms.Screen.AllScreens[i];
            Button($"Mover sesión al monitor {i+1}", async () =>
            {
                window.OpenFromTray(); window.WindowState=WindowState.Normal;
                var dpi=VisualTreeHelper.GetDpi(window);
                window.Left=(target.WorkingArea.Left+60)/dpi.DpiScaleX; window.Top=(target.WorkingArea.Top+60)/dpi.DpiScaleY;
                await Flush(window); window.RecoverPosition(); await CaptureActual("monitor-move");
            });
        }
        Button("Registrar estado y captura", () => CaptureActual("observed"));
        Button("Ajustar ventana al área útil", async () => { window.RecoverPosition(); await CaptureActual("fit-workarea"); });
        Button("Registrar maximizada", async () => { window.WindowState=WindowState.Maximized; await CaptureActual("maximized"); window.WindowState=WindowState.Normal; });
        Button("Mostrar menú real del componente de bandeja", () =>
        {
            // This exercises the production menu, not Windows' notification-area entry point.
            var icon=(Forms.NotifyIcon)typeof(DesktopTray).GetField("icon",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(tray)!;
            icon.ContextMenuStrip!.Show(Forms.Cursor.Position); Record("real-component-menu-open"); return Task.CompletedTask;
        });
        Button("Abrir sesión", () => { window.OpenFromTray(); Record("open"); return Task.CompletedTask; });
        Button("Salir de la sesión de prueba", () => { window.RequestExit(); return Task.CompletedTask; });
        var controls = new Window { Title="Control de aceptación · Song Sense", Width=480, Height=470, Content=new ScrollViewer { Content=stack }, WindowStartupLocation=WindowStartupLocation.CenterScreen };
        controls.Show(); Record("started"); await ended.Task; controls.Close();
        File.AppendAllText(Path.Combine(output,"observations.jsonl"), JsonSerializer.Serialize(new { action="session-exited", time=DateTimeOffset.Now })+Environment.NewLine);
    }
}
#endif
