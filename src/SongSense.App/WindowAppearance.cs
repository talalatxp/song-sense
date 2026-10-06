using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SongSense.App;

internal static class WindowAppearance
{
    public static void Apply(Window window)
    {
        // Retain the native title bar and its keyboard/window management behavior.
        var enabled = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
