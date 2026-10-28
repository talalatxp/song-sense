using System.IO;
using System.Net;
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
            try { await Run(); report.Add("PASS A3-08 all account/catalog states, one visible catalog result, recovery, focus and essential confirmation."); }
            catch (Exception e) { report.Add(e.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(output, "manifest.txt"), report); app.Shutdown(Environment.ExitCode); }
        }); app.Run();
    }
    static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static async Task Flush(Window w) { for (var i = 0; i < 3; i++) await w.Dispatcher.InvokeAsync(w.UpdateLayout, DispatcherPriority.ApplicationIdle); }
    static async Task Run()
    {
        report.Add($"Synthetic catalog HTTP in-process; no network/browser. Version={BuildIdentity.ProductVersion}; build={BuildIdentity.FileVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))}");
        foreach (var scale in new[] { 1d, 1.57d })
        foreach (var width in new[] { 360, 640 })
        foreach (var scenario in new[] { "new", "pending", "loading", "cancelled", "empty", "rejected", "available", "confirm", "ready" })
        {
            UiTypography.Apply(Application.Current.Resources, scale);
            var store = new ChatGptCredentialStore(Path.Combine(output, "data", Guid.NewGuid() + ".dpapi"));
            var account = new ChatGptRegistration { ClientId = "oaiapp_fixture", Subject = "fixture", Email = "fixture@example.invalid", Model = scenario is "confirm" or "ready" ? "fixture-model" : null, AccessToken = "fixture-access", RefreshToken = "fixture-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Scopes = [ChatGptOAuthClient.PlanScope] };
            await store.SaveAsync(new() { ActiveClientId = scenario is "new" or "pending" ? null : account.ClientId, PendingClientId = scenario == "pending" ? account.ClientId : null, Registrations = scenario is "new" or "pending" ? [] : [account] }, CancellationToken.None);
            using var handler = new Catalog(scenario); using var http = new HttpClient(handler);
            var connection = new ChatGptConnection(store, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
            var vm = new ConnectionViewModel(connection, CancellationToken.None);
            if (scenario == "ready") { vm.CreditsDisabledConfirmed = true; await vm.EnablePlanAsync(); }
            var window = new ConnectionWindow(vm) { Width = width, Height = width == 360 ? 480 : 800, OpenUsagePage = _ => throw new InvalidOperationException("No browser action expected") };
            Task? operation = null;
            try
            {
                window.Show(); window.Activate(); await Flush(window);
                if (scenario is "loading" or "cancelled" or "empty" or "rejected" or "available")
                {
                    operation = vm.ModelsAsync();
                    if (scenario is "loading" or "cancelled") await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    if (scenario == "cancelled") vm.Cancel();
                    if (scenario != "loading") await operation;
                    await Flush(window);
                    Check(Find<TextBlock>(window).Count(t => t.IsVisible && t.Text == vm.ModelMessage) == 1 && !vm.ShowOperationResult, "Catalog result duplicated or absent");
                }
                var config = (Expander)window.FindName("ConfigurationGroup"); var help = (Expander)window.FindName("ActivityHelpGroup");
                Check(config.IsVisible && help.IsVisible && Find<TextBlock>(window).Any(t => t.IsVisible && t.Text == vm.StepTitle), "Stage or configuration entry absent");
                var primary = (Button)window.FindName(vm.IsConfirmStep ? "ConfirmEnableButton" : "NextButton");
                primary.BringIntoView(); await Flush(window); Check(primary.IsVisible, "No visible next action");
                if (primary.IsEnabled) Check(primary.Focus(), "Cannot focus next action");
                Render(window, $"{scenario}-{width}-scale{scale:0.00}-stage");
                if (scenario is "empty" or "rejected" or "cancelled")
                {
                    Check(vm.PrimaryLabel == "Consultar de nuevo" && vm.CanPrimary && vm.ModelMessage.Contains("Consultar de nuevo"), "No human recovery action");
                    if (scenario == "rejected") Check(vm.ModelMessage.Contains("no confirma la causa") && !vm.ModelMessage.Contains("HTTP"), "Rejection invents cause or demands technical detail");
                    handler.Scenario = "available"; await vm.PrimaryAsync(); await Flush(window);
                    Check(vm.HasModels && handler.Calls == 2 && connection.Mode == SongSense.Core.AiConnectionMode.ChatGptIncluded, "Retry failed or changed provider mode");
                }
                if (scenario == "loading") { vm.Cancel(); await operation!; await Flush(window); }
                config.IsExpanded = true; await Flush(window); config.BringIntoView(); await Flush(window);
                var radio = Find<RadioButton>(config).First(); Check(radio.Focus(), "Configuration cannot receive focus");
                var advanced = Find<Expander>(config).Single(); advanced.IsExpanded = true; await Flush(window);
                Check(Find<Button>(advanced).Any(b => Equals(b.Content, "Conectar una cuenta distinta")), "Account recovery lost");
                help.IsExpanded = true; await Flush(window); var diagnostic = Find<Button>(help).Single(b => Equals(b.Content, "Diagnóstico"));
                diagnostic.BringIntoView(); await Flush(window); Check(diagnostic.Focus(), "Diagnostic entry unreachable");
                Render(window, $"{scenario}-{width}-scale{scale:0.00}-configuration-help");
                report.Add($"{scenario}/{width}/scale{scale}: stage={vm.Step}; result={vm.ModelMessage}; primary={vm.PrimaryLabel}; calls={handler.Calls}; focused=next/configuration/diagnostic; mode={connection.Mode}");
            }
            finally { vm.Cancel(); if (operation is not null) await operation; window.Close(); await Flush(window); }
        }
    }
    static IEnumerable<T> Find<T>(DependencyObject p) where T : DependencyObject { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(p); i++) { var c = VisualTreeHelper.GetChild(p, i); if (c is T t) yield return t; foreach (var n in Find<T>(c)) yield return n; } }
    static void Render(Window w, string name)
    {
        report.Add($"capture={name}; window={w.ActualWidth}x{w.ActualHeight}; observedDpi={VisualTreeHelper.GetDpi(w).PixelsPerInchX}; uiFactor={(double)w.FindResource("UiControlFont")/14}; renderDpi=96");
        var content = (FrameworkElement)w.Content; if (VisualTreeHelper.GetParent(content) is FrameworkElement p) content = p;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual(); using (var d = bg.RenderOpen()) d.DrawRectangle(w.Background, null, new Rect(content.RenderSize)); bitmap.Render(bg); bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var f = File.Create(Path.Combine(output, name + ".png")); png.Save(f);
    }
    sealed class Catalog(string scenario) : HttpMessageHandler
    {
        public string Scenario = scenario; public int Calls; public TaskCompletionSource Started { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t)
        {
            Calls++; Check(r.RequestUri!.AbsolutePath.Contains("models"), "Unexpected inference/auth request");
            if (Scenario is "loading" or "cancelled") { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, t); }
            return new HttpResponseMessage(Scenario == "rejected" ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = new StringContent(Scenario == "rejected" ? "{\"error\":{\"code\":\"unknown\"}}" : Scenario == "empty" ? "{\"models\":[]}" : "{\"models\":[{\"slug\":\"fixture-model\",\"display_name\":\"Modelo de prueba\",\"visibility\":\"list\"}]}") };
        }
    }
}
