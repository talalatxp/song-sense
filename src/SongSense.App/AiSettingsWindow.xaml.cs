using System.Windows;

namespace SongSense.App;

public partial class AiSettingsWindow : Window
{
    private readonly AiSettingsViewModel viewModel;
    private bool closing;
    private bool canClose;
    private bool closed;
    public async Task<bool> RequestCloseAsync()
    {
        if (closed) return true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, EventArgs args) => completion.TrySetResult();
        Closed += Completed;
        try { Close(); if (!closing && !closed) return false; await completion.Task; return true; }
        finally { Closed -= Completed; }
    }
    public AiSettingsWindow(AiSettingsViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.ValidationFailed += FocusInvalidField;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        Loaded += async (_, _) => await viewModel.InitializeAsync();
        Closing += async (_, args) =>
        {
            if (canClose) return;
            args.Cancel = true;
            if (closing) return;
            if (viewModel.HasChanges && !ConfirmationWindow.Ask(this, "Cambios sin guardar", "Hay cambios sin guardar. Descartar elimina también la clave recién escrita de este formulario. No se guardará ningún borrador. ¿Descartar y cerrar?", "Descartar y cerrar", "Continuar editando")) return;
            closing = true;
            KeyInput.Clear();
            await viewModel.StopAsync();
            canClose = true;
            // StopAsync can complete synchronously. Defer Close until this Closing
            // event has returned, and skip it if the owner already closed us.
            await Dispatcher.InvokeAsync(() => { if (IsLoaded) Close(); });
        };
        Closed += (_, _) => { closed = true; viewModel.ValidationFailed -= FocusInvalidField; viewModel.Dispose(); };
    }
    private async void SaveSettings(object sender, RoutedEventArgs args)
    {
        using var key = KeyInput.SecurePassword;
        await viewModel.SaveAsync(key);
        if (!viewModel.HasChanges) KeyInput.Clear();
    }
    private async void DeleteKey(object sender, RoutedEventArgs args)
    {
        if (viewModel.BeginKeyDeletion() is not { } revision) return;
        if (!ConfirmationWindow.Ask(this, "Eliminar clave de API", "Se eliminará la clave de API guardada en este dispositivo y se desactivará la IA de API. No se activa otro modo ni se revoca la clave en OpenAI. Conserva caché, preferencias y contador. Para pausar consultas basta con desactivar IA y guardar. ¿Eliminar la clave local?", "Eliminar clave")) return;
        KeyInput.Clear(); await viewModel.ConfirmKeyDeletionAsync(revision);
    }
    private async void TestConnection(object sender, RoutedEventArgs args) => await viewModel.TestAsync();
    private void KeyChanged(object sender, RoutedEventArgs args) { if (viewModel is not null) { using var key = KeyInput.SecurePassword; viewModel.KeyEdited(key.Length); } }
    private void FocusInvalidField(object? sender, EventArgs args)
    {
        FrameworkElement target = viewModel.FirstInvalidField switch { "Model" => ModelInput, "Limit" => LimitInput, "Notice" => NoticeInput, _ => KeyInput };
        FrameworkElement group = viewModel.FirstInvalidField switch { "Model" => ModelValidationGroup, "Limit" => LimitValidationGroup, "Notice" => NoticeValidationGroup, _ => KeyValidationGroup };
        Dispatcher.InvokeAsync(() =>
        {
            if (closed || !IsLoaded) return;
            target.Focus();
            // Validate against the updated layout, then frame label, field and error
            // together. A tall error keeps its beginning visible; full text is also
            // exposed through the input's accessible help text.
            FormScroll.UpdateLayout();
            group.BringIntoView(new Rect(0, 0, group.ActualWidth, Math.Min(group.ActualHeight, FormScroll.ViewportHeight)));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void OpenModelDocumentation(object sender, RoutedEventArgs args)
    { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developers.openai.com/api/docs/models") { UseShellExecute = true }); } catch { MessageBox.Show(this, "No se pudo abrir el navegador. Consulta la documentación de modelos de OpenAI.", "Ayuda del modelo"); } }
    private void CancelOperation(object sender, RoutedEventArgs args) => viewModel.Cancel();
}
