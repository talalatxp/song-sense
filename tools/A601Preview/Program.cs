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
 static string output=""; static bool before,quick,keyboard,paused,result; static readonly List<object> report=[];
 [STAThread] public static void Main(string[] args)
 {
  result=args.Contains("--result");keyboard=args.Contains("--keyboard");paused=args.Contains("--paused");if(args.Contains("--software"))RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;output=args[1];before=args.Contains("--before");quick=args.Contains("--quick");Directory.CreateDirectory(output);
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var resources=XDocument.Load(Path.Combine(args[0],"src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
  app.Dispatcher.InvokeAsync(async()=>{try{if(keyboard) await KeyboardRun(); else await Run();File.WriteAllText(Path.Combine(output,"PASS.txt"),"PASS synthetic main composition and explicit actions; no real HTTP.");}catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Environment.ExitCode=1;}finally{File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(Environment.ExitCode);}});app.Run();
 }
 static void Check(bool v,string reason){if(!v)throw new InvalidOperationException(reason);}
 static async Task Flush(Window w){await w.Dispatcher.InvokeAsync(w.UpdateLayout,DispatcherPriority.ApplicationIdle);await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);}
 static async Task Run()
 {
  report.Add(new {version=BuildIdentity.ProductVersion, hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))), before});
  foreach(var scale in new[]{1d,1.57d,2d}) foreach(var size in (quick?new[]{(360,480)}:new[]{(360,480),(600,700),(860,780)}))
  foreach(var points in new[]{12,16,20,24})
  {
   UiTypography.Apply(Application.Current.Resources,scale); var provider=new Provider("analysis");
   var vm=new MainViewModel(new Store("analysis"),provider,insightProvider:provider);await vm.InitializeAsync();
   vm.ApplyDetection(new(ApplicationState.Playing,new(1,"Ventana propia","Artista sintético","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));
   var text=string.Join("\n\n",Enumerable.Range(1,24).Select(i=>$"Pasaje {i}: abro una ventana\nY dejo entrar la luz\nRegreso a este lugar"));
   vm.SaveManual(vm.BeginLyricsEdit()!,text);await vm.WaitForCacheAsync();
   if(result){provider.Block=false;await vm.GenerateInsightAsync();provider.Block=true;}
   if(paused)vm.AutomaticUpdatesEnabled=false;
   var w=new MainWindow{DataContext=vm,Width=size.Item1,Height=size.Item2};w.Show();w.ReadingFontSize=points;await Flush(w);
   var tabs=(TabControl)w.FindName("ReadingTabs");var outer=(ScrollViewer)w.FindName("MainOuterScroll");
   Task? op=null;
   try
   {
    foreach(var phase in new[]{"before","during","cancelled"})
    {
     if(phase=="during"){op=result?vm.RegenerateInsightAsync():vm.GenerateInsightAsync();await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));}
     if(phase=="cancelled")
     {
      var counterBeforeCancel=vm.Activity.Counter;var cancel=(Button)w.FindName("PrimaryActionButton");
      Check(cancel.Focus(),"Cancel must accept keyboard focus");
      ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(cancel).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
      await op!.WaitAsync(TimeSpan.FromSeconds(5));Check(provider.Cancelled,"Cancellation did not reach token");
      Check(vm.HasReadableOriginal && vm.Activity.Counter==counterBeforeCancel,"Cancellation lost lyrics or changed request counter");
      Check(vm.Usage.State.ToString()!="NoSolicitado","Cancellation must not claim no request");
     }
     foreach(var tab in new[]{0,1})
     {
      tabs.SelectedIndex=tab;outer.ScrollToTop();await Flush(w);
      var doc=(ReadingDocument)w.FindName(tab==0?"TranslationScroll":"MeaningScroll");doc.ScrollToHome();await Flush(w);
      foreach(var position in phase=="during" && (tab==0 || vm.HasInsight)?new[]{"initial","advanced"}:new[]{"initial"})
      {
       if(position=="advanced"){doc.Focus();doc.ScrollToVerticalOffset(450);await Flush(w);}
       var name=$"{phase}-{size.Item1}-ui{scale:0.00}-pt{points}-tab{tab}-{position}";Capture(w,name);
       var cancel=(Button)w.FindName("PrimaryActionButton");
       var documentBounds=ClientBounds(doc,w);var actionBounds=ClientBounds(cancel,w);
       report.Add(new{name,scale,points,tab,phase,position,clientWidth=((FrameworkElement)w.Content).ActualWidth,clientHeight=((FrameworkElement)w.Content).ActualHeight,dpi=VisualTreeHelper.GetDpi(w).PixelsPerInchX,visibleDocumentBounds=VisibleBounds(doc,w),visibleActionBounds=VisibleBounds(cancel,w),documentBounds,actionBounds,documentHeight=doc.ActualHeight,visibleDocument=VisibleHeight(doc,w),visibleAction=VisibleHeight(cancel,w),actionHeight=cancel.ActualHeight,offset=doc.VerticalOffset,extent=doc.ExtentHeight,viewport=doc.ViewportHeight,outer=outer.VerticalOffset,outerExtent=outer.ScrollableHeight,requests=vm.Activity.Counter,usage=vm.Usage.State.ToString(),provider.Calls});
       if(!before && phase=="before" && tab==0) Check(VisibleHeight(doc,w)>0,"Reading is outside the initial viewport: "+name);
       if(!before && (phase=="during" || phase=="cancelled"))
       {
        Check(VisibleBounds(cancel,w).Width>=cancel.ActualWidth-1 && VisibleHeight(cancel,w)>=cancel.ActualHeight-1,"Cancel not fully visible: "+name);
        if(tab==0 || vm.HasInsight){Check(VisibleHeight(doc,w)>doc.FontSize*2,"No readable fragment: "+name);if(position=="advanced" && doc.ExtentHeight>doc.ViewportHeight+1)Check(doc.VerticalOffset>0,"Overflowing document did not advance");}
        Check(outer.VerticalOffset==0 && outer.ScrollableHeight<1,"Operation must not need outer scroll: "+name);
       }
       if(!before && tab==1 && phase!="before" && !vm.HasInsight){var read=(Button)w.FindName("ReadOriginalButton");Check(VisibleHeight(read,w)>=read.ActualHeight-1 && read.ActualHeight>0,"Read original must stay reachable");}
       Check(provider.Calls==(phase=="before"?0:1)+(result?1:0),"Navigation generated inference");
      }
     }
    }
   }
   finally{vm.CancelInsight();if(op is not null)await op;w.Close();await vm.StopAsync();}
  }
 }
 static async Task KeyboardRun()
 {
  UiTypography.Apply(Application.Current.Resources,2);var p=new Provider("analysis");var vm=new MainViewModel(new Store("analysis"),p,insightProvider:p);await vm.InitializeAsync();
  vm.ApplyDetection(new(ApplicationState.Playing,new(1,"Ventana de prueba A6","Artista sintético","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));
  vm.SaveManual(vm.BeginLyricsEdit()!,string.Join("\n\n",Enumerable.Range(1,30).Select(i=>$"Pasaje {i}: una ventana abierta\nDeja entrar la luz")));
  var w=new MainWindow{Title="A6-01 · Prueba sintética de teclado",DataContext=vm,Width=360,Height=480};w.Show();w.ReadingFontSize=24;
  var op=vm.GenerateInsightAsync();await p.Started.Task;await Flush(w);var doc=(ReadingDocument)w.FindName("TranslationScroll");doc.Focus();
  w.PreviewKeyDown+=(_,e)=>File.AppendAllText(Path.Combine(output,"keys.txt"),$"{e.Key} · {System.Windows.Input.Keyboard.FocusedElement}\n");
  File.WriteAllText(Path.Combine(output,"ready.txt"),"Synthetic only; no real HTTP");Capture(w,"keyboard-initial");
  var deadline=DateTime.UtcNow.AddMinutes(8);
  while(!p.Cancelled && DateTime.UtcNow<deadline)await Task.Delay(100);
  Check(p.Cancelled,"Native keyboard did not cancel");await op;await Flush(w);Capture(w,"keyboard-cancelled");
  report.Add(new{p.Calls,p.Cancelled,vm.Activity.Counter,usage=vm.Usage.State.ToString(),vm.HasReadableOriginal,offset=doc.VerticalOffset,visibleDocument=VisibleHeight(doc,w)});
  Check(vm.HasReadableOriginal && p.Calls==1 && vm.Usage.State==AiUsageState.NoComunicado,"Native cancellation lost invariants");
  w.Close();await vm.StopAsync();
 }
 static object ClientBounds(FrameworkElement element,Window w)
 {
  var r=element.TransformToAncestor((FrameworkElement)w.Content).TransformBounds(new Rect(element.RenderSize));
  return new{x=r.X,y=r.Y,width=r.Width,height=r.Height};
 }
 static Rect VisibleBounds(FrameworkElement document,Window w)
 {
  if(!document.IsVisible)return new Rect(0,0,0,0);var scroll=(ScrollViewer)w.FindName("MainOuterScroll");var bounds=document.TransformToAncestor(scroll).TransformBounds(new Rect(document.RenderSize));bounds.Intersect(new Rect(0,0,scroll.ViewportWidth,scroll.ViewportHeight));return bounds.IsEmpty?new Rect(0,0,0,0):bounds;
 }
 static double VisibleHeight(FrameworkElement element,Window w)=>VisibleBounds(element,w).Height;
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
  public int Calls,Searches;public bool Block=true,Cancelled;public TaskCompletionSource Started=new();
  async Task Wait(CancellationToken t){Started.TrySetResult();try{await Task.Delay(Timeout.Infinite,t);}catch(OperationCanceledException){Cancelled=true;throw;}}
  public async Task<LyricsSearchResult> FindAsync(CurrentTrack track,CancellationToken t){Searches++;if(scenario=="search")await Wait(t);if(scenario=="error")throw new LyricsProviderException(LyricsFailure.Offline);return new(track.Revision,scenario=="instrumental"?LyricsSearchKind.Instrumental:LyricsSearchKind.NotFound,scenario=="instrumental"?new(track.Revision,"",LyricsOrigin.Lrclib,1,true):null,[]);}
  public async Task<SongInsight> GenerateAsync(CurrentTrack track,ResolvedLyrics lyrics,CancellationToken t){Calls++;if(scenario=="analysis" && Block)await Wait(t);return new(track.Revision,"es",[],"La ventana representa una oportunidad de cambio. El narrador vuelve al mismo lugar y decide empezar de nuevo; la luz enlaza el recuerdo con una posibilidad abierta.",["Cambio"],[new("La luz","Una posibilidad de cambio")],["Otra lectura posible"],["Interpretación tentativa"]);}
 }
}
