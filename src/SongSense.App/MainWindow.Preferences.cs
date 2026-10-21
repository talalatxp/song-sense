using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SongSense.Core;
using SongSense.Infrastructure;

namespace SongSense.App;

public partial class MainWindow
{
    private static readonly DependencyPropertyKey ReadingTextSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(ReadingTextSize), typeof(double), typeof(MainWindow), new PropertyMetadata(16d * 4 / 3));
    public static readonly DependencyProperty ReadingTextSizeProperty = ReadingTextSizePropertyKey.DependencyProperty;
    public double ReadingTextSize => (double)GetValue(ReadingTextSizeProperty);
    // WPF measures fonts in DIP (96/inch); the user setting is points (72/inch).
    public static readonly DependencyProperty ReadingFontSizeProperty = DependencyProperty.Register(nameof(ReadingFontSize), typeof(double), typeof(MainWindow),
        new PropertyMetadata(16d, (owner, args) => owner.SetValue(ReadingTextSizePropertyKey, (double)args.NewValue * 4 / 3)));
    public double ReadingFontSize { get => (double)GetValue(ReadingFontSizeProperty); set => SetValue(ReadingFontSizeProperty, Math.Clamp(value, 12, 24)); }
    private WindowPreferenceStore? preferenceStore;
    private readonly DispatcherTimer preferenceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    public void InitializePreferences(WindowPreferenceStore store)
    {
        preferenceStore = store;
        var saved = WindowPlacement.Fit(store.Read(), Areas());
        Width = Math.Max(MinWidth, saved.Width); Height = Math.Max(MinHeight, saved.Height);
        if (saved.Left is { } left && saved.Top is { } top) { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
        Topmost = saved.AlwaysOnTop; AlwaysOnTop.IsChecked = Topmost;
        ReadingFontSize = saved.TextSize; ReadingTabs.SelectedIndex = saved.Tab;
        preferenceTimer.Tick += (_, _) => { preferenceTimer.Stop(); SavePreferences(); };
        LocationChanged += (_, _) => QueuePreferences(); SizeChanged += (_, _) => QueuePreferences();
        ReadingTabs.SelectionChanged += (_, _) => QueuePreferences();
        TextSizeSlider.ValueChanged += (_, _) => QueuePreferences();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(DisplayChanged);
    }
    private IReadOnlyList<WorkArea> Areas()
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        return System.Windows.Forms.Screen.AllScreens.Select(screen => { var area = screen.WorkingArea; return new WorkArea(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY); }).ToArray();
    }
    public void RecoverPosition()
    {
        if (WindowState == WindowState.Maximized) return;
        var fit = WindowPlacement.Fit(CapturePreferences(), Areas());
        Width = Math.Max(MinWidth, fit.Width); Height = Math.Max(MinHeight, fit.Height);
        Left = fit.Left!.Value; Top = fit.Top!.Value;
    }
    private IntPtr DisplayChanged(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x007E or 0x02E0) Dispatcher.BeginInvoke(RecoverPosition, DispatcherPriority.Loaded);
        return IntPtr.Zero;
    }
    private WindowPreferences CapturePreferences()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height) : RestoreBounds;
        return new(Left: bounds.Left, Top: bounds.Top, Width: bounds.Width, Height: bounds.Height, Tab: ReadingTabs.SelectedIndex, TextSize: ReadingFontSize, AlwaysOnTop: Topmost);
    }
    private void QueuePreferences() { if (preferenceStore is null) return; preferenceTimer.Stop(); preferenceTimer.Start(); }
    public void SavePreferences()
    {
        preferenceTimer.Stop();
        if (preferenceStore is null) return;
        PreferenceStatus.Text = preferenceStore.Save(CapturePreferences()) ? "" : "No se han podido guardar las preferencias de ventana.";
    }
    private void ToggleTopmost(object sender, RoutedEventArgs args) { Topmost = AlwaysOnTop.IsChecked == true; QueuePreferences(); }
    public event EventHandler? ExitRequested;
    private void ExitApplication(object sender, RoutedEventArgs args) => ExitRequested?.Invoke(this, EventArgs.Empty);
    public void OpenFromTray()
    {
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        RecoverPosition(); Activate();
    }
}
