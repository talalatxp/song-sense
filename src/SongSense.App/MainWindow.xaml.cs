using System.Windows;

namespace SongSense.App;

public partial class MainWindow : Window
{
    public Func<AiSettingsWindow>? CreateSettingsWindow { get; init; }
    public Func<ConnectionWindow>? CreateConnectionWindow { get; init; }
    private ConnectionWindow? connectionWindow;
    private void OpenConnection(object sender, RoutedEventArgs args)
    {
        if (connectionWindow is not null) { connectionWindow.Activate(); return; }
        connectionWindow = CreateConnectionWindow?.Invoke();
        if (connectionWindow is null) return;
        connectionWindow.Owner = this;
        connectionWindow.Closed += (_, _) => connectionWindow = null;
        connectionWindow.Show();
    }
    public void ShowApiSettings() => OpenAiSettings(this, new RoutedEventArgs());
    private AiSettingsWindow? settingsWindow;
    private void OpenAiSettings(object sender, RoutedEventArgs args)
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = CreateSettingsWindow?.Invoke();
        if (settingsWindow is null) return;
        settingsWindow.Owner = this;
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }
    public MainWindow()
    {
        InitializeComponent();
        ReadingTabs.SelectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.OriginalSource, ReadingTabs)) return;
            Dispatcher.BeginInvoke(() => {
                if (ReadingTabs.SelectedIndex == 1) MeaningScroll.ScrollToTop(); else TranslationScroll.ScrollToTop();
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        };
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
    }
    private void AdaptReadingLayout(object sender, SizeChangedEventArgs args)
        => ApplyReadingLayout();
    private void ReflowOptions(object sender, RoutedEventArgs args)
    {
        if (!ReferenceEquals(args.OriginalSource, OptionsPanel)) return;
        ApplyReadingLayout();
        if (OptionsPanel.IsExpanded) Dispatcher.BeginInvoke(WindowViewport.ScrollToEnd, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
    private void ApplyReadingLayout()
    {
        if (ReadingTabs is null) return;
        var shortWindow = ActualHeight < 600 || ActualWidth < 600 || OptionsPanel?.IsExpanded == true;
        WindowViewport.VerticalScrollBarVisibility = shortWindow ? System.Windows.Controls.ScrollBarVisibility.Auto : System.Windows.Controls.ScrollBarVisibility.Disabled;
        ReadingTabs.Height = shortWindow ? Math.Clamp(ActualHeight - 310, 220, 460) : double.NaN;
        ReadingTabs.MinHeight = 200;
        RootGrid.Margin = new Thickness(shortWindow ? 16 : 28);
        HeaderBlock.Margin = new Thickness(0, 0, 0, 16);
        SongCard.Padding = new Thickness(shortWindow ? 16 : 18);
        SongCard.Margin = new Thickness(0, 0, 0, shortWindow ? 14 : 20);
        TrackMetadata.Visibility = Visibility.Collapsed;
        TrackTitleText.FontSize = shortWindow ? 24 : 32;
        ActionsPanel.Margin = new Thickness(0, 18, 0, 0);
    }
    private void OpenSongMenu(object sender, RoutedEventArgs args)
    {
        if (sender is System.Windows.Controls.Button { ContextMenu: { } menu } button)
        { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void ChangeLyrics(object sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel vm && vm.BeginLyricsEdit() is { } context)
            new LyricsEditorWindow(new LyricsEditorViewModel(vm, context)) { Owner = this }.Show();
    }
    private async void RecoverStorage(object sender, RoutedEventArgs args)
    {
        if (DataContext is not MainViewModel vm || !vm.CanRecoverStorage) return;
        if (MessageBox.Show(this, "Cierra otras instancias de Song Sense. Se conservará una copia local del original y se creará una base nueva. La IA quedará desactivada; si los contadores no se pueden leer, no habrá solicitudes disponibles hoy. La caché dañada se conservará en la copia. ¿Recuperar?", "Recuperar base local", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await vm.RecoverStorageAsync();
    }
}
