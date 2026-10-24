using System.Windows;
using System.Windows.Controls;
namespace SongSense.App;
public sealed class ConfirmationWindow : Window
{
    public ConfirmationWindow(string title, string message, string accept, string cancel = "Cancelar")
    {
        Title = title; Width = 480; SizeToContent = SizeToContent.Height; MinWidth = 360;
        SetResourceReference(BackgroundProperty, "Canvas"); SetResourceReference(ForegroundProperty, "Ink");
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var actions = new WrapPanel();
        var safe = new Button { Content = cancel, IsDefault = true, IsCancel = true, Margin = new Thickness(0, 0, 10, 8) };
        var confirm = new Button { Content = accept, Margin = new Thickness(0, 0, 0, 8) };
        safe.Click += (_, _) => DialogResult = false; confirm.Click += (_, _) => DialogResult = true;
        actions.Children.Add(safe); actions.Children.Add(confirm); panel.Children.Add(actions); Content = panel;
        Loaded += (_, _) => safe.Focus(); SourceInitialized += (_, _) => WindowAppearance.Apply(this);
    }
    public static bool Ask(Window owner, string title, string message, string accept, string cancel = "Cancelar") =>
        new ConfirmationWindow(title, message, accept, cancel) { Owner = owner }.ShowDialog() == true;
}
public sealed record CacheDeletionRequest(bool All, long? TrackRevision, long LyricsRevision, long Epoch, string Label);
