using System.Windows;

namespace SongSense.App;

public partial class AiSettingsWindow : Window
{
    private readonly AiSettingsViewModel viewModel;
    private bool closing;
    private bool canClose;
    public AiSettingsWindow(AiSettingsViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        Loaded += async (_, _) => await viewModel.InitializeAsync();
        Closing += async (_, args) =>
        {
            if (canClose) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            KeyInput.Clear();
            await viewModel.StopAsync();
            canClose = true;
            // StopAsync can complete synchronously. Defer Close until this Closing
            // event has returned, and skip it if the owner already closed us.
            await Dispatcher.InvokeAsync(() => { if (IsLoaded) Close(); });
        };
        Closed += (_, _) => viewModel.Dispose();
    }
    private async void SaveSettings(object sender, RoutedEventArgs args)
    {
        using var key = KeyInput.SecurePassword;
        KeyInput.Clear();
        await viewModel.SaveAsync(key);
    }
    private async void DeleteKey(object sender, RoutedEventArgs args) { KeyInput.Clear(); await viewModel.DeleteKeyAsync(); }
    private async void TestConnection(object sender, RoutedEventArgs args) { KeyInput.Clear(); await viewModel.TestAsync(); }
    private void CancelOperation(object sender, RoutedEventArgs args) => viewModel.Cancel();
}
