using SongSense.Infrastructure;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Documents;
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
        app.Dispatcher.InvokeAsync(async () => { var previousClipboard = Clipboard.GetDataObject(); try { await Run(args.Contains("--interactive")); } catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); Environment.ExitCode = 1; } finally { if (previousClipboard is null) Clipboard.Clear(); else Clipboard.SetDataObject(previousClipboard, true); } app.Shutdown(Environment.ExitCode); }); app.Run();
    }
    static TrackDetection Track(long revision) => new(ApplicationState.Playing, new(revision, "Ventana de prueba " + revision, "Artista sintético", "Álbum propio", TimeSpan.FromMinutes(3), PlaybackState.Playing), null);
    static async Task Flush(Window window) { await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle); await Task.Delay(30); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
    static async Task Run(bool interactive)
    {
        var store = new Store(); var provider = new Provider();
        var database = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N") + ".db");
        await new SqliteSettingsStore(database).InitializeAsync();
        var vm = new MainViewModel(store, provider, insightProvider: provider, cache: new SqliteCacheStore(database));
        await vm.InitializeAsync(); vm.ApplyDetection(Track(1));
        var lyrics = "I open a window · áéí ñ 漢字 🎵 e\u0301\nAbro una ventana\n\n" + new string('W', 250) + "\n\n" + string.Join("\n\n", Enumerable.Range(1, 45).Select(i => $"Verse {i}: I let the daylight enter\nY dejo entrar la luz"));
        vm.SaveManual(vm.BeginLyricsEdit()!, lyrics); await vm.WaitForCacheAsync();
        var main = new MainWindow { DataContext = vm, Title = "UX09 · Lectura sintética", ShowActivated = interactive }; main.Show();
        var tabs = (TabControl)main.FindName("ReadingTabs");
        var original = (ReadingDocument)main.FindName("TranslationScroll"); var meaning = (ReadingDocument)main.FindName("MeaningScroll"); var mode = (ComboBox)main.FindName("ReadingMode");
        await Capture(main, "unknown-no-analysis");
        Check(mode.SelectedIndex == 0 && !((ComboBoxItem)mode.Items[1]).IsEnabled && Text(original).Contains("Idioma desconocido"), "unknown, unavailable mode disabled with cause/action");
        await vm.GenerateInsightAsync();
        Check(vm.InsightSaveState==ContentSaveState.Saved,"synthetic analysis passes real SQLite cache validation");
        Check(mode.SelectedIndex == 2 && Text(original).Contains("Idiomas mixtos") && Text(original).Contains("Traducción · Español"), "mixed original and translation roles");
        foreach (var (width,height) in new[] {(860,780),(600,700),(360,480)})
        foreach (var size in new[] {12,16,20,24})
        {
            main.Width=width;main.Height=height; main.ReadingFontSize=size; await Capture(main,$"mixed-{width}-{size}");
            Check(Math.Abs(original.Document.FontSize-size*4d/3)<.01 && Math.Abs(original.Document.LineHeight-size*2d)<.01,"point conversion and 1.5 line height");
            Check(original.ViewportHeight >= original.FontSize * 1.5, $"viewport cannot fit one reading line {width}/{size}: {original.ViewportHeight}");
            var scroll = Find<ScrollViewer>(original).First(); Check(scroll.ExtentWidth <= scroll.ViewportWidth+.1, "unbroken line wraps without horizontal overflow");
            var sample = new FormattedText("La voz narrativa busca una oportunidad de cambio y observa cómo entra la luz.", System.Globalization.CultureInfo.GetCultureInfo("es-ES"), FlowDirection.LeftToRight, new Typeface("Georgia"), original.FontSize, Brushes.Black, 1);
            var prose = original.ViewportWidth-original.Document.PagePadding.Left-original.Document.PagePadding.Right; Check(prose <= original.FontSize*36+.1,"prose capped at 36 em");
            report.Add($"approx-characters={prose/(sample.WidthIncludingTrailingWhitespace/75):F1}");
            report.Add($"width={width}; pt={size}; viewport={original.ViewportHeight}; prose-width={original.ViewportWidth-original.Document.PagePadding.Left-original.Document.PagePadding.Right}; max={original.FontSize*36}");
        }
        main.Width=860;main.Height=780; main.ReadingFontSize=16; await Flush(main);
        main.Activate(); original.Focus(); await Flush(main);
        original.SelectAll(); Check(original.Selection.Text.Contains("I open a window"),"SelectAll selects the complete original before native Copy"); original.Copy(); Check(Clipboard.GetText().Contains("I open a window"),"copy across document metadata succeeds"); Clipboard.Clear();
        var first = original.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First(p=>new System.Windows.Documents.TextRange(p.ContentStart,p.ContentEnd).Text.StartsWith("I open"));
        var start = first.ContentStart; var finish=start.GetPositionAtOffset(1+"I open a window".Length)!;
        original.Selection.Select(start,finish); var fragment=original.Selection.Text;
        Check(fragment.Contains("I open a window") && fragment.Length<25,"partial selection, not full document");
        original.Copy(); Check(Clipboard.GetText()==fragment,"native Copy command copies only selected verse"); Clipboard.Clear();
        var document=original.Document; var before=Text(original); original.Focus();
        System.Windows.Input.ApplicationCommands.Cut.Execute(null,original); System.Windows.Input.ApplicationCommands.Delete.Execute(null,original); System.Windows.Input.ApplicationCommands.Paste.Execute(null,original);
        Check(before==Text(original) && original.IsReadOnly,"cut/delete/paste cannot edit read-only content");
        original.ScrollToVerticalOffset(180); await Flush(main); original.RememberPosition(); var originalAnchor=original.Bookmark!;
        tabs.SelectedIndex=1; await Flush(main);
        var link = MeaningLink(meaning,"MeaningMetaphors");
        Check(new TextRange(link.ContentStart,link.ContentEnd).Text.Contains("Imágenes y metáforas"),"semantic metaphor identity and human title");
        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent)); await Flush(main);
        Check(Text(meaning).Contains("ventana permite"),"expanded explanation belongs to same selection document");
        meaning.ScrollToVerticalOffset(220); await Flush(main); meaning.RememberPosition(); var meaningAnchor=meaning.Bookmark!;
        tabs.SelectedIndex=0; await Flush(main); CheckAnchor(original,originalAnchor,"lyrics tab return");Check(original.Selection.Text==fragment,"partial selection survives tab return");
        main.ReadingFontSize=24; await Flush(main);CheckAnchor(original,originalAnchor,"visible lyrics 16 to24");
        Check(ReferenceEquals(document,original.Document)&&original.Selection.Text==fragment,"typography preserves document and partial selection");
        tabs.SelectedIndex=1; await Flush(main);CheckAnchor(meaning,meaningAnchor,"hidden meaning 16 to24");
        meaningAnchor=meaning.CaptureBookmark()!;main.ReadingFontSize=12;await Flush(main);CheckAnchor(meaning,meaningAnchor,"visible meaning 24 to12");
        meaningAnchor=meaning.CaptureBookmark()!;main.Width=360;main.Height=480;await Flush(main);CheckAnchor(meaning,meaningAnchor,"meaning narrow reflow");
        meaningAnchor=meaning.CaptureBookmark()!;link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));await Flush(main);CheckAnchor(meaning,meaningAnchor,"nearest valid block after collapse");
        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));await Capture(main,"meaning-details-reflow");
        tabs.SelectedIndex=0;await Flush(main);CheckAnchor(original,originalAnchor,"hidden lyrics 24 to12 and narrow");Check(original.Selection.Text==fragment,"selection survives hidden reflow");
        main.Width=860;main.Height=780;await Flush(main);
        tabs.SelectedIndex=0;mode.SelectedIndex=0;await Capture(main,"original-mode");Check(!Text(original).Contains("Traducción ·"),"original mode");
        mode.SelectedIndex=1;await Capture(main,"translation-mode");Check(!Text(original).Contains("Original ·"),"translation mode");
        mode.SelectedIndex=2;await Flush(main);Check(provider.Calls==1,"modes and typography make zero inference calls");
        vm.ApplyDetection(Track(2)); vm.SaveManual(vm.BeginLyricsEdit()!,new string('漢',20000)); await vm.WaitForCacheAsync(); await Capture(main,"long-20000");
        Check(Text(original).Contains(new string('漢',20000)) && original.VerticalOffset==0 && original.Selection.IsEmpty,"20000 characters retained; new document resets scroll");
        provider.Language="es"; await vm.GenerateInsightAsync(); await Capture(main,"same-language");
        Check(vm.InsightSaveState==ContentSaveState.Saved,"20000-character fixture quote and cache validation succeed");
        Check(mode.SelectedIndex==0 && !((ComboBoxItem)mode.Items[2]).IsEnabled && Text(original).Contains("se muestra una sola vez") && !Text(original).Contains("Traducción ·"),"same language has one original and explanation");
        var beforeLanguageCalls=provider.Calls; store.Fail=true; vm.TargetLanguage="en"; await vm.WaitForCacheAsync(); await Flush(main);
        Check(vm.RetryLanguageCommand.CanExecute(null) && vm.Preparation.Action==PreparationAction.RetryLanguage && Find<StatusNotice>(main).Any(n=>n.IsVisible && n.Message.Contains("idioma")) && !((ComboBoxItem)mode.Items[1]).IsEnabled,"failed simulated language save exposes active retry and no empty translation mode");
        await Capture(main,"language-failed"); store.Fail=false; await vm.RetryLanguageAsync(); await Flush(main); Check(provider.Calls==beforeLanguageCalls,"language retry makes zero inference calls");
        vm.TargetLanguage="es"; await vm.WaitForCacheAsync();
        vm.ApplyDetection(Track(3));vm.SaveManual(vm.BeginLyricsEdit()!,lyrics);await vm.WaitForCacheAsync(); provider.Language="mixed";await vm.GenerateInsightAsync();main.Width=600;main.Height=700;main.ReadingFontSize=16;await Flush(main);
        await Capture(main,"interactive-ready");
        foreach(var preferred in new[]{0,1,2})
        {
            var preferences=new WindowPreferenceStore(Path.Combine(output,$"mode-{preferred}.json"));
            preferences.Save(new WindowPreferences(Width:600,Height:700));
            var choose=new MainWindow {DataContext=vm,ShowActivated=false};choose.InitializePreferences(preferences);choose.Show();await Flush(choose);
            ((ComboBox)choose.FindName("ReadingMode")).SelectedIndex=preferred;await Flush(choose);choose.SavePreferences();choose.Close();
            Check(preferences.Read().ReadingMode==preferred,"explicit mode persisted "+preferred);
            var reopened=new MainWindow {DataContext=vm,ShowActivated=false};reopened.InitializePreferences(preferences);reopened.Show();await Flush(reopened);
            Check(((ComboBox)reopened.FindName("ReadingMode")).SelectedIndex==preferred,"restart restores compatible mode "+preferred);
            vm.ApplyDetection(Track(10+preferred));vm.SaveManual(vm.BeginLyricsEdit()!,lyrics+"\n\nTexto propio distinto para preferencia "+preferred);await vm.WaitForCacheAsync();await Flush(reopened);reopened.SavePreferences();
            Check(!vm.HasInsight,"fallback fixture has no compatible cached analysis "+preferred);
            Check(((ComboBox)reopened.FindName("ReadingMode")).SelectedIndex==0 && reopened.PreferredReadingMode==preferred && preferences.Read().ReadingMode==preferred,"original fallback preserves preference "+preferred);
            await vm.GenerateInsightAsync();await Flush(reopened);Check(((ComboBox)reopened.FindName("ReadingMode")).SelectedIndex==preferred,"compatible content restores explicit mode "+preferred);
            await Capture(reopened,"restart-mode-"+preferred);reopened.Close();
        }
        File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
        if(interactive){var closed=new TaskCompletionSource();main.Closed+=(_,_)=>closed.TrySetResult();await closed.Task;}else main.Close();
        await vm.StopAsync(); report.Add("PASS: every UX09 step completed; logical anchors, semantic details, native copy/selection, readonly, modes, 20000 characters, language retry; synthetic fixtures, no HTTP."); report.Add("App SHA256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location)))); File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
    }
    static Hyperlink MeaningLink(ReadingDocument view,string identity)=>view.Document.Blocks.OfType<Paragraph>().SelectMany(p=>p.Inlines.OfType<Hyperlink>()).Single(link=>AutomationProperties.GetAutomationId(link)==identity);
    static void CheckAnchor(ReadingDocument view,ReadingBookmark anchor,string step)
    {
        var pointer=ReadingAnchors.ResolveView(anchor,ReadingAnchors.Index(view.Document));
        Check(pointer is {HasValidLayout:true},step+" resolves logical anchor");
        var rect=pointer!.GetCharacterRect(LogicalDirection.Forward);
        report.Add($"{step}: key={anchor.View.Key}; character={anchor.View.Character}; top={rect.Top}; bottom={rect.Bottom}; viewport={view.ViewportHeight}; pixelOffset={view.VerticalOffset}");
        Check(rect.Bottom>0 && rect.Top<view.ViewportHeight,step+" anchored text remains visible");
    }
    static string Text(ReadingDocument view)=>new System.Windows.Documents.TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text;
    static void Check(bool value,string description){report.Add($"{description}: {value}");File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);if(!value)throw new InvalidOperationException(description);}
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
        public int Calls; public string Language = "mixed";
        public Task<LyricsSearchResult> FindAsync(CurrentTrack t,CancellationToken c)=>throw new InvalidOperationException("No network in UX08 fixture");
        public Task<SongInsight> GenerateAsync(CurrentTrack t,ResolvedLyrics l,CancellationToken c)=>GenerateAsync(t,l,"es",c);
        public Task<SongInsight> GenerateAsync(CurrentTrack t,ResolvedLyrics l,string language,CancellationToken c)
        {
            Calls++;var quote=l.Text.Contains("Abro una ventana")?"Abro una ventana":l.Text[..3];var result=new SongInsight(t.Revision,Language,Language==language?[]:l.Text.Split('\n').Select((line,index)=>(line,index)).Where(pair=>!string.IsNullOrWhiteSpace(pair.line)).Select(pair=>new TranslatedLine(pair.index+1,pair.index%2==1 && language=="es"?pair.line:"Verso traducido "+(pair.index+1),Language=="mixed" && pair.index%2==1?"es":Language=="mixed"?"en":Language)).ToArray(),string.Join(" ",Enumerable.Repeat("La voz narrativa busca una oportunidad de cambio y observa cómo entra la luz.",8)),["Cambio"],[new(quote,string.Join(" ",Enumerable.Repeat("La ventana permite mirar otra posibilidad.",50)))],[],[]){Model="fixture-model",TargetLanguage=language,Usage=new(100,50,150)};
#if CURRENT_NAVIGATION
            result=result with{Provenance=new(AiConnectionMode.ApiKey,"fixture-model",DateTimeOffset.Parse("2026-10-21T12:00:00Z"))};
#endif
            return Task.FromResult(result);
        }
    }
}
