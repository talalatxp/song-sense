using System.IO;
using System.Net.Http;
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
    [STAThread] public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single().ToString());
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await Run(); report.Add("PASS A3-07 essential order, bounds, focus, external-launch isolation and confirmation expiry."); }
            catch (Exception e) { report.Add(e.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(output, "manifest.txt"), report); app.Shutdown(Environment.ExitCode); }
        }); app.Run();
    }
    static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static async Task Flush(Window w) { for (var i = 0; i < 3; i++) await w.Dispatcher.InvokeAsync(w.UpdateLayout, DispatcherPriority.ApplicationIdle); }
    static async Task Run()
    {
        report.Add($"Synthetic connection, no HTTP/browser. Version={BuildIdentity.ProductVersion}; build={BuildIdentity.FileVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}");
        foreach (var scale in new[] { 1d, 1.57d })
        foreach (var width in new[] { 360, 640 })
        {
            UiTypography.Apply(Application.Current.Resources, scale);
            var store = new ChatGptCredentialStore(Path.Combine(output, "data", Guid.NewGuid() + ".dpapi"));
            await store.SaveAsync(new() { ActiveClientId = "oaiapp_fixture", Registrations = [new() { ClientId = "oaiapp_fixture", Subject = "fixture", Email = "fixture@example.invalid", Model = "fixture-model", AccessToken = "fixture-access", RefreshToken = "fixture-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Scopes = [ChatGptOAuthClient.PlanScope] }] }, CancellationToken.None);
            using var http = new HttpClient(new NoNetwork()); var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
            var vm = new ConnectionViewModel(connection, CancellationToken.None);
            var calls = new List<Uri>(); var window = new ConnectionWindow(vm) { Width = width, Height = width == 360 ? 480 : 800, OpenUsagePage = uri => calls.Add(uri) };
            try
            {
                window.Show(); window.Activate(); await Flush(window);
                var manage = (Button)window.FindName("ManageUsageButton"); var confirm = (CheckBox)window.FindName("CreditsConfirmation"); var enable = (Button)window.FindName("ConfirmEnableButton");
                var warning = (TextBlock)window.FindName("CreditsWarning"); var outer = (ScrollViewer)window.Content;
                Check(vm.Step == 3 && !vm.CreditsDisabledConfirmed && !enable.IsEnabled, "Confirmation is not initially explicit");
                Check(manage.TransformToAncestor(window).Transform(new Point()).Y < confirm.TransformToAncestor(window).Transform(new Point()).Y && confirm.TransformToAncestor(window).Transform(new Point()).Y < enable.TransformToAncestor(window).Transform(new Point()).Y, "Wrong essential order");
                if (scale == 1) foreach (var item in new FrameworkElement[] { warning, manage, confirm, enable })
                {
                    var r = item.TransformToAncestor(outer).TransformBounds(new Rect(item.RenderSize));
                    Check(item.IsVisible && r.Top >= 0 && r.Bottom <= outer.ViewportHeight + 1 && r.Right <= outer.ViewportWidth + 1, $"Essential item clipped: {r}");
                }
                manage.BringIntoView(); await Flush(window); Check(manage.Focus(), "Manage cannot focus");
                Check(manage.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && confirm.IsKeyboardFocusWithin, "Tab does not reach confirmation after usage");
                manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(calls.Single().AbsoluteUri == "https://chatgpt.com/settings/usage" && !vm.CreditsDisabledConfirmed && !connection.CanGenerate, "Opening usage changed authorization");
                Render(window, $"confirm-{width}-scale{scale:0.00}-unchecked");
                confirm.IsChecked = true; await Flush(window);
                manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(vm.CreditsDisabledConfirmed && !connection.PlanUseEnabled, "Returning from usage changed existing checkbox/session");
                confirm.Focus(); Check(confirm.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) && enable.IsKeyboardFocused, "Tab skips enabling action");
                enable.BringIntoView(); await Flush(window); Render(window, $"confirm-{width}-scale{scale:0.00}-checked");
                enable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush(window); Check(connection.CanGenerate && vm.Step == 4, "Explicit fixture enable failed");
                var restarted = new ChatGptConnection(store, new ChatGptOAuthClient(http)); await restarted.InitializeAsync(CancellationToken.None);
                Check(!restarted.PlanUseEnabled && !restarted.CanGenerate, "Confirmation persisted across restart");
                report.Add($"{width}/scale{scale}: link launches={calls.Count}; unchecked initially; no automatic session enable; focus order=usage > checkbox > enable; expiry=PASS");
            }
            finally { window.Close(); await Flush(window); }
        }
    }
    static void Render(Window w, string name)
    {
        report.Add($"capture={name}; window={w.ActualWidth}x{w.ActualHeight}; observedDpi={VisualTreeHelper.GetDpi(w).PixelsPerInchX}; uiFactor={(double)w.FindResource("UiControlFont")/14}; renderDpi=96");
        var content = (FrameworkElement)w.Content; if (VisualTreeHelper.GetParent(content) is FrameworkElement p) content = p;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var d = background.RenderOpen()) d.DrawRectangle(w.Background, null, new Rect(content.RenderSize)); bitmap.Render(background); bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name + ".png")); png.Save(file);
    }
    sealed class NoNetwork : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) => throw new InvalidOperationException("Network forbidden"); }
}
