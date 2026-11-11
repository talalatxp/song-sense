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
  output=args[1];before=args.Contains("--before");quick=args.Contains("--quick");flows=args.Contains("--flows");keyOnly=args.Contains("--key-only");Directory.CreateDirectory(output);
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(XDocument.Load(Path.Combine(args[0],"src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single().ToString());
  app.Dispatcher.InvokeAsync(async()=>{try{report.Add(new{hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),version=BuildIdentity.ProductVersion,before});await Run();File.WriteAllText(Path.Combine(output,"PASS.txt"),"PASS controlled WPF. Synthetic accounts, external launcher and transport. No real HTTP or clipboard writes.");}catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Environment.ExitCode=1;}finally{File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(Environment.ExitCode);}});app.Run();
 }
 static void Check(bool v,string m){if(!v)throw new InvalidOperationException(m);}
 static async Task Flush(Window w){await w.Dispatcher.InvokeAsync(w.UpdateLayout,DispatcherPriority.ApplicationIdle);await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);}
 static async Task Run()
 {
  foreach(var scale in quick?new[]{2d}:new[]{1d,1.57d,2d})
  {
   UiTypography.Apply(Application.Current.Resources,scale);
   if(!flows&&!keyOnly) foreach(var size in quick?new[]{(360,480)}:new[]{(360,480),(600,700),(860,780),(1200,900)}) foreach(var pt in quick?new[]{24}:new[]{12,16,20,24})await Meaning(size,pt,scale);
   foreach(var width in quick?new[]{360}:new[]{360,640})
   {
    foreach(var state in keyOnly?Array.Empty<string>():flows?new[]{"confirm"}:new[]{"connect","loading","cancelled","empty","rejected","available","chosen","confirm","ready"})await Account(width,scale,state);
    foreach(var state in keyOnly?new[]{"key"}:flows?new[]{"cancelled"}:new[]{"clean","model","zero","text","decimal","consent","corrected","reverted","saved","tested","cancelled","help","key"})await Api(width,scale,state);
   }
  }
 }
 static async Task Meaning((int,int) size,int pt,double scale)
 {
  var store=new Store();store.Value=new(true,"fixture-model",20);var provider=new Lyrics();var vm=new MainViewModel(store,provider,insightProvider:provider);await vm.InitializeAsync();vm.ApplyDetection(new(ApplicationState.Playing,new(1,"Ventana propia","Artista sintético","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));vm.SaveManual(vm.BeginLyricsEdit()!,"Abro una ventana\nY dejo entrar la luz\n\nRegreso al pasaje\nY vuelvo a comenzar");await vm.GenerateInsightAsync();
  var w=new MainWindow{DataContext=vm,Width=size.Item1,Height=size.Item2};try
  {
   w.Show();w.ReadingFontSize=pt;((TabControl)w.FindName("ReadingTabs")).SelectedIndex=1;await Flush(w);var doc=(ReadingDocument)w.FindName("MeaningScroll");Capture(w,$"meaning-{size.Item1}-ui{scale:0.00}-pt{pt}-brief");
   var toggle=Links(doc).First(x=>new TextRange(x.ContentStart,x.ContentEnd).Text=="Leer explicación completa");toggle.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));await Flush(w);Capture(w,$"meaning-{size.Item1}-ui{scale:0.00}-pt{pt}-expanded");
   foreach(var l in Links(doc).Where(x=>AutomationProperties.GetAutomationId(x).StartsWith("Meaning")).ToArray())l.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));await Flush(w);Capture(w,$"meaning-{size.Item1}-ui{scale:0.00}-pt{pt}-open");
   var actual=new TextRange(doc.Document.ContentStart,doc.Document.ContentEnd).Text;Check(actual.Contains(provider.Result.Summary)&&actual.Contains("Explicación propia 3")&&actual.Contains("Alternativa propia 3"),"Expanded meaning lost content");
   var summary=doc.Document.Blocks.OfType<Paragraph>().First(x=>new TextRange(x.ContentStart,x.ContentEnd).Text.Contains(provider.Result.Summary));var run=summary.Inlines.OfType<Run>().First();doc.Selection.Select(run.ContentStart,run.ContentStart.GetPositionAtOffset(10)!);var selected=doc.Selection.Text;
   w.Width=size.Item1==360?599:size.Item1-20;await Flush(w);Check(doc.Selection.Text==selected,"Reflow lost summary selection");Check(ApplicationCommands.Copy.CanExecute(null,doc),"Selection not copyable");w.Width=size.Item1;await Flush(w);
   doc.ScrollToEnd();((ScrollViewer)w.FindName("MainOuterScroll")).ScrollToEnd();await Flush(w);Capture(w,$"meaning-{size.Item1}-ui{scale:0.00}-pt{pt}-bottom");Check(provider.Calls==1,"Editorial navigation generated AI");
  }finally{w.Close();await vm.StopAsync();}
 }
 static IEnumerable<Hyperlink> Links(ReadingDocument v)=>v.Document.Blocks.OfType<Paragraph>().SelectMany(x=>x.Inlines.OfType<Hyperlink>());
 static async Task Account(int width,double scale,string state)
 {
  var path=Path.Combine(output,"data",Guid.NewGuid()+".dpapi");var store=new ChatGptCredentialStore(path);var reg=new ChatGptRegistration{ClientId="oaiapp_fixture",Subject="fixture",Email="fixture@example.invalid",Model=state is "confirm" or "ready"?"fixture-model":null,AccessToken="fixture-access",RefreshToken="fixture-refresh",ExpiresAt=DateTimeOffset.UtcNow.AddHours(1),Scopes=[ChatGptOAuthClient.PlanScope]};await store.SaveAsync(new(){ActiveClientId=state=="connect"?null:reg.ClientId,Registrations=state=="connect"?[]:[reg]},CancellationToken.None);
  using var transport=new Transport(state);using var http=new HttpClient(transport);var connection=new ChatGptConnection(store,new ChatGptOAuthClient(http));await connection.InitializeAsync(CancellationToken.None);var vm=new ConnectionViewModel(connection,CancellationToken.None,_=>throw new InvalidOperationException("Unexpected authentication launch"));int launches=0;var w=new ConnectionWindow(vm){Width=width,Height=width==360?480:800,OpenUsagePage=_=>launches++};Task? op=null;
  try
  {
   if(state is "loading" or "cancelled" or "empty" or "rejected" or "available" or "chosen"){op=vm.ModelsAsync();if(state is "loading" or "cancelled"){await transport.Started.Task;if(state=="cancelled"){vm.Cancel();await op;}}else await op;}
   if(state=="chosen")vm.SelectedModel="fixture-model";
   if(state=="ready"){vm.CreditsDisabledConfirmed=true;await vm.EnablePlanAsync();}
   w.Show();await Flush(w);Capture(w,$"account-{state}-{width}-ui{scale:0.00}-top");
   if(state=="available")
   {
    var select=(ComboBox)w.FindName("ModelSelection");Check(select.SelectedIndex==-1&&vm.SelectedModel is null&&!vm.CanSaveModel,"Catalog selected automatically");
    if(!before){Check(((TextBlock)w.FindName("ModelPlaceholder")).IsVisible,"Missing placeholder");Check(!vm.Account.Contains("Registro"),"Technical registration leaked into account identity");}
   }
   if(state=="confirm")
   {
    var manage=(Button)w.FindName("ManageUsageButton");var check=(CheckBox)w.FindName("CreditsConfirmation");var enable=(Button)w.FindName("ConfirmEnableButton");((TextBlock)w.FindName("CreditsWarning")).BringIntoView();await Flush(w);Capture(w,$"account-confirm-{width}-ui{scale:0.00}-essential-warning");manage.BringIntoView();await Flush(w);manage.Focus();Check(manage.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))&&check.IsKeyboardFocusWithin,"Usage/confirmation focus order");manage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(launches==1&&!vm.CreditsDisabledConfirmed&&!connection.PlanUseEnabled,"Usage granted authorization");check.IsChecked=true;check.Focus();Check(check.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))&&enable.IsKeyboardFocused,"Confirmation/enable focus order");enable.BringIntoView();await Flush(w);Capture(w,$"account-confirm-{width}-ui{scale:0.00}-essential-enable");enable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Flush(w);Check(connection.PlanUseEnabled,"Synthetic explicit enable failed");
   }
   ((Expander)w.FindName("ConfigurationGroup")).IsExpanded=true;if(w.FindName("AdvancedGroup") is Expander a)a.IsExpanded=true;((ScrollViewer)w.FindName("ConnectionOuterScroll")).ScrollToEnd();await Flush(w);Capture(w,$"account-{state}-{width}-ui{scale:0.00}-bottom");
   if(state=="loading"){vm.Cancel();await op!;Check(vm.CatalogState==ModelCatalogState.Cancelled,"Catalog cancel failed");}
   var restarted=new ChatGptConnection(store,new ChatGptOAuthClient(http));await restarted.InitializeAsync(CancellationToken.None);Check(!restarted.PlanUseEnabled,"Session confirmation persisted");
  }finally{vm.Cancel();if(op is not null)await op;w.Close();await Flush(w);}
 }
 static async Task Api(int width,double scale,string state)
 {
  var store=new Store();bool configured=state is "tested" or "cancelled" or "key";store.Value=new(configured,"fixture-model",20);var secrets=new DpapiSecretStore(Path.Combine(output,"data",Guid.NewGuid()+".dpapi"));if(configured){using var key=Secure("synthetic-existing");await secrets.SaveKeyAsync(key);}
  using var transport=new Transport(state);using var http=new HttpClient(transport);var vm=new AiSettingsViewModel(new AiConfigurationService(store,secrets,store,new OpenAiConnectionProbe(http)),CancellationToken.None);var w=new AiSettingsWindow(vm){Width=width,Height=width==360?480:780};Task? op=null;
  try
  {
   w.Show();for(int i=0;i<100&&!vm.CanManage;i++)await Task.Delay(10);Check(vm.CanManage,"API initialize timeout");
   if(state is "model" or "corrected" or "reverted")vm.Model="invalid model";
   if(state is "zero" or "text" or "decimal")vm.Limit=state=="zero"?"0":state=="text"?"texto":"1.5";
   if(state=="consent"){vm.AiEnabled=true;using var seed=Secure("synthetic-replacement");await secrets.SaveKeyAsync(seed);await vm.InitializeAsync();vm.AiEnabled=true;}
   if(state is "model" or "zero" or "text" or "decimal" or "consent" or "corrected" or "reverted"){using var key=new SecureString();await vm.SaveAsync(key);Check(store.Writes==0&&store.Reservations==0&&transport.Calls==0,"Invalid form wrote or probed");}
   if(state is "corrected" or "reverted")vm.Model=state=="corrected"?"another-model":"fixture-model";
   if(state=="saved"){vm.Model="saved-model";using var key=new SecureString();await vm.SaveAsync(key);Check(store.Writes==1&&transport.Calls==0,"Local save probed");}
   if(state is "tested" or "cancelled"){op=vm.TestAsync();if(state=="cancelled"){await transport.Started.Task;((FrameworkElement)w.FindName("ActionFooter")).BringIntoView();await Flush(w);Capture(w,$"api-operation-{width}-ui{scale:0.00}-busy");var cancel=Descendants(w).OfType<Button>().Single(x=>Equals(x.Content,"Cancelar operación")||Equals(x.Content,"Cancelar"));cancel.BringIntoView();await Flush(w);Check(cancel.Focus()&&cancel.IsEnabled,"Operation cancellation unreachable");Capture(w,$"api-operation-{width}-ui{scale:0.00}-cancel");vm.Cancel();}await op;Check(store.Reservations==1&&transport.Calls==1,"Explicit test not accounted once");}
   if(state=="key")((PasswordBox)w.FindName("KeyInput")).Password="fixture-new";
   if(state=="help")foreach(var e in Descendants(w).OfType<Expander>().Where(x=>Equals(x.Header,"Requisitos del modelo")||Equals(x.Header,"Datos y retención")||Equals(x.Header,"Datos y almacenamiento")))e.IsExpanded=true;
   await Flush(w);Capture(w,$"api-{state}-{width}-ui{scale:0.00}-focus");
   if(state is "model" or "zero" or "text" or "decimal" or "consent")
   {
    var field=(Control)w.FindName(state=="model"?"ModelInput":state=="consent"?"NoticeInput":"LimitInput");Check(field.IsKeyboardFocusWithin,"Invalid field focus lost");
    if(!before&&state!="consent")Check(Equals(field.BorderBrush,w.FindResource("Error")),"Invalid field border not semantic Error");
   }
   var scroll=(ScrollViewer)w.FindName("FormScroll");scroll.ScrollToTop();await Flush(w);Capture(w,$"api-{state}-{width}-ui{scale:0.00}-top");scroll.ScrollToEnd();await Flush(w);Capture(w,$"api-{state}-{width}-ui{scale:0.00}-bottom");
   if(state is "corrected" or "reverted")Check(store.Writes==0&&transport.Calls==0,"Correction saved or probed");
   if(state=="key")
   {
    _ = w.Dispatcher.BeginInvoke(()=>{var prompt=Application.Current.Windows.OfType<ConfirmationWindow>().Single();Descendants(prompt).OfType<Button>().Single(x=>Equals(x.Content,"Continuar editando")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},DispatcherPriority.ContextIdle);
    Check(!await w.RequestCloseAsync()&&w.IsVisible&&vm.HasDraftKey,"Continue editing lost draft");
    Check(((PasswordBox)w.FindName("KeyInput")).SecurePassword.Length==11&&store.Writes==0&&transport.Calls==0,"Continue editing changed key or probed");
   }
  }finally
  {
   vm.Cancel();if(op is not null)await op;((PasswordBox)w.FindName("KeyInput")).Clear();vm.Model=store.Value.Model??"";vm.Limit=store.Value.DailyRequestLimit.ToString();vm.AiEnabled=store.Value.AiEnabled;vm.NoticeAccepted=false;w.Close();await Flush(w);
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
  public AppSettings Value=new();public int Writes,Reservations;
  public Task InitializeAsync(CancellationToken t=default)=>Task.CompletedTask;public Task<AppSettings> ReadSettingsAsync(CancellationToken t=default)=>Task.FromResult(Value);public Task SaveSettingsAsync(AppSettings s,CancellationToken t=default){Writes++;Value=s;return Task.CompletedTask;}public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken t=default)=>Task.FromResult(Reservations);public Task<bool> TryReserveRequestAsync(DateOnly d,CancellationToken t=default){Reservations++;return Task.FromResult(true);}
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
   Calls++;if(state is "loading" or "cancelled"){Started.TrySetResult();await Task.Delay(Timeout.Infinite,t);}
   if(state=="rejected")return new(HttpStatusCode.Forbidden){Content=new StringContent("{}")};
   if(r.RequestUri!.AbsolutePath.Contains("models"))return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{models=state=="empty"?Array.Empty<object>():new object[]{new{slug="fixture-model",display_name="Modelo sintético",visibility="list"},new{slug="fixture-alternative",display_name="Modelo alternativo",visibility="list"}}}))};
   return new(HttpStatusCode.OK){Content=new StringContent("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"ok\\\":true}\"}]}]}")};
  }
 }
}
