using System.IO;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            try { await Run(); report.Add("PASS S07 draft transitions/history and S08 discard/continue/clean close; no writes or new probes by editing/closing."); }
            catch (Exception ex) { report.Add(ex.ToString()); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(output, "manifest.txt"), report); app.Shutdown(Environment.ExitCode); }
        }); app.Run();
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static async Task Flush(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    static async Task WaitFor(Window window, Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!predicate()) { if (DateTime.UtcNow > until) throw new TimeoutException("Fixture did not initialize"); await Task.Delay(10); }
        await Flush(window);
    }
    static async Task Run()
    {
        report.Add("Synthetic settings, isolated DPAPI fixture, in-process HTTP handler. No real network or personal configuration.");
        report.Add($"Version={BuildIdentity.ProductVersion}; build={BuildIdentity.FileVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(AiSettingsWindow).Assembly.Location)))}");
        foreach (var scale in new[] { 1d, 1.57d })
        foreach (var width in new[] { 360, 600 })
        {
            UiTypography.Apply(Application.Current.Resources, scale);
            foreach (var scenario in new[] { "empty", "configured", "chat", "new-key" })
            foreach (var field in new[] { "Model", "Limit" })
                await Draft(scenario, field, width, scale);
            foreach (var change in new[] { "model", "limit", "key", "combined" }) await Discard(change, width, scale);
            await History(width, scale);
        }
    }
    sealed class Fixture : IDisposable
    {
        public Store Store { get; } = new();
        public Transport Transport { get; } = new();
        public AiSettingsViewModel Vm { get; private set; } = null!;
        public AiSettingsWindow Window { get; private set; } = null!;
        public string Path { get; } = System.IO.Path.Combine(output, "data", Guid.NewGuid() + ".dpapi");
        public byte[]? Cipher;
        HttpClient http = null!;
        public async Task Open(string scenario, int width)
        {
            var secrets = new DpapiSecretStore(Path);
            if (scenario != "empty")
            {
                Store.Value = new(true, "fixture-model", 20);
                using var key = Secure("synthetic-existing"); await secrets.SaveKeyAsync(key); Cipher = await File.ReadAllBytesAsync(Path);
            }
            http = new(Transport);
            Vm = new(new AiConfigurationService(Store, secrets, Store, new OpenAiConnectionProbe(http)), CancellationToken.None, analysisSession: new ModeSession(scenario == "chat"));
            Window = new(Vm) { Width = width, Height = width == 360 ? 480 : 780 };
            Window.Show(); await WaitFor(Window, () => Vm.CanManage);
        }
        public void CheckUntouched(int probes = 0)
        {
            Check(Store.Writes == 0 && Store.Reservations == probes && Transport.Calls == probes, "Unexpected save/probe by editing or closing");
            Check(Cipher is null ? !File.Exists(Path) : File.ReadAllBytes(Path).SequenceEqual(Cipher), "Persisted credential changed");
        }
        public async Task CloseClean()
        {
            if (!Window.IsVisible) return;
            ((PasswordBox)Window.FindName("KeyInput")).Clear();
            Vm.Model = Store.Value.Model ?? ""; Vm.Limit = Store.Value.DailyRequestLimit.ToString(); Vm.AiEnabled = Store.Value.AiEnabled; Vm.NoticeAccepted = false;
            Check(!Vm.HasChanges, "Cleanup must restore every field");
            Check(await Window.RequestCloseAsync(), "Clean close failed");
            Check(!Application.Current.Windows.OfType<ConfirmationWindow>().Any(), "Clean close opened a confirmation");
        }
        public void Dispose() => http.Dispose();
    }
    static async Task Draft(string scenario, string field, int width, double scale)
    {
        using var f = new Fixture(); await f.Open(scenario, width); var vm = f.Vm;
        try
        {
            if (scenario == "new-key") ((PasswordBox)f.Window.FindName("KeyInput")).Password = "synthetic-replacement";
            var input = (TextBox)f.Window.FindName(field + "Input"); var original = input.Text;
            input.Text = field == "Model" ? "bad model" : "0";
            using var key = ((PasswordBox)f.Window.FindName("KeyInput")).SecurePassword; await vm.SaveAsync(key); await Flush(f.Window);
            await State("invalid", false);
            input.Text = field == "Model" ? "corrected-model" : "30"; await Flush(f.Window); await State("corrected", true);
            input.Text = original; await Flush(f.Window); await State("restored", true);
            Check(vm.HasChanges == (scenario == "new-key"), "Restoring fields left stale dirty state");
            Check(vm.CanTest == (scenario == "configured"), "Probe availability contradicts restored fields/mode/key");
            input.Text = field == "Model" ? "invalid again" : "101"; await Flush(f.Window); await State("invalid-again", false);
            f.CheckUntouched();
            async Task State(string step, bool valid)
            {
                Check(vm.IsDraftValid == valid, "Wrong current validity");
                var notice = (StatusNotice)f.Window.FindName("DraftNotice");
                Check(notice.Message == vm.Message && notice.Summary == vm.FormInstruction, "Stale footer binding");
                Check(Descendants<Button>(f.Window).Single(b => Equals(b.Content, "Guardar")).IsEnabled == vm.CanManage, "Save availability differs from VM");
                Check(Descendants<Button>(f.Window).Single(b => Equals(b.Content, "Probar · de pago")).IsEnabled == vm.CanTest, "Probe availability differs from VM");
                if (!vm.HasChanges) Check(!vm.FormInstruction.Contains("Guarda antes"), "Stale save instruction");
                if (!valid) Check(vm.FirstInvalidField == field && vm.FormInstruction.Contains("inválido"), "Stale correction survived invalid draft");
                var name = $"S07-{scenario}-{field}-{step}-{width}-scale{scale:0.00}";
                Capture(f.Window, name); report.Add($"{name}: changes={vm.HasChanges}; valid={vm.IsDraftValid}; save={vm.CanManage}; test={vm.CanTest}; instruction={vm.FormInstruction}; calls={f.Transport.Calls}; writes={f.Store.Writes}");
                if (width == 360 && scale > 1)
                {
                    notice.BringIntoView(); await Flush(f.Window);
                    var footer = (ScrollViewer)f.Window.FindName("FooterScroll");
                    var viewport = Descendants<ScrollContentPresenter>(footer).First();
                    var bounds = notice.TransformToAncestor(viewport).TransformBounds(new Rect(notice.RenderSize));
                    Check(bounds.Top >= -1 && bounds.Bottom <= viewport.ActualHeight + 1, "Current draft notice inaccessible in footer scroll");
                    Capture(f.Window, name + "-footer");
                    report.Add($"{name}-footer: notice={bounds}; viewport={viewport.ActualHeight}; scroll={footer.VerticalOffset}");
                }
            }
        }
        finally { await f.CloseClean(); }
    }
    static async Task Discard(string change, int width, double scale)
    {
        using var f = new Fixture(); await f.Open("configured", width); var vm = f.Vm;
        if (change is "model" or "combined") ((TextBox)f.Window.FindName("ModelInput")).Text = "draft-model";
        if (change is "limit" or "combined") ((TextBox)f.Window.FindName("LimitInput")).Text = "25";
        if (change == "combined") { vm.AiEnabled = false; vm.NoticeAccepted = true; }
        var key = (PasswordBox)f.Window.FindName("KeyInput");
        if (change is "key" or "combined") key.Password = "synthetic-replacement";
        var model = vm.Model; var limit = vm.Limit; var enabled = vm.AiEnabled; var accepted = vm.NoticeAccepted;
        var draftKey = key.Password; var length = draftKey.Length;
        Check(!await Choose("Continuar editando"), "Continue unexpectedly closed window");
        Check(f.Window.IsVisible && vm.Model == model && vm.Limit == limit && vm.AiEnabled == enabled && vm.NoticeAccepted == accepted && key.Password == draftKey, "Continue lost draft fields");
        f.CheckUntouched();
        Check(await Choose("Descartar y cerrar"), "Discard did not close window");
        Check(!f.Window.IsVisible && key.Password.Length == 0, "Discard did not close/clear form key"); f.CheckUntouched();
        async Task<bool> Choose(string choice)
        {
            var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = f.Window.Dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    var prompt = Application.Current.Windows.OfType<ConfirmationWindow>().Single(); await Flush(prompt);
                    var safe = Descendants<Button>(prompt).Single(b => Equals(b.Content, "Continuar editando"));
                    Check(safe.IsDefault && safe.IsCancel && safe.IsKeyboardFocused, "Safe choice not default/focused");
                    var text = Descendants<TextBlock>(prompt).Single(t => t.Text == vm.DiscardChangesMessage).Text;
                    Check(text.Contains("clave nueva") == (length > 0), "Prompt falsely claims a new key");
                    Check(text.Contains("modelo") == (change is "model" or "combined") && text.Contains("límite diario") == (change is "limit" or "combined"), "Wrong discard fields");
                    Check(!text.Contains("synthetic-"), "Prompt exposed synthetic credential value");
                    var name = $"S08-{change}-{width}-scale{scale:0.00}-{choice.Split(' ')[0]}"; Capture(prompt, name); report.Add($"{name}: {text}");
                    Descendants<Button>(prompt).Single(b => Equals(b.Content, choice)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); observed.SetResult();
                }
                catch (Exception ex) { observed.SetException(ex); foreach (var prompt in Application.Current.Windows.OfType<ConfirmationWindow>().ToArray()) prompt.Close(); }
            }, DispatcherPriority.ContextIdle);
            var closing = f.Window.RequestCloseAsync(); await observed.Task; return await closing;
        }
    }
    static async Task History(int width, double scale)
    {
        using var f = new Fixture(); await f.Open("configured", width);
        try
        {
            await f.Vm.TestAsync(); var history = f.Vm.LastOperationMessage;
            f.Vm.Model = "draft-model"; ((Expander)f.Window.FindName("OperationResult")).IsExpanded = true; await Flush(f.Window);
            Check(f.Vm.HasOperationResult && history.Contains("Conexión válida") && !f.Vm.Message.Contains("Conexión válida"), "History confused with draft instruction");
            Check(f.Vm.FormInstruction.Contains("Guarda") && !f.Vm.CanTest, "Historical success enabled draft test");
            Capture(f.Window, $"S07-history-{width}-scale{scale:0.00}"); f.CheckUntouched(1);
            ((Expander)f.Window.FindName("OperationResult")).BringIntoView(); await Flush(f.Window);
            Capture(f.Window, $"S07-history-{width}-scale{scale:0.00}-footer");
            report.Add($"S07-history-{width}-scale{scale:0.00}: title={f.Vm.LastOperationTitle}; current={f.Vm.FormInstruction}; fixture calls=1; no additional probe/save by editing");
        }
        finally { await f.CloseClean(); }
    }
    static void Capture(Window window, string name)
    {
        report.Add($"capture={name}; window={window.ActualWidth}x{window.ActualHeight}; observedDpi={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; uiFactor={(double)window.FindResource("UiControlFont")/14}; renderDpi=96");
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T typed) yield return typed; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    static SecureString Secure(string value) { var key = new SecureString(); foreach (var c in value) key.AppendChar(c); return key; }
    sealed class Store : ISettingsStore, IRequestBudget
    {
        public AppSettings Value = new(); public int Writes, Reservations;
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(Value);
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) { Writes++; Value = s; return Task.CompletedTask; }
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(Reservations);
        public Task<bool> TryReserveRequestAsync(DateOnly d, CancellationToken t = default) { Reservations++; return Task.FromResult(true); }
    }
    sealed class ModeSession(bool chat) : IAnalysisSession
    {
        public AiConnectionMode Mode => chat ? AiConnectionMode.ChatGptIncluded : AiConnectionMode.ApiKey;
        public string? Model => null; public string CacheScope => "fixture"; public bool CanGenerate => true; public string Status => "fixture";
        public event EventHandler? Changed { add { } remove { } }
    }
    sealed class Transport : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken t)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"ok\\\":true}\"}]}]}") }); }
    }
}
