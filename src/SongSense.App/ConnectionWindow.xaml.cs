using System.Windows;
using SongSense.Core;

namespace SongSense.App;

public partial class ConnectionWindow : Window
{
    private readonly ConnectionViewModel viewModel;
    private bool closing, canClose;
    public Action? OpenApiSettingsAction { get; init; }
    public ConnectionWindow(ConnectionViewModel viewModel)
    {
        InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        Closing += async (_, args) =>
        {
            if (canClose) return;
            args.Cancel = true; if (closing) return; closing = true;
            await viewModel.StopAsync(); canClose = true;
            await Dispatcher.InvokeAsync(() => { if (IsLoaded) Close(); });
        };
        Closed += (_, _) => viewModel.Dispose();
    }
    private async void Connect(object sender, RoutedEventArgs args) => await viewModel.ConnectAsync(false);
    private async void AddAccount(object sender, RoutedEventArgs args) => await viewModel.ConnectAsync(true);
    private async void SelectAccount(object sender, RoutedEventArgs args) => await viewModel.SelectAccountAsync();
    private async void SelectModel(object sender, RoutedEventArgs args) => await viewModel.SelectModelAsync();
    private async void LoadModels(object sender, RoutedEventArgs args) => await viewModel.ModelsAsync();
    private async void Disconnect(object sender, RoutedEventArgs args) => await viewModel.DisconnectAsync();
    private async void UseChatGpt(object sender, RoutedEventArgs args) => await viewModel.ModeAsync(AiConnectionMode.ChatGptIncluded);
    private async void UseApiKey(object sender, RoutedEventArgs args) => await viewModel.ModeAsync(AiConnectionMode.ApiKey);
    private void OpenApiSettings(object sender, RoutedEventArgs args) => OpenApiSettingsAction?.Invoke();
    private void ManageUsage(object sender, RoutedEventArgs args)
    { try { ConnectionViewModel.ManageUsage(); } catch (Exception) { MessageBox.Show(this, "No se pudo abrir el navegador. Abre ChatGPT y entra en Ajustes → Uso.", "Gestionar uso"); } }
    private void CancelOperation(object sender, RoutedEventArgs args) => viewModel.Cancel();
}
