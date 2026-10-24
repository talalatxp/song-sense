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
using SongSense.Infrastructure;
public static partial class Preview
{
    static string output = ""; static readonly List<string> report = [];
    static string Lyrics => string.Join("\n\n", Enumerable.Range(1, 50).Select(i => $"Abro una ventana {i}\nY dejo entrar la luz"));
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output); var oldError = Path.Combine(output,"error.txt"); if(File.Exists(oldError))File.Delete(oldError); RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single(); app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        app.Dispatcher.InvokeAsync(async () => { try { await RunSelected(args); } catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); Environment.ExitCode = 1; } app.Shutdown(); }); app.Run();
    }
    static Task RunSelected(string[] args)
    {
#if CURRENT_LIFETIME
        if(args.Contains("--acceptance")) return Acceptance();
#endif
        return Run(args.Contains("--interactive"));
    }
    static TrackDetection Track(long revision) => new(ApplicationState.Playing, new(revision, "Ventana de prueba " + revision, "Artista sintético", "Álbum propio", TimeSpan.FromMinutes(3), PlaybackState.Playing), null);
    static async Task Flush(Window window) { await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle); await Task.Delay(30); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
    static async Task Run(bool interactive)
    {
        var provider=new Provider();var vm=new MainViewModel(new Store(),provider,insightProvider:provider);await vm.InitializeAsync();
        vm.ApplyDetection(Track(1));vm.SaveManual(vm.BeginLyricsEdit()!,Lyrics);await vm.GenerateInsightAsync();
        var prefsPath=Path.Combine(output,"data",Guid.NewGuid()+".json");var main=new MainWindow{DataContext=vm,ShowActivated=interactive,Title="UX13 y UX14 · Prueba local"};main.InitializePreferences(new WindowPreferenceStore(prefsPath)); main.Show();
        foreach(var(w,h)in new[]{(860,780),(600,700),(360,480)})foreach(var size in new[]{12,16,20,24}){main.Width=w;main.Height=h;main.ReadingFontSize=size;await Capture(main,$"reading-{w}-{size}");}
        var tabs=(TabControl)main.FindName("ReadingTabs");tabs.SelectedIndex=1;main.Width=860;main.Height=780;main.ReadingFontSize=16;await Capture(main,"meaning");
#if CURRENT_LIFETIME
        var app=Application.Current;ThemeResources.Apply(app.Resources,true);await Capture(main,"system-palette-synthetic");ThemeResources.Apply(app.Resources,false);
        var preference=new PreferencesWindow(main,vm){ShowActivated=false};preference.Show();await Capture(preference,"preferences");preference.Width=360;preference.Height=480;await Capture(preference,"preferences-min");preference.Close();
        main.WindowState=WindowState.Maximized;await Capture(main,"maximized");main.WindowState=WindowState.Normal;
        int hints=0,releases=0;bool heldCancelled=false;var ended=new TaskCompletionSource();main.Closed+=(_,_)=>ended.TrySetResult();
        main.KeepInTray=true;main.ConfigureCloseBehavior(async()=>{releases++;await vm.StopAsync();},()=>hints++);
        main.Close();await Flush(main);Check(!main.IsVisible&&releases==0&&hints==1,"X hides, preserves session and explains first hide");
        main.OpenFromTray();await Flush(main);main.Close();await Flush(main);Check(!main.IsVisible&&hints==1,"repeated hide does not repeat notification");main.OpenFromTray();
        main.SavePreferences();var saved=new WindowPreferenceStore(prefsPath).Read();Check(saved.KeepInTray&&saved.TrayHintShown,"close preference and first-hide acknowledgement survive reload");
        using(var tray=new DesktopTray(vm,main.OpenFromTray,main.RequestExit))
        {
            main.Width=600;main.Height=700;main.ReadingFontSize=16;
            var more=(Button)main.FindName("ApplicationMenuButton");more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Capture(main,"application-menu");more.ContextMenu.IsOpen=false;
            if(interactive){File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);await ended.Task;}
            else
            {
                provider.Hold=true;var generation=vm.RegenerateInsightAsync();await provider.Started.Task;await Capture(main,"held-analysis");
                _=main.Dispatcher.BeginInvoke(()=>{var dialog=Application.Current.Windows.OfType<ConfirmationWindow>().Single();Find<Button>(dialog).First(b=>Equals(b.Content,"Salir y cancelar")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},DispatcherPriority.ContextIdle);
                main.RequestExit();await ended.Task;await generation;heldCancelled=provider.Cancelled;Check(releases==1&&heldCancelled,"exit cancels held provider and waits for release");
            }
        }
        if(!interactive)
        {
            var exitModel=new MainViewModel(new Store(),new Provider());await exitModel.InitializeAsync();var exitWindow=new MainWindow{DataContext=exitModel,KeepInTray=false,ShowActivated=false};var exited=new TaskCompletionSource();exitWindow.Closed+=(_,_)=>exited.TrySetResult();exitWindow.ConfigureCloseBehavior(()=>exitModel.StopAsync(),()=>throw new InvalidOperationException("Must not hide"));exitWindow.Show();exitWindow.Close();await exited.Task;await Flush(exitWindow);Check(!exitWindow.IsLoaded,"close-as-exit completes without recursive Closing");
        }
        report.Add("Build="+BuildIdentity.ProductVersion+"; file="+BuildIdentity.FileVersion);
#else
        main.Close();await vm.StopAsync();
#endif
        report.Add("App SHA256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))));File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
    }
    static void Check(bool value,string name){report.Add($"{name}: {value}");if(!value)throw new InvalidOperationException(name);}
    sealed class Store:ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t=default)=>Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t=default)=>Task.FromResult(new AppSettings(true,"fixture-model"));
        public Task SaveSettingsAsync(AppSettings s,CancellationToken t=default)=>Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken t=default)=>Task.FromResult(0);
    }
    sealed class Provider:ILyricsProvider,IInsightProvider
    {
        public bool Hold {get;set;} public bool Cancelled; public readonly TaskCompletionSource Started=new();
        public Task<LyricsSearchResult> FindAsync(CurrentTrack t,CancellationToken c)=>throw new InvalidOperationException("No real network");
        public async Task<SongInsight> GenerateAsync(CurrentTrack t,ResolvedLyrics l,CancellationToken c)
        {
            if(Hold){Started.TrySetResult();try{await Task.Delay(Timeout.Infinite,c);}catch(OperationCanceledException){Cancelled=true;throw;}}
            return new(t.Revision,"es",[],string.Join(" ",Enumerable.Repeat("La ventana representa una oportunidad abierta al cambio.",16)),["Cambio","Esperanza"],[new("Abro una ventana 1","La imagen permite explorar otra posibilidad.")],["Puede tratarse de un recuerdo."],["No consta contexto biográfico."]){Model="fixture-model"};
        }
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

}
