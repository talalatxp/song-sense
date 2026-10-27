using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SongSense.App;

/// <summary>Announces changes of validation state, never the character counter on every keystroke.</summary>
public sealed class LiveText : TextBlock
{
    private readonly StatusAnnouncements announcements = new();
    private bool queued;
    public LiveText()
    {
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Loaded += (_, _) => QueueAnnouncement();
        IsVisibleChanged += (_, _) => { if (IsVisible) QueueAnnouncement(); };
    }
    static LiveText() => TextProperty.OverrideMetadata(typeof(LiveText), new FrameworkPropertyMetadata("", (owner, _) => ((LiveText)owner).QueueAnnouncement()));
    private void QueueAnnouncement()
    {
        if (!IsLoaded || queued) return;
        queued = true;
        Dispatcher.BeginInvoke(() =>
        {
            queued = false;
            if (!IsVisible || !announcements.Observe(NoticeKind.Error, Text)) return;
            var peer = UIElementAutomationPeer.FromElement(this) ?? UIElementAutomationPeer.CreatePeerForElement(this);
            if (peer is not null && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }, DispatcherPriority.Background);
    }
}
