using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using SongSense.Core;

namespace SongSense.App;

/// <summary>Presentation only: keeps the stored insight intact and uses the reading document's selection.</summary>
public sealed class MeaningPresentation : IDisposable
{
    private readonly ReadingDocument view;
    private readonly string summary;
    private readonly Run summaryRun = new();
    private readonly Paragraph summaryToggle = new() { FontFamily = new FontFamily("Segoe UI"), FontSize = 14, LineHeight = 21, Tag = "metadata" };
    private readonly Paragraph summaryHeading = Heading("Resumen");
    private readonly Paragraph summaryParagraph;
    private bool expanded;
    private (double Width, double Font, double Height) measured;

    public MeaningPresentation(ReadingDocument view, SongInsight insight)
    {
        this.view = view; summary = insight.Summary;
        summaryParagraph = new Paragraph(summaryRun) { Tag = "metadata" };
        ReadingAnchors.SetKey(summaryHeading, "meaning:summary-heading"); ReadingAnchors.SetKey(summaryParagraph, "meaning:summary-text"); ReadingAnchors.SetKey(summaryToggle, "meaning:summary-toggle");
        Add(summaryHeading); Add(summaryParagraph);
        var more = new Hyperlink(new Run("Leer explicación completa")) { Focusable = true };
        KeyboardNavigation.SetIsTabStop(more, true);
        more.SetResourceReference(TextElement.ForegroundProperty, "Gold");
        AutomationProperties.SetName(more, "Leer explicación completa");
        more.Click += (_, _) => view.PreserveReadingPosition(() =>
        {
            expanded = !expanded;
            var label = expanded ? "Mostrar resumen breve" : "Leer explicación completa";
            more.Inlines.Clear(); more.Inlines.Add(label); AutomationProperties.SetName(more, label);
            Reflow();
        });
        summaryToggle.Inlines.Add(more);
        if (insight.Themes.Count > 0)
        {
            Add(Heading("Temas"));
            var themes = new List { FontFamily = new FontFamily("Segoe UI"), FontSize = 14, LineHeight = 21, Margin = new Thickness(0), MarkerStyle = TextMarkerStyle.Disc, Padding = new Thickness(24, 0, 0, 0) };
            foreach (var theme in insight.Themes) themes.ListItems.Add(new ListItem(new Paragraph(new Run(theme)) { Tag = "metadata" }));
            Add(themes);
        }
        AddDetails("MeaningMetaphors", $"Imágenes y metáforas · {insight.Metaphors.Count}", insight.Metaphors.Select(item =>
        {
            var section = new Section();
            section.Blocks.Add(Heading("Cita original"));
            var quote = new Paragraph(new Italic(new Run($"«{item.Text}»"))) { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(12, 0, 0, 0) };
            quote.SetResourceReference(Block.BorderBrushProperty, "Gold"); section.Blocks.Add(quote);
            section.Blocks.Add(Heading("Interpretación de la imagen"));
            section.Blocks.Add(new Paragraph(new Run(item.Explanation))); return section;
        }).ToArray(), insight.Metaphors.FirstOrDefault()?.Text);
        AddDetails("MeaningAlternatives", $"Interpretaciones alternativas · {insight.Alternatives.Count}", insight.Alternatives.Select((text, index) =>
        {
            var section = new Section(); section.Blocks.Add(Heading($"Lectura alternativa {index + 1}"));
            section.Blocks.Add(new Paragraph(new Run(text))); return section;
        }).ToArray(), insight.Alternatives.FirstOrDefault());
        if (insight.Warnings.Count > 0)
        {
            Add(Heading("Límites de esta interpretación"));
            Add(new Paragraph(new Run("Límites generales del análisis; no están vinculados a una afirmación concreta.")));
            var limits = new List { MarkerStyle = TextMarkerStyle.Disc, Padding = new Thickness(24, 0, 0, 0) };
            foreach (var warning in insight.Warnings) limits.ListItems.Add(new ListItem(new Paragraph(new Run(warning))));
            Add(limits);
        }
        view.TypographyChanged += TypographyChanged;
        view.SizeChanged += ViewSizeChanged;
        Reflow();
    }

