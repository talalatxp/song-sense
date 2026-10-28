using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using SongSense.App;
using SongSense.Core;
using Xunit;

namespace SongSense.Tests;

public sealed class MeaningPresentationTests
{
    private static void Sta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static SongInsight Result(string summary, int count = 0) => new(1, "es", [], summary, ["Cambio", "Esperanza"],
        Enumerable.Range(1, count).Select(i => new ExplainedMetaphor($"Cita {i}", $"Explicación {i}")).ToArray(),
        Enumerable.Range(1, count).Select(i => $"Alternativa {i}").ToArray(), Enumerable.Range(1, count).Select(i => $"Límite {i}").ToArray());
    private static string Text(ReadingDocument view) => new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
    private static IEnumerable<Hyperlink> Links(ReadingDocument view) => view.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Hyperlink>());
    private static void Click(Hyperlink link) => link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
    private static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph) yield return paragraph;
            if (block is Section section) foreach (var nested in Paragraphs(section.Blocks)) yield return nested;
            if (block is System.Windows.Documents.List list) foreach (var item in list.ListItems) foreach (var nested in Paragraphs(item.Blocks)) yield return nested;
        }
    }
    public static IEnumerable<object[]> TypographyMatrix()
    {
        foreach (var points in new[] { 12d, 16d, 20d, 24d })
        foreach (var scale in new[] { 1d, 1.57d }) yield return [points, scale];
    }
    [Theory]
    [MemberData(nameof(TypographyMatrix))]
    public void FullMeaningUsesReadingSizeForEveryContentCategoryAndScaledUiForHierarchy(double points, double scale) => Sta(() =>
    {
        var view = new ReadingDocument { UiControlSize = 14 * scale, UiSecondarySize = 12 * scale }; view.SetPointSize(points);
        using var presentation = new MeaningPresentation(view, Result("Resumen corto", 2));
        foreach (var link in Links(view).ToArray()) Click(link);
        foreach (var paragraph in Paragraphs(view.Document.Blocks))
        {
            var role = ReadingDocument.GetTextRole(paragraph);
            var expected = role switch
            {
                ReadingTextRole.Heading => Math.Max(14 * scale, .9 * points * 4 / 3),
                ReadingTextRole.Navigation => Math.Max(14 * scale, .85 * points * 4 / 3),
                ReadingTextRole.Secondary => 12 * scale,
                _ => points * 4 / 3
            };
            Assert.Equal(expected, paragraph.FontSize, 6); Assert.Equal(expected * 1.5, paragraph.LineHeight, 6);
            if (role == ReadingTextRole.Heading) Assert.Equal(FontWeights.SemiBold, paragraph.FontWeight);
        }
        var body = Paragraphs(view.Document.Blocks).ToArray();
        foreach (var category in new[] { "Resumen corto", "Cambio", "«Cita 1»", "Explicación 1", "Alternativa 1", "Límite 1" })
        {
            // WPF includes the list marker in a paragraph's native text range.
            var paragraph = Assert.Single(body, p => ReadingDocument.GetTextRole(p) != ReadingTextRole.Secondary && new TextRange(p.ContentStart, p.ContentEnd).Text.Trim().TrimStart('•', '\t', ' ') == category);
            Assert.Equal(points * 4 / 3, paragraph.FontSize, 6);
        }
    });
    [Fact]
    public void UiResourcesRecalculateHierarchyWithoutMultiplyingReadingSizeOrReplacingDocument() => Sta(() =>
    {
        var view = new ReadingDocument(); UiTypography.Apply(view.Resources, 1); view.SetPointSize(12);
        using var presentation = new MeaningPresentation(view, Result("Resumen corto", 1)); var document = view.Document;
        UiTypography.Apply(view.Resources, 1.57);
        Assert.Equal(14 * 1.57, view.UiControlSize, 6); Assert.Equal(12 * 1.57, view.UiSecondarySize, 6);
        Assert.Equal(16, view.FontSize); Assert.Same(document, view.Document);
        var heading = view.Document.Blocks.OfType<Paragraph>().First(p => ReadingDocument.GetTextRole(p) == ReadingTextRole.Heading);
        Assert.Equal(21.98, heading.FontSize, 6);
    });
    [Fact]
    public void SummaryReinsertedAfterSmallViewportUsesTheNewReadingSize() => Sta(() =>
    {
        var view = new ReadingDocument(); view.SetPointSize(12);
        using var presentation = new MeaningPresentation(view, Result(string.Join(' ', Enumerable.Repeat("Una oportunidad para observar la luz.", 70))));
        var window = new Window { Width = 360, Height = 180, Content = view, ShowActivated = false };
        void Flush()
        {
            for (var i = 0; i < 4; i++)
            {
                window.UpdateLayout(); var frame = new DispatcherFrame();
                window.Dispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle); Dispatcher.PushFrame(frame);
            }
        }
        try
        {
            window.Show(); Flush();
            Assert.DoesNotContain(view.Document.Blocks.OfType<Paragraph>(), p => ReadingAnchors.GetKey(p) == "meaning:summary-text");
            view.SetPointSize(24); window.Height = 800; Flush();
            var paragraph = Assert.Single(view.Document.Blocks.OfType<Paragraph>(), p => ReadingAnchors.GetKey(p) == "meaning:summary-text");
            Assert.Equal(32, paragraph.FontSize); Assert.Equal(48, paragraph.LineHeight);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(120,12)] [InlineData(220,24)] [InlineData(500,20)]
    public void PreviewUsesAtMostThirtyFivePercentOfViewport(double viewport,double points) => Sta(() =>
    {
        var font=points*4/3;var text=string.Join(" ",Enumerable.Repeat("Una interpretación completa y conservada.",100));
        var preview=MeaningPresentation.PreviewSummary(text,280,font,viewport*.35);
        if(preview.Length==0) return;
        var measure=new TextBlock {Text=preview,FontFamily=new("Georgia"),FontSize=font,TextWrapping=TextWrapping.Wrap,LineHeight=font*1.5,LineStackingStrategy=LineStackingStrategy.BlockLineHeight};
        measure.Measure(new Size(280,double.PositiveInfinity));Assert.True(measure.DesiredSize.Height<=viewport*.35+.1);
    });
    [Fact]
    public void DiagnosticCopyRemovesCredentialsAndAccountIdentifiers()
    {
        var result=AuxiliaryContent.Sanitize("HTTP401 Bearer abc-secret sk-fake-key person@example.invalid");
        Assert.DoesNotContain("abc-secret",result);Assert.DoesNotContain("sk-fake-key",result);Assert.DoesNotContain("person@",result);Assert.Contains("HTTP401",result);
    }
    [Theory]
    [InlineData(12)] [InlineData(16)] [InlineData(20)] [InlineData(24)]
    public void LongPreviewFitsSixLinesAndKeepsWholeGraphemes(double points) => Sta(() =>
    {
        var body = string.Concat(Enumerable.Repeat("Otra lectura · e\u0301 🎵 漢字. ", 80));
        var size = points * 4 / 3;
        var preview = MeaningPresentation.PreviewSummary(body, 280, size);
        Assert.EndsWith("…", preview); Assert.StartsWith(preview[..^1], body);
        var cut = preview[..^1].Length; Assert.Contains(cut, StringInfo.ParseCombiningCharacters(body));
        var measure = new TextBlock { Text = preview, FontFamily = new("Georgia"), FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        measure.Measure(new Size(280, double.PositiveInfinity)); Assert.InRange(measure.DesiredSize.Height, size * 1.5 * 5, size * 1.5 * 6 + .1);
    });
    [Fact]
    public void ShortSummaryHasNoToggleOrEmptyDetails() => Sta(() =>
    {
        var view = new ReadingDocument(); using var presentation = new MeaningPresentation(view, Result("Una lectura corta."));
        Assert.Contains("Una lectura corta.", Text(view)); Assert.Contains("Temas", Text(view));
        Assert.Empty(Links(view)); Assert.DoesNotContain("Límites de esta interpretación", Text(view));
    });
    [Theory]
    [InlineData(1)] [InlineData(3)]
    public void DetailsHaveCountsAndDistinctQuoteExplanationAndThemesWhileWarningsStayVisible(int count) => Sta(() =>
    {
        var view = new ReadingDocument(); using var presentation = new MeaningPresentation(view, Result("Resumen corto", count));
        Assert.Contains($"Imágenes y metáforas · {count}", Text(view)); Assert.Contains($"Interpretaciones alternativas · {count}", Text(view));
        Assert.Contains("Límite 1", Text(view)); Assert.Contains("no están vinculados a una afirmación concreta", Text(view));
        Assert.DoesNotContain("Explicación 1", Text(view));
        Click(Links(view).First()); Assert.Contains("Cita original", Text(view)); Assert.Contains("«Cita 1»", Text(view)); Assert.Contains("Interpretación de la imagen", Text(view)); Assert.Contains("Explicación 1", Text(view));
        Click(Links(view).First()); Assert.DoesNotContain("Explicación 1", Text(view)); Assert.Contains("Límite 1", Text(view));
        Click(Links(view).Last()); Assert.Contains("Lectura alternativa 1", Text(view)); Assert.Contains("Alternativa 1", Text(view));
    });
    [Fact]
    public void RevealUsesExactStoredSummaryAndKeepsDocument() => Sta(() =>
    {
        var summary = string.Join(" ", Enumerable.Repeat("El narrador contempla una nueva oportunidad.", 80));
        var result = Result(summary, 1); var view = new ReadingDocument(); using var presentation = new MeaningPresentation(view, result);
        var document = view.Document; Assert.DoesNotContain(summary, Text(view));
        Click(Links(view).First()); Assert.Contains(summary, Text(view)); Assert.Same(document, view.Document); Assert.Equal(summary, result.Summary);
        Click(Links(view).First()); Assert.DoesNotContain(summary, Text(view)); Assert.Equal(summary, result.Summary);
    });
    [Fact]
    public void ReflowKeepsSelectionInVisibleSummaryPrefixAndLeavesStoredResultIntact() => Sta(() =>
    {
        var summary = string.Join(" ", Enumerable.Repeat("Otra oportunidad para observar la luz.", 60));
        var result = Result(summary); var view = new ReadingDocument(); view.SetPointSize(16);
        using var presentation = new MeaningPresentation(view, result);
        var paragraph = view.Document.Blocks.OfType<Paragraph>().First(p => new TextRange(p.ContentStart, p.ContentEnd).Text.StartsWith("Otra oportunidad"));
        var run = Assert.IsType<Run>(paragraph.Inlines.FirstInline);
        view.Selection.Select(run.ContentStart, run.ContentStart.GetPositionAtOffset(16)!);
        var selection = view.Selection.Text; var document = view.Document;
        view.SetPointSize(24);
        Assert.Equal(selection, view.Selection.Text); Assert.Same(document, view.Document); Assert.Equal(summary, result.Summary);
    });
}
