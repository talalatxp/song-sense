using System.IO;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
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
 static string output=""; static bool before,quick,flows,keyOnly;static List<object> report=[];
 [STAThread] public static void Main(string[] args)
 {
  RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;output=args[1];before=args.Contains("--before");quick=args.Contains("--quick");flows=args.Contains("--flows");keyOnly=args.Contains("--key-only");Directory.CreateDirectory(output);
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(XDocument.Load(Path.Combine(args[0],"src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single().ToString());
  app.Dispatcher.InvokeAsync(async()=>{try{report.Add(new{hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),version=BuildIdentity.ProductVersion,before});if(args.Contains("--main"))await MainMessages.Execute(Path.Combine(output,"main"),before,args.Contains("--known"));else await Run();File.WriteAllText(Path.Combine(output,"PASS.txt"),"PASS controlled WPF. Synthetic accounts, external launcher and transport. No real HTTP or clipboard writes.");}catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Environment.ExitCode=1;}finally{File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(Environment.ExitCode);}});app.Run();
 }
 static void Check(bool v,string m){if(!v)throw new InvalidOperationException(m);}
 static async Task Flush(Window w){await w.Dispatcher.InvokeAsync(w.UpdateLayout,DispatcherPriority.ApplicationIdle);await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);}
 static async Task Run()
 {
  foreach(var scale in new[]{1d,1.57d,2d})
  {
   UiTypography.Apply(Application.Current.Resources,scale);
   foreach(var width in new[]{360,600})
   foreach(var state in new[]{"clean","modified","zero","corrected","reverted","saving","testing","tested","known","error","saveerror","cancelled","consent","loaderror"})await Api(width,scale,state);
  }
 }
 static async Task Api(int width,double scale,string state)
 {
  var store=new Store{FailRead=state=="loaderror"};bool configured=state is "tested" or "cancelled" or "testing" or "known" or "error";store.Value=new(configured,"fixture-model",20);var secrets=new DpapiSecretStore(Path.Combine(output,"data",Guid.NewGuid()+".dpapi"));if(configured){using var key=Secure("synthetic-existing");await secrets.SaveKeyAsync(key);}
  using var transport=new Transport(state);using var http=new HttpClient(transport);var vm=new AiSettingsViewModel(new AiConfigurationService(store,secrets,store,new OpenAiConnectionProbe(http)),CancellationToken.None);var w=new AiSettingsWindow(vm){Width=width,Height=width==360?480:780};Task? op=null;
  try
  {
   w.Show();for(int i=0;i<100 && (vm.IsOperating || !vm.CanManage && state!="loaderror");i++)await Task.Delay(10);Check(state=="loaderror" || vm.CanManage,"API initialize timeout");
   if(state is "modified" or "model" or "corrected" or "reverted")vm.Model=state=="modified"?"another-model":"invalid model";
   if(state is "zero" or "text" or "decimal")vm.Limit=state=="zero"?"0":state=="text"?"texto":"1.5";
   if(state=="consent"){vm.AiEnabled=true;using var seed=Secure("synthetic-replacement");await secrets.SaveKeyAsync(seed);await vm.InitializeAsync();vm.AiEnabled=true;}
   if(state is "model" or "zero" or "text" or "decimal" or "consent" or "corrected" or "reverted"){using var key=new SecureString();await vm.SaveAsync(key);Check(store.Writes==0&&store.Reservations==0&&transport.Calls==0,"Invalid form wrote or probed");}
   if(state is "corrected" or "reverted")vm.Model=state=="corrected"?"another-model":"fixture-model";
   if(state=="saved"){vm.Model="saved-model";using var key=new SecureString();await vm.SaveAsync(key);Check(store.Writes==1&&transport.Calls==0,"Local save probed");}
   if(state is "saving" or "saveerror"){vm.Model="saved-model";store.BlockWrite=state=="saving";store.FailWrite=state=="saveerror";using var key=new SecureString();op=vm.SaveAsync(key);if(state=="saving")await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));else await op;}
   if(state is "tested" or "cancelled" or "testing" or "known" or "error"){op=vm.TestAsync();if(state is "cancelled" or "testing"){await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));((FrameworkElement)w.FindName("ActionFooter")).BringIntoView();await Flush(w);Capture(w,$"api-operation-{width}-ui{scale:0.00}-busy");var cancel=Descendants(w).OfType<Button>().Single(x=>Equals(x.Content,"Cancelar operación")||Equals(x.Content,"Cancelar"));cancel.BringIntoView();await Flush(w);Check(cancel.Focus()&&cancel.IsEnabled,"Operation cancellation unreachable");Capture(w,$"api-operation-{width}-ui{scale:0.00}-cancel");if(state=="cancelled")vm.Cancel();}if(state!="testing")await op;Check(store.Reservations==1&&transport.Calls==1,"Explicit test not accounted once");}
   if(state=="key")((PasswordBox)w.FindName("KeyInput")).Password="fixture-new";
   if(state=="help")foreach(var e in Descendants(w).OfType<Expander>().Where(x=>Equals(x.Header,"Requisitos del modelo")||Equals(x.Header,"Datos y retención")||Equals(x.Header,"Datos y almacenamiento")))e.IsExpanded=true;
   await Flush(w);Capture(w,$"api-{state}-{width}-ui{scale:0.00}-focus");
   if(state is "model" or "zero" or "text" or "decimal" or "consent")
   {
    var field=(Control)w.FindName(state=="model"?"ModelInput":state=="consent"?"NoticeInput":"LimitInput");Check(field.IsKeyboardFocusWithin,"Invalid field focus lost");
    if(!before&&state!="consent")Check(Equals(field.BorderBrush,w.FindResource("Error")),"Invalid field border not semantic Error");
   }
   var scroll=(ScrollViewer)w.FindName("FormScroll");scroll.ScrollToTop();await Flush(w);Capture(w,$"api-{state}-{width}-ui{scale:0.00}-top");scroll.ScrollToEnd();await Flush(w);Capture(w,$"api-{state}-{width}-ui{scale:0.00}-bottom");
   var footer=(FrameworkElement)w.FindName("ActionFooter");scroll.ScrollToVerticalOffset(scroll.VerticalOffset+footer.TransformToAncestor(scroll).Transform(new Point()).Y);await Flush(w);Capture(w,$"api-{state}-{width}-ui{scale:0.00}-footer");
   if(!before && state is "error" or "saveerror")Check(((Expander)w.FindName("OperationResult")).IsExpanded,"New operation error was not revealed automatically");
   if(vm.HasOperationResult){var result=(Expander)w.FindName("OperationResult");result.IsExpanded=true;await Flush(w);scroll.ScrollToVerticalOffset(scroll.VerticalOffset+result.TransformToAncestor(scroll).Transform(new Point()).Y);await Flush(w);Capture(w,$"api-{state}-{width}-ui{scale:0.00}-result");}
   if(state=="known")Check(vm.Activity.Last?.State==AiUsageState.Comunicado && vm.Activity.Last.Tokens?.TotalTokens==7,"Known usage lost");
   if(state is "corrected" or "reverted"){Check(store.Writes==0&&transport.Calls==0,"Correction saved or probed");Check(((Control)w.FindName("ModelInput")).IsKeyboardFocusWithin,"Correction lost field focus");}
   if(!before)
   {
    var draft=(StatusNotice)w.FindName("DraftNotice");
    var classification=draft.GetType().GetProperty("Presentation")?.GetValue(draft)?.ToString();
    var expected=state is "zero" or "consent" or "loaderror"?"Attention":state is "modified" or "corrected" or "saving" or "testing" or "saveerror"?"Contextual":"Routine";
    Check(classification==expected,"Unexpected draft message presentation: "+state+"/"+classification);
    Check(!draft.HasDetails,"Draft instruction must not add a redundant detail");
    var texts=Descendants(w).OfType<TextBlock>().Where(x=>x.IsVisible).Select(x=>x.Text).ToArray();
    if(state=="clean")Check(texts.Count(x=>x.Contains("Sin cambios pendientes"))==1,"Clean status repeated");
    if(state is "error" or "saveerror")Check(((Expander)w.FindName("OperationResult")).IsExpanded,"Operation error hidden by collapsed result");
    var focused=Keyboard.FocusedElement;var count=transport.Calls;
    ((Expander)w.FindName("OperationResult")).IsExpanded=true;await Flush(w);
    Check(transport.Calls==count,"Opening result generated a request");
    if(focused is not null)Check(ReferenceEquals(focused,Keyboard.FocusedElement),"Opening detail stole focus");
   }
   report.Add(new{state,width,scale,store.Writes,store.Reservations,transport.Calls,vm.FormInstruction,vm.HasChanges,vm.CanTest,vm.LastOperationKind});

  }finally
  {
   store.Release.TrySetResult();if(state!="saving")vm.Cancel();if(op is not null)await op;((PasswordBox)w.FindName("KeyInput")).Clear();vm.Model=store.Value.Model??"";vm.Limit=store.Value.DailyRequestLimit.ToString();vm.AiEnabled=store.Value.AiEnabled;vm.NoticeAccepted=false;w.Close();await Flush(w);
  }
 }
 static SecureString Secure(string text){var s=new SecureString();foreach(var c in text)s.AppendChar(c);return s;}
 static IEnumerable<DependencyObject> Descendants(DependencyObject p){for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++){var c=VisualTreeHelper.GetChild(p,i);yield return c;foreach(var d in Descendants(c))yield return d;}}
 static void Capture(Window w,string name)
 {
  if(flows&&!name.Contains("essential")&&!name.Contains("operation"))return;
  var c=(FrameworkElement)w.Content;if(VisualTreeHelper.GetParent(c) is FrameworkElement p)c=p;var bmp=new RenderTargetBitmap((int)Math.Ceiling(c.ActualWidth),(int)Math.Ceiling(c.ActualHeight),96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();using(var d=background.RenderOpen())d.DrawRectangle(w.Background,null,new Rect(c.RenderSize));bmp.Render(background);bmp.Render(c);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(Path.Combine(output,name+".png"));enc.Save(f);report.Add(new{name,size=$"{w.ActualWidth}x{w.ActualHeight}",ui=(double)w.FindResource("UiControlFont")/14,reading=w is MainWindow m?m.ReadingFontSize:0,dpi=VisualTreeHelper.GetDpi(w).PixelsPerInchX});
 }
 sealed class Store:ISettingsStore,IRequestBudget
 {
  public AppSettings Value=new();public int Writes,Reservations;public bool BlockWrite,FailWrite,FailRead;public TaskCompletionSource Started=new(),Release=new();
  public Task InitializeAsync(CancellationToken t=default)=>Task.CompletedTask;public Task<AppSettings> ReadSettingsAsync(CancellationToken t=default)=>FailRead?Task.FromException<AppSettings>(new IOException("Synthetic read failure")):Task.FromResult(Value);public async Task SaveSettingsAsync(AppSettings s,CancellationToken t=default){if(BlockWrite){Started.TrySetResult();await Release.Task.WaitAsync(t);}if(FailWrite)throw new IOException("Synthetic write failure");Writes++;Value=s;}public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken t=default)=>Task.FromResult(Reservations);public Task<bool> TryReserveRequestAsync(DateOnly d,CancellationToken t=default){Reservations++;return Task.FromResult(true);}
 }
 sealed class Lyrics:ILyricsProvider,IInsightProvider
 {
  public int Calls;public SongInsight Result=new(1,"es",[],string.Join(" ",Enumerable.Repeat("La ventana representa una oportunidad de cambio. El narrador vuelve a su pasaje y decide comenzar de nuevo.",8)),["Cambio","Memoria"],Enumerable.Range(1,3).Select(i=>new ExplainedMetaphor("Cita propia "+i,"Explicación propia "+i)).ToArray(),Enumerable.Range(1,3).Select(i=>"Alternativa propia "+i).ToArray(),["Interpretación tentativa","No representa una afirmación del autor"]);
  public Task<LyricsSearchResult> FindAsync(CurrentTrack track,CancellationToken t)=>throw new InvalidOperationException("No search expected");public Task<SongInsight> GenerateAsync(CurrentTrack track,ResolvedLyrics lyrics,CancellationToken t){Calls++;return Task.FromResult(Result);}
 }
 sealed class Transport(string state):HttpMessageHandler
 {
  public int Calls;public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)
  {
   Calls++;if(state is "loading" or "cancelled" or "testing"){Started.TrySetResult();await Task.Delay(Timeout.Infinite,t);}
   if(state is "rejected" or "error")return new(HttpStatusCode.Forbidden){Content=new StringContent("{}")};
   if(r.RequestUri!.AbsolutePath.Contains("models"))return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{models=state=="empty"?Array.Empty<object>():new object[]{new{slug="fixture-model",display_name="Modelo sintético",visibility="list"},new{slug="fixture-alternative",display_name="Modelo alternativo",visibility="list"}}}))};
   return new(HttpStatusCode.OK){Content=new StringContent((state=="known"?"{\"usage\":{\"input_tokens\":5,\"output_tokens\":2,\"total_tokens\":7},":"{")+"\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"ok\\\":true}\"}]}]}")};
  }
 }
}
