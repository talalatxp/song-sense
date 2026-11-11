using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
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
 static string output=""; static bool before,quick,resizeCheck; static readonly List<object> report=[];
 [STAThread] public static void Main(string[] args)
 {
  output=args[1];before=args.Contains("--before");quick=args.Contains("--quick");resizeCheck=args.Contains("--resize-check");Directory.CreateDirectory(output);
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var resources=XDocument.Load(Path.Combine(args[0],"src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
  app.Dispatcher.InvokeAsync(async()=>{try{await Run();File.WriteAllText(Path.Combine(output,"PASS.txt"),"PASS synthetic main composition and explicit actions; no real HTTP.");}catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Environment.ExitCode=1;}finally{File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(Environment.ExitCode);}});app.Run();
 }
 static void Check(bool v,string reason){if(!v)throw new InvalidOperationException(reason);}
 static async Task Flush(Window w){await w.Dispatcher.InvokeAsync(w.UpdateLayout,DispatcherPriority.ApplicationIdle);await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);}
 static async Task Run()
 {
  report.Add(new{version=BuildIdentity.ProductVersion,hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),before});
  foreach(var scale in (resizeCheck?new[]{1d}:quick?new[]{2d}:new[]{1d,1.57d,2d})) foreach(var size in (quick?new[]{(360,480)}:new[]{(360,480),(600,700),(860,780),(1200,900)}))
  {
   foreach(var scenario in (resizeCheck?new[]{"ready"}:quick?new[]{"ready","paused"}:new[]{"ready","paused","lyrics","empty","instrumental","notfound","error","analysis","search","disabled","quota","languagefail","savefail","title40","title120"}))
   foreach(var points in (resizeCheck?new[]{20}:quick?new[]{24}:scenario is "ready" or "paused" or "analysis" ? new[]{12,16,20,24}:new[]{16}))
   {
    UiTypography.Apply(Application.Current.Resources,scale);var store=new Store(scenario);var provider=new Provider(scenario);var cache=scenario=="savefail"?new Cache():null;
    var vm=new MainViewModel(store,provider,insightProvider:provider,cache:cache);await vm.InitializeAsync();MainWindow? w=null;Task? op=null;
    try
    {
     if(scenario!="empty")vm.ApplyDetection(new(ApplicationState.Playing,new(1,scenario=="title40"?new string('A',40):scenario=="title120"?string.Join(' ',Enumerable.Repeat("Canción propia",10)):"Ventana propia",scenario.StartsWith("title")?"Un artista de nombre extraordinariamente largo para verificar el acceso completo":"Artista sintético","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));
     if(scenario is "instrumental" or "notfound" or "error")await vm.SearchLyricsAsync();
     else if(scenario=="search")op=vm.SearchLyricsAsync();
     else if(scenario!="empty")
     {
      vm.SaveManual(vm.BeginLyricsEdit()!,"Abro una ventana\nY dejo entrar la luz\n\nRegreso a este pasaje\nY vuelvo a comenzar");await vm.WaitForCacheAsync();
      if(scenario is "ready" or "paused" or "title40" or "title120" or "savefail")await vm.GenerateInsightAsync();
      if(scenario=="analysis")op=vm.GenerateInsightAsync();
      if(scenario=="languagefail"){vm.TargetLanguage="en";await vm.WaitForCacheAsync();}
     }
     if(scenario=="paused"){vm.AutomaticUpdatesEnabled=false;vm.ApplyDetection(new(ApplicationState.Playing,new(2,"Otra canción","Otro artista","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));}
     if(op is not null)await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
     w=new MainWindow{DataContext=vm,Width=size.Item1,Height=size.Item2};w.Show();w.ReadingFontSize=points;await Flush(w);
     var tabs=(TabControl)w.FindName("ReadingTabs");var calls=provider.Calls;
     foreach(var tab in new[]{0,1})
     {
      tabs.SelectedIndex=tab;await Flush(w);var name=$"{scenario}-{size.Item1}-ui{scale:0.00}-pt{points}-tab{tab}";Capture(w,name);
      var doc=(ReadingDocument)w.FindName(tab==0?"TranslationScroll":"MeaningScroll");
      report.Add(new{name,window=$"{w.ActualWidth}x{w.ActualHeight}",scale,points,tab,viewport=doc.ViewportHeight,identityHeight=((FrameworkElement)w.FindName("IdentityBlock")).ActualHeight,readingTop=doc.TransformToAncestor(w).Transform(new Point()).Y,barHeight=(tabs.Template.FindName("ReadingBar",tabs) as FrameworkElement)?.ActualHeight,visibleDocument=VisibleHeight(doc,w),docHeight=doc.ActualHeight,padding=doc.Document.PagePadding.ToString(),outer=((ScrollViewer)w.FindName("MainOuterScroll")).ScrollableHeight,document=new TextRange(doc.Document.ContentStart,doc.Document.ContentEnd).Text,preparation=vm.Preparation.Cause.ToString(),action=vm.Preparation.Action.ToString(),calls=provider.Calls});
      Check(provider.Calls==calls,"Navigation generated AI");
      if(!before && tab==1 && scenario=="lyrics")
      {
       var read=w.FindName("ReadOriginalButton") as Button;Check(read is {IsVisible:true},"Meaning empty needs original reading choice");read!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Flush(w);Check(tabs.SelectedIndex==0 && provider.Calls==calls,"Read original must navigate without AI");tabs.SelectedIndex=1;
      }
     }
     if(!before)
     {
      if(scenario=="quota")
      {
       var button=(Button)w.FindName("PrimaryActionButton");Check(button.Content.ToString()!.Contains("restablece"),"Quota recovery must explain availability");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Flush(w);Check(provider.Calls==calls,"Quota details generated AI");
      }
      if(scenario=="error")
      {
       var beforeSearches=provider.Searches;((Button)w.FindName("PrimaryActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Flush(w);Check(provider.Searches==beforeSearches+1 && provider.Calls==0,"Lyrics retry must be explicit and local to its notice");
      }
      if(scenario=="languagefail")
      {
       store.LanguageFails=false;((Button)w.FindName("PrimaryActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await vm.WaitForCacheAsync();await Flush(w);Check(vm.LanguageNoticeKind!=NoticeKind.Error && provider.Calls==0,"Language retry must not generate AI");
      }
      if(scenario=="savefail")
      {
       cache!.Fail=false;var notice=(StatusNotice)w.FindName("SaveStatus");((Button)notice.ActionContent!).Command.Execute(null);
       for(int i=0;i<100 && notice.IsVisible;i++){await Task.Delay(10);await Flush(w);}
       Check(!notice.IsVisible && provider.Calls==calls,"Retry local save must preserve existing inference");
      }
      var menu=(Button)w.FindName("ApplicationMenuButton");var options=menu.ContextMenu.Items.OfType<MenuItem>().First(x=>x.Header.ToString()!.StartsWith("Opciones de lectura"));
      if(size.Item1<600){options.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Flush(w);Check(((System.Windows.Controls.Primitives.Popup)w.FindName("ReadingToolsPopup")).IsOpen,"Reading options not reachable");var language=(ComboBox)w.FindName("ReadingLanguage");language.Focus();await vm.RefreshPreparationTimeAsync();await Flush(w);Check(language.IsKeyboardFocusWithin,"Presentation refresh must not steal language focus");((System.Windows.Controls.Primitives.Popup)w.FindName("ReadingToolsPopup")).IsOpen=false;}
      if(scenario.StartsWith("title"))
      {
       var details=menu.ContextMenu.Items.OfType<MenuItem>().First(x=>x.Header.ToString()!.StartsWith("Detalles"));details.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Flush(w);
       var owned=w.OwnedWindows.Cast<Window>().Single(x=>x.Title.StartsWith("Detalles"));Check(Descendants(owned).OfType<TextBox>().Any(x=>x.Text.Contains(vm.TrackTitle)) || Descendants(owned).OfType<RichTextBox>().Any(x=>new TextRange(x.Document.ContentStart,x.Document.ContentEnd).Text.Contains(vm.TrackTitle)),"Full title must be available without tooltip");owned.Close();
      }
      if(size.Item1==360 && vm.HasPreparedLyrics)
      {
       tabs.SelectedIndex=scenario=="ready"?1:0;await Flush(w);menu.ContextMenu.Items.OfType<MenuItem>().First(x=>Equals(x.Header,"Ir a lectura")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Flush(w);Capture(w,$"{scenario}-{size.Item1}-ui{scale:0.00}-pt{points}-reading-task");
      }
      var e=(Expander)w.FindName("ReadingActions");e.IsExpanded=false;if(resizeCheck)File.AppendAllText(Path.Combine(output,"steps.txt"),$"focus {size}\n");menu.Focus();foreach(var width in new[]{599,600,599}){if(resizeCheck)File.AppendAllText(Path.Combine(output,"steps.txt"),$"set {size} => {width}\n");w.Width=width;await Flush(w);if(resizeCheck)File.AppendAllText(Path.Combine(output,"steps.txt"),$"flushed {width}\n");Check(!e.IsExpanded,"Resize opened state");Check(menu.IsKeyboardFocused,$"Menu focus lost across 599/600: width={width}; focused={System.Windows.Input.Keyboard.FocusedElement}; visible={menu.IsVisible}; enabled={menu.IsEnabled}; active={w.IsActive}");}
      if(op is not null){((Button)w.FindName("PrimaryActionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await op.WaitAsync(TimeSpan.FromSeconds(5));Check(provider.Cancelled,"Cancel must reach provider token");}
     }
    }finally{vm.CancelLyrics();vm.CancelInsight();if(op is not null)await op.WaitAsync(TimeSpan.FromSeconds(5));w?.Close();await vm.StopAsync();}
   }
  }
 }
 static double VisibleHeight(FrameworkElement document,Window w)
 {
  if(!document.IsVisible)return 0;var scroll=(ScrollViewer)w.FindName("MainOuterScroll");var bounds=document.TransformToAncestor(scroll).TransformBounds(new Rect(document.RenderSize));bounds.Intersect(new Rect(0,0,scroll.ViewportWidth,scroll.ViewportHeight));return bounds.IsEmpty?0:bounds.Height;
 }
 static IEnumerable<DependencyObject> Descendants(DependencyObject p){for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++){var c=VisualTreeHelper.GetChild(p,i);yield return c;foreach(var d in Descendants(c))yield return d;}}
 static void Capture(Window w,string name){var c=(FrameworkElement)w.Content;if(VisualTreeHelper.GetParent(c) is FrameworkElement parent)c=parent;var bmp=new RenderTargetBitmap((int)Math.Ceiling(c.ActualWidth),(int)Math.Ceiling(c.ActualHeight),96,96,PixelFormats.Pbgra32);var bg=new DrawingVisual();using(var d=bg.RenderOpen())d.DrawRectangle(w.Background,null,new Rect(c.RenderSize));bmp.Render(bg);bmp.Render(c);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(Path.Combine(output,name+".png"));enc.Save(f);}
 sealed class Store(string scenario):ISettingsStore,IOutputLanguageSettings
 {
  public bool LanguageFails=scenario=="languagefail";
  public Task InitializeAsync(CancellationToken t=default)=>Task.CompletedTask;
  public Task<AppSettings> ReadSettingsAsync(CancellationToken t=default)=>Task.FromResult(new AppSettings(scenario!="disabled","fixture-model",20));
  public Task SaveSettingsAsync(AppSettings s,CancellationToken t=default)=>Task.CompletedTask;
  public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken t=default)=>Task.FromResult(scenario=="quota"?20:0);
  public Task<string> ReadOutputLanguageAsync(CancellationToken t=default)=>Task.FromResult("es");
  public Task SaveOutputLanguageAsync(string language,CancellationToken t=default)=>LanguageFails?Task.FromException(new IOException("synthetic save failure")):Task.CompletedTask;
 }
 sealed class Provider(string scenario):ILyricsProvider,IInsightProvider
 {
  public int Calls,Searches;public bool Cancelled;public TaskCompletionSource Started=new();
  async Task Wait(CancellationToken t){Started.TrySetResult();try{await Task.Delay(Timeout.Infinite,t);}catch(OperationCanceledException){Cancelled=true;throw;}}
  public async Task<LyricsSearchResult> FindAsync(CurrentTrack track,CancellationToken t){Searches++;if(scenario=="search")await Wait(t);if(scenario=="error")throw new LyricsProviderException(LyricsFailure.Offline);return new(track.Revision,scenario=="instrumental"?LyricsSearchKind.Instrumental:LyricsSearchKind.NotFound,scenario=="instrumental"?new(track.Revision,"",LyricsOrigin.Lrclib,1,true):null,[]);}
  public async Task<SongInsight> GenerateAsync(CurrentTrack track,ResolvedLyrics lyrics,CancellationToken t){Calls++;if(scenario=="analysis")await Wait(t);return new(track.Revision,"es",[],"La ventana representa una oportunidad de cambio. El narrador vuelve al mismo lugar y decide empezar de nuevo; la luz enlaza el recuerdo con una posibilidad abierta.",["Cambio"],[new("La luz","Una posibilidad de cambio")],["Otra lectura posible"],["Interpretación tentativa"]);}
 }
 sealed class Cache:ICacheStore
 {
  public bool Fail=true;
  public Task<CachedSong?> ReadSongAsync(CurrentTrack t,CancellationToken c=default)=>Task.FromResult<CachedSong?>(null);
  public Task SaveLyricsAsync(CurrentTrack t,ResolvedLyrics? l,CancellationToken c=default)=>Fail?Task.FromException(new IOException("synthetic cache failure")):Task.CompletedTask;
  public Task<SongInsight?> ReadInsightAsync(ResolvedLyrics l,string m,string v,CancellationToken c=default,CurrentTrack? t=null,string scope="api-key",string targetLanguage="es")=>Task.FromResult<SongInsight?>(null);
  public Task SaveInsightAsync(CurrentTrack t,ResolvedLyrics l,string m,string v,SongInsight s,CancellationToken c=default,string scope="api-key",string targetLanguage="es")=>Fail?Task.FromException(new IOException("synthetic cache failure")):Task.CompletedTask;
  public Task DeleteSongAsync(CurrentTrack t,CancellationToken c=default)=>Task.CompletedTask;public Task ClearAsync(CancellationToken c=default)=>Task.CompletedTask;
 }
}
