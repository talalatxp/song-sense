using Forms = System.Windows.Forms;

namespace SongSense.App;

public sealed class DesktopTray : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon drawingIcon;
    public DesktopTray(MainViewModel model, Action open, Action exit)
    {
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/SongSense;component/Assets/SongSense.ico"))!.Stream;
        drawingIcon = new System.Drawing.Icon(stream);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir", null, (_, _) => open());
        var pause = new Forms.ToolStripMenuItem();
        pause.Click += (_, _) => model.AutomaticUpdatesEnabled = !model.AutomaticUpdatesEnabled;
        menu.Items.Add(pause);
        menu.Opening += (_, _) => pause.Text = model.AutomaticUpdatesEnabled ? "Pausar seguimiento" : "Reanudar seguimiento";
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("Salir", null, (_, _) => exit());
        icon = new Forms.NotifyIcon { Icon = drawingIcon, Text = "Song Sense · Abrir, seguimiento y Salir", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => open();
        icon.BalloonTipClicked += (_, _) => open();
    }
    public void ShowFirstHideHint()
    {
        icon.BalloonTipTitle = "Song Sense sigue en la bandeja";
        icon.BalloonTipText = "Pulsa aquí para Abrir. Doble clic en el icono también abre la ventana. Para terminar: clic derecho en el icono → Salir. Ocultar no habilita análisis.";
        icon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        icon.ShowBalloonTip(8000);
    }
    public void Dispose() { icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Dispose(); drawingIcon.Dispose(); }
}
