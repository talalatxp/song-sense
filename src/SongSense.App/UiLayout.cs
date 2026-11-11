using System.Windows;
using System.Windows.Controls;

namespace SongSense.App;

/// <summary>Opt-in layout roles; screen migrations remain independent of shared foundations.</summary>
public static class UiLayout
{
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached("IsCompact", typeof(bool), typeof(UiLayout), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetIsCompact(DependencyObject value) => (bool)value.GetValue(IsCompactProperty);
    public static void SetIsCompact(DependencyObject target, bool value) => target.SetValue(IsCompactProperty, value);
}

/// <summary>Gaps belong to the row, including wrapped rows, rather than to individual buttons.</summary>
public sealed class ActionPanel : Panel
{
    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(nameof(Gap), typeof(double), typeof(ActionPanel), new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure), ValidGap);
    public static readonly DependencyProperty RowGapProperty = DependencyProperty.Register(nameof(RowGap), typeof(double), typeof(ActionPanel), new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure), ValidGap);
    private static bool ValidGap(object value) => value is double d && double.IsFinite(d) && d >= 0;
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
    public double RowGap { get => (double)GetValue(RowGapProperty); set => SetValue(RowGapProperty, value); }
    protected override Size MeasureOverride(Size available)
    {
        foreach (UIElement item in InternalChildren) item.Measure(available);
        return Layout(available.Width, false);
    }
    protected override Size ArrangeOverride(Size final) { Layout(final.Width, true); return final; }
    private Size Layout(double width, bool arrange)
    {
        double x = 0, y = 0, height = 0, used = 0;
        foreach (UIElement item in InternalChildren)
        {
            if (item.Visibility == Visibility.Collapsed) continue;
            var size = item.DesiredSize;
            if (x > 0 && x + size.Width > width) { y += height + RowGap; x = 0; height = 0; }
            if (arrange) item.Arrange(new Rect(x, y, Math.Min(size.Width, width), size.Height));
            used = Math.Max(used, x + size.Width); height = Math.Max(height, size.Height); x += size.Width + Gap;
        }
        return new Size(used, y + height);
    }
}
