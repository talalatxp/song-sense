using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using SongSense.App;
using SongSense.Core;

public static partial class Preview
{
    private static async Task VerifyCandidateAbout(double scale)
    {
        var vm = new MainViewModel(new Store(), new Provider());
        await vm.InitializeAsync();
        var owner = new MainWindow { DataContext = vm, Width = 860, Height = 780 };
        try
        {
            owner.Show();
            await Flush(owner);
            var menu = (Button)owner.FindName("ApplicationMenuButton");
            var about = menu.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Acerca de Song Sense"));
            var observed = owner.Dispatcher.InvokeAsync(() =>
            {
                var dialog = owner.OwnedWindows.OfType<Window>().Single(w => w.Title == "Acerca de Song Sense");
                try
                {
                    dialog.UpdateLayout();
                    var document = Descendants(dialog).OfType<RichTextBox>().Single().Document;
                    var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
                    Check(text.Contains(BuildIdentity.ProductVersion) && text.Contains(BuildIdentity.FileVersion), "About identity disagrees with assembly");
                    Capture(dialog, $"candidate-about-ui{scale:0.00}");
                    var back = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "Volver"));
                    ((IInvokeProvider)new ButtonAutomationPeer(back).GetPattern(PatternInterface.Invoke)).Invoke();
                }
                catch
                {
                    dialog.Close();
                    throw;
                }
            }, DispatcherPriority.ContextIdle);
            about.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await observed;
            Check(!owner.OwnedWindows.OfType<Window>().Any(w => w.IsVisible) && menu.IsKeyboardFocused, "About did not return to its invoker");
        }
        finally
        {
            owner.Close();
            await vm.StopAsync();
        }
    }

    private static async Task ReadingAndWelcome(int width, double scale)
    {
        var provider = new ReadingProvider();
        var store = new ReadingStore();
        var vm = new MainViewModel(store, provider, insightProvider: provider);
        await vm.InitializeAsync();
        var window = new MainWindow { DataContext = vm, Width = width, Height = width == 360 ? 480 : 780 };
        var prefix = $"reading-{width}-ui{scale:0.00}";
        try
        {
            window.Show();
            await Flush(window);
            var menu = (Button)window.FindName("ApplicationMenuButton");
            var entry = (Button)window.FindName("ReadingToolsButton");
            var popup = (Popup)window.FindName("ReadingToolsPopup");
            var language = (ComboBox)window.FindName("ReadingLanguage");
            var mode = (ComboBox)window.FindName("ReadingMode");
            var tabs = (TabControl)window.FindName("ReadingTabs");
            var artist = (TextBlock)window.FindName("ArtistText");
            var title = (TextBlock)window.FindName("TrackTitleText");
            vm.ApplyDetection(new(ApplicationState.NoSpotify, null, null));
            await Flush(window);
            Capture(window, prefix + "-no-spotify");
            if (!before) Check(!artist.IsVisible && title.Text == "Song Sense", "Welcome repeats a missing identity");
            vm.ApplyDetection(new(ApplicationState.NoTrack, null, null));
            await Flush(window);
            Capture(window, prefix + "-no-track");
            vm.ApplyDetection(new(ApplicationState.Error, null, null));
            await Flush(window);
            Capture(window, prefix + "-detection-error");
            if (!before)
                Check(((StatusNotice)window.FindName("PreparationNotice")).IsVisible && !((FrameworkElement)window.FindName("LyricsEmpty")).IsVisible, "Detection error confused with welcome");
            vm.ApplyDetection(new(ApplicationState.NoTrack, null, null));
            await Flush(window);

            async Task OpenTools()
            {
                if (window.ActualWidth < 600)
                {
                    if (before) menu.ContextMenu.Items.OfType<MenuItem>().First(i => i.Header.ToString()!.StartsWith("Opciones de lectura")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    else
                    {
                        Check(entry.IsVisible && entry.IsEnabled, "Compact tools hidden");
                        entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    await Flush(window);
                    Check(popup.IsOpen && language.IsVisible, "Tools did not open in one activation");
                    if (!before) Check(language.IsKeyboardFocusWithin, "Opening tools did not move focus");
                }
                else Check(language.IsVisible, "Inline tools missing");
            }

            async Task CloseTools()
            {
                if (!popup.IsOpen) return;
                if (before) popup.IsOpen = false;
                else
                {
                    language.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(language)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Check(!popup.IsOpen && entry.IsKeyboardFocused, "Escape did not restore the invoker");
                }
                await Flush(window);
            }

            await OpenTools();
            language.SelectedValue = "en";
            await vm.WaitForCacheAsync();
            await Flush(window);
            Check(vm.TargetLanguage == "en" && store.Language == "en" && provider.Calls == 0, "Language selection before playback was not local");
            await CloseTools();
            vm.ApplyDetection(Track(1));
            vm.SaveManual(vm.BeginLyricsEdit()!, "Abro una ventana");
            await Flush(window);
            Capture(window, prefix + "-first-song");
            Check(vm.TargetLanguage == "en" && provider.Calls == 0, "First track replaced preference or called AI");
            await vm.GenerateInsightAsync();
            await Flush(window);
            await OpenTools();
            mode.SelectedIndex = 1;
            await Flush(window);
            Check(window.PreferredReadingMode == 1 && provider.Calls == 1, "Mode did not stay local");
            await CloseTools();
            Capture(window, prefix + "-lyrics");
            if (!before && width == 360)
                Check(((TextBlock)window.FindName("ReadingToolsLabel")).Text == "EN · Traducción" && AutomationProperties.GetName(entry).Contains("Inglés") && ReferenceEquals(menu.Parent, window.FindName("NavigationBlock")), "Compact context or header menu lost");
            tabs.SelectedIndex = 1;
            await Flush(window);
            Capture(window, prefix + "-meaning");
            Check(!((FrameworkElement)window.FindName("ReadingModeGroup")).IsVisible, "Meaning exposes lyrics mode");
            if (!before && width == 360) Check(((TextBlock)window.FindName("ReadingToolsLabel")).Text == "EN · Lectura", "Meaning context not named");
            await OpenTools();
            foreach (var destination in OutputLanguages.All)
            {
                language.SelectedValue = destination.Code;
                await vm.WaitForCacheAsync();
                await Flush(window);
                Check(vm.TargetLanguage == destination.Code && store.Language == destination.Code && provider.Calls == 1, "Language selection performed inference or was not saved");
            }
            await CloseTools();
            Check(window.PreferredReadingMode == 1, "Language change replaced the preferred reading mode");
            if (!before && width == 360)
            {
                window.Width = 599;
                await Flush(window);
                await OpenTools();
                window.Width = 600;
                await Flush(window);
                Check(!popup.IsOpen && language.IsKeyboardFocusWithin, "Compact to wide lost the control or focus");
                window.Width = 599;
                await Flush(window);
                Check(entry.IsKeyboardFocused && !popup.IsOpen, "Wide to compact lost focus");
            }
            if (width == 360)
            {
                window.Width = 360;
                tabs.SelectedIndex = 0;
                window.ReadingFontSize = 24;
                vm.TargetLanguage = "en";
                await vm.WaitForCacheAsync();
                await vm.GenerateInsightAsync();
                vm.AutomaticUpdatesEnabled = false;
                provider.Hold = true;
                var operation = vm.RegenerateInsightAsync();
                try
                {
                    await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await Flush(window);
                    Capture(window, prefix + "-translated-operation");
                    if (!before)
                    {
                        var document = (ReadingDocument)window.FindName("TranslationScroll");
                        var action = (Button)window.FindName("PrimaryActionButton");
                        Check(((TextBlock)window.FindName("ReadingToolsLabel")).Text == "EN · Traducción", "Operation discarded the active reading mode");
                        Check(document.ViewportHeight > document.FontSize * 2 && action.IsVisible, "Long mode label consumed the reading area");
                    }
                }
                finally
                {
                    vm.CancelInsight();
                    await operation;
                }
            }
        }
        finally
        {
            window.Close();
            await vm.StopAsync();
        }
    }

    private static async Task EditorFeedback(int width, double scale)
    {
        var provider = new Provider();
        var cache = new EditorCache();
        var vm = new MainViewModel(new Store(), provider, cache: cache);
        await vm.InitializeAsync();
        vm.ApplyDetection(Track(1));
        await vm.WaitForCacheAsync();
        var prefix = $"editor-a6-{width}-ui{scale:0.00}";
        var empty = new LyricsEditorWindow(new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!), false) { Width = width, Height = width == 360 ? 480 : 700 };
        empty.Show();
        await Flush(empty);
        Capture(empty, prefix + "-no-versions");
        empty.Close();

        await vm.SearchLyricsAsync();
        var edit = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!);
        var window = new LyricsEditorWindow(edit, false) { Width = width, Height = width == 360 ? 480 : 700 };
        Task? saving = null;
        try
        {
            window.Show();
            await Flush(window);
            var list = (ListBox)window.FindName("VersionsList");
            var preview = (ScrollViewer)window.FindName("VersionPreview");
            var details = Descendants(preview).OfType<Expander>().SingleOrDefault();
            Capture(window, prefix + "-unselected");
            if (!before)
            {
                Check(details is null || !details.IsVisible, "Details shown with no selection");
                Check(Gap(window, list, (FrameworkElement)window.FindName("VersionActions")) >= 15.9, "Version confirmation meets the list");
            }
            list.SelectedIndex = 4;
            if (width < 640) ((Button)window.FindName("PreviewNext")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Flush(window);
            details = Descendants(preview).OfType<Expander>().Single();
            Check(details.IsVisible, "Selected identity inaccessible");
            details.IsExpanded = true;
            await Flush(window);
            Capture(window, prefix + "-identity");
            if (!before) Check(edit.PreviewIdentity.Contains(edit.Options[4].Title) && edit.PreviewIdentity.Contains("Origen: LRCLIB"), "Full identity absent");
            edit.Selected = edit.Options[0];
            await Flush(window);
            Check(edit.PreviewIdentity.Contains(edit.Options[0].Title) && !edit.PreviewIdentity.Contains(edit.Options[4].Title), "Stale identity");
            edit.Selected = null;
            await Flush(window);
            Capture(window, prefix + "-removed");
            if (!before) Check(!details.IsVisible && edit.PreviewIdentity.Length == 0, "Identity survives deselection");

            ((TabControl)window.FindName("EditorTabs")).SelectedIndex = 1;
            await Flush(window);
            var input = (TextBox)window.FindName("ManualInput");
            var scroll = (ScrollViewer)window.FindName("EditorOuterScroll");
            var save = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "Guardar letra"));
            async Task Frame(string state)
            {
                await Flush(window);
                scroll.ScrollToTop();
                await Flush(window);
                Capture(window, prefix + "-" + state);
                scroll.ScrollToEnd();
                await Flush(window);
                Capture(window, prefix + "-" + state + "-footer");
            }
            string Status() => (window.FindName("ManualStatus") as TextBlock)?.Text ?? "";
            await Frame("empty");
            if (!before)
            {
                Check(!Status().Contains("sin guardar"), "Empty draft reported as dirty");
                Check(Gap(window, (FrameworkElement)window.FindName("ManualLabel"), input) >= 7.9, "Manual label meets input");
                Check(Gap(window, input, (FrameworkElement)window.FindName("ManualCount")) >= 7.9, "Counter meets input");
                Check(Gap(window, (FrameworkElement)window.FindName("ManualStatus"), (FrameworkElement)window.FindName("ManualActions")) >= 11.9, "Feedback meets actions");
            }
            edit.ManualText = new string('x', 20_001);
            await Frame("oversize");
            Check(!save.IsEnabled && input.Text.Length == 20_001, "Oversize draft truncated or enabled");
            edit.ManualText = new string('x', 20_000);
            await Frame("valid");
            Check(save.IsEnabled && input.Text.Length == 20_000, "Exact limit did not remain editable");
            edit.ManualText = "";
            await Frame("restored");
            if (!before) Check(!Status().Contains("sin guardar") && !Status().Contains("corregida"), "Restored draft still reports obsolete correction");

            edit.ManualText = "Texto sintético que conserva el editor.";
            saving = edit.SaveAsync();
            await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Frame("saving");
            Check(!save.IsEnabled, "Save enabled during write");
            if (!before) Check(Status().Contains("Guardando"), "Missing saving feedback");
            cache.Release.TrySetResult();
            await saving.WaitAsync(TimeSpan.FromSeconds(5));
            await Frame("failed");
            if (!before) Check(Status().Contains("No se pudo") && save.IsEnabled, "Save failure masked");
            vm.ApplyDetection(Track(2));
            edit.ManualText = new string('x', 20_001);
            await Frame("stale");
            input.SelectAll();
            Check(!save.IsEnabled && edit.CanCopyDraft && input.SelectionLength == 20_001 && ApplicationCommands.Copy.CanExecute(null,input), "Stale draft lost copy or guard");
            if (!before) Check(Status().Contains("ha cambiado") && !Status().Contains("20.000"), "Stale draft validation outranks context");
            Check(provider.Searches == 1 && provider.Calls == 0 && cache.Writes == 1, "Browsing or correcting caused an operation");
        }
        finally
        {
            cache.Release.TrySetResult();
            if (saving is not null) await saving;
            edit.Selected = null;
            edit.ManualText = "";
            if (window.IsVisible) window.Close();
            await vm.StopAsync();
        }
    }

    private static double Gap(Window window, FrameworkElement above, FrameworkElement below)
    {
        var top = below.TranslatePoint(new Point(), window).Y;
        return top - above.TranslatePoint(new Point(0, above.ActualHeight), window).Y;
    }

    private sealed class ReadingStore : ISettingsStore, IOutputLanguageSettings
    {
        public string Language { get; private set; } = "es";
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken token = default) => Task.FromResult(new AppSettings(true, "fixture-model"));
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken token = default) => Task.FromResult(0);
        public Task<string> ReadOutputLanguageAsync(CancellationToken token = default) => Task.FromResult(Language);
        public Task SaveOutputLanguageAsync(string language, CancellationToken token = default) { Language = language; return Task.CompletedTask; }
    }

    private sealed class ReadingProvider : ILyricsProvider, IInsightProvider
    {
        public int Calls;
        public bool Hold;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => throw new InvalidOperationException("Unexpected lyrics request");
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) => GenerateAsync(track, lyrics, "es", token);
        public async Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, string language, CancellationToken token)
        {
            Calls++;
            if (Hold)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            return new SongInsight(track.Revision, "es", [new(1, "I open a window", "es")], "A synthetic reading of the window.", [], [], [], []) { TargetLanguage = language };
        }
    }

    private sealed class EditorCache : ICacheStore
    {
        public int Writes;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CachedSong?> ReadSongAsync(CurrentTrack track, CancellationToken token = default) => Task.FromResult<CachedSong?>(null);
        public async Task SaveLyricsAsync(CurrentTrack track, ResolvedLyrics? lyrics, CancellationToken token = default)
        {
            if (lyrics is null) return;
            Writes++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            throw new System.IO.IOException("Synthetic storage failure");
        }
        public Task<SongInsight?> ReadInsightAsync(ResolvedLyrics lyrics, string model, string version, CancellationToken token = default, CurrentTrack? track = null, string scope = "api-key", string targetLanguage = "es") => Task.FromResult<SongInsight?>(null);
        public Task SaveInsightAsync(CurrentTrack track, ResolvedLyrics lyrics, string model, string version, SongInsight insight, CancellationToken token = default, string scope = "api-key", string targetLanguage = "es") => Task.CompletedTask;
        public Task DeleteSongAsync(CurrentTrack track, CancellationToken token = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken token = default) => Task.CompletedTask;
    }
}