    private void Add(Block block) => view.Document.Blocks.Add(block);
    private static Paragraph Heading(string text) => new(new Bold(new Run(text))) { FontFamily = new FontFamily("Segoe UI"), FontSize = 14, LineHeight = 21, Tag = "metadata" };
    private void AddDetails(string identity, string label, Section[] sections, string? hint)
    {
        if (sections.Length == 0) return;
        var link = new Hyperlink(new Run(label + " ▸")) { Focusable = true }; var heading = new Paragraph(link) { FontFamily = new FontFamily("Segoe UI"), FontSize = 14, LineHeight = 21, Tag = "metadata" }; Add(heading);
        KeyboardNavigation.SetIsTabStop(link, true);
        link.SetResourceReference(TextElement.ForegroundProperty, "Gold");
        AutomationProperties.SetAutomationId(link, identity);
        ReadingAnchors.SetKey(heading, identity);
        AutomationProperties.SetName(link, label + " · desplegar");
        var preview = new Paragraph(new Run(ShortHint(hint ?? ""))) { Tag = "metadata", FontSize = 12, LineHeight = 18, FontFamily = new FontFamily("Segoe UI") };
        preview.SetResourceReference(TextElement.ForegroundProperty, "Muted"); Add(preview);
        var contents = new Section(); foreach (var section in sections) contents.Blocks.Add(section);
        bool open = false;
        link.Click += (_, _) => view.PreserveReadingPosition(() =>
        {
            open = !open; link.Inlines.Clear(); link.Inlines.Add(label + (open ? " ▾" : " ▸"));
            AutomationProperties.SetName(link, label + (open ? " · contraer" : " · desplegar"));
            if (open) view.Document.Blocks.InsertAfter(preview, contents); else view.Document.Blocks.Remove(contents);
            view.ApplyTypography();
        });
    }
    private static string ShortHint(string text)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        return boundaries.Length <= 70 ? text : text[..boundaries[70]] + "…";
    }
    private void TypographyChanged(object? sender, EventArgs e)
    {
        var key = (ContentWidth, view.FontSize, view.ViewportHeight > 0 ? view.ViewportHeight : view.ActualHeight);
        if (key == measured) return;
        Reflow();
    }
    private double ContentWidth => Math.Max(40, (view.ViewportWidth > 0 ? view.ViewportWidth : 768) - view.Document.PagePadding.Left - view.Document.PagePadding.Right);
    private void Reflow() => view.PreserveReadingPosition(ReflowCore);
    private void ReflowCore()
    {
        measured = (ContentWidth, view.FontSize, view.ViewportHeight > 0 ? view.ViewportHeight : view.ActualHeight);
        // Heading and reveal link each use a 21 DIP line. Short summaries need no reveal link.
        var budget = measured.Height > 0 ? measured.Height * .35 : double.PositiveInfinity;
        var whole = PreviewSummary(summary, measured.Width, measured.Font, Math.Max(0, budget - 21));
        var preview = whole == summary ? summary : PreviewSummary(summary, measured.Width, measured.Font, Math.Max(0, budget - 42));
        var text = expanded ? summary : preview;
        if (summaryRun.Text != text)
        {
            // Reflow may shorten the preview. Preserve selections that still lie in its visible prefix.
            var inside = ReferenceEquals(summaryParagraph.Parent, view.Document) && summaryRun.ContentStart.CompareTo(view.Selection.Start) <= 0 && summaryRun.ContentEnd.CompareTo(view.Selection.End) >= 0;
            int start = inside ? summaryRun.ContentStart.GetOffsetToPosition(view.Selection.Start) : -1;
            int end = inside ? summaryRun.ContentStart.GetOffsetToPosition(view.Selection.End) : -1;
            summaryRun.Text = text;
            if (inside && end <= text.Length) view.Selection.Select(summaryRun.ContentStart.GetPositionAtOffset(start)!, summaryRun.ContentStart.GetPositionAtOffset(end)!);
        }
        if (text.Length == 0 && ReferenceEquals(summaryParagraph.Parent, view.Document)) view.Document.Blocks.Remove(summaryParagraph);
        else if (text.Length > 0 && !ReferenceEquals(summaryParagraph.Parent, view.Document)) view.Document.Blocks.InsertAfter(summaryHeading, summaryParagraph);
        var present = ReferenceEquals(summaryToggle.Parent, view.Document);
        if (preview != summary && !present) view.Document.Blocks.InsertAfter(text.Length > 0 ? summaryParagraph : summaryHeading, summaryToggle);
        else if (preview == summary && present) view.Document.Blocks.Remove(summaryToggle);
    }

    public static string PreviewSummary(string text, double width, double fontSize, double maximumHeight = double.PositiveInfinity)
    {
        var measure = new TextBlock { FontFamily = new FontFamily("Georgia"), FontSize = fontSize, TextWrapping = TextWrapping.Wrap, LineHeight = fontSize * 1.5, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        bool Fits(string value) { measure.Text = value; measure.Measure(new Size(Math.Max(40, width), double.PositiveInfinity)); return measure.DesiredSize.Height <= Math.Min(fontSize * 1.5 * 6, maximumHeight) + .1; }
        if (Fits(text)) return text;
        var boundaries = StringInfo.ParseCombiningCharacters(text); int low = 0, high = boundaries.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            var end = middle == boundaries.Length ? text.Length : boundaries[middle];
            if (Fits(text[..end] + "…")) low = middle; else high = middle - 1;
        }
        if (low == 0) return "";
        var length = low == boundaries.Length ? text.Length : boundaries[low];
        return text[..length].TrimEnd() + "…";
    }

    private void ViewSizeChanged(object sender, SizeChangedEventArgs e) => Reflow();
    public void Dispose() { view.TypographyChanged -= TypographyChanged; view.SizeChanged -= ViewSizeChanged; }
}
