using System.Windows;

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
        VersionsList.SelectionChanged += (_, _) => { if (ActualWidth < 640 && editor.Selected is not null) { previewStep = true; ApplyEditorLayout(); VersionPreview.Focus(); } };

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
    private bool previewStep;
    private void ShowSelectedVersion(object sender, RoutedEventArgs args) { if (editor.Selected is null) return; previewStep = true; ApplyEditorLayout(); VersionPreview.Focus(); }
    private void ShowVersions(object sender, RoutedEventArgs args) { previewStep = false; ApplyEditorLayout(); VersionsList.Focus(); }
    private void AdaptEditor(object sender, SizeChangedEventArgs args) => ApplyEditorLayout();
    private void ApplyEditorLayout()
    {
        if (VersionsList is null) return;
        var compact = ActualWidth < 640;
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
        PreviewNext.Visibility = compact && !previewStep ? Visibility.Visible : Visibility.Collapsed;
        PreviewNext.IsEnabled = editor.Selected is not null;
        VersionBack.Visibility = compact && previewStep ? Visibility.Visible : Visibility.Collapsed;
    }
}
