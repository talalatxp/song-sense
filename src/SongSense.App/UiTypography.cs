using System.Windows;
using Windows.UI.ViewManagement;

namespace SongSense.App;

/// <summary>UI text follows Windows text scaling; document size remains the user's reading preference.</summary>
public static class UiTypography
{
    public static void Apply(ResourceDictionary resources, double scale)
    {
        scale = double.IsFinite(scale) ? Math.Clamp(scale, 1, 2.25) : 1;
        resources["UiControlFont"] = 14d * scale;
        resources["UiInstructionFont"] = 13d * scale;
        resources["UiSecondaryFont"] = 12d * scale;
        resources["UiEditorMinHeight"] = scale > 1.01 ? 540d * scale : 0d;
        resources["UiMainMinimumHeight"] = scale > 1.01 ? 480d * scale : 0d;
        foreach (var size in new[] { 18, 20, 22, 24, 26 }) resources[$"UiHeading{size}"] = size * scale;
    }
    public static IDisposable FollowSystem(Application app)
    {
        var settings = new UISettings();
        void ApplyCurrent() => Apply(app.Resources, settings.TextScaleFactor);
        ApplyCurrent();
        void Changed(UISettings sender, object args)
        { if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.InvokeAsync(ApplyCurrent); }
        settings.TextScaleFactorChanged += Changed;
        return new Subscription(() => settings.TextScaleFactorChanged -= Changed);
    }
    private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
}
