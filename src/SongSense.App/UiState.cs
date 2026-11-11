using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Threading;

namespace SongSense.App;

public static class UiState
{
    private static readonly DependencyPropertyKey HasFieldErrorPropertyKey = DependencyProperty.RegisterAttachedReadOnly("HasFieldError", typeof(bool), typeof(UiState), new PropertyMetadata(false));
    public static readonly DependencyProperty HasFieldErrorProperty = HasFieldErrorPropertyKey.DependencyProperty;
    public static bool GetHasFieldError(DependencyObject target) => (bool)target.GetValue(HasFieldErrorProperty);
    public static readonly DependencyProperty FieldErrorProperty = DependencyProperty.RegisterAttached("FieldError", typeof(string), typeof(UiState), new PropertyMetadata("", (owner, args) => owner.SetValue(HasFieldErrorPropertyKey, !string.IsNullOrWhiteSpace(args.NewValue as string))));
    public static string GetFieldError(DependencyObject target) => (string)target.GetValue(FieldErrorProperty);
    public static void SetFieldError(DependencyObject target, string value) => target.SetValue(FieldErrorProperty, value);
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.RegisterAttached(
        "IsBusy", typeof(bool), typeof(UiState), new FrameworkPropertyMetadata(false));
    public static bool GetIsBusy(DependencyObject target) => (bool)target.GetValue(IsBusyProperty);
    public static void SetIsBusy(DependencyObject target, bool value) => target.SetValue(IsBusyProperty, value);
}

public enum NoticeKind { Information, Success, Warning, Error, Progress }
public enum NoticePresentation { Routine, Contextual, Attention }

public sealed class StatusNotice : Control
{
    public static readonly DependencyProperty PresentationProperty = DependencyProperty.Register(nameof(Presentation), typeof(NoticePresentation), typeof(StatusNotice), new FrameworkPropertyMetadata(NoticePresentation.Attention, UpdateText));
    public NoticePresentation Presentation { get => (NoticePresentation)GetValue(PresentationProperty); set => SetValue(PresentationProperty, value); }
    public static readonly DependencyProperty ExplanationProperty = DependencyProperty.Register(nameof(Explanation), typeof(string), typeof(StatusNotice), new PropertyMetadata(""));
    public string Explanation { get => (string)GetValue(ExplanationProperty); set => SetValue(ExplanationProperty, value); }
    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(StatusNotice), new PropertyMetadata(null, (owner, args) => owner.SetValue(HasActionPropertyKey, args.NewValue is not null)));
    private static readonly DependencyPropertyKey HasActionPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasAction), typeof(bool), typeof(StatusNotice), new PropertyMetadata(false));
    public static readonly DependencyProperty HasActionProperty = HasActionPropertyKey.DependencyProperty;
    public object? ActionContent { get => GetValue(ActionContentProperty); set => SetValue(ActionContentProperty, value); }
    public bool HasAction => (bool)GetValue(HasActionProperty);
    private readonly StatusAnnouncements announcements = new();
    private bool announcementQueued;
    private string previousAnnouncement = "";
    public static readonly DependencyProperty AnnouncementScopeProperty = DependencyProperty.Register(nameof(AnnouncementScope), typeof(string), typeof(StatusNotice), new PropertyMetadata("", (owner, _) => ((StatusNotice)owner).QueueAnnouncement()));
    public string AnnouncementScope { get => (string)GetValue(AnnouncementScopeProperty); set => SetValue(AnnouncementScopeProperty, value); }
    public StatusNotice()
    {
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Loaded += (_, _) => QueueAnnouncement();
        IsVisibleChanged += (_, _) => { if (IsVisible) QueueAnnouncement(); else announcements.Reset(); };
    }
    private void QueueAnnouncement()
    {
        if (!IsLoaded || announcementQueued) return;
        announcementQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            announcementQueued = false;
            if (!IsLoaded || !IsVisible || !announcements.Observe(Kind, Message, AnnouncementScope)) return;
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
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(nameof(Summary), typeof(string), typeof(StatusNotice), new FrameworkPropertyMetadata("", UpdateText));
    public string Summary { get => (string)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    private static readonly DependencyPropertyKey HasDetailsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasDetails), typeof(bool), typeof(StatusNotice), new PropertyMetadata(false));
    public static readonly DependencyProperty HasDetailsProperty = HasDetailsPropertyKey.DependencyProperty;
    public bool HasDetails => (bool)GetValue(HasDetailsProperty);
    public string DisplayText => (string)GetValue(DisplayTextProperty);
    private static void UpdateText(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var notice = (StatusNotice)target;
        var label = notice.Kind switch { NoticeKind.Success => "Listo", NoticeKind.Warning => "Atención", NoticeKind.Error => "Error", NoticeKind.Progress => "En curso", _ => "Información" };
        var text = notice.Presentation == NoticePresentation.Routine || string.IsNullOrWhiteSpace(notice.Summary) ? notice.Message : notice.Summary;
        notice.SetValue(DisplayTextPropertyKey, (notice.Presentation == NoticePresentation.Attention ? label + ": " : "") + text);
        notice.SetValue(HasDetailsPropertyKey, notice.Presentation != NoticePresentation.Routine && !string.IsNullOrWhiteSpace(notice.Summary) && notice.Summary != notice.Message && notice.Message.Length > 0);
        notice.QueueAnnouncement();
    }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public NoticeKind Kind { get => (NoticeKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    protected override AutomationPeer OnCreateAutomationPeer() => new NoticePeer(this);
    private sealed class NoticePeer(StatusNotice notice) : FrameworkElementAutomationPeer(notice)
    {
        protected override string GetClassNameCore() => nameof(StatusNotice);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetNameCore() => (notice.Presentation != NoticePresentation.Attention ? "" : notice.Kind switch
        {
            NoticeKind.Success => "Listo: ", NoticeKind.Warning => "Atención: ", NoticeKind.Error => "Error: ",
            NoticeKind.Progress => "En curso: ", _ => "Información: "
        }) + notice.Message;
    }
}
