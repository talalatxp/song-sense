using System.Windows;
using SongSense.Core;

namespace SongSense.App;

public partial class ConnectionWindow : Window
{
    private readonly ConnectionViewModel viewModel;
    private bool closing, canClose;
    public Action? OpenApiSettingsAction { get; init; }
    public void FocusPreparationStep(PreparationAction action) => Dispatcher.InvokeAsync(() =>
    {
        viewModel.Review(action == PreparationAction.ChooseModel ? 2 : action == PreparationAction.ReviewPlan ? 3 : 1);
        FrameworkElement target = viewModel.IsConfirmStep ? CreditsConfirmation : NextButton;
        target.BringIntoView(); target.Focus();
    }, System.Windows.Threading.DispatcherPriority.Loaded);
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
    private async void ChangePendingAccount(object sender, RoutedEventArgs args) => await viewModel.ChangePendingAccountAsync();
    private async void SelectAccount(object sender, RoutedEventArgs args) => await viewModel.SelectAccountAsync();
    private async void SelectModel(object sender, RoutedEventArgs args) => await viewModel.SelectModelAsync();
    private async void LoadModels(object sender, RoutedEventArgs args) => await viewModel.ModelsAsync();
    private async void EnablePlan(object sender, RoutedEventArgs args) => await viewModel.EnablePlanAsync();
    private async void PausePlan(object sender, RoutedEventArgs args) => await viewModel.PausePlanAsync();
    private async void Disconnect(object sender, RoutedEventArgs args) => await viewModel.DisconnectAsync();
    private async void UseChatGpt(object sender, RoutedEventArgs args) => await viewModel.ModeAsync(AiConnectionMode.ChatGptIncluded);
    private async void UseApiKey(object sender, RoutedEventArgs args) => await viewModel.ModeAsync(AiConnectionMode.ApiKey);
    private void OpenApiSettings(object sender, RoutedEventArgs args) => OpenApiSettingsAction?.Invoke();
    private void ManageUsage(object sender, RoutedEventArgs args)
    { try { ConnectionViewModel.ManageUsage(); } catch (Exception) { MessageBox.Show(this, "No se pudo abrir el navegador. Abre ChatGPT y entra en Ajustes → Uso.", "Gestionar uso"); } }
    private void CancelOperation(object sender, RoutedEventArgs args) => viewModel.Cancel();
    private async void NextStep(object sender, RoutedEventArgs args)
    {
        if (viewModel.IsApiMode) OpenApiSettingsAction?.Invoke();
        else if (viewModel.IsReadyStep) Close();
        else await viewModel.PrimaryAsync();
    }
    private void ReviewAccount(object sender, RoutedEventArgs args) => viewModel.Review(1);
    private void ReviewModel(object sender, RoutedEventArgs args) => viewModel.Review(2);
    private void OpenDiagnostic(object sender, RoutedEventArgs args)
    {
        var dialog = new Window { Owner = this, Title = "Diagnóstico de conexión", Width = 460, Height = 360, MinWidth = 360, MinHeight = 320, Content = AuxiliaryContent.Create(viewModel, nameof(ConnectionViewModel.DiagnosticDetails), "Diagnóstico de conexión", viewModel.PrimaryReason, true, nameof(ConnectionViewModel.PrimaryReason)), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SourceInitialized += (_, _) => WindowAppearance.Apply(dialog); dialog.Show();
    }
}
