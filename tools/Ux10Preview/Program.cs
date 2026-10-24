using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        var store=new Store();var provider=new Provider();var cache=new Cache();
        var vm=new MainViewModel(store,provider,insightProvider:provider,cache:cache);await vm.InitializeAsync();
        var main=new MainWindow{DataContext=vm,Title="UX10 · Significado sintético",ShowActivated=interactive};main.Show();
        var tabs=(TabControl)main.FindName("ReadingTabs");tabs.SelectedIndex=1;var meaning=(ReadingDocument)main.FindName("MeaningScroll");var lyricsView=(ReadingDocument)main.FindName("TranslationScroll");
        long revision=0;
        foreach(var (name,count,longSummary,longWarning) in new[]{("short-empty",0,false,false),("long-one",1,true,false),("long-many-warnings",3,true,true)})
        {
            provider.Count=count;provider.Long=longSummary;provider.LongWarning=longWarning;
            vm.ApplyDetection(Track(++revision));vm.SaveManual(vm.BeginLyricsEdit()!,Lyrics+"\n\nEscena "+revision);await vm.WaitForCacheAsync();await vm.GenerateInsightAsync();await Flush(main);
            foreach(var (width,height,size) in new[]{(860,780,16),(360,480,16),(360,480,24),(860,780,24)})
            {
                main.Width=width;main.Height=height;main.ReadingFontSize=size;meaning.ScrollToHome();await Capture(main,$"{name}-{width}-{size}");
#if CURRENT_MEANING
                var text=Text(meaning);Check(text.StartsWith("Una interpretación posible, generada por IA"),"interpretive identity precedes result and metadata");
                Check(count==0?!text.Contains("Imágenes y metáforas"):text.Contains($"Imágenes y metáforas · {count}"),"no empty metaphor section, correct count");
                Check(count==0?!text.Contains("Interpretaciones alternativas"):text.Contains($"Interpretaciones alternativas · {count}"),"no empty alternatives, correct count");
                Check(count==0?!text.Contains("Límites de esta interpretación"):text.Contains(provider.Warning),"full warnings visible without accordion");
                var summary=meaning.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First(p=>new System.Windows.Documents.TextRange(p.ContentStart,p.ContentEnd).Text.StartsWith(provider.Summary[..20]));
                var bounds=summary.ContentStart.GetCharacterRect(LogicalDirection.Forward);var endBounds=summary.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                Check(endBounds.Bottom-bounds.Top<=meaning.Document.LineHeight*6+1,"collapsed summary at most approximately six rendered lines");
                report.Add($"summary: width={width}; pt={size}; height={endBounds.Bottom-bounds.Top}; lineHeight={meaning.Document.LineHeight}");
#endif
            }
#if CURRENT_MEANING
            if(longSummary)
            {
                main.Width=860;main.Height=780;main.ReadingFontSize=16;await Flush(main);
                var doc=meaning.Document;var calls=provider.Calls;var saved=vm.Insight!.Summary;
                var more=Link(meaning,"Leer explicación completa");Click(more);await Capture(main,name+"-expanded");Check(Text(meaning).Contains(saved),"full stored summary revealed unchanged");Check(ReferenceEquals(doc,meaning.Document)&&provider.Calls==calls&&vm.Insight.Summary==saved,"expand retains document and stored content, zero inference");
                var body=meaning.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First(p=>new System.Windows.Documents.TextRange(p.ContentStart,p.ContentEnd).Text.StartsWith(saved[..20]));
                var start=body.ContentStart.GetPositionAtOffset(1)!;meaning.Selection.Select(start,start.GetPositionAtOffset(20)!);var fragment=meaning.Selection.Text;
                meaning.Copy();Check(Clipboard.GetText()==fragment,"expanded summary supports partial native copy");Clipboard.Clear();
                meaning.ScrollToVerticalOffset(180);await Flush(main);tabs.SelectedIndex=0;await Flush(main);lyricsView.ScrollToVerticalOffset(180);await Flush(main);tabs.SelectedIndex=1;await Flush(main);
                Check(meaning.Selection.Text==fragment&&Math.Abs(meaning.VerticalOffset-180)<1,"tab return preserves meaning selection and scroll");
                main.ReadingFontSize=24;await Flush(main);Check(meaning.Selection.Text==fragment,"expanded summary selection survives typography");
                Click(more);await Flush(main);Check(!Text(meaning).Contains(saved),"summary collapses without replacing stored text");
            }
            if(count>0)
            {
                var calls=provider.Calls;var metaphor=Link(meaning,"Imágenes y metáforas");Click(metaphor);await Flush(main);
                Check(Text(meaning).Contains("Cita original")&&Text(meaning).Contains("Interpretación de la imagen")&&Text(meaning).Contains("Explicación propia 1"),"quote and explanation are structurally distinct");
                Click(Link(meaning,"Interpretaciones alternativas"));await Flush(main);((Paragraph)metaphor.Parent).BringIntoView();await Capture(main,name+"-details");
                Check(Text(meaning).Contains("Lectura alternativa 1")&&Text(meaning).Contains("Alternativa propia 1"),"all alternatives accessible in reading document");
                Check(provider.Calls==calls,"details produce zero inference");
                meaning.Document.Blocks.OfType<Paragraph>().First(p=>new TextRange(p.ContentStart,p.ContentEnd).Text.StartsWith("Límites de esta interpretación")).BringIntoView();await Capture(main,name+"-limits-start"); meaning.ScrollToEnd();await Capture(main,name+"-limits");
            }
#endif
        }
        main.Width=600;main.Height=700;main.ReadingFontSize=16;meaning.ScrollToHome();await Flush(main);
