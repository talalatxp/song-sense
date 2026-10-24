using System.ComponentModel;
using System.Windows;

namespace SongSense.App;

public static class ThemeResources
{
    public static Uri PaletteUri(bool highContrast) => new($"pack://application:,,,/SongSense;component/Themes/{(highContrast ? "HighContrast" : "Dark")}.xaml");
    public static void Apply(ResourceDictionary resources, bool highContrast)
    {
        var palette = new ResourceDictionary { Source = PaletteUri(highContrast) };
        var index = resources.MergedDictionaries.ToList().FindIndex(item =>
            item.Source?.OriginalString.Contains("/Themes/Dark.xaml", StringComparison.Ordinal) == true ||
            item.Source?.OriginalString.Contains("/Themes/HighContrast.xaml", StringComparison.Ordinal) == true);
        if (index < 0) resources.MergedDictionaries.Insert(0, palette);
        else resources.MergedDictionaries[index] = palette;
    }
    public static IDisposable FollowSystem(Application app)
    {
        Apply(app.Resources, SystemParameters.HighContrast);
        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(SystemParameters.HighContrast) || app.Dispatcher.HasShutdownStarted) return;
            app.Dispatcher.InvokeAsync(() =>
            {
                Apply(app.Resources, SystemParameters.HighContrast);
                foreach (Window window in app.Windows) WindowAppearance.Apply(window);
            });
        }
        SystemParameters.StaticPropertyChanged += Changed;
        return new Subscription(() => SystemParameters.StaticPropertyChanged -= Changed);
    }
    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
