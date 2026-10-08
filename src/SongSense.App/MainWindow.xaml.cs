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
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
    }
    private void AdaptReadingLayout(object sender, SizeChangedEventArgs args)
    {
        if (TranslationPreview is null || TranslationColumn is null) return;
        var wide = ActualWidth >= 700;
        System.Windows.Controls.Grid.SetColumn(TranslationPreview, wide ? 1 : 0);
        System.Windows.Controls.Grid.SetRow(TranslationPreview, wide ? 0 : 1);
        TranslationPreview.Margin = wide ? new Thickness(14, 0, 0, 0) : new Thickness(0, 10, 0, 0);
        TranslationRow.Height = wide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        TranslationColumn.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        var shortWindow = ActualHeight < 700 || !wide;
        WindowViewport.VerticalScrollBarVisibility = shortWindow ? System.Windows.Controls.ScrollBarVisibility.Auto : System.Windows.Controls.ScrollBarVisibility.Disabled;
        ReadingTabs.Height = shortWindow ? (wide ? 320 : 440) : double.NaN;
        HeaderBlock.MaxHeight = shortWindow ? 48 : double.PositiveInfinity;
        RootGrid.Margin = new Thickness(shortWindow ? 16 : 24);
        HeaderBlock.Margin = new Thickness(0, 0, 0, shortWindow ? 10 : 18);
        BrandTagline.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        SongCard.Padding = new Thickness(shortWindow ? 12 : 18);
        SongCard.Margin = new Thickness(0, 0, 0, shortWindow ? 10 : 18);
        TrackMetadata.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
        TrackTitleText.FontSize = shortWindow ? 22 : 26;
        ActionsPanel.Margin = new Thickness(0, shortWindow ? 12 : 0, 0, 0);
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
