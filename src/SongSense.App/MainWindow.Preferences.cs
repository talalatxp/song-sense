using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using SongSense.Core;
using SongSense.Infrastructure;

namespace SongSense.App;

public partial class MainWindow
{
    public static readonly DependencyProperty IsCompactLayoutProperty = DependencyProperty.Register(nameof(IsCompactLayout), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool IsCompactLayout { get => (bool)GetValue(IsCompactLayoutProperty); private set => SetValue(IsCompactLayoutProperty, value); }
    private static readonly DependencyPropertyKey ReadingTextSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(ReadingTextSize), typeof(double), typeof(MainWindow), new PropertyMetadata(16d * 4 / 3));
    public static readonly DependencyProperty ReadingTextSizeProperty = ReadingTextSizePropertyKey.DependencyProperty;
    public double ReadingTextSize => (double)GetValue(ReadingTextSizeProperty);
    // WPF measures fonts in DIP (96/inch); the user setting is points (72/inch).
    public static readonly DependencyProperty ReadingFontSizeProperty = DependencyProperty.Register(nameof(ReadingFontSize), typeof(double), typeof(MainWindow),
        new PropertyMetadata(16d, (owner, args) => { var window = (MainWindow)owner; window.SetValue(ReadingTextSizePropertyKey, (double)args.NewValue * 4 / 3); window.UpdateReadingTypography(); }));
    public double ReadingFontSize { get => (double)GetValue(ReadingFontSizeProperty); set => SetValue(ReadingFontSizeProperty, Math.Clamp(value, 12, 24)); }
    private WindowPreferenceStore? preferenceStore;
    private readonly DispatcherTimer preferenceTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    public void InitializePreferences(WindowPreferenceStore store)
    {
        preferenceStore = store;
        var saved = WindowPlacement.Fit(store.Read(), Areas());
        Width = Math.Max(MinWidth, saved.Width); Height = Math.Max(MinHeight, saved.Height);
        if (saved.Left is { } left && saved.Top is { } top) { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
        Topmost = saved.AlwaysOnTop;
        KeepInTray = saved.KeepInTray; TrayHintShown = saved.TrayHintShown;
        ReadingFontSize = saved.TextSize; ReadingTabs.SelectedIndex = saved.Tab;
        preferenceTimer.Tick += (_, _) => { preferenceTimer.Stop(); SavePreferences(); };
        LocationChanged += (_, _) => QueuePreferences(); SizeChanged += (_, _) => QueuePreferences();
        ReadingTabs.SelectionChanged += (_, _) => QueuePreferences();
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
        return new(Left: bounds.Left, Top: bounds.Top, Width: bounds.Width, Height: bounds.Height, Tab: ReadingTabs.SelectedIndex, TextSize: ReadingFontSize, AlwaysOnTop: Topmost, KeepInTray: KeepInTray, TrayHintShown: TrayHintShown);
    }
    private void QueuePreferences() { if (preferenceStore is null) return; preferenceTimer.Stop(); preferenceTimer.Start(); }
    public void SavePreferences()
    {
        preferenceTimer.Stop();
        if (preferenceStore is null) return;
        PreferenceMessage = preferenceStore.Save(CapturePreferences()) ? "" : "No se han podido guardar las preferencias de ventana.";
    }
    public static readonly DependencyProperty PreferenceMessageProperty = DependencyProperty.Register(nameof(PreferenceMessage), typeof(string), typeof(MainWindow), new PropertyMetadata(""));
    public string PreferenceMessage { get => (string)GetValue(PreferenceMessageProperty); private set => SetValue(PreferenceMessageProperty, value); }
    public void SchedulePreferenceSave() => QueuePreferences();
    public static readonly DependencyProperty KeepInTrayProperty = DependencyProperty.Register(nameof(KeepInTray), typeof(bool), typeof(MainWindow), new PropertyMetadata(true, (owner, _) => ((MainWindow)owner).QueuePreferences()));
    public bool KeepInTray { get => (bool)GetValue(KeepInTrayProperty); set => SetValue(KeepInTrayProperty, value); }
    public bool TrayHintShown { get; private set; }
    public bool MarkTrayHintShown()
    {
        if (TrayHintShown) return false;
        TrayHintShown = true; SavePreferences(); return true;
    }
    public event EventHandler? ExitRequested;
    private void ExitApplication(object sender, RoutedEventArgs args) => RequestExit();
    private bool exitPending;
    public async void RequestExit()
    {
        if (exitPending) return;
        exitPending = true;
        try
        {
            var sending = DataContext is MainViewModel vm && vm.Preparation.Action == PreparationAction.CancelAnalysis ||
                Application.Current.Windows.OfType<AiSettingsWindow>().Any(w => w.Owner == this && w.DataContext is AiSettingsViewModel api && api.CanCancel && api.Activity.Last?.State == AiUsageState.EnCurso);
            if (sending && !ConfirmationWindow.Ask(this, "Salir durante una consulta", "Salir cancelará las operaciones pendientes. Una solicitud ya enviada puede haber consumido tokens o generado coste; no se devuelve automáticamente el intento al contador. ¿Salir y cancelar?", "Salir y cancelar", "Continuar en Song Sense")) return;
            foreach (var editor in Application.Current.Windows.OfType<LyricsEditorWindow>().Where(w => w.Owner == this).ToArray()) if (!editor.RequestClose()) return;
            foreach (var api in Application.Current.Windows.OfType<AiSettingsWindow>().Where(w => w.Owner == this).ToArray()) if (!await api.RequestCloseAsync()) return;
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }
        finally { exitPending = false; }
    }
    public void OpenFromTray()
    {
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        RecoverPosition(); Activate();
    }
}
