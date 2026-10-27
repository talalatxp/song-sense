using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;

namespace SongSense.App;

public sealed partial class ReadingDocument
{
    private ReadingBookmark? bookmark;
    private long navigationVersion;
    private int navigationDepth;
    private bool restorationPending, startRequested;
    public ReadingBookmark? Bookmark => bookmark;

    private void InitializeNavigation()
    {
        Loaded += (_, _) => RestoreBookmark();
        IsVisibleChanged += (_, _) => { if (IsVisible) RestoreBookmark(); };
        SelectionChanged += (_, _) => RememberPosition();
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0 && e.ViewportWidthChange == 0) RememberPosition();
        }));
    }
    public ReadingBookmark? CaptureBookmark()
    {
        if (!IsLoaded || !IsVisible || ViewportHeight <= 0 || !Document.ContentStart.HasValidLayout) return bookmark;
        var index = ReadingAnchors.Index(Document);
        var pointer = GetPositionFromPoint(new Point(Document.PagePadding.Left + 2, Document.PagePadding.Top + 2), true);
        if (pointer is null || ReadingAnchors.Locate(pointer, index) is not { } anchor) return bookmark;
        var top = pointer.GetCharacterRect(LogicalDirection.Forward).Top;
        if (!double.IsFinite(top)) return bookmark;
        return new(anchor, top, index.Select(entry => entry.Key).ToArray(), ReadingAnchors.Locate(Selection.Start, index), ReadingAnchors.Locate(Selection.End, index), Selection.Text);
    }
    public void RememberPosition()
    {
        if (!restorationPending && navigationDepth == 0) bookmark = CaptureBookmark();
    }
    public void PreserveReadingPosition(Action change) => PreserveReadingPosition(change, true);
    private void PreserveReadingPosition(Action change, bool capture)
    {
        if (navigationDepth == 0 && !restorationPending && capture) bookmark = CaptureBookmark();
        navigationDepth++;
        try { change(); }
        finally { if (--navigationDepth == 0) RestoreBookmark(); }
    }
    public void ResetReadingPosition()
    {
        bookmark = null; startRequested = true; RestoreBookmark();
    }
    public void RestoreBookmark()
    {
        var version = ++navigationVersion; restorationPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (version != navigationVersion) return;
            if (!IsLoaded || !IsVisible) { restorationPending = false; return; }
            UpdateLayout();
            if (startRequested)
            {
                Selection.Select(Document.ContentStart, Document.ContentStart); ScrollToHome(); startRequested = false;
            }
            else if (bookmark is { } saved)
            {
                var index = ReadingAnchors.Index(Document);
                if (saved.SelectionStart is { } start && saved.SelectionEnd is { } end && ReadingAnchors.Resolve(start, index) is { } from && ReadingAnchors.Resolve(end, index) is { } to && from.CompareTo(to) <= 0 && new TextRange(from, to).Text == saved.SelectionText)
                    Selection.Select(from, to);
                UpdateLayout();
                if (ReadingAnchors.ResolveView(saved, index) is { HasValidLayout: true } pointer)
                {
                    var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
                    if (!rect.IsEmpty && double.IsFinite(rect.Top)) ScrollToVerticalOffset(Math.Max(0, VerticalOffset + rect.Top - saved.Top));
                }
            }
            Dispatcher.BeginInvoke(() =>
            {
                if (version != navigationVersion) return;
                restorationPending = false; RememberPosition();
            }, DispatcherPriority.ContextIdle);
        }, DispatcherPriority.ContextIdle);
    }
}
