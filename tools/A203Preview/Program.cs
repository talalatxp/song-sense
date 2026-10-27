using System.IO;
using System.Net;
using System.Net.Http;
using System.Security;
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
    static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static async Task Flush(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    static async Task WaitFor(Window window, Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!predicate()) { if (DateTime.UtcNow > until) throw new TimeoutException("Fixture did not reach expected state"); await Task.Delay(10); await Flush(window); }
        await Flush(window);
    }
    static async Task Run()
    {
        report.Add($"Synthetic records only; HTTP handled in-process. Version={BuildIdentity.ProductVersion}; build={BuildIdentity.FileVersion}; SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(AiSettingsWindow).Assembly.Location)))}");
        var runData = Path.Combine(output, "data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(runData);
        foreach (var scenario in new[] { "clean", "with-key", "editing", "invalid-model", "limit-zero", "limit-101", "corrected", "opening", "saving", "save-cancelled", "probe-held", "probe-cancelled", "probe-failure", "probe-success", "delete-confirmation", "theme-progress" })
        {
            bool themed = scenario == "theme-progress";
            if (themed) ThemeResources.Apply(Application.Current.Resources, true);
            var data = Path.Combine(runData, scenario); Directory.CreateDirectory(data);
            var backing = new SqliteSettingsStore(Path.Combine(data, "synthetic.db")); await backing.InitializeAsync();
            var store = new HeldStore(backing);
            var secrets = new DpapiSecretStore(Path.Combine(data, "synthetic.dpapi"));
            if (scenario != "clean")
            {
                using var key = Secure("fixture-only"); await secrets.SaveKeyAsync(key);
                await backing.SaveSettingsAsync(new(true, "fixture-model", 20));
            }
            using var transport = new Transport { Hold = scenario is "probe-held" or "probe-cancelled" or "theme-progress", Fail = scenario == "probe-failure" };
            using var http = new HttpClient(transport);
            var service = new AiConfigurationService(store, secrets, store, new OpenAiConnectionProbe(http));
            var vm = new AiSettingsViewModel(service, CancellationToken.None);
            if (scenario == "opening") store.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new AiSettingsWindow(vm) { ShowActivated = false }; window.Show();
            if (scenario == "opening") await store.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            else await WaitFor(window, () => vm.CanManage);
            Task? operation = null;
            if (scenario == "editing") vm.Model = "draft-model";
            if (scenario == "corrected") { vm.Limit = "0"; using var empty = new SecureString(); await vm.SaveAsync(empty); vm.Limit = "20"; }
            if (scenario is "saving" or "save-cancelled")
            {
                vm.Model = "updated-model"; store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                using var empty = new SecureString(); operation = vm.SaveAsync(empty); await store.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (scenario == "save-cancelled") { vm.Cancel(); await operation; }
            }
            if (scenario.StartsWith("probe-") || themed)
            {
                operation = vm.TestAsync();
                if (transport.Hold) await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                else await operation;
                if (scenario == "probe-cancelled") { vm.Cancel(); await operation; }
            }
            if (scenario == "delete-confirmation")
            {
                await ChooseDeletion(window, "Cancelar", "delete-cancel-prompt");
                Check(vm.HasKey, "Cancel must retain saved key");
                await ChooseDeletion(window, "Eliminar clave", "delete-confirm-prompt");
                await WaitFor(window, () => !vm.HasKey && !vm.IsOperating);
                Check(!vm.AiEnabled && (await backing.ReadSettingsAsync()).AiEnabled == false, "Deletion must disable API");
            }
            foreach (var (width, height) in new[] { (600, 780), (360, 480) })
            {
                window.Width = width; window.Height = height; await Flush(window);
                if (scenario is "invalid-model" or "limit-zero" or "limit-101")
                {
                    vm.Model = scenario == "invalid-model" ? "has spaces" : "fixture-model";
                    vm.Limit = scenario == "limit-zero" ? "0" : scenario == "limit-101" ? "101" : "20";
                    using var empty = new SecureString(); await vm.SaveAsync(empty); await Flush(window);
                    MeasureValidation(window, vm, scenario == "invalid-model" ? "Model" : "Limit");
                }
                var progress = (ProgressBar)window.FindName("OperationProgress");
                Check(progress.Visibility == (vm.IsOperating ? Visibility.Visible : Visibility.Collapsed) && progress.IsIndeterminate == vm.IsOperating, "Progress outlived operation");
                var deletion = (Button)window.FindName("DeleteKeyButton");
                Check(deletion.Visibility == (vm.HasKey ? Visibility.Visible : Visibility.Collapsed), "Key deletion offered without saved key");
                if (themed) Check(((SolidColorBrush)progress.Foreground).Color == ((SolidColorBrush)window.FindResource("Progress")).Color, "Progress lost theme brush");
                await Capture(window, $"{scenario}-{width}");
                report.Add($"{scenario}-{width}: operation={vm.Operation}; active={vm.IsOperating}; hasKey={vm.HasKey}; requests={transport.Calls}; counter={(await backing.ReadRequestCountAsync(service.LocalDate))}");
            }
            if (scenario == "opening") { store.ReadGate!.SetResult(); await WaitFor(window, () => vm.CanManage); }
            if (scenario == "saving") { store.WriteGate!.SetResult(); await operation!; Check(!vm.HasChanges, "Successful save retained dirty form"); }
            if (scenario is "probe-held" or "theme-progress") { vm.Cancel(); await operation!; }
            Check(!vm.IsOperating, "Operation remained active after completion");
            Check(transport.Calls == (scenario.StartsWith("probe-") || themed ? 1 : 0), "Unexpected synthetic HTTP call");
            await Capture(window, scenario + "-settled");
            if (vm.HasChanges) _ = window.Dispatcher.BeginInvoke(() =>
            {
                var prompt = Application.Current.Windows.OfType<ConfirmationWindow>().Single();
                Descendants<Button>(prompt).Single(b => Equals(b.Content, "Descartar y cerrar")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.ContextIdle);
            window.Close(); await WaitFor(window, () => !window.IsVisible);
            if (themed) ThemeResources.Apply(Application.Current.Resources, false);
        }
        report.Add("PASS: validation label/field/error bounds and logical focus, activity and completion, conditional deletion/confirmation; no real HTTP, no personal records.");
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), report);
    }
    static void MeasureValidation(Window window, AiSettingsViewModel vm, string field)
    {
        Check(vm.FirstInvalidField == field, "Wrong first invalid field");
        var form = (ScrollViewer)window.FindName("FormScroll");
        var viewport = Descendants<ScrollContentPresenter>(form).First();
        var input = (FrameworkElement)window.FindName(field + "Input");
        Check(input.IsKeyboardFocused || ReferenceEquals(FocusManager.GetFocusedElement(window), input), "Invalid input did not receive focus");
        foreach (var name in new[] { field + "Label", field + "Input", field + "ErrorText" })
        {
            var element = (FrameworkElement)window.FindName(name);
            var rect = element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
            report.Add($"validation {window.Width} {name}: y={rect.Y}; height={rect.Height}; viewport={viewport.ActualHeight}; offset={form.VerticalOffset}");
            Check(rect.Top >= -.5 && rect.Bottom <= viewport.ActualHeight + .5, "Validation group was scrolled out: " + name);
        }
    }
    static async Task ChooseDeletion(Window window, string choice, string capture)
    {
        var selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = window.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var prompt = Application.Current.Windows.OfType<ConfirmationWindow>().Single();
                await Capture(prompt, capture);
                Descendants<Button>(prompt).Single(b => Equals(b.Content, choice)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); selected.SetResult();
            }
            catch (Exception e) { selected.SetException(e); foreach (var prompt in Application.Current.Windows.OfType<ConfirmationWindow>().ToArray()) prompt.Close(); }
        }, DispatcherPriority.ContextIdle);
        ((Button)window.FindName("DeleteKeyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await selected.Task;
    }
    static async Task Capture(Window window, string name)
    {
        await Flush(window); var content = (FrameworkElement)window.Content;
        if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize));
        bitmap.Render(background); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        var form = window.FindName("FormScroll") as ScrollViewer;
        report.Add($"{name}: window={window.ActualWidth}x{window.ActualHeight} DIP; client={content.ActualWidth}x{content.ActualHeight}; DPI observed={VisualTreeHelper.GetDpi(window).PixelsPerInchX}; render=96 DPI; form viewport={form?.ViewportHeight}; point offset={form?.VerticalOffset}; origin=synthetic; palette={(name.StartsWith("theme-") ? "synthetic-system" : "dark")}");
    }
    static SecureString Secure(string value) { var key = new SecureString(); foreach (var ch in value) key.AppendChar(ch); return key; }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T item) yield return item; foreach (var match in Descendants<T>(child)) yield return match; }
    }
    sealed class HeldStore(SqliteSettingsStore store) : ISettingsStore, IRequestBudget
    {
        public TaskCompletionSource? ReadGate, WriteGate;
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InitializeAsync(CancellationToken token = default) => store.InitializeAsync(token);
        public async Task<AppSettings> ReadSettingsAsync(CancellationToken token = default) { if (ReadGate is not null) { ReadStarted.TrySetResult(); await ReadGate.Task.WaitAsync(token); } return await store.ReadSettingsAsync(token); }
        public async Task SaveSettingsAsync(AppSettings value, CancellationToken token = default) { if (WriteGate is not null) { WriteStarted.TrySetResult(); await WriteGate.Task.WaitAsync(token); } await store.SaveSettingsAsync(value, token); }
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken token = default) => store.ReadRequestCountAsync(date, token);
        public Task<bool> TryReserveRequestAsync(DateOnly date, CancellationToken token = default) => store.TryReserveRequestAsync(date, token);
    }
    sealed class Transport : HttpMessageHandler
    {
        public bool Hold, Fail;
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Check(request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/responses", "Unexpected endpoint");
            Calls++; Started.TrySetResult(); if (Hold) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(Fail ? HttpStatusCode.InternalServerError : HttpStatusCode.OK) { Content = new StringContent(Fail ? "{}" : """
                {"status":"completed","output":[{"type":"message","status":"completed","content":[{"type":"output_text","text":"{\"ok\":true}"}]}]}
                """) };
        }
    }
}
