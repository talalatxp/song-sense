using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using SongSense.App;
using SongSense.Core;
public static class Preview
{
    static string output = ""; static readonly List<string> report = [];
    static string Lyrics => string.Join("\n\n", Enumerable.Range(1, 50).Select(i => $"Abro una ventana {i}\nY dejo entrar la luz"));
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output); var oldError = Path.Combine(output,"error.txt"); if(File.Exists(oldError))File.Delete(oldError); RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single(); app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        app.Dispatcher.InvokeAsync(async () => { try { await Run(args.Contains("--interactive")); } catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); Environment.ExitCode = 1; } app.Shutdown(); }); app.Run();
    }
    static TrackDetection Track(long revision) => new(ApplicationState.Playing, new(revision, "Ventana de prueba " + revision, "Artista sintético", "Álbum propio", TimeSpan.FromMinutes(3), PlaybackState.Playing), null);
    static async Task Flush(Window window) { await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle); await Task.Delay(30); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
    static async Task Run(bool interactive)
    {
        var store = new Store(); var provider = new Provider(); var vm = new MainViewModel(store, provider, insightProvider: provider, cache: new Cache());
        await vm.InitializeAsync(); vm.ApplyDetection(Track(1)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics); await vm.WaitForCacheAsync(); await vm.GenerateInsightAsync();
        var main = new MainWindow { DataContext = vm, Title = "UX08 · Navegación sintética", ShowActivated = interactive }; main.Show();
        var tabs = (TabControl)main.FindName("ReadingTabs"); var translation = (ScrollViewer)main.FindName("TranslationScroll"); var meaning = (ScrollViewer)main.FindName("MeaningScroll");
        foreach (var (width, height) in new[] { (860,780), (600,700), (360,480) })
        {
            main.Width = width; main.Height = height; tabs.SelectedIndex = 0;
            foreach (var size in new[] { 16,24 }) { main.ReadingFontSize = size; await Capture(main, $"reading-{width}-{size}"); Measure(main); }
        }
        main.Width = 860; main.Height = 780; main.ReadingFontSize = 16; tabs.SelectedIndex = 0; await Flush(main);
        translation.ScrollToVerticalOffset(180); await Flush(main); tabs.SelectedIndex = 1; await Flush(main);
        meaning.ScrollToVerticalOffset(220); await Flush(main); tabs.SelectedIndex = 0; await Flush(main);
        var backTranslation = translation.VerticalOffset; tabs.SelectedIndex = 1; await Flush(main); var backMeaning = meaning.VerticalOffset;
        report.Add($"tab-return: translation={backTranslation}; meaning={backMeaning}; expected=180/220; inference={provider.Calls}");
#if CURRENT_NAVIGATION
        if (Math.Abs(backTranslation-180)>1 || Math.Abs(backMeaning-220)>1) throw new InvalidOperationException("Tab scroll not preserved");
        var expander = Find<Expander>(meaning).First(); expander.IsExpanded = true; await Flush(main);
        if (Math.Abs(meaning.VerticalOffset-220)>1) throw new InvalidOperationException("Expanding detail reset scroll");
        var more = Find<Button>(main).Single(b => b.ContextMenu is not null); ((MenuItem)more.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Flush(main);
        var details = Application.Current.Windows.Cast<Window>().Single(w => w.Title.StartsWith("Detalles de la canción")); await Capture(details,"provenance-details"); details.Close();
        if (Math.Abs(meaning.VerticalOffset-220)>1) throw new InvalidOperationException("Opening details reset scroll");
        report.Add("details: expanded/opened without changing meaning offset=220");
        vm.ApplyDetection(Track(2)); vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics); await vm.WaitForCacheAsync(); await Flush(main);
        if (meaning.VerticalOffset != 0) throw new InvalidOperationException("New song did not start at top");
        tabs.SelectedIndex = 0; await Flush(main); translation.ScrollToVerticalOffset(170); await Flush(main);
        vm.SaveManual(vm.BeginLyricsEdit()!, Lyrics+"\nOtra línea propia"); await vm.WaitForCacheAsync(); await Flush(main);
        if (translation.VerticalOffset != 0) throw new InvalidOperationException("New lyrics did not start at top");
        translation.ScrollToVerticalOffset(160); await Flush(main); await vm.GenerateInsightAsync(); await Flush(main);
        if (translation.VerticalOffset != 0) throw new InvalidOperationException("New result did not start at top");
        report.Add("new-song/lyrics/result: offset=0");
        var calls = provider.Calls; vm.TargetLanguage = "en"; await vm.WaitForCacheAsync(); await Flush(main);
        if (vm.HasInsight || !vm.LanguageMessage.Contains("guardado confirmado")) throw new InvalidOperationException("Language transition used old result or missed confirmed save");
        await Capture(main,"language-english-missing");
        vm.TargetLanguage = "es"; await vm.WaitForCacheAsync(); await Flush(main);
        if (vm.Insight?.TargetLanguage != "es" || vm.Usage.State != AiUsageState.RecuperadoDeCache) throw new InvalidOperationException("Returning language did not restore cache");
        await Capture(main,"language-spanish-restored");
        store.Fail = true; vm.TargetLanguage = "fr"; await vm.WaitForCacheAsync(); await Flush(main);
        if (vm.HasInsight || vm.GenerateInsightCommand.CanExecute(null) || vm.LanguageNoticeKind != NoticeKind.Error) throw new InvalidOperationException("Failed language save is not blocked");
        main.Width = 360; main.Height = 480; await Capture(main,"language-save-failed-minimum"); Measure(main);
        store.Fail = false; await vm.RetryLanguageAsync(); await vm.WaitForCacheAsync();
        if (provider.Calls != calls) throw new InvalidOperationException("Preferences sent inference");
        report.Add($"language: es→en→es→fr failure/retry; cached-return=True; inference-before={calls}; after={provider.Calls}");
        vm.TargetLanguage = "es"; await vm.WaitForCacheAsync();
#endif
        if (interactive) { main.Width=360; main.Height=480; main.Title="UX08 · Teclado sintético"; var done=new TaskCompletionSource(); main.Closed+=(_,_)=>done.TrySetResult(); await done.Task; } else main.Close();
        await vm.StopAsync(); report.Add("App SHA256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))); File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
    }
    static void Measure(MainWindow main)
    {
        var scroll=(ScrollViewer)main.FindName("TranslationScroll"); var root=(FrameworkElement)main.Content; if(VisualTreeHelper.GetParent(root) is FrameworkElement parent)root=parent;
        report.Add($"viewport: {main.Width}x{main.Height}; client={root.ActualWidth}x{root.ActualHeight}; document={scroll.ViewportHeight}; ratio={scroll.ViewportHeight/root.ActualHeight:P2}");
#if CURRENT_NAVIGATION
        if(main.Width==360&&scroll.ViewportHeight<160 || main.Width==860&&scroll.ViewportHeight/root.ActualHeight<.6)throw new InvalidOperationException("Viewport regression");
        var combo=(ComboBox)main.FindName("ReadingLanguage"); var last=(TabItem)((TabControl)main.FindName("ReadingTabs")).Items[1];
        var tabRight=last.TransformToAncestor(main).Transform(new Point()).X+last.ActualWidth; var comboLeft=combo.TransformToAncestor(main).Transform(new Point()).X;
        if(tabRight>comboLeft)throw new InvalidOperationException($"Language selector overlaps tabs: {tabRight}/{comboLeft}");
        report.Add($"language-bar: tabs-right={tabRight}; selector-left={comboLeft}; no-overlap=True");
#endif
    }
    static async Task Capture(Window window,string name)
    {
        foreach(var text in Find<TextBlock>(window)){text.InvalidateMeasure();text.InvalidateVisual();} await Flush(window);
        var content=(FrameworkElement)window.Content;if(VisualTreeHelper.GetParent(content)is FrameworkElement parent)content=parent;
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual();using(var draw=background.RenderOpen())draw.DrawRectangle(window.Background,null,new Rect(content.RenderSize));bitmap.Render(background);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
        report.Add($"{name}: {window.ActualWidth}x{window.ActualHeight}; Windows={VisualTreeHelper.GetDpi(window).PixelsPerInchX} DPI; render=96 DPI");
    }
    static IEnumerable<T> Find<T>(DependencyObject p)where T:DependencyObject{for(var i=0;i<VisualTreeHelper.GetChildrenCount(p);i++){var c=VisualTreeHelper.GetChild(p,i);if(c is T t)yield return t;foreach(var n in Find<T>(c))yield return n;}}
    sealed class Store:ISettingsStore,IOutputLanguageSettings
    {
        public bool Fail { get; set; } string language="es";
        public Task InitializeAsync(CancellationToken t=default)=>Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t=default)=>Task.FromResult(new AppSettings(true,"fixture-model"));
        public Task SaveSettingsAsync(AppSettings s,CancellationToken t=default)=>Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken t=default)=>Task.FromResult(0);
        public Task<string> ReadOutputLanguageAsync(CancellationToken t=default)=>Task.FromResult(language);
        public Task SaveOutputLanguageAsync(string value,CancellationToken t=default){if(Fail)throw new IOException("synthetic");language=value;return Task.CompletedTask;}
    }
    sealed class Provider:ILyricsProvider,IInsightProvider
    {
        public int Calls;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack t,CancellationToken c)=>throw new InvalidOperationException("No network in UX08 fixture");
        public Task<SongInsight> GenerateAsync(CurrentTrack t,ResolvedLyrics l,CancellationToken c)=>GenerateAsync(t,l,"es",c);
        public Task<SongInsight> GenerateAsync(CurrentTrack t,ResolvedLyrics l,string language,CancellationToken c)
        {
            Calls++;var result=new SongInsight(t.Revision,"es",[],string.Join(" ",Enumerable.Repeat("La voz narrativa busca una oportunidad de cambio y observa cómo entra la luz.",60)),["Cambio"],[new("Abro una ventana",string.Join(" ",Enumerable.Repeat("La ventana permite mirar otra posibilidad.",50)))],[],[]){Model="fixture-model",TargetLanguage=language,Usage=new(100,50,150)};
#if CURRENT_NAVIGATION
            result=result with{Provenance=new(AiConnectionMode.ApiKey,"fixture-model",DateTimeOffset.Parse("2026-10-21T12:00:00Z"))};
#endif
            return Task.FromResult(result);
        }
    }
    sealed class Cache:ICacheStore
    {
        readonly Dictionary<string,ResolvedLyrics?> songs=[];readonly Dictionary<string,SongInsight> analyses=[];
        public Task<CachedSong?> ReadSongAsync(CurrentTrack t,CancellationToken c=default)=>Task.FromResult<CachedSong?>(songs.TryGetValue(CacheIdentity.Song(t),out var l)?new(l is null?null:l with{TrackRevision=t.Revision},l is null,false):null);
        public Task SaveLyricsAsync(CurrentTrack t,ResolvedLyrics? l,CancellationToken c=default){songs[CacheIdentity.Song(t)]=l;return Task.CompletedTask;}
        public Task<SongInsight?> ReadInsightAsync(ResolvedLyrics l,string m,string v,CancellationToken c=default,CurrentTrack? track=null,string scope="api-key",string targetLanguage="es")=>Task.FromResult<SongInsight?>(analyses.TryGetValue(CacheIdentity.Analysis(l,m,v,scope,targetLanguage),out var i)?i with{TrackRevision=l.TrackRevision}:null);
        public Task SaveInsightAsync(CurrentTrack t,ResolvedLyrics l,string m,string v,SongInsight i,CancellationToken c=default,string scope="api-key",string targetLanguage="es"){analyses[CacheIdentity.Analysis(l,m,v,scope,targetLanguage)]=i;songs[CacheIdentity.Song(t)]=l;return Task.CompletedTask;}
        public Task DeleteSongAsync(CurrentTrack t,CancellationToken c=default)=>Task.CompletedTask;
        public Task ClearAsync(CancellationToken c=default)=>Task.CompletedTask;
    }
}
