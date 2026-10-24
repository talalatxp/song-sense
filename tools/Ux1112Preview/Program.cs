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
        var data = Path.Combine(output,"data",Guid.NewGuid().ToString("N"));
        foreach(var scenario in (interactive ? Array.Empty<string>() : new[]{"no-account","pending","denied","models","available","empty","error","loading","return","multiple","expired","api"}))
        {
            var directory=Path.Combine(data,scenario);Directory.CreateDirectory(directory);var store=new ChatGptCredentialStore(Path.Combine(directory,"session.dpapi"));
            var registrations=new List<ChatGptRegistration>();
            if(scenario is not ("no-account" or "pending"))
            for(int i=0;i<(scenario=="multiple"?2:1);i++)registrations.Add(new(){ClientId="oaiapp_fixture"+i,Subject="fixture-subject"+i,Email="fixture@example.invalid",AccessToken=scenario=="expired"?null:"fixture-access",RefreshToken=scenario=="expired"?null:"fixture-refresh",ExpiresAt=DateTimeOffset.UtcNow.AddHours(1),Scopes=scenario is "denied" or "expired"?[]:[ChatGptOAuthClient.PlanScope],Model=scenario is "return" or "multiple" or "api"?"fixture-model":null});
            await store.SaveAsync(new(){Registrations=registrations,ActiveClientId=registrations.FirstOrDefault()?.ClientId,PendingClientId=scenario=="pending"?"oaiapp_pending":null,Mode=scenario=="api"?AiConnectionMode.ApiKey:AiConnectionMode.ChatGptIncluded,LoginOutcome=scenario is "denied" or "expired"?ChatGptLoginOutcome.Failed:ChatGptLoginOutcome.None,LoginFailure=scenario=="expired"?AiFailure.SessionExpired:AiFailure.Authentication,LoginStep=scenario=="denied"?AuthenticationStep.Consent:AuthenticationStep.None},CancellationToken.None);
            using var handler=new Transport{Scenario=scenario};using var http=new System.Net.Http.HttpClient(handler);var connection=new ChatGptConnection(store,new ChatGptOAuthClient(http));await connection.InitializeAsync(CancellationToken.None);
            var vm=new ConnectionViewModel(connection,CancellationToken.None,_=>throw new InvalidOperationException("No browser in fixture"));var window=new ConnectionWindow(vm){ShowActivated=false};window.Show();await Flush(window);
            Task? loading=null;
            if(scenario is "available" or "empty" or "error" or "loading") {loading=vm.ModelsAsync();if(scenario!="loading")await loading;}
            foreach(var (w,h) in new[]{(640,800),(360,480)}){window.Width=w;window.Height=h;await Capture(window,$"chat-{scenario}-{w}");
#if CURRENT_FORMS
                if(scenario=="return"&&w==360){var modeChoice=Find<Expander>(window).First(e=>Equals(e.Header,"Cambiar modo de IA"));modeChoice.IsExpanded=true;await Capture(window,"chat-mode-choices");modeChoice.IsExpanded=false;window.FocusPreparationStep(PreparationAction.ReviewPlan);await Capture(window,"chat-return-focus");}
                MeasureFooter(window);Check(!vm.CreditsDisabledConfirmed,"credit confirmation never automatic");Check(vm.ShowAccountSelector==(scenario=="multiple"),"account picker only offers real choice");
#endif
            }
#if CURRENT_FORMS
            if(scenario=="return"){Check(vm.Step==3&&vm.SessionSummary.Contains("fixture-model"),"return opens confirmation with saved account/model");vm.CreditsDisabledConfirmed=true;await vm.EnablePlanAsync();Check(vm.Step==4&&handler.InferenceCalls==0,"session enable makes no inference");}
            if(scenario=="available"){vm.SelectedModel="fixture-model";await vm.SelectModelAsync();Check(vm.Step==3&&handler.InferenceCalls==0,"save model makes no inference");}
            if(scenario=="loading"){Check(vm.CanCancel,"catalog loading cancellable");vm.Cancel();await loading!;}
#endif
            window.Close();await vm.StopAsync();await Flush(window);
        }
        var path=Path.Combine(data,"api");Directory.CreateDirectory(path);var settings=new SqliteSettingsStore(Path.Combine(path,"settings.db"));await settings.InitializeAsync();var secrets=new DpapiSecretStore(Path.Combine(path,"key.dpapi"));
        using var probeTransport=new Transport();using var probeHttp=new System.Net.Http.HttpClient(probeTransport);
        var service=new AiConfigurationService(settings,secrets,settings,new OpenAiConnectionProbe(probeHttp));
        var apiVm=new AiSettingsViewModel(service,CancellationToken.None);var api=new AiSettingsWindow(apiVm){ShowActivated=interactive,Title="UX11 y UX12 · API sintética"};api.Show();await Flush(api);
        foreach(var(w,h)in new[]{(600,780),(360,480)}){api.Width=w;api.Height=h;await Capture(api,$"api-clean-{w}");
#if CURRENT_FORMS
            MeasureFooter(api);
#endif
        }
