using System.Windows;
namespace SongSense.App;
public partial class PreferencesWindow : Window
{
    private readonly MainWindow main;
    public PreferencesWindow(MainWindow owner, MainViewModel vm)
    {
        main = owner; Owner = owner; DataContext = vm; InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        ReadingSize.ValueChanged += (_, _) => main.SchedulePreferenceSave();
        TopmostToggle.Checked += (_, _) => main.SchedulePreferenceSave(); TopmostToggle.Unchecked += (_, _) => main.SchedulePreferenceSave();
        Closed += (_, _) => main.SavePreferences();
    }
    private async void DeleteSong(object s, RoutedEventArgs e) => await main.ConfirmDeletionAsync(false);
    private async void DeleteAll(object s, RoutedEventArgs e) => await main.ConfirmDeletionAsync(true);
    private void Diagnostic(object s, RoutedEventArgs e) => main.ShowDiagnostic();
    private void Recover(object s, RoutedEventArgs e) => main.ShowStorageRecovery();
    private void Exit(object s, RoutedEventArgs e) => main.RequestExit();
}
