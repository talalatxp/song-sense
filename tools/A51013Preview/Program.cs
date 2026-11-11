using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using SongSense.App;
using SongSense.Core;
public static partial class Preview
{
 static string output="";static bool before,quick,decisionsOnly,resumeOnly,a6,editorOnly,aboutOnly;static readonly List<object> report=[];
 [STAThread]public static void Main(string[] args)
 {
  if(args.Contains("--software"))RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
  aboutOnly=args.Contains("--about-only");a6=args.Contains("--a6");editorOnly=args.Contains("--editor-only");output=args[1];before=args.Contains("--before");quick=args.Contains("--quick");decisionsOnly=args.Contains("--decisions-only");resumeOnly=args.Contains("--resume-only");Directory.CreateDirectory(output);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(XDocument.Load(Path.Combine(args[0],"src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single().ToString());
  app.Dispatcher.InvokeAsync(async()=>{try{report.Add(new{hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))),version=BuildIdentity.ProductVersion,before});await Run();File.WriteAllText(Path.Combine(output,"PASS.txt"),"PASS synthetic preferences/editor/auxiliary/decision contracts; no network, clipboard writes or installed settings.");}catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Environment.ExitCode=1;}finally{File.WriteAllText(Path.Combine(output,"manifest.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(Environment.ExitCode);}});app.Run();
 }
 static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 static async Task Flush(Window w){await w.Dispatcher.InvokeAsync(w.UpdateLayout,DispatcherPriority.ApplicationIdle);await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);}
 static async Task Run()
 {
  foreach(var scale in quick?new[]{2d}:new[]{1d,1.57d,2d})
  {
   UiTypography.Apply(Application.Current.Resources,scale);
   if(aboutOnly){await VerifyCandidateAbout(scale);continue;}
   if(a6)
   {
    foreach(var width in new[]{360,860}) await ReadingAndWelcome(width,scale);
    foreach(var width in new[]{360,640,860}) await EditorFeedback(width,scale);
    continue;
   }
   if(editorOnly)
   {
    foreach(var width in new[]{360,640,860})
    foreach(var method in new[]{"button","enter","double"}) await Editor(width,scale,method);
    continue;
   }
   if(decisionsOnly){foreach(var width in new[]{360,480})await Decisions(width,scale);continue;}
   if(resumeOnly){await Resume(scale);continue;}
   foreach(var width in quick?new[]{360}:new[]{360,480}){await Preferences(width,scale);await Auxiliary(width,scale);await Decisions(width,scale);}
   foreach(var width in quick?new[]{360}:new[]{360,639,640,860})foreach(var method in quick?new[]{"button"}:new[]{"button","enter","double"})await Editor(width,scale,method);
  }
 }
 static TrackDetection Track(int revision)=>new(ApplicationState.Playing,new(revision,"Ventana propia con un título largo para revisar el contexto completo","Artista sintético","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null);
 static async Task Resume(double scale)
 {
  var provider=new Provider();var vm=new MainViewModel(new Store(),provider,insightProvider:provider);await vm.InitializeAsync();vm.ApplyDetection(Track(1));vm.SaveManual(vm.BeginLyricsEdit()!,"Pasaje propio que queda fijado.");vm.AutomaticUpdatesEnabled=false;vm.ApplyDetection(Track(2));var w=new MainWindow{DataContext=vm,Width=360,Height=480};try
  {
   w.Show();await Flush(w);Check(vm.CurrentTrack?.Revision==1&&vm.ShowTrackingPause,"Pause did not retain original");Capture(w,$"resume-ui{scale:0.00}-paused");var button=(Button)w.FindName("ResumeTrackingButton");button.BringIntoView();await Flush(w);Check(button.Focus()&&button.IsEnabled,"Resume not focusable");((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();await Flush(w);Check(vm.CurrentTrack?.Revision==2&&vm.AutomaticUpdatesEnabled&&!vm.ShowTrackingPause,"Resume did not adopt latest observation");Check(provider.Calls==0,"Resume generated AI");Capture(w,$"resume-ui{scale:0.00}-resumed");
   var edit = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!); edit.ManualText = new string('Z', 20000);
   var editor = new LyricsEditorWindow(edit, true) { Owner = w, Width = 360, Height = 480 };
   var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); edit.Committed += (_, _) => saved.TrySetResult();
   editor.Show(); await Flush(editor); var save = Descendants(editor).OfType<Button>().Single(b => Equals(b.Content, "Guardar letra"));
   save.BringIntoView(); await Flush(editor); Check(save.IsEnabled, "Valid 20000-character draft disabled"); Capture(editor, $"manual-ui{scale:0.00}-20000-ready");
   ((IInvokeProvider)new ButtonAutomationPeer(save).GetPattern(PatternInterface.Invoke)).Invoke(); await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
   Check(vm.OriginalLyrics.Length == 20000 && provider.Calls == 0 && !editor.IsVisible, "Native manual save lost content or generated AI"); await Flush(w); Capture(w, $"manual-ui{scale:0.00}-20000-saved");
  }finally{w.Close();await vm.StopAsync();}
 }
 static async Task Preferences(int width,double scale)
 {
  var provider=new Provider();var vm=new MainViewModel(new Store(),provider,insightProvider:provider);await vm.InitializeAsync();vm.ApplyDetection(Track(1));vm.SaveManual(vm.BeginLyricsEdit()!,"Abro una ventana\nY dejo entrar la luz");var main=new MainWindow{DataContext=vm,Width=860,Height=780};main.Show();var w=new PreferencesWindow(main,vm){Width=width,Height=width==360?480:600};try
  {
   w.Show();await Flush(w);var original=vm.OriginalLyrics;var slider=(Slider)w.FindName("ReadingSize");var range=(IRangeValueProvider)new SliderAutomationPeer(slider).GetPattern(PatternInterface.RangeValue);range.SetValue(24);await Flush(w);
   if(!before){Check(((TextBlock)w.FindName("ReadingSample")).FontSize==32,"Preview point conversion or double scale");Check(vm.OriginalLyrics==original,"Sample changed song");}
   slider.Focus();slider.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(w)!,Environment.TickCount,Key.Left){RoutedEvent=Keyboard.KeyDownEvent});await Flush(w);Check(slider.Value==23,"Slider Left does not reduce one point");Capture(w,$"preferences-{width}-ui{scale:0.00}-reading");if(w.FindName("ReadingSample") is FrameworkElement sample)sample.BringIntoView();await Flush(w);Capture(w,$"preferences-{width}-ui{scale:0.00}-sample");
   foreach(var e in Descendants(w).OfType<Expander>())e.IsExpanded=true;((ScrollViewer)w.Content).ScrollToEnd();await Flush(w);Capture(w,$"preferences-{width}-ui{scale:0.00}-expanded-bottom");
   foreach(var e in Descendants(w).OfType<Expander>())e.IsExpanded=false;((ScrollViewer)w.Content).ScrollToTop();await Flush(w);Capture(w,$"preferences-{width}-ui{scale:0.00}-closed");Check(provider.Searches==0&&provider.Calls==0,"Preferences consumed provider");
  }finally{w.Close();main.Close();await vm.StopAsync();}
 }
 static async Task Editor(int width,double scale,string method)
 {
  var p=new Provider();var vm=new MainViewModel(new Store(),p,insightProvider:p);await vm.InitializeAsync();vm.ApplyDetection(Track(1));await vm.SearchLyricsAsync();var edit=new LyricsEditorViewModel(vm,vm.BeginLyricsEdit()!);var w=new LyricsEditorWindow(edit,false){Width=width,Height=width==360?480:700};try
  {
   w.Show();await Flush(w);Capture(w,$"editor-{method}-{width}-ui{scale:0.00}-empty");var list=(ListBox)w.FindName("VersionsList");list.SelectedIndex=0;await Flush(w);var first=(ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);first.Focus();first.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(w)!,Environment.TickCount,Key.Down){RoutedEvent=Keyboard.KeyDownEvent});await Flush(w);Check(list.SelectedIndex==1&&!vm.HasPreparedLyrics,"Arrows must select without applying");list.SelectedIndex=4;list.ScrollIntoView(list.SelectedItem);await Flush(w);var item=(ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(4);item.Focus();
   if(method=="button")((Button)w.FindName("PreviewNext")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   else if(method=="enter")item.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(w)!,Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
   else list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent,Source=item});
   await Flush(w);Check(edit.VersionsStep==LyricsVersionsStep.Preview||edit.ComparingVersions,"Version did not open explicitly");Check(!vm.HasPreparedLyrics,"Browsing applied version");Capture(w,$"editor-{method}-{width}-ui{scale:0.00}-preview");
   ((ScrollViewer)w.FindName("VersionPreview")).ScrollToEnd();await Flush(w);Capture(w,$"editor-{method}-{width}-ui{scale:0.00}-preview-bottom");
   if(width<640){((Button)w.FindName("VersionBack")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Flush(w);Check(list.IsKeyboardFocusWithin&&list.SelectedIndex==4,"Back lost candidate focus");}
   var tabs=(TabControl)w.FindName("EditorTabs");tabs.SelectedIndex=1;edit.ManualText="";await Flush(w);Capture(w,$"editor-{method}-{width}-ui{scale:0.00}-manual-empty");edit.ManualText=new string('A',20001);await Flush(w);Check(!edit.SaveCommand.CanExecute(null)&&edit.ManualText.Length==20001,"Oversize draft was truncated or enabled");Capture(w,$"editor-{method}-{width}-ui{scale:0.00}-oversize");
   vm.ApplyDetection(Track(2));edit.ManualText="Borrador anterior que sigue siendo editable y copiable.";await Flush(w);Check(edit.CanEditDraft&&edit.CanCopyDraft&&!edit.SaveCommand.CanExecute(null),"Stale draft contract");var input=(TextBox)w.FindName("ManualInput");input.SelectAll();Check(ApplicationCommands.Copy.CanExecute(null,input),"Draft not copyable");Capture(w,$"editor-{method}-{width}-ui{scale:0.00}-stale");((ScrollViewer)w.FindName("EditorOuterScroll")).ScrollToEnd();await Flush(w);Capture(w,$"editor-{method}-{width}-ui{scale:0.00}-manual-footer");
   _=w.Dispatcher.BeginInvoke(()=>{var prompt=Application.Current.Windows.OfType<ConfirmationWindow>().Single();Descendants(prompt).OfType<Button>().Single(b=>Equals(b.Content,"Continuar editando")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},DispatcherPriority.ContextIdle);Check(!w.RequestClose()&&edit.ManualText.Contains("Borrador anterior"),"Continue discarded editor");
   Check(p.Searches==1&&p.Calls==0,"Browsing editor called provider");
  }finally
  {
   if(w.IsVisible){_=w.Dispatcher.BeginInvoke(()=>{var prompt=Application.Current.Windows.OfType<ConfirmationWindow>().Single();Descendants(prompt).OfType<Button>().Single(b=>Equals(b.Content,"Descartar borrador")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},DispatcherPriority.ContextIdle);w.RequestClose();}await vm.StopAsync();
  }
  // A separate current context confirms only the chosen version.
  var current=new MainViewModel(new Store(),new Provider());await current.InitializeAsync();current.ApplyDetection(Track(3));await current.SearchLyricsAsync();using var version=new LyricsEditorViewModel(current,current.BeginLyricsEdit()!);version.Selected=version.Options[2];await version.SaveAsync(true);Check(current.HasPreparedLyrics&&current.OriginalLyrics.Contains("versión 3"),"Explicit confirmation did not apply chosen version");await current.StopAsync();
 }
 static async Task Auxiliary(int width,double scale)
 {
  foreach(var kind in new[]{"details","about","diagnostic"})
  {
   var body=kind=="about"?$"Versión: {BuildIdentity.ProductVersion}\nBuild: {BuildIdentity.FileVersion}\nEstado: Aceptación externa pendiente":kind=="details"?"Canción: "+new string('A',120)+"\nArtista: Artista sintético\nLetra: Manual\nAnálisis: Sin solicitar":string.Join('\n',Enumerable.Repeat("Estado: causa sintética. Cuenta fixture@example.invalid",20));
   var factory=typeof(AuxiliaryContent).GetMethod(before?"Create":"CreatePresentation")!;var content=(FrameworkElement)factory.Invoke(null,new object?[]{new{Body=body},"Body",kind,"Descripción humana de la situación actual.",kind=="diagnostic",null})!;var w=new Window{Width=width,Height=width==360?480:600,MinWidth=360,Content=content,Background=(Brush)Application.Current.FindResource("Canvas")};try{w.Show();await Flush(w);Capture(w,$"aux-{kind}-{width}-ui{scale:0.00}-closed");foreach(var e in Descendants(w).OfType<Expander>())e.IsExpanded=true;await Flush(w);Capture(w,$"aux-{kind}-{width}-ui{scale:0.00}-open");Check(Descendants(w).OfType<Button>().Count()==2,"Auxiliary actions duplicated");Check(AuxiliaryContent.Sanitize(body).Contains("[cuenta omitida]")== (kind=="diagnostic"),"Diagnostic sanitizing changed");}finally{w.Close();}
  }
 }
 static async Task Decisions(int width,double scale)
 {
  foreach(var decision in new[]{"Eliminar datos guardados","Descartar cambios de API","Descartar borrador"})
  {
   var ctor=typeof(ConfirmationWindow).GetConstructors().Single();var args=new List<object?>{decision,string.Join(' ',Enumerable.Repeat("Los cambios se perderán si confirmas. Puedes volver y conservarlos.",12)),"Descartar","Continuar editando"};if(ctor.GetParameters().Length==5)args.Add("Canción propia · "+new string('A',120));else args[1]="Canción propia · "+new string('A',120)+"\n\n"+args[1];var w=(Window)ctor.Invoke(args.ToArray());w.Width=width;w.SizeToContent=SizeToContent.Manual;w.Height=480;try{w.Show();await Flush(w);var safe=Descendants(w).OfType<Button>().Single(x=>Equals(x.Content,"Continuar editando"));Check(safe.IsDefault&&safe.IsCancel&&safe.IsKeyboardFocused,"Unsafe dialog default");Capture(w,$"decision-{decision.Replace(' ','_')}-{width}-ui{scale:0.00}");}finally{w.Close();}
  }
 }
 static IEnumerable<DependencyObject> Descendants(DependencyObject p){for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++){var c=VisualTreeHelper.GetChild(p,i);yield return c;foreach(var d in Descendants(c))yield return d;}}
 static void Capture(Window w,string name)
 {
  var c=(FrameworkElement)w.Content;if(VisualTreeHelper.GetParent(c) is FrameworkElement p)c=p;var bmp=new RenderTargetBitmap((int)Math.Ceiling(c.ActualWidth),(int)Math.Ceiling(c.ActualHeight),96,96,PixelFormats.Pbgra32);var bg=new DrawingVisual();using(var d=bg.RenderOpen())d.DrawRectangle(w.Background??Brushes.Black,null,new Rect(c.RenderSize));bmp.Render(bg);bmp.Render(c);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(Path.Combine(output,name+".png"));enc.Save(f);report.Add(new{name,width=w.ActualWidth,height=w.ActualHeight,scale=(double)w.FindResource("UiControlFont")/14,dpi=VisualTreeHelper.GetDpi(w).PixelsPerInchX,renderDpi=96});
 }
 sealed class Store:ISettingsStore{public Task InitializeAsync(CancellationToken t=default)=>Task.CompletedTask;public Task<AppSettings> ReadSettingsAsync(CancellationToken t=default)=>Task.FromResult(new AppSettings());public Task SaveSettingsAsync(AppSettings s,CancellationToken t=default)=>Task.CompletedTask;public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken t=default)=>Task.FromResult(0);}
 sealed class Provider:ILyricsProvider,IInsightProvider
 {
  public int Searches,Calls;
  public Task<LyricsSearchResult> FindAsync(CurrentTrack track,CancellationToken t){Searches++;return Task.FromResult(new LyricsSearchResult(track.Revision,LyricsSearchKind.Candidates,null,Enumerable.Range(1,5).Select(i=>new LyricsCandidate(i,i==1?"Ventana propia":i==2?new string('A',40):string.Join(' ',Enumerable.Repeat("Ventana propia",10)),"Artista sintético","Edición "+i,TimeSpan.FromSeconds(180+i),false,$"Letra propia de versión {i}\n"+string.Join('\n',Enumerable.Repeat("Abro una ventana y dejo entrar la luz",50)),null)).ToArray()));}
  public Task<SongInsight> GenerateAsync(CurrentTrack t,ResolvedLyrics l,CancellationToken c){Calls++;throw new InvalidOperationException("AI forbidden");}
 }
}