#if CURRENT_FORMS
        var key=(PasswordBox)api.FindName("KeyInput");key.Password="fixture-only";apiVm.Model="has spaces";apiVm.Limit="101";apiVm.AiEnabled=true;
        using(var secure=key.SecurePassword)await apiVm.SaveAsync(secure);await Capture(api,"api-invalid");Check(apiVm.FirstInvalidField=="Model"&&key.SecurePassword.Length>0,"first invalid field focused, new key retained only in field");
        apiVm.Model="fixture-model";apiVm.Limit="20";apiVm.NoticeAccepted=true;
        using(var secure=key.SecurePassword)await apiVm.SaveAsync(secure);key.Clear();Check(!apiVm.HasChanges,"save clears dirty state");
        apiVm.Model="draft-model";await apiVm.TestAsync();Check(probeTransport.InferenceCalls==0,"dirty probe blocked before transport");key.Password="discard-only";
        var dispatcher=api.Dispatcher;_ = dispatcher.BeginInvoke(()=>{var prompt=Application.Current.Windows.OfType<ConfirmationWindow>().Single();Find<Button>(prompt).First(b=>Equals(b.Content,"Continuar editando")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},DispatcherPriority.ContextIdle);
        api.Close();await Flush(api);Check(api.IsLoaded&&apiVm.Model=="draft-model"&&key.SecurePassword.Length>0,"cancel close preserves editing");
        var owner = new MainWindow { ShowActivated = false }; owner.Show(); api.Owner = owner; var exit = new TaskCompletionSource(); owner.ExitRequested += (_,_)=>exit.TrySetResult();
        _ = dispatcher.BeginInvoke(()=>{var prompt=Application.Current.Windows.OfType<ConfirmationWindow>().Single();Find<Button>(prompt).First(b=>Equals(b.Content,"Continuar editando")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},DispatcherPriority.ContextIdle);
        owner.RequestExit();await Flush(api);Check(api.IsLoaded&&!exit.Task.IsCompleted,"cancel application exit preserves API draft");
        _ = dispatcher.BeginInvoke(()=>{var prompt=Application.Current.Windows.OfType<ConfirmationWindow>().Single();Find<Button>(prompt).First(b=>Equals(b.Content,"Descartar y cerrar")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},DispatcherPriority.ContextIdle);
        owner.RequestExit(); await exit.Task; await Flush(api); owner.Close(); Check(!api.IsLoaded&&key.SecurePassword.Length==0,"explicit discard closes and clears newly typed key");Check(await secrets.ReadKeyAsync()=="fixture-only","discarded key does not replace saved secret");Check((await settings.ReadSettingsAsync()).Model=="fixture-model","discard does not persist draft");
        apiVm=new AiSettingsViewModel(service,CancellationToken.None);api=new AiSettingsWindow(apiVm){Title="UX11 y UX12 · API sintética",ShowActivated=interactive};api.Show();await Flush(api);api.Width=360;api.Height=480;Check(apiVm.Model=="fixture-model"&&!apiVm.HasChanges,"reopen restores saved settings");
        probeTransport.Hold=true;var testing=apiVm.TestAsync();await Flush(api);Check(apiVm.CanCancel,"probe progress and cancellation");await Capture(api,"api-probe-progress");apiVm.Cancel();await testing;Check(probeTransport.InferenceCalls==1,"one explicit synthetic probe only");
        await Capture(api,"api-probe-cancelled");
#endif
        api.Width=600;api.Height=780;await Capture(api,"interactive-ready");File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
        if(interactive){var done=new TaskCompletionSource();api.Closed+=(_,_)=>done.TrySetResult();await done.Task;}else { api.Close(); await Flush(api); }
        report.Add("App SHA256="+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))));File.WriteAllLines(Path.Combine(output,"manifest.txt"),report);
    }
    static void Check(bool pass,string label){report.Add($"{label}: {pass}");if(!pass)throw new InvalidOperationException(label);}
    static void MeasureFooter(Window window){var footer=(FrameworkElement)window.FindName("ActionFooter");var point=footer.TransformToAncestor(window).Transform(new Point());Check(point.Y>=0&&point.Y+footer.ActualHeight<=window.ActualHeight,"footer entirely inside window");report.Add($"footer: window={window.Width}x{window.Height}; y={point.Y}; height={footer.ActualHeight}");}
    sealed class Transport:System.Net.Http.HttpMessageHandler
    {
        public string Scenario="";public bool Hold {get;set;}public int InferenceCalls;
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken token)
        {
            if(Scenario=="loading"){await Task.Delay(Timeout.Infinite,token);}
            if(request.RequestUri!.AbsolutePath.Contains("models"))return new(Scenario=="error"?System.Net.HttpStatusCode.Forbidden:System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StringContent(Scenario=="empty"?"{\"models\":[]}":"{\"models\":[{\"visibility\":\"list\",\"slug\":\"fixture-model\",\"display_name\":\"Modelo de prueba\"}]}")};
            InferenceCalls++;if(Hold)await Task.Delay(Timeout.Infinite,token);
            return new(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StringContent("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"ok\\\":true}\"}]}]}")};
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