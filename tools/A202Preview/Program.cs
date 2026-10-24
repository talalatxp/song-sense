using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await Run(); }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); Environment.ExitCode = 1; }
            app.Shutdown(Environment.ExitCode);
        }); app.Run();
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static async Task Flush(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    static async Task Run()
    {
        report.Add($"Version={BuildIdentity.ProductVersion}; build={BuildIdentity.FileVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ConnectionWindow).Assembly.Location)))}");
        var runData = Path.Combine(output, "data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(runData);
        foreach (var scenario in new[] { "initial", "pending", "no-model", "confirmation", "enabled", "loading", "cancelled", "available", "empty", "forbidden", "unauthorized", "invalid", "missing-model", "expired", "scope-lost", "account-change", "access-cancelled" })
        {
            var store = new ChatGptCredentialStore(Path.Combine(runData, scenario + ".dpapi"));
            var credentials = new ChatGptCredentials { PendingClientId = scenario == "pending" ? "oaiapp_pending_fixture" : null };
            if (scenario is not ("initial" or "pending" or "access-cancelled"))
            {
                credentials.ActiveClientId = "oaiapp_preview_fixture";
                credentials.Registrations.Add(new() { ClientId = credentials.ActiveClientId, Subject = "fixture-subject", Email = "fixture@example.invalid",
                    AccessToken = "fixture-access", RefreshToken = "fixture-refresh", Model = scenario is "no-model" or "available" or "empty" or "forbidden" or "invalid" ? null : "model-a",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(scenario is "expired" or "scope-lost" ? -1 : 1), Scopes = [ChatGptOAuthClient.PlanScope] });
                if (scenario == "account-change") credentials.Registrations.Add(new() { ClientId = "oaiapp_second_fixture", Subject = "second-subject", Email = "second@example.invalid", AccessToken = "second-access", RefreshToken = "second-refresh", Model = "model-b", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Scopes = [ChatGptOAuthClient.PlanScope] });
            }
            await store.SaveAsync(credentials, CancellationToken.None);
            using var transport = new Transport(scenario); using var http = new HttpClient(transport);
            var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
            var browserReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = new ConnectionViewModel(connection, CancellationToken.None, _ => browserReached.TrySetResult());
            if (scenario is "enabled" or "loading" or "cancelled" or "missing-model" or "expired" or "scope-lost" or "unauthorized" or "account-change")
            { vm.CreditsDisabledConfirmed = true; await vm.EnablePlanAsync(); }
            var window = new ConnectionWindow(vm) { ShowActivated = false }; window.Show(); await Flush(window);
            Task? operation = null;
            if (scenario is "loading" or "cancelled")
            {
                operation = vm.ModelsAsync(); await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(vm.Step == 2 && connection.Connected && connection.PlanGranted && vm.CreditsDisabledConfirmed && !connection.CanGenerate, "Catalog lost stable account/confirmation");
                if (scenario == "cancelled") { vm.Cancel(); await operation; }
            }
            else if (scenario == "access-cancelled")
            {
                operation = vm.ConnectAsync(false); await browserReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await CaptureSizes(window, vm, "access-loading"); vm.Cancel(); await operation;
            }
            else if (scenario == "account-change")
            { vm.SelectedClientId = "oaiapp_second_fixture"; await vm.SelectAccountAsync(); Check(!connection.CanGenerate && !vm.CreditsDisabledConfirmed, "Account change retained confirmation"); }
            else if (scenario is "available" or "empty" or "forbidden" or "invalid" or "missing-model" or "expired" or "scope-lost" or "unauthorized") await vm.ModelsAsync();
            await CaptureSizes(window, vm, scenario);
            if (scenario is "forbidden" or "expired")
            {
                report.Add($"{scenario} sanitized diagnostic: {vm.DiagnosticDetails.Replace('\n', '|')}");
                Check(!vm.DiagnosticDetails.Contains("fixture-access") && !vm.DiagnosticDetails.Contains("private-response"), "Private diagnostic");
            }
            if (scenario == "loading") { vm.Cancel(); await operation!; }
            Check(transport.Inferences == 0, "Inference requested by account UI");
            report.Add($"{scenario}: step={vm.Step}; catalog={vm.CatalogState}; connected={connection.Connected}; permission={connection.PlanGranted}; enabled={connection.PlanUseEnabled}; confirmed={vm.CreditsDisabledConfirmed}; result={vm.Message}; HTTP={transport.Calls}; analyses={transport.Inferences}");
            window.Close(); await Flush(window);
            Check(!window.IsVisible, "Window did not close");
        }
        report.Add("PASS: stable account, typed operation/progress, catalog selector replacement, cancellation, lost model, expiry, scope loss, account change; synthetic transport only; zero analyses.");
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), report);
    }
    static async Task CaptureSizes(ConnectionWindow window, ConnectionViewModel vm, string scenario)
    {
        foreach (var (width, height) in new[] { (640, 800), (360, 480) })
        {
            window.Width = width; window.Height = height; await Flush(window);
            var selector = (ComboBox)window.FindName("ModelSelection");
            Check(selector.Visibility == (vm.HasModels ? Visibility.Visible : Visibility.Collapsed), "Empty catalog exposes selector");
            var bar = Descendants<ProgressBar>(window).Single();
            Check(bar.IsVisible == vm.IsOperating, "Progress not tied to operation");
            var footer = (FrameworkElement)window.FindName("ActionFooter");
            Check(footer.IsVisible, "Footer hidden");
            var content = (FrameworkElement)window.Content;
            if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
            bitmap.Render(background); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"{scenario}-{width}.png")); encoder.Save(file);
            report.Add($"{scenario}-{width}: window={width}x{height} DIP; client={content.ActualWidth}x{content.ActualHeight}; DPI observed={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; render=96 DPI; footer={footer.ActualHeight}; step={vm.Step}; catalog={vm.CatalogState}; point=top; origin=synthetic");
        }
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match; foreach (var item in Descendants<T>(child)) yield return item; }
    }
    sealed class Transport(string scenario) : HttpMessageHandler
    {
        public int Calls, Inferences;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (request.RequestUri!.AbsolutePath == "/api/accounts/oauth/token")
                return scenario == "expired" ? Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest)
                    : Json(new { access_token = "rotated-access", refresh_token = "rotated-refresh", token_type = "Bearer", expires_in = 3600, scope = "openid" });
            if (request.RequestUri.AbsolutePath != "/v1/models") { Inferences++; throw new InvalidOperationException("Unexpected HTTP request"); }
            Started.TrySetResult();
            if (scenario is "loading" or "cancelled") await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return scenario switch
            {
                "forbidden" => Json(new { error = new { code = "unknown", message = "private-response" } }, HttpStatusCode.Forbidden),
                "unauthorized" => Json(new { error = "invalid_token" }, HttpStatusCode.Unauthorized),
                "invalid" => Json(new { wrong = "private-response" }),
                _ => Json(new { models = (scenario == "empty" ? Array.Empty<string>() : new[] { scenario == "missing-model" ? "model-b" : "model-a" }).Select(m => new { slug = m, display_name = "Modelo " + m, visibility = "list" }) })
            };
        }
        static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    }
}
