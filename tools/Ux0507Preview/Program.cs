using System.IO;
using System.Windows;
using System.Windows.Automation;
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
    [STAThread]
    public static void Main(string[] args)
    {
        output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var resources = XDocument.Load(Path.Combine(args[0], "src/SongSense.App/App.xaml")).Root!.Elements().First().Elements().Single();
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString());
        app.Dispatcher.InvokeAsync(async () => { try { await Run(args.Contains("--interactive")); } catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); Environment.ExitCode = 1; } app.Shutdown(); }); app.Run();
    }
    static TrackDetection Track(long id) => new(ApplicationState.Playing, new(id, "Ventana de prueba con un título largo · versión acústica " + id, "Artista sintético con nombre largo", "Álbum propio", TimeSpan.FromMinutes(3), PlaybackState.Playing), null);
    static async Task Run(bool interactive)
    {
        var provider = new Provider(); var vm = new MainViewModel(new Store(), provider, insightProvider: provider);
        await vm.InitializeAsync(); vm.ApplyDetection(Track(1));
        vm.SaveManual(vm.BeginLyricsEdit()!, string.Join("\n\n", Enumerable.Range(1, 50).Select(i => $"Abro una ventana {i}\nY dejo entrar la luz")));
        await vm.GenerateInsightAsync();
        var main = new MainWindow { DataContext = vm, Title = "UX05–07 · Lectura sintética", ShowActivated = interactive }; main.Show();
        foreach (var (width, height) in new[] { (860, 780), (600, 700), (360, 480) })
        {
            main.Width = width; main.Height = height;
            foreach (var size in new[] { 16, 24 })
            {
                main.ReadingFontSize = size;
                ((TabControl)main.FindName("ReadingTabs")).SelectedIndex = 0;
                await Capture(main, $"reading-{width}-{size}"); Measure(main, $"reading-{width}-{size}");
                ((TabControl)main.FindName("ReadingTabs")).SelectedIndex = 1;
                await Capture(main, $"meaning-{width}-{size}"); Measure(main, $"meaning-{width}-{size}");
            }
        }
        main.WindowState = WindowState.Maximized; await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Capture(main, "maximized"); Measure(main, "maximized"); main.WindowState = WindowState.Normal;
#if CURRENT_READING
        main.Width = 860; main.Height = 780;
        ((TabControl)main.FindName("ReadingTabs")).SelectedIndex = 0;
        await main.Dispatcher.InvokeAsync(() => main.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        var scroll = (ScrollViewer)main.FindName("TranslationScroll"); scroll.ScrollToVerticalOffset(180);
        await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var offset = scroll.VerticalOffset; var viewport = scroll.ViewportHeight; var calls = provider.Calls;
        main.OpenPreferences(main, new RoutedEventArgs());
        var preferences = Application.Current.Windows.OfType<PreferencesWindow>().Single();
        await Capture(preferences, "preferences-normal"); preferences.Width = 360; preferences.Height = 480; await Capture(preferences, "preferences-minimum");
        if (scroll.VerticalOffset != offset || scroll.ViewportHeight != viewport || provider.Calls != calls) throw new InvalidOperationException("Preferences changed the document or sent inference");
        preferences.Close(); report.Add($"preferences: offset={offset}; viewport={viewport}; unchanged=True; inference={provider.Calls}");
        var more = Find<Button>(main).Single(b => b.ContextMenu is not null);
        ((MenuItem)more.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await main.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var details = Application.Current.Windows.Cast<Window>().Single(w => w.Title.StartsWith("Detalles de la canción"));
        await Capture(details, "song-details");
        if (!Find<TextBox>(details).Single().Text.Contains("Álbum propio")) throw new InvalidOperationException("Song details are missing metadata");
        details.Close(); report.Add("song-details: opened=True; metadata=True; original-document-preserved=True");
        main.Width = 360; main.Height = 480; vm.AutomaticUpdatesEnabled = false;
        var paused = Track(4); vm.ApplyDetection(paused with { State = ApplicationState.Paused, Track = paused.Track! with { Playback = PlaybackState.Paused } });
        await Capture(main, "frozen-minimum"); Measure(main, "frozen-minimum");
        var fresh = new MainViewModel(new Store(), provider); await fresh.InitializeAsync(); fresh.ApplyDetection(Track(5)); main.DataContext = fresh;
        await Capture(main, "search-minimum"); Measure(main, "search-minimum");
        var primary = Find<Button>(main).Single(b => AutomationProperties.GetAutomationId(b) == "PrimaryAction");
        if (!primary.IsVisible || primary.TransformToAncestor(main).Transform(new Point()).Y + primary.ActualHeight > 441) throw new InvalidOperationException("Primary action is outside the viewport");
        main.DataContext = vm; await fresh.StopAsync();
        var cached = new MainViewModel(new Store(), provider, cache: new Cache()); await cached.InitializeAsync(); cached.ApplyDetection(Track(6)); await cached.WaitForCacheAsync(); main.DataContext = cached;
        await Capture(main, "cache-minimum"); Measure(main, "cache-minimum");
        var footer = (TextBlock)main.FindName("UsageFooter"); if (!footer.IsVisible || footer.Text != "Guardado en este dispositivo · 0 tokens nuevos") throw new InvalidOperationException("Cache usage is not fully available in compact layout");
        main.DataContext = vm; await cached.StopAsync();
        var dialog = new ConfirmationWindow("Eliminar contenido guardado", "Canción sintética: elimina letra manual, selección y análisis locales. Conserva claves, preferencias y contador. No modifica Spotify.", "Eliminar contenido") { Owner = main };
        dialog.Show(); await Capture(dialog, "deletion-confirmation");
        var safe = Find<Button>(dialog).Single(b => b.IsDefault); if (safe.Content.ToString() != "Cancelar" || !safe.IsCancel) throw new InvalidOperationException("Unsafe default"); dialog.Close();
        var editVm = new MainViewModel(new Store(), provider); await editVm.InitializeAsync(); editVm.ApplyDetection(Track(2)); await editVm.SearchLyricsAsync();
        var editorModel = new LyricsEditorViewModel(editVm, editVm.BeginLyricsEdit()!);
        var editor = new LyricsEditorWindow(editorModel) { Owner = main }; editor.Show(); editorModel.Selected = editorModel.Options[0];
        await Capture(editor, "editor-versions-wide"); editor.Width = 360; editor.Height = 480; await Capture(editor, "editor-versions-compact");
        report.Add($"editor: compact-preview-height={((ScrollViewer)editor.FindName("VersionPreview")).ViewportHeight}");
        ((TabControl)editor.FindName("EditorTabs")).SelectedIndex = 1; editorModel.ManualText = "Borrador propio que debe conservarse\nSegunda línea";
        editVm.ApplyDetection(Track(3)); await Capture(editor, "editor-stale-draft");
        if (!editorModel.CanEditDraft || editorModel.SaveCommand.CanExecute(null)) throw new InvalidOperationException("Stale draft lost edit access or can apply");
        AnswerNextDialog(editor, false); editor.Close(); if (!editor.IsVisible || !editorModel.HasChanges) throw new InvalidOperationException("Dirty draft closed by cancellation");
        report.Add("editor: stale-editable=True; dirty-close-cancelled=True");
        AnswerNextDialog(editor, true); editor.Close(); await editVm.StopAsync();
        var cleanModel = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!); var cleanEditor = new LyricsEditorWindow(cleanModel) { Owner = main }; cleanEditor.Show(); cleanEditor.Close();
        if (cleanEditor.IsVisible) throw new InvalidOperationException("Clean editor did not close");
        report.Add("editor: clean-close=True; explicit-discard=True");
#endif
        if (interactive)
        {
            main.Width = 360; main.Height = 480; main.Title = "UX05–07 · Teclado sintético";
            var done = new TaskCompletionSource(); main.Closed += (_, _) => done.TrySetResult(); await done.Task;
        }
        else main.Close(); await vm.StopAsync();
        report.Add("App SHA256=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(MainWindow).Assembly.Location))));
        File.WriteAllLines(Path.Combine(output, "manifest.txt"), report);
    }
#if CURRENT_READING
    static void AnswerNextDialog(Window owner, bool answer) => owner.Dispatcher.BeginInvoke(() =>
    {
        var dialog = Application.Current.Windows.OfType<ConfirmationWindow>().Single(w => w.Owner == owner); dialog.DialogResult = answer;
    }, DispatcherPriority.ApplicationIdle);
#endif
    static void Measure(MainWindow main, string name)
    {
        var selected = ((TabControl)main.FindName("ReadingTabs")).SelectedIndex;
        var scroll = (ScrollViewer)main.FindName(selected == 0 ? "TranslationScroll" : "MeaningScroll");
        var root = (FrameworkElement)main.Content; if (VisualTreeHelper.GetParent(root) is FrameworkElement parent) root = parent;
        var top = scroll.TransformToAncestor(main).Transform(new Point()).Y;
        var visible = Math.Max(0, Math.Min(scroll.ActualHeight, root.ActualHeight - top));
        var ratio = visible / root.ActualHeight;
        report.Add($"{name}: client={root.ActualWidth}x{root.ActualHeight}; document={visible:F2} DIP; ratio={ratio:P2}; screen={SystemParameters.WorkArea.Width}x{SystemParameters.WorkArea.Height}; inference={1}");
#if CURRENT_READING
        if (main.Width == 360 && main.WindowState == WindowState.Normal && visible < 160 || main.Width == 860 && main.WindowState == WindowState.Normal && ratio < .60) throw new InvalidOperationException("Reading viewport below contract: " + name);
        if (Find<ScrollViewer>(main).Any(s => s.Name == "WindowViewport")) throw new InvalidOperationException("Outer document scroll still exists");
#endif
    }
    static async Task Capture(Window window, string name)
    {
        foreach (var text in Find<TextBlock>(window)) { text.InvalidateMeasure(); text.InvalidateVisual(); }
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle); await Task.Delay(40);
        var content = (FrameworkElement)window.Content; if (VisualTreeHelper.GetParent(content) is FrameworkElement parent) content = parent;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background, null, new Rect(content.RenderSize)); bitmap.Render(background); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        report.Add($"{name}: {window.ActualWidth}x{window.ActualHeight} DIP; Windows {VisualTreeHelper.GetDpi(window).PixelsPerInchX} DPI; render 96 DPI; reading {((window as MainWindow)?.ReadingFontSize ?? 16)} pt");
    }
    static IEnumerable<T> Find<T>(DependencyObject p) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(p); i++) { var c = VisualTreeHelper.GetChild(p, i); if (c is T found) yield return found; foreach (var nested in Find<T>(c)) yield return nested; } }
    sealed class Store : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings(true, "fixture-model"));
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(0);
    }
    sealed class Provider : ILyricsProvider, IInsightProvider
    {
        public int Calls;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken t) => Task.FromResult(new LyricsSearchResult(track.Revision, LyricsSearchKind.Candidates, null,
            [new(1, "Ventana original", "Artista sintético", "Álbum", TimeSpan.FromMinutes(3), false, "Abro una ventana\nY dejo entrar la luz\nCamino despacio\nHacia un día azul\n\nOtra estrofa", null), new(2, "Ventana acústica", "Artista sintético", "Otro álbum", TimeSpan.FromMinutes(4), false, "Otra versión propia", null)]));
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics l, CancellationToken t) { Calls++; return Task.FromResult(new SongInsight(track.Revision, "es", [], string.Join(" ", Enumerable.Repeat("La voz narrativa busca una oportunidad de cambio al abrir una ventana.", 60)), ["Cambio"], [], [], [])); }
    }
    sealed class Cache : ICacheStore
    {
        public Task<CachedSong?> ReadSongAsync(CurrentTrack track, CancellationToken t = default) => Task.FromResult<CachedSong?>(new(new(track.Revision, "Abro una ventana\nY dejo entrar la luz", LyricsOrigin.Manual, null, false), false, false));
        public Task SaveLyricsAsync(CurrentTrack track, ResolvedLyrics? l, CancellationToken t = default) => Task.CompletedTask;
        public Task<SongInsight?> ReadInsightAsync(ResolvedLyrics l, string m, string v, CancellationToken t = default, CurrentTrack? track = null, string scope = "api-key", string targetLanguage = "es") => Task.FromResult<SongInsight?>(new(l.TrackRevision, "es", [], "Resultado propio guardado.", ["Cambio"], [], [], []));
        public Task SaveInsightAsync(CurrentTrack track, ResolvedLyrics l, string m, string v, SongInsight i, CancellationToken t = default, string scope = "api-key", string targetLanguage = "es") => Task.CompletedTask;
        public Task DeleteSongAsync(CurrentTrack track, CancellationToken t = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken t = default) => Task.CompletedTask;
    }
}
