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
    private void AdaptEditor(object sender, SizeChangedEventArgs args)
    {
        if (VersionsList is null) return;
        var compact = ActualWidth < 640;
        VersionsGrid.RowDefinitions[1].Height = new GridLength(compact ? .7 : 1, GridUnitType.Star);
        VersionsGrid.RowDefinitions[2].Height = new GridLength(compact ? 1.3 : 1, GridUnitType.Star);
        VersionsGrid.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(220);
        VersionsGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        System.Windows.Controls.Grid.SetRowSpan(VersionsList, compact ? 1 : 2);
        System.Windows.Controls.Grid.SetRow(VersionPreview, compact ? 2 : 1);
        System.Windows.Controls.Grid.SetColumn(VersionPreview, compact ? 0 : 1);
        System.Windows.Controls.Grid.SetRowSpan(VersionPreview, compact ? 1 : 2);
    }
}
