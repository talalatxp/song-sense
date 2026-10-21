namespace SongSense.Core;

public sealed record WindowPreferences(int Version = 1, double? Left = null, double? Top = null,
    double Width = 860, double Height = 780, int Tab = 0, double TextSize = 16, bool AlwaysOnTop = false);
public sealed record WorkArea(double Left, double Top, double Width, double Height);
public static class WindowPlacement
{
    public static WindowPreferences Normalize(WindowPreferences value) => value.Version != 1 ? new() : value with
    {
        Left = value.Left is { } left && double.IsFinite(left) ? left : null,
        Top = value.Top is { } top && double.IsFinite(top) ? top : null,
        Width = double.IsFinite(value.Width) ? Math.Clamp(value.Width, 360, 4096) : 860,
        Height = double.IsFinite(value.Height) ? Math.Clamp(value.Height, 480, 4096) : 780,
        Tab = value.Tab is 0 or 1 ? value.Tab : 0,
        TextSize = double.IsFinite(value.TextSize) ? Math.Clamp(value.TextSize, 12, 24) : 16
    };
    public static WindowPreferences Fit(WindowPreferences value, IReadOnlyList<WorkArea> areas)
    {
        value = Normalize(value);
        if (areas.Count == 0) return value;
        var left = value.Left ?? areas[0].Left; var top = value.Top ?? areas[0].Top;
        var area = areas.OrderByDescending(a => Math.Max(0, Math.Min(left + value.Width, a.Left + a.Width) - Math.Max(left, a.Left)) *
            Math.Max(0, Math.Min(top + value.Height, a.Top + a.Height) - Math.Max(top, a.Top))).First();
        var width = Math.Min(value.Width, area.Width); var height = Math.Min(value.Height, area.Height);
        return value with { Width = width, Height = height, Left = Math.Clamp(left, area.Left, area.Left + area.Width - width), Top = Math.Clamp(top, area.Top, area.Top + area.Height - height) };
    }
}
