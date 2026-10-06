using System.Windows;

namespace SongSense.App;

public partial class LyricsEditorWindow : Window
{
    public LyricsEditorWindow(LyricsEditorViewModel editor)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowAppearance.Apply(this);
        DataContext = editor;
        editor.Committed += OnCommitted;
        Closed += (_, _) => { editor.Committed -= OnCommitted; editor.Dispose(); };
    }
    private void OnCommitted(object? sender, EventArgs args) => Close();
}
