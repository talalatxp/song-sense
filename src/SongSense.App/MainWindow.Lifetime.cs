using System.Windows;

namespace SongSense.App;
public partial class MainWindow
{
    /// <summary>Shared close path for X, Alt+F4, application menu and notification-area menu.</summary>
    public void ConfigureCloseBehavior(Func<Task> release, Action firstHideHint)
    {
        bool exitApproved = false, releasing = false, allowClose = false;
        ExitRequested += (_, _) => { exitApproved = true; Close(); };
        Closing += async (_, args) =>
        {
            if (allowClose) return;
            args.Cancel = true; SavePreferences();
            if (!exitApproved)
            {
                if (KeepInTray)
                {
                    if (MarkTrayHintShown()) firstHideHint();
                    Hide(); return;
                }
                _ = Dispatcher.BeginInvoke(RequestExit); return;
            }
            if (releasing) return;
            releasing = true;
            await release();
            allowClose = true;
            await Dispatcher.InvokeAsync(Close);
        };
    }
}
