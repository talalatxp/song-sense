using System.Windows;

namespace SongSense.App;

public partial class MainWindow : Window
{
    public Func<AiSettingsWindow>? CreateSettingsWindow { get; init; }
    public Func<ConnectionWindow>? CreateConnectionWindow { get; init; }
    private ConnectionWindow? connectionWindow;
    private Window? diagnosticWindow;
    private PreferencesWindow? preferencesWindow;
    private Window? songDetailsWindow;
    public void OpenPreferences(object sender, RoutedEventArgs args)
    {
        if (preferencesWindow is not null) { preferencesWindow.Activate(); return; }
        if (DataContext is not MainViewModel vm) return;
        preferencesWindow = new PreferencesWindow(this, vm);
        preferencesWindow.Closed += (_, _) => preferencesWindow = null;
        preferencesWindow.Show();
    }
    public void ShowDiagnostic() { if (DataContext is MainViewModel vm) OpenDiagnostic(vm); }
    public void ShowStorageRecovery() => RecoverStorage(this, new RoutedEventArgs());
    private void OpenSongDetails(object sender, RoutedEventArgs args)
    {
        if (songDetailsWindow is not null) { songDetailsWindow.Activate(); return; }
        songDetailsWindow = new Window { Owner = this, DataContext = DataContext, Title = "Detalles de la canción · Song Sense", Width = 480, Height = 400, MinWidth = 360, MinHeight = 320, Content = AuxiliaryContent.Create(DataContext!, nameof(MainViewModel.SongDetails), "Detalles de la canción", "Canción, letra y análisis guardado."), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        songDetailsWindow.SourceInitialized += (_, _) => { if (songDetailsWindow is not null) WindowAppearance.Apply(songDetailsWindow); };
        songDetailsWindow.Closed += (_, _) => songDetailsWindow = null; songDetailsWindow.Show();
    }
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
    private void ReviewAiDecision(object sender, RoutedEventArgs args)
    {
        if (DataContext is not MainViewModel vm) return;
        OpenConnection(sender, args);
        connectionWindow?.FocusPreparationStep(vm.AiDecisionAction);
    }
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
        InitializeReadingNavigation();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        var preparationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        bool refreshing = false;
        preparationTimer.Tick += async (_, _) =>
        {
            if (refreshing || DataContext is not MainViewModel vm) return;
            refreshing = true;
            try { await vm.RefreshPreparationTimeAsync(); }
            finally { refreshing = false; }
        };
        Loaded += (_, _) => preparationTimer.Start();
        Closed += (_, _) => preparationTimer.Stop();
    }
    private async void ExecutePrimaryAction(object sender, RoutedEventArgs args)
    {
        if (DataContext is not MainViewModel vm || !vm.CanPrimaryAction) return;
        var action = vm.Preparation.Action;
        switch (action)
        {
            case PreparationAction.Search: await vm.SearchLyricsAsync(); break;
            case PreparationAction.Generate: await vm.GenerateInsightAsync(); break;
            case PreparationAction.Regenerate: await vm.RegenerateInsightAsync(); break;
            case PreparationAction.CancelSearch: vm.CancelLyrics(); break;
            case PreparationAction.CancelAnalysis: vm.CancelInsight(); break;
            case PreparationAction.ChooseVersion:
            case PreparationAction.EditLyrics:
                if (vm.BeginLyricsEdit() is { } context)
                    new LyricsEditorWindow(new LyricsEditorViewModel(vm, context), action == PreparationAction.EditLyrics ? true : null) { Owner = this }.Show();
                break;
            case PreparationAction.ConfirmCachedLyrics: vm.ConfirmCachedLyricsCommand.Execute(null); break;
            case PreparationAction.OpenConnection:
            case PreparationAction.ChooseModel:
            case PreparationAction.ReviewPlan:
                OpenConnection(sender, args); connectionWindow?.FocusPreparationStep(action); break;
            case PreparationAction.OpenApi: ShowApiSettings(); break;
            case PreparationAction.RetryLanguage: await vm.RetryLanguageAsync(); break;
            case PreparationAction.RefreshCounter: await vm.RefreshAiUsageAsync(); break;
            case PreparationAction.RetryStorage: await vm.InitializeAsync(); break;
            case PreparationAction.RecoverStorage: RecoverStorage(sender, args); break;
            case PreparationAction.Diagnostic: OpenDiagnostic(vm); break;
        }
    }
    private void ToggleDiagnostic(object sender, RoutedEventArgs args)
    { if (DataContext is MainViewModel vm) OpenDiagnostic(vm); }
    private void OpenDiagnostic(MainViewModel vm)
    {
        if (diagnosticWindow is not null) { diagnosticWindow.Activate(); return; }
        diagnosticWindow = new Window { Owner = this, DataContext = vm, Title = "Diagnóstico · Song Sense", Width = 600, Height = 480,
            MinWidth = 360, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = AuxiliaryContent.Create(vm, nameof(MainViewModel.DiagnosticDetails), "Diagnóstico", vm.Preparation.Message, true, "Preparation.Message") };
        diagnosticWindow.SourceInitialized += (_, _) => { if (diagnosticWindow is not null) WindowAppearance.Apply(diagnosticWindow); };
        diagnosticWindow.Closed += (_, _) => diagnosticWindow = null;
        diagnosticWindow.Show();
    }
    private void AdaptReadingLayout(object sender, SizeChangedEventArgs args)
        => ApplyReadingLayout();
    private void ApplyReadingLayout()
    {
        if (ReadingTabs is null) return;
        var compact = ActualWidth < 600;
        if (IsCompactLayout != compact) ReadingActions.IsExpanded = !compact;
        IsCompactLayout = compact;
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        UsageBadge.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        MainOuterScroll.Margin = new Thickness(compact ? 4 : 12);
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
    private async void DeleteSavedSong(object sender, RoutedEventArgs args) => await ConfirmDeletionAsync(false);
    private async void DeleteAllSaved(object sender, RoutedEventArgs args) => await ConfirmDeletionAsync(true);
    public async Task ConfirmDeletionAsync(bool all)
    {
        if (DataContext is not MainViewModel vm || vm.BeginCacheDeletion(all) is not { } request) return;
        if (!ConfirmationWindow.Ask(this, "Eliminar contenido guardado", $"Se eliminará {request.Label}: letra manual o seleccionada, selección de versión y análisis guardados. Es una operación local; no modifica Spotify. Conserva claves, preferencias y contador de solicitudes. No se puede deshacer. ¿Eliminar este contenido?", "Eliminar contenido")) return;
        if (!await vm.ConfirmCacheDeletionAsync(request)) MessageBox.Show(this, vm.CacheMessage, "Contenido guardado");
    }
}
