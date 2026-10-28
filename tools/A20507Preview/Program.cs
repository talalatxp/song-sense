using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
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
    static string output = "", data = "";
    static readonly List<string> report = [];
    static readonly List<MainViewModel> models = [];
    static readonly HttpClient http = new(new NoNetwork());
    static readonly Provider provider = new();
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        data = Path.Combine(output, "data", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var dictionary = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (args.Contains("--interactive")) await Interactive(); else await Run();
                foreach (var vm in models) await vm.StopAsync();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
            app.Shutdown(Environment.ExitCode);
        }); app.Run();
    }
    static void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    static async Task Flush(Window window) => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
    static async Task<MainWindow> Main(string scenario)
    {
        var path = Path.Combine(data, Guid.NewGuid() + ".db"); var store = new SqliteSettingsStore(path); await store.InitializeAsync(); await store.SaveSettingsAsync(new(true, "fixture-model", 20));
        var vm = new MainViewModel(scenario == "storage" ? new FailedStore() : store, provider, insightProvider: provider, cache: new SqliteCacheStore(path)); models.Add(vm); await vm.InitializeAsync();
        if (scenario is not ("empty" or "storage"))
        {
            vm.ApplyDetection(new(ApplicationState.Playing, new(1, "Ventana propia", "Artista sintético", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null)); await vm.WaitForCacheAsync();
            if (scenario == "offline") await vm.SearchLyricsAsync();
            else { vm.SaveManual(vm.BeginLyricsEdit()!, "Luz de la mañana\nAbro la ventana\nVuelvo a respirar\n\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\nOtro verso propio\n"); await vm.WaitForCacheAsync(); if (scenario == "ready") await vm.GenerateInsightAsync(); }
        }
        return new MainWindow { DataContext = vm, ShowActivated = false, Title = "A2-05-07 · Principal · " + scenario };
    }
    static async Task<Window> Create(string family)
    {
        if (family.StartsWith("main-")) return await Main(family[5..]);
        var main = await Main("lyrics"); var vm = (MainViewModel)main.DataContext;
        if (family == "preferences")
        {
            main.Show();
            var window = new PreferencesWindow(main, vm) { Title = "A2-05-07 · Preferencias", ShowActivated = false };
            window.Loaded += (_, _) => { foreach (var e in Find<Expander>(window).Where(e => Equals(e.Header, "Contenido guardado"))) e.IsExpanded = true; };
            return window;
        }
        if (family == "editor")
        {
            var editor = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!);
            vm.ApplyDetection(new(ApplicationState.Playing, new(2, "Otra canción propia", "Artista", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
            return new LyricsEditorWindow(editor, true) { ShowActivated = false, Title = "A2-05-07 · Editor · borrador anterior" };
        }
        if (family == "api")
        {
            var store = new SqliteSettingsStore(Path.Combine(data, Guid.NewGuid() + ".db")); await store.InitializeAsync();
            return new AiSettingsWindow(new(new AiConfigurationService(store, new DpapiSecretStore(Path.Combine(data, Guid.NewGuid() + ".dpapi")), store, new OpenAiConnectionProbe(http)), CancellationToken.None)) { ShowActivated = false, Title = "A2-05-07 · API" };
        }
        var credentials = new ChatGptCredentialStore(Path.Combine(data, Guid.NewGuid() + ".dpapi"));
        await credentials.SaveAsync(new() { ActiveClientId = "oaiapp_fixture", Registrations = [new() { ClientId = "oaiapp_fixture", Subject = "subject", Email = "fixture@example.invalid", Model = "fixture-model", AccessToken = "fixture-access", RefreshToken = "fixture-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), Scopes = [ChatGptOAuthClient.PlanScope] }] }, CancellationToken.None);
        var connection = new ChatGptConnection(credentials, new ChatGptOAuthClient(http)); await connection.InitializeAsync(CancellationToken.None);
        return new ConnectionWindow(new(connection, CancellationToken.None)) { ShowActivated = false, Title = "A2-05-07 · Cuenta" };
    }
    static async Task Run()
    {
        report.Add("Synthetic own text, local stores, no HTTP; UI scale100%; measured visible body lines and client viewport separately.");
        foreach (var size in new[] { (360,480), (600,700), (860,780) })
        foreach (var points in new[] { 12d,16d,20d,24d })
        foreach (var scenario in new[] { "ready", "lyrics" })
        {
            var main = await Main(scenario); main.Width = size.Item1; main.Height = size.Item2; main.ReadingFontSize = points; main.Show(); await Flush(main);
            var reading = (ReadingDocument)main.FindName("TranslationScroll");
            var body = reading.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First(p => Equals(p.Tag,"lyrics-body"));
            var run = (System.Windows.Documents.Run)body.Inlines.FirstInline!;
            var visible = new List<object>(); var cursor = run.ContentStart;
            var completeLines=0;
            for (int i=0;i<3;i++) { var rect=cursor.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward); var fullyVisible=rect.Top>=0 && rect.Bottom<=reading.ViewportHeight; visible.Add(new { i, rect.Y, rect.Bottom, fullyVisible }); if(fullyVisible) completeLines++; cursor=cursor.GetLineStartPosition(1) ?? cursor; }
            Check(completeLines>=1,"No complete body line at chosen size: "+points+" "+scenario);
            report.Add(JsonSerializer.Serialize(new { name="reading-measure", width=size.Item1,height=size.Item2, points, scenario, viewport=reading.ViewportHeight, controlHeight=reading.ActualHeight, lines=visible }));
            await Capture(main,$"main-{scenario}-{size.Item1}-{points}");
            ((TabControl)main.FindName("ReadingTabs")).SelectedIndex=1; await Flush(main);
            if(scenario=="ready") {
                var meaning=(ReadingDocument)main.FindName("MeaningScroll");
                var heading=meaning.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single(p=>new System.Windows.Documents.TextRange(p.ContentStart,p.ContentEnd).Text.Trim()=="Resumen");
                var toggle=meaning.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SingleOrDefault(p=>p.Inlines.OfType<System.Windows.Documents.Hyperlink>().Any(h=>AutomationProperties.GetName(h)=="Leer explicación completa"));
                if(toggle is not null && meaning.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Any(p=>ReadingAnchors.GetKey(p)=="meaning:summary-text")) {
                    var top=heading.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward).Top;
                    var bottom=toggle.ContentEnd.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward).Bottom;
                    Check(bottom-top<=meaning.ViewportHeight*.35+.5,"Summary group exceeds35%");
                    report.Add(JsonSerializer.Serialize(new {name="summary-budget",width=size.Item1,height=size.Item2,points,used=bottom-top,available=meaning.ViewportHeight,maximum=meaning.ViewportHeight*.35}));
                }
            }
            await Capture(main,$"meaning-{scenario}-{size.Item1}-{points}"); main.Close();
        }
        foreach (var size in new[] { (360,480),(600,700) })
        foreach (var state in new[] { "empty", "valid", "excess", "stale" })
        {
            var main=await Main("empty"); var vm=(MainViewModel)main.DataContext;
            vm.ApplyDetection(new(ApplicationState.Playing,new(9,"Canción propia","Autor","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));
            var editor=new LyricsEditorViewModel(vm,vm.BeginLyricsEdit()!);
            if(state!="empty") editor.ManualText=state=="excess"?new string('a',20001):string.Join("\n",Enumerable.Repeat("Estrofa propia continua",60));
            if(state=="stale") vm.ApplyDetection(new(ApplicationState.Playing,new(10,"Otra canción","Autor","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));
            var window=new LyricsEditorWindow(editor,true) {Width=size.Item1,Height=size.Item2};window.Show();await Flush(window);
            var input=(TextBox)window.FindName("ManualInput"); var scroll=Find<ScrollViewer>(input).First();
            report.Add(JsonSerializer.Serialize(new { name="editor-measure",state,width=size.Item1,height=size.Item2,viewport=scroll.ViewportHeight,box=input.ActualHeight }));
            Check(scroll.ViewportHeight>=160,"Editor viewport under160: "+state+" "+scroll.ViewportHeight);
            await Capture(window,$"editor-{state}-{size.Item1}");
            // Discard fixture-only contents without invoking a user confirmation.
            window.Hide(); editor.Dispose();
        }
        foreach (var family in new[] {"preferences","account"})
        foreach (var size in new[] {(360,480),(600,700)})
        {
            var window=await Create(family);window.Width=size.Item1;window.Height=size.Item2;window.Show();await Flush(window);
            if(family=="account") {
                var box=(CheckBox)window.FindName("CreditsConfirmation");var button=(Button)window.FindName("NextButton");
                var content=(FrameworkElement)window.Content;var rect=box.TransformToAncestor(content).TransformBounds(new Rect(box.RenderSize));
                Check(rect.Top>=0&&rect.Bottom<=content.ActualHeight,"Credits clipped");Check(box.IsChecked!=true,"Credits auto-checked");
                report.Add(JsonSerializer.Serialize(new {name="confirm-measure",size,checkbox=rect.ToString(),button=button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize)).ToString()}));
            }
            await Capture(window,$"{family}-{size.Item1}");window.Close();
        }
        foreach(var size in new[] {(360,480),(600,700),(860,780)})
        {
            var vm=new MainViewModel(new MemoryStore(),new Provider {HasCandidates=true});models.Add(vm);await vm.InitializeAsync();
            vm.ApplyDetection(new(ApplicationState.Playing,new(1,"Versiones propias","Autor","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));await vm.SearchLyricsAsync();
            var editor=new LyricsEditorViewModel(vm,vm.BeginLyricsEdit()!);var window=new LyricsEditorWindow(editor,false){Width=size.Item1,Height=size.Item2};window.Show();await Flush(window);
            Check(editor.ManualError.Length==0&&editor.Error.Length==0,"Versions initial manual error");await Capture(window,$"versions-list-{size.Item1}");
            editor.Selected=editor.Options[0];await Flush(window);
            if(size.Item1<640) ((Button)window.FindName("PreviewNext")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Flush(window);var preview=(ScrollViewer)window.FindName("VersionPreview");
            report.Add(JsonSerializer.Serialize(new {name="version-viewport",width=size.Item1,height=size.Item2,preview.ViewportHeight}));
            Check(preview.ViewportHeight>=160,"Version preview under160");await Capture(window,$"versions-preview-{size.Item1}");window.Hide();editor.Dispose();
        }
        foreach(var language in OutputLanguages.All)
        {
            var main=await Main("lyrics");main.Width=360;main.Height=480;main.ReadingFontSize=24;var vm=(MainViewModel)main.DataContext;vm.TargetLanguage=language.Code;await vm.WaitForCacheAsync();main.Show();await Flush(main);
            await Capture(main,$"language-{language.Code}-without");await vm.GenerateInsightAsync();await Flush(main);
            Check(vm.Insight is not null,"Language result missing");
            foreach(var mode in language.Code=="es"?new[]{0}:new[]{0,1,2}) { ((ComboBox)main.FindName("ReadingMode")).SelectedIndex=mode;await Flush(main);await Capture(main,$"language-{language.Code}-mode{mode}"); }
            main.Close();
        }
        foreach(var state in new[]{"storage-failure","language-failure"})
        {
            var vm=new MainViewModel(state=="storage-failure"?new FailedStore():new LanguageFailureStore(),provider);models.Add(vm);await vm.InitializeAsync();
            if(state=="language-failure") { vm.TargetLanguage="fr";await vm.WaitForCacheAsync(); }
            var owner=new MainWindow {DataContext=vm,ShowActivated=false};owner.Show();var prefs=new PreferencesWindow(owner,vm){Width=360,Height=480};prefs.Show();await Flush(prefs);
            var advanced=Find<Expander>(prefs).Single(e=>Equals(e.Header,"Avanzado · diagnóstico y reparación"));Check(!advanced.IsExpanded,"Repair dominates initial state");
            var retry=Find<Button>(prefs).Single(b=>Equals(b.Content,state=="storage-failure"?"Reintentar almacenamiento":"Reintentar guardado de idioma"));
            Check(retry.IsEnabled && retry.Visibility==Visibility.Visible,"Active retry missing");retry.BringIntoView();await Flush(prefs);await Capture(prefs,"preferences-"+state);prefs.Close();owner.Close();
        }
        provider.ShortSummary=true;
        foreach(var size in new[] {(360,480),(600,700),(860,780)}) {
            var shortWindow=await Main("ready");shortWindow.Width=size.Item1;shortWindow.Height=size.Item2;shortWindow.ReadingFontSize=24;shortWindow.Show();((TabControl)shortWindow.FindName("ReadingTabs")).SelectedIndex=1;await Flush(shortWindow);
            var meaning=(ReadingDocument)shortWindow.FindName("MeaningScroll");
            if(!new System.Windows.Documents.TextRange(meaning.Document.ContentStart,meaning.Document.ContentEnd).Text.Contains("Otra oportunidad."))
            {
                var reveal=meaning.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(p=>p.Inlines.OfType<System.Windows.Documents.Hyperlink>()).Single(h=>AutomationProperties.GetName(h)=="Leer explicación completa");
                report.Add("Short summary uses explicit reveal because heading plus one content line do not fit the 35% budget.");
                reveal.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));await Flush(shortWindow);
            }
            Check(new System.Windows.Documents.TextRange(meaning.Document.ContentStart,meaning.Document.ContentEnd).Text.Contains("Otra oportunidad."),"Short summary cannot be read even after explicit reveal");
            await Capture(shortWindow,$"short-summary-{size.Item1}-24");shortWindow.Close();
        }
        provider.ShortSummary=false;
        provider.MixedSource=true;
        var mixed=await Main("lyrics");mixed.Width=360;mixed.Height=480;mixed.ReadingFontSize=24;var mixedVm=(MainViewModel)mixed.DataContext;mixedVm.TargetLanguage="fr";await mixedVm.WaitForCacheAsync();await mixedVm.GenerateInsightAsync();mixed.Show();await Flush(mixed);await Capture(mixed,"mixed-language-360-24");mixed.Close();provider.MixedSource=false;
        var auxiliary=new Window {Width=360,Height=480,Content=AuxiliaryContent.Create(new {Version="Versión sintética\nBuild local\nAceptación externa pendiente"},"Version","Song Sense","Versión y aceptación")};auxiliary.Show();await Flush(auxiliary);
        Check(Find<TextBox>(auxiliary).Single().IsReadOnly,"Auxiliary allows edits");Check(Find<Button>(auxiliary).Any(b=>Equals(b.Content,"Volver")),"No return action");await Capture(auxiliary,"about-readonly");auxiliary.Close();
        report.Add("PASS matrix completed");File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
    }
    static async Task Interactive()
    {
        using var typography = UiTypography.FollowSystem(Application.Current);
        var panel = new StackPanel { Margin = new Thickness(16) };
        var controller = new Window { Title = "A2-05-07 · Pruebas locales", Width = 410, Height = 600, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        double lastScale = -1; int lastWindows = -1; bool recording = false;
        var recorder = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        recorder.Tick += async (_, _) =>
        {
            if (recording) return;
            var scale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
            var windows = Application.Current.Windows.Cast<Window>().Where(w => w != controller && w.IsLoaded && w.IsVisible && w.ActualWidth > 0).ToArray();
            if (scale == lastScale && windows.Length == lastWindows) return;
            recording = true;
            try
            {
                lastScale = scale; lastWindows = windows.Length;
                foreach (var window in windows) await Capture(window, "auto-" + DateTime.Now.ToString("HHmmss") + "-" + window.Title.Replace('·', '-'));
                File.WriteAllLines(Path.Combine(output, "native-manifest.txt"), report);
            }
            catch (Exception ex) { File.AppendAllText(Path.Combine(output, "recorder-error.txt"), ex.ToString()); }
            finally { recording = false; }
        };
        controller.Loaded += (_, _) => recorder.Start(); controller.Closed += (_, _) => recorder.Stop();
        foreach (var family in new[] { "main-ready", "main-empty", "main-offline", "main-storage", "preferences", "account", "api", "editor" })
        {
            var button = new Button { Content = family, Margin = new Thickness(0, 3, 0, 3) }; panel.Children.Add(button);
            button.Click += async (_, _) => { var window = await Create(family); window.Width = 360; window.Height = 480; window.ShowActivated = true; window.Show(); };
        }
        var capture = new Button { Content = "Registrar ventanas y texto de Windows" }; panel.Children.Add(capture);
        capture.Click += async (_, _) =>
        {
            foreach (Window window in Application.Current.Windows.Cast<Window>().Where(w => w != controller).ToArray()) await Capture(window, "native-" + DateTime.Now.ToString("HHmmss") + "-" + window.Title.Replace('·', '-'));
            File.WriteAllLines(Path.Combine(output, "native-manifest.txt"), report);
        };
        var done = new TaskCompletionSource(); controller.Closed += (_, _) => done.TrySetResult(); controller.Show();
        foreach (var family in new[] { "main-ready", "preferences", "account", "api", "editor" })
        { var window = await Create(family); window.Width = 360; window.Height = 480; window.Show(); }
        await done.Task;
    }
    static async Task Capture(Window window, string name)
    {
        await Flush(window); var content = (FrameworkElement)window.Content;
        if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize)); bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        report.Add(JsonSerializer.Serialize(new { name, version = BuildIdentity.ProductVersion, hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))), width = window.ActualWidth, height = window.ActualHeight, clientWidth = content.ActualWidth, clientHeight = content.ActualHeight, dpi = dpi.PixelsPerInchX, uiFont = window.FindResource("UiControlFont"), systemTextFactor = new Windows.UI.ViewManagement.UISettings().TextScaleFactor, origin = "synthetic contents; native WPF", readingViewport = window is MainWindow m ? ((FrameworkElement)m.FindName("TranslationScroll")).ActualHeight : (double?)null }));
        File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
    }
    static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T t) yield return t; foreach (var x in Find<T>(child)) yield return x; } }
    sealed class NoNetwork : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken t) => throw new InvalidOperationException("No HTTP allowed"); }
    sealed class FailedStore : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t = default) => Task.FromException(new IOException("synthetic"));
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromException<AppSettings>(new IOException("synthetic"));
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(0);
    }
    sealed class LanguageFailureStore : ISettingsStore,IOutputLanguageSettings
    {
        public Task InitializeAsync(CancellationToken t=default)=>Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t=default)=>Task.FromResult(new AppSettings());
        public Task SaveSettingsAsync(AppSettings s,CancellationToken t=default)=>Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken t=default)=>Task.FromResult(0);
        public Task<string> ReadOutputLanguageAsync(CancellationToken t=default)=>Task.FromResult("es");
        public Task SaveOutputLanguageAsync(string language,CancellationToken t=default)=>Task.FromException(new IOException("synthetic"));
    }
    sealed class MemoryStore : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t=default)=>Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t=default)=>Task.FromResult(new AppSettings());
        public Task SaveSettingsAsync(AppSettings s,CancellationToken t=default)=>Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken t=default)=>Task.FromResult(0);
    }
    sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public Task<SongInsight> GenerateAsync(CurrentTrack track,ResolvedLyrics lyrics,string target,CancellationToken token)
        {
            IReadOnlyList<TranslatedLine> translation=target=="es"?[]:InsightValidation.Lines(lyrics.Text).Select(line=>new TranslatedLine(line.Id,string.IsNullOrWhiteSpace(line.Text)?"":$"[{target}] {line.Text}","es")).ToArray();
            return Task.FromResult(new SongInsight(track.Revision,MixedSource?"mixed":"es",translation,ShortSummary?"Otra oportunidad.":string.Join(' ',Enumerable.Repeat("La voz narrativa podría observar una oportunidad de cambio mientras contempla la luz.",30)),["Cambio"],[new("Luz","Una imagen propia.")],["Otra lectura posible."],["Interpretación orientativa."]) {TargetLanguage=target});
        }
        public bool HasCandidates, MixedSource, ShortSummary;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => HasCandidates ? Task.FromResult(new LyricsSearchResult(track.Revision,LyricsSearchKind.Candidates,null,Enumerable.Range(1,3).Select(i=>new LyricsCandidate(i,"Versión propia "+i,"Autor","Álbum",TimeSpan.FromMinutes(3),false,string.Join("\n",Enumerable.Repeat("Estrofa propia continua",80)),null)).ToArray())) : throw new LyricsProviderException(LyricsFailure.Offline);
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) => Task.FromResult(new SongInsight(track.Revision, "es", [], string.Join(' ', Enumerable.Repeat("La voz narrativa podría observar una oportunidad de cambio mientras contempla la luz que entra por la ventana.", 5)), ["Cambio"], [], [], []));
    }
}
