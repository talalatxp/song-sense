using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Documents;
using System.Windows.Input;
using SongSense.App;
using Xunit;

namespace SongSense.Tests;

public sealed class ReadingDocumentTests
{
    private static void Sta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData(12)] [InlineData(16)] [InlineData(20)] [InlineData(24)]
    public void PointScaleReflowsWithoutReplacingDocumentOrSelection(double points) => Sta(() =>
    {
        var view = new ReadingDocument();
        var paragraph = new Paragraph(new Run("A selected verse\nOtra línea · 漢字 🎵 e\u0301")); view.Document.Blocks.Add(paragraph);
        var start = paragraph.ContentStart.GetPositionAtOffset(1)!;
        view.Selection.Select(start, start.GetPositionAtOffset(16)!);
        var selected = view.Selection.Text; var document = view.Document;
        view.SetPointSize(points);
        Assert.Same(document, view.Document); Assert.Equal(selected, view.Selection.Text);
        Assert.Equal(points * 4 / 3, view.Document.FontSize, 6);
        Assert.Equal(points * 2, view.Document.LineHeight, 6);
        Assert.Equal(points, paragraph.Margin.Bottom, 6);
    });

    [Fact]
    public void NativeTextPatternReportsReadOnlyAndPartialSelection() => Sta(() =>
    {
        var view = new ReadingDocument();
        var paragraph = new Paragraph(new Run("One verse\nA different verse")); view.Document.Blocks.Add(paragraph);
        var start = paragraph.ContentStart.GetPositionAtOffset(1)!;
        view.Selection.Select(start, start.GetPositionAtOffset(9)!);
        var peer = new RichTextBoxAutomationPeer(view);
        var text = Assert.IsAssignableFrom<ITextProvider>(peer.GetPattern(PatternInterface.Text));
        Assert.Equal(true, text.DocumentRange.GetAttributeValue(TextPatternIdentifiers.IsReadOnlyAttribute.Id));
        Assert.Equal("One verse", Assert.Single(text.GetSelection()).GetText(-1));
        var before = text.DocumentRange.GetText(-1);
        ApplicationCommands.Cut.Execute(null, view); ApplicationCommands.Delete.Execute(null, view);
        Assert.Equal(before, text.DocumentRange.GetText(-1)); Assert.False(view.IsUndoEnabled);
    });

    [Fact]
    public void LongUnbrokenUnicodeTextRetainsEveryCharacterAndFitsViewport() => Sta(() =>
    {
        var view = new ReadingDocument(); var body = new string('漢', 20000);
        view.Document.Blocks.Add(new Paragraph(new Run(body)));
        view.SetPointSize(24);
        view.Measure(new Size(300, 180)); view.Arrange(new Rect(0, 0, 300, 180)); view.UpdateLayout();
        Assert.Contains(body, new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text);
        Assert.True(view.ExtentWidth <= view.ViewportWidth + .1);
        Assert.True(view.ExtentHeight > view.ViewportHeight);
    });

    [Fact]
    public void ReplacingReadOnlyDocumentCannotUndoToPreviousSong() => Sta(() =>
    {
        var view = new ReadingDocument(); view.Document.Blocks.Add(new Paragraph(new Run("Previous song")));
        view.Document.Blocks.Clear(); view.Document.Blocks.Add(new Paragraph(new Run("Current song")));
        ApplicationCommands.Undo.Execute(null, view);
        var text = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
        Assert.Contains("Current song", text); Assert.DoesNotContain("Previous song", text);
    });
}
