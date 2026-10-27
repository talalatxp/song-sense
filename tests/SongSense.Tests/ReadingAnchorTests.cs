using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class ReadingAnchorTests
{
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA test did not finish");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static void Flush(Window window)
    {
        for (int i = 0; i < 4; i++)
        {
            window.UpdateLayout(); var frame = new DispatcherFrame();
            window.Dispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ContextIdle); Dispatcher.PushFrame(frame);
        }
    }
    private static ReadingDocument Document()
    {
        var view = new ReadingDocument(); view.Document.Blocks.Clear(); view.SetPointSize(16);
        for (int i = 0; i < 40; i++)
        {
            var p = new Paragraph(new Run($"Estrofa propia {i}: abro una ventana y observo la luz.\nOtro verso continuo que ocupa su propio espacio."));
            ReadingAnchors.SetKey(p, "lyrics:original:" + i); view.Document.Blocks.Add(p);
        }
        return view;
    }
    private static void Visible(ReadingDocument view, ReadingBookmark bookmark)
    {
        var position = ReadingAnchors.ResolveView(bookmark, ReadingAnchors.Index(view.Document)); Assert.NotNull(position); Assert.True(position.HasValidLayout);
        var rect = position.GetCharacterRect(LogicalDirection.Forward); Assert.True(rect.Bottom > 0 && rect.Top < view.ViewportHeight, $"Anchor {bookmark.View.Key}: {rect} outside viewport {view.ViewportHeight}");
    }
    [Fact]
    public void VisibleAndHiddenTabsKeepLogicalTextAndSelectionAcrossTypographyAndWidth() => Sta(() =>
    {
        var first = Document(); var second = Document();
        var tabs = new TabControl { Items = { new TabItem { Header = "Letra", Content = first }, new TabItem { Header = "Significado", Content = second } } };
        var window = new Window { Width = 700, Height = 600, Content = tabs, ShowActivated = false };
        try
        {
            window.Show(); Flush(window); first.ScrollToVerticalOffset(800); Flush(window); first.RememberPosition(); var a = first.Bookmark!;
            tabs.SelectedIndex = 1; Flush(window); second.ScrollToVerticalOffset(1000); Flush(window); second.RememberPosition(); var b = second.Bookmark!;
            first.SetPointSize(24); second.SetPointSize(24); window.Width = 360; Flush(window); Visible(second, b);
            tabs.SelectedIndex = 0; Flush(window); Visible(first, a);
            var paragraph = first.Document.Blocks.OfType<Paragraph>().ElementAt(8); var run = (Run)paragraph.Inlines.FirstInline!;
            first.Selection.Select(run.ContentStart, run.ContentStart.GetPositionAtOffset(20)!); var selected = first.Selection.Text;
            first.RememberPosition(); a = first.Bookmark!; tabs.SelectedIndex = 1; Flush(window);
            first.SetPointSize(12); second.SetPointSize(12); Flush(window); Visible(second, b);
            tabs.SelectedIndex = 0; Flush(window); Visible(first, a); Assert.Equal(selected, first.Selection.Text);
        }
        finally { window.Close(); }
    });
    [Fact]
    public void EquivalentStanzaAndNearestSurvivingBlockAreResolvedWithoutPixels() => Sta(() =>
    {
        var view = Document(); var before = ReadingAnchors.Index(view.Document);
        var anchor = new ReadingBookmark(new(before[5].Key, 10), 4, before.Select(e => e.Key).ToArray(), null, null, "");
        ReadingAnchors.SetKey(before[5].Paragraph, "lyrics:translation:5");
        Assert.Same(before[5].Paragraph, ReadingAnchors.ResolveView(anchor, ReadingAnchors.Index(view.Document))!.Paragraph);
        view.Document.Blocks.Remove(before[5].Paragraph);
        Assert.Same(before[4].Paragraph, ReadingAnchors.ResolveView(anchor, ReadingAnchors.Index(view.Document))!.Paragraph);
    });
    [Fact]
    public void NewDocumentCancelsLateRestoreAndStartsAtTopWithNoSelection() => Sta(() =>
    {
        var view = Document(); var window = new Window { Width = 400, Height = 500, Content = view, ShowActivated = false };
        try
        {
            window.Show(); Flush(window); view.ScrollToVerticalOffset(900); Flush(window); view.RememberPosition();
            view.SetPointSize(24); view.Document.Blocks.Clear(); view.Document.Blocks.Add(new Paragraph(new Run("Documento nuevo"))); view.ResetReadingPosition(); Flush(window);
            Assert.Equal(0, view.VerticalOffset); Assert.True(view.Selection.IsEmpty);
        }
        finally { window.Close(); }
    });
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ReadingModeRoundTripsIndependentlyOfOtherPreferences(int mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SongSense.Mode", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WindowPreferenceStore(Path.Combine(directory, "window.json"));
            var expected = new WindowPreferences(Left: 42, Width: 600, Tab: 1, TextSize: 24, KeepInTray: false, TrayHintShown: true, ReadingMode: mode);
            Assert.True(store.Save(expected)); Assert.Equal(expected, store.Read());
            File.WriteAllText(Path.Combine(directory, "window.json"), "{\"Version\":1,\"TextSize\":20,\"KeepInTray\":false}");
            Assert.Equal(2, store.Read().ReadingMode); Assert.Equal(20, store.Read().TextSize); Assert.False(store.Read().KeepInTray);
            Assert.Equal(2, WindowPlacement.Normalize(expected with { ReadingMode = 99 }).ReadingMode);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