#if CURRENT_MEANING
        Check(provider.Calls==3,"only three explicitly requested synthetic analyses");
#endif
        await Capture(main,"interactive-ready");File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
        if(interactive){var closed=new TaskCompletionSource();main.Closed+=(_,_)=>closed.TrySetResult();await closed.Task;}else main.Close();await vm.StopAsync();
        report.Add("App SHA256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))));File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
    }
    static IEnumerable<System.Windows.Documents.Hyperlink> Links(ReadingDocument view)=>view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(p=>p.Inlines.OfType<System.Windows.Documents.Hyperlink>());
    static System.Windows.Documents.Hyperlink Link(ReadingDocument view,string prefix)=>Links(view).First(l=>new System.Windows.Documents.TextRange(l.ContentStart,l.ContentEnd).Text.StartsWith(prefix));
    static void Click(System.Windows.Documents.Hyperlink link)=>link.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
    static string Text(ReadingDocument view)=>new System.Windows.Documents.TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text;
    static void Check(bool value,string description){report.Add($"{description}: {value}");if(!value)throw new InvalidOperationException(description);}
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
        public int Calls; public int Count; public bool Long, LongWarning; public string Summary => Long ? string.Join(" ",Enumerable.Repeat("La voz narrativa contempla otra oportunidad de cambio y deja entrar la luz.",9)) : "La voz busca una oportunidad para cambiar. La ventana representa una posibilidad abierta."; public string Warning => LongWarning ? string.Join(" ",Enumerable.Repeat("El contexto biográfico no consta; esta lectura admite otras interpretaciones.",30)) : "No se conoce el contexto biográfico.";
        public Task<LyricsSearchResult> FindAsync(CurrentTrack t,CancellationToken c)=>throw new InvalidOperationException("No network in UX08 fixture");
        public Task<SongInsight> GenerateAsync(CurrentTrack t,ResolvedLyrics l,CancellationToken c)=>GenerateAsync(t,l,"es",c);
        public Task<SongInsight> GenerateAsync(CurrentTrack t,ResolvedLyrics l,string language,CancellationToken c)
        {
            Calls++;var result=new SongInsight(t.Revision,"es",[],Summary,["Cambio","Esperanza"],Enumerable.Range(1,Count).Select(i=>new ExplainedMetaphor("Abro una ventana "+i,"Explicación propia "+i+": la imagen permite explorar una oportunidad de cambio.")).ToArray(),Enumerable.Range(1,Count).Select(i=>"Alternativa propia "+i+": la voz también podría estar recordando otra etapa.").ToArray(),Count==0?[]:[Warning,"Los límites son generales: el resultado no incluye una relación con afirmaciones concretas."]){Model="fixture-model",TargetLanguage=language,Usage=new(100,50,150),Provenance=new(AiConnectionMode.ApiKey,"fixture-model",DateTimeOffset.Parse("2026-10-21T12:00:00Z"))};
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
