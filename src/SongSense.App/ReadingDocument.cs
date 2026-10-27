using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;

namespace SongSense.App;

/// <summary>A single native selection surface and scroll owner for each reading tab.</summary>
public sealed partial class ReadingDocument : RichTextBox
{
    public event EventHandler? TypographyChanged;
    public ReadingDocument()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        IsReadOnlyCaretVisible = true;
        IsDocumentEnabled = true;
        AcceptsTab = false;
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "Ink");
        SetResourceReference(SelectionBrushProperty, "Gold");
        Document.PagePadding = new Thickness(4);
        InitializeNavigation();
        SizeChanged += (_, _) => PreserveReadingPosition(ApplyTypographyCore, false);
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => { if (e.ViewportWidthChange != 0) PreserveReadingPosition(ApplyTypographyCore, false); }));
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Copiar", Command = ApplicationCommands.Copy });
        ContextMenu = menu;
    }

    public void SetPointSize(double points)
    {
        PreserveReadingPosition(() => { FontSize = Math.Clamp(points, 12, 24) * 4 / 3; ApplyTypographyCore(); });
    }

    public void ApplyTypography() => PreserveReadingPosition(ApplyTypographyCore);
    private void ApplyTypographyCore()
    {
        Document.FontSize = FontSize;
        Document.FontFamily = new FontFamily("Georgia");
        Document.LineHeight = FontSize * 1.5;
        Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        // Reserve the native vertical scrollbar; center prose at at most 36 em.
        var available = ViewportWidth > 0 ? ViewportWidth : Math.Max(0, ActualWidth - SystemParameters.VerticalScrollBarWidth);
        var inset = Math.Max(4, (available - FontSize * 36) / 2);
        Document.PagePadding = new Thickness(inset, 4, inset, 8);
        foreach (var paragraph in Paragraphs(Document.Blocks))
            paragraph.Margin = Equals(paragraph.Tag, "metadata") ? new Thickness(0) : new Thickness(0, 0, 0, FontSize * .75);
        TypographyChanged?.Invoke(this, EventArgs.Empty);
    }
    private static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph) yield return paragraph;
            if (block is Section section) foreach (var nested in Paragraphs(section.Blocks)) yield return nested;
            if (block is List list) foreach (var item in list.ListItems) foreach (var nested in Paragraphs(item.Blocks)) yield return nested;
        }
    }
}
