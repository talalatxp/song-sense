using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SongSense.App;

public partial class LyricsEditorWindow : Window
{
    private readonly LyricsEditorViewModel editor;
    private bool committed, closed;
    public LyricsEditorWindow(LyricsEditorViewModel editor, bool? manual = null)
    {
        InitializeComponent();
        this.editor = editor;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        DataContext = editor;
        EditorTabs.SelectedIndex = (manual ?? editor.PreferManual) ? 1 : 0;
        editor.Committed += OnCommitted;
        Loaded += (_, _) => ApplyEditorLayout();
        VersionsList.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || !editor.CanPreviewVersion) return;
            ShowSelectedVersion(VersionsList, args); args.Handled = true;
        };
        VersionsList.MouseDoubleClick += (_, args) =>
        {
            if (args.ChangedButton != MouseButton.Left || args.OriginalSource is not DependencyObject source ||
                ItemsControl.ContainerFromElement(VersionsList, source) is not ListBoxItem { IsSelected: true } || !editor.CanPreviewVersion) return;
            ShowSelectedVersion(VersionsList, args); args.Handled = true;
        };

        Closing += (_, args) =>
        {
            if (committed) return;
            if (editor.Saving) { args.Cancel = true; return; }
            if (editor.HasChanges && !ConfirmationWindow.Ask(this, "Borrador sin guardar", "El borrador pertenece a " + editor.TrackLabel + ". Puedes continuar editando y usar Copiar borrador antes de decidir. Descartarlo elimina los cambios de esta ventana.", "Descartar borrador", "Continuar editando")) args.Cancel = true;
        };
        Closed += (_, _) => { closed = true; editor.Committed -= OnCommitted; editor.Dispose(); };
    }
    public bool RequestClose() { Close(); return closed; }
    private void OnCommitted(object? sender, EventArgs args) { committed = true; Close(); }
    private void CopyDraft(object sender, RoutedEventArgs args)
    {
        try { if (editor.ManualText.Length > 0) Clipboard.SetText(editor.ManualText); }
        catch (System.Runtime.InteropServices.COMException) { MessageBox.Show(this, "No se pudo copiar. El borrador sigue en esta ventana; vuelve a intentarlo."); }
    }
    private void ShowSelectedVersion(object sender, RoutedEventArgs args)
    {
        if (!editor.OpenSelectedVersion()) return;
        ApplyEditorLayout(); VersionPreview.Focus();
    }
    private void ShowVersions(object sender, RoutedEventArgs args) { editor.ReturnToVersions(); ApplyEditorLayout(); FocusSelectedVersion(); }
    private void FocusSelectedVersion()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (closed || !VersionsList.IsVisible) return;
            if (editor.Selected is not { } selected) { VersionsList.Focus(); return; }
            VersionsList.ScrollIntoView(selected); UpdateLayout();
            if (VersionsList.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item)
            { item.Focus(); item.BringIntoView(); }
        }, DispatcherPriority.Loaded);
    }
    private void AdaptEditor(object sender, SizeChangedEventArgs args) => ApplyEditorLayout();
    private void ApplyEditorLayout()
    {
        if (VersionsList is null || editor is null) return;
        var compact = ActualWidth < 640;
        var listFocused = VersionsList.IsKeyboardFocusWithin;
        var previewFocused = VersionPreview.IsKeyboardFocusWithin;
        var navigationFocused = VersionBack.IsKeyboardFocusWithin || PreviewNext.IsKeyboardFocusWithin;
        var previewStep = editor.VersionsStep == LyricsVersionsStep.Preview;
        editor.SetVersionsComparison(!compact);
        VersionsGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        VersionsGrid.RowDefinitions[2].Height = new GridLength(0);
        VersionsGrid.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(220);
        VersionsGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        System.Windows.Controls.Grid.SetRowSpan(VersionsList, 1);
        System.Windows.Controls.Grid.SetRow(VersionPreview, 1);
        System.Windows.Controls.Grid.SetColumn(VersionPreview, compact ? 0 : 1);
        System.Windows.Controls.Grid.SetRowSpan(VersionPreview, 1);
        VersionsList.Visibility = compact && previewStep ? Visibility.Collapsed : Visibility.Visible;
        VersionPreview.Visibility = !compact || previewStep ? Visibility.Visible : Visibility.Collapsed;
        VersionPreview.Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(10, 0, 0, 8);
        PreviewNext.Visibility = compact && !previewStep ? Visibility.Visible : Visibility.Collapsed;
        VersionBack.Visibility = compact && previewStep ? Visibility.Visible : Visibility.Collapsed;
        if (listFocused && !VersionsList.IsVisible) VersionPreview.Focus();
        else if (previewFocused && !VersionPreview.IsVisible || navigationFocused && !compact) FocusSelectedVersion();
    }
}
