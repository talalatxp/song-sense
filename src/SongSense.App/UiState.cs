using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Threading;

namespace SongSense.App;

public static class UiState
{
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.RegisterAttached(
        "IsBusy", typeof(bool), typeof(UiState), new FrameworkPropertyMetadata(false));
    public static bool GetIsBusy(DependencyObject target) => (bool)target.GetValue(IsBusyProperty);
    public static void SetIsBusy(DependencyObject target, bool value) => target.SetValue(IsBusyProperty, value);
}

public enum NoticeKind { Information, Success, Warning, Error, Progress }

public sealed class StatusNotice : Control
{
    private readonly StatusAnnouncements announcements = new();
    private bool announcementQueued;
    private string previousAnnouncement = "";
    public static readonly DependencyProperty AnnouncementScopeProperty = DependencyProperty.Register(nameof(AnnouncementScope), typeof(string), typeof(StatusNotice), new PropertyMetadata("", (owner, _) => ((StatusNotice)owner).QueueAnnouncement()));
    public string AnnouncementScope { get => (string)GetValue(AnnouncementScopeProperty); set => SetValue(AnnouncementScopeProperty, value); }
    public StatusNotice()
    {
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Loaded += (_, _) => QueueAnnouncement();
    }
    private void QueueAnnouncement()
    {
        if (!IsLoaded || announcementQueued) return;
        announcementQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            announcementQueued = false;
            if (!IsLoaded || !announcements.Observe(Kind, Message, AnnouncementScope)) return;
            var peer = UIElementAutomationPeer.FromElement(this) ?? UIElementAutomationPeer.CreatePeerForElement(this);
            if (peer is not null && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            {
                peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, previousAnnouncement, DisplayText);
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            previousAnnouncement = DisplayText;
        }, DispatcherPriority.Background);
    }
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(StatusNotice), new PropertyMetadata(false));
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DisplayText), typeof(string), typeof(StatusNotice), new FrameworkPropertyMetadata(""));
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(StatusNotice), new FrameworkPropertyMetadata("", UpdateText));
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(NoticeKind), typeof(StatusNotice), new FrameworkPropertyMetadata(NoticeKind.Information, UpdateText));
    public string DisplayText => (string)GetValue(DisplayTextProperty);
    private static void UpdateText(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var notice = (StatusNotice)target;
        var label = notice.Kind switch { NoticeKind.Success => "Listo", NoticeKind.Warning => "Atención", NoticeKind.Error => "Error", NoticeKind.Progress => "En curso", _ => "Información" };
        notice.SetValue(DisplayTextPropertyKey, label + ": " + notice.Message);
        notice.QueueAnnouncement();
    }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public NoticeKind Kind { get => (NoticeKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    protected override AutomationPeer OnCreateAutomationPeer() => new NoticePeer(this);
    private sealed class NoticePeer(StatusNotice notice) : FrameworkElementAutomationPeer(notice)
    {
        protected override string GetClassNameCore() => nameof(StatusNotice);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetNameCore() => notice.Kind switch
        {
            NoticeKind.Success => "Listo: ", NoticeKind.Warning => "Atención: ", NoticeKind.Error => "Error: ",
            NoticeKind.Progress => "En curso: ", _ => "Información: "
        } + notice.Message;
    }
}
