using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SongSense.App;
using Xunit;

namespace SongSense.Tests;

public sealed class AuxiliaryLayoutTests
{
    [Theory]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(1.57, false)] [InlineData(1.57, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void LongAuxiliaryContentKeepsWholeActionsVisibleAndBodyUsable(double scale, bool sanitize) => Sta(() =>
    {
        var content = AuxiliaryContent.Create(new { Body = string.Join('\n', Enumerable.Repeat("Contenido largo seleccionable de diagnóstico.", 100)) }, "Body", "Diagnóstico de conexión", string.Join(' ', Enumerable.Repeat("Descripción larga de la situación actual.", 20)), sanitize);
        var window = new Window { Width = 360, Height = 480, Content = content, ShowActivated = false };
        ThemeResources.Apply(window.Resources, false);
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SongSense;component/Themes/Controls.xaml") });
        UiTypography.Apply(window.Resources, scale);
        try
        {
            window.Show(); Flush(window);
            var buttons = Find<Button>(content).Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Auxiliary")).ToArray();
            Assert.Equal(2, buttons.Length);
            var copy = Assert.Single(buttons, b => AutomationProperties.GetAutomationId(b) == "AuxiliaryCopy");
            var back = Assert.Single(buttons, b => AutomationProperties.GetAutomationId(b) == "AuxiliaryBack");
            foreach (var button in buttons)
            {
                var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                Assert.True(button.IsVisible && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= content.ActualWidth + 1 && bounds.Bottom <= content.ActualHeight + 1, $"Action clipped: {bounds}");
                Assert.Equal(14 * scale, button.FontSize, 6);
                var label = Assert.Single(Find<AccessText>(button));
                Assert.Equal(button.Content, label.Text);
                Assert.True(label.DesiredSize.Width <= label.ActualWidth + 1 && label.DesiredSize.Height <= label.ActualHeight + 1, "Action label clipped");
            }
            var copyBounds = copy.TransformToAncestor(content).TransformBounds(new Rect(copy.RenderSize));
            var backBounds = back.TransformToAncestor(content).TransformBounds(new Rect(back.RenderSize));
            Assert.False(copyBounds.IntersectsWith(backBounds));
            var body = Assert.Single(Find<TextBox>(content)); Assert.True(body.IsReadOnly);
            Assert.True(body.ActualHeight > body.FontSize * 1.1, "Long header consumed the body");
            body.Select(0, 24); Assert.NotEmpty(body.SelectedText); body.ScrollToEnd(); Flush(window); Assert.True(body.VerticalOffset > 0);
            UiTypography.Apply(window.Resources, scale == 2 ? 1 : 2); Flush(window);
            Assert.Equal(14 * (scale == 2 ? 1 : 2), copy.FontSize, 6);
            Assert.Equal(20 * (scale == 2 ? 1 : 2), Find<TextBlock>(content).First(t => t.Text == "Diagnóstico de conexión").FontSize, 6);
        }
        finally { window.Close(); }
    });
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StructuredAuxiliaryRetainsSelectableContentAndSafeReturn(bool diagnostic) => Sta(() =>
    {
        var content = AuxiliaryContent.CreatePresentation(new { Body = "Canción: Ventana propia\nCuenta: fixture@example.invalid\nEstado: pendiente" }, "Body", "Detalles", "Causa humana", diagnostic);
        var window = new Window { Width = 360, Height = 480, Content = content, ShowActivated = false };
        ThemeResources.Apply(window.Resources, false);
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SongSense;component/Themes/Controls.xaml") });
        try
        {
            window.Show(); Flush(window);
            Assert.Equal(2, Find<Button>(content).Count());
            if (diagnostic) { var details = Assert.Single(Find<Expander>(content)); Assert.False(details.IsExpanded); details.IsExpanded = true; Flush(window); Assert.Contains("fixture@example.invalid", Assert.Single(Find<TextBox>(content)).Text); }
            else { var doc = Assert.Single(Find<RichTextBox>(content)); Assert.True(doc.IsReadOnly); Assert.Contains("Canción: Ventana propia", new System.Windows.Documents.TextRange(doc.Document.ContentStart, doc.Document.ContentEnd).Text); }
            Assert.Contains("[cuenta omitida]", AuxiliaryContent.Sanitize("Cuenta: fixture@example.invalid"));
        }
        finally { window.Close(); }
    });
    [Theory]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(1.57, false)] [InlineData(1.57, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public void StructuredAuxiliaryReachesBodyBeforeHeaderByTab(double scale, bool diagnostic) => Sta(() =>
    {
        var content = (DockPanel)AuxiliaryContent.CreatePresentation(new { Body = "Contenido seleccionable\nSegunda línea" }, "Body", "Detalles", "Descripción de la ventana", diagnostic);
        var window = new Window { Width = 360, Height = 480, Content = content };
        ThemeResources.Apply(window.Resources, false);
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SongSense;component/Themes/Controls.xaml") });
        UiTypography.Apply(window.Resources, scale);
        try
        {
            window.Show(); Flush(window);
            var copy = Find<Button>(content).Single(b => AutomationProperties.GetAutomationId(b) == "AuxiliaryCopy");
            var back = Find<Button>(content).Single(b => AutomationProperties.GetAutomationId(b) == "AuxiliaryBack");
            var body = (FrameworkElement)content.Children[content.Children.Count - 1];
            Assert.True(copy.Focus());
            Assert.True(copy.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next))); Assert.True(back.IsKeyboardFocused);
            Assert.True(back.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            Assert.True(body.IsKeyboardFocusWithin, $"Tab after Volver skipped the reading region: {Keyboard.FocusedElement}");
            if (diagnostic)
            {
                Find<Expander>(body).Single().IsExpanded = true; Flush(window);
                Assert.True(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.True(Find<TextBox>(body).Single().IsKeyboardFocused, "Opening technical detail must keep its text next in the reading order");
            }
            Assert.True(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            var header = content.Children.OfType<ScrollViewer>().Single(s => DockPanel.GetDock(s) == Dock.Top);
            Assert.True(header.IsKeyboardFocused);
            Assert.True(header.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous))); Assert.True(body.IsKeyboardFocusWithin);
            if (diagnostic) Assert.True(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)));
            Assert.True(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous))); Assert.True(back.IsKeyboardFocused);
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.False(window.IsVisible);
        }
        finally { if (window.IsVisible) window.Close(); }
    });
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); if (child is T t) yield return t; foreach (var nested in Find<T>(child)) yield return nested; } }
    private static void Flush(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            window.UpdateLayout(); var frame = new DispatcherFrame();
            window.Dispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle); Dispatcher.PushFrame(frame);
        }
    }
    private static void Sta(Action test)
    {
        Exception? error = null; var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
