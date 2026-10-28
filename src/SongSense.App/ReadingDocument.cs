using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;

namespace SongSense.App;

public enum ReadingTextRole { Inherited, Content, Heading, Navigation, Secondary }

/// <summary>A single native selection surface and scroll owner for each reading tab.</summary>
public sealed partial class ReadingDocument : RichTextBox
{
    public static readonly DependencyProperty TextRoleProperty = DependencyProperty.RegisterAttached("TextRole", typeof(ReadingTextRole), typeof(ReadingDocument), new PropertyMetadata(ReadingTextRole.Inherited));
    public static void SetTextRole(DependencyObject target, ReadingTextRole role) => target.SetValue(TextRoleProperty, role);
    public static ReadingTextRole GetTextRole(DependencyObject target) => (ReadingTextRole)target.GetValue(TextRoleProperty);
    public static readonly DependencyProperty UiControlSizeProperty = DependencyProperty.Register(nameof(UiControlSize), typeof(double), typeof(ReadingDocument), new PropertyMetadata(14d, UiSizeChanged));
    public static readonly DependencyProperty UiSecondarySizeProperty = DependencyProperty.Register(nameof(UiSecondarySize), typeof(double), typeof(ReadingDocument), new PropertyMetadata(12d, UiSizeChanged));
    public double UiControlSize { get => (double)GetValue(UiControlSizeProperty); set => SetValue(UiControlSizeProperty, value); }
    public double UiSecondarySize { get => (double)GetValue(UiSecondarySizeProperty); set => SetValue(UiSecondarySizeProperty, value); }
    private static void UiSizeChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) => ((ReadingDocument)owner).ApplyTypography();
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
        SetResourceReference(UiControlSizeProperty, "UiControlFont");
        SetResourceReference(UiSecondarySizeProperty, "UiSecondaryFont");
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
        foreach (var block in TypographyBlocks(Document.Blocks))
            ApplyBlockTypography(block);
        foreach (var paragraph in Paragraphs(Document.Blocks))
            paragraph.Margin = Equals(paragraph.Tag, "metadata") ? new Thickness(0) : new Thickness(0, 0, 0, FontSize * .75);
        TypographyChanged?.Invoke(this, EventArgs.Empty);
    }
    internal void ApplyBlockTypography(Block block)
    {
        var role = GetTextRole(block);
        if (role == ReadingTextRole.Inherited) return;
        block.FontSize = role switch
        {
            ReadingTextRole.Heading => Math.Max(UiControlSize, .9 * FontSize),
            ReadingTextRole.Navigation => Math.Max(UiControlSize, .85 * FontSize),
            ReadingTextRole.Secondary => UiSecondarySize,
            _ => FontSize
        };
        block.LineHeight = 1.5 * block.FontSize;
        block.FontFamily = new FontFamily(role == ReadingTextRole.Content ? "Georgia" : "Segoe UI");
        block.FontWeight = role == ReadingTextRole.Heading ? FontWeights.SemiBold : FontWeights.Normal;
        if (block is List list && role == ReadingTextRole.Content)
            list.Padding = new Thickness(Math.Max(24, FontSize * 1.5), 0, 0, 0);
    }
    private static IEnumerable<Block> TypographyBlocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            if (block is Section section) foreach (var nested in TypographyBlocks(section.Blocks)) yield return nested;
            if (block is List list) foreach (var item in list.ListItems) foreach (var nested in TypographyBlocks(item.Blocks)) yield return nested;
        }
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
