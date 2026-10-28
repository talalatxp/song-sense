using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace SongSense.App;

public static class AuxiliaryContent
{
    public static string Sanitize(string text) => Regex.Replace(
        Regex.Replace(text, @"(?i)\bBearer\s+\S+|\bsk-[\w-]+", "[credencial omitida]"),
        @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "[cuenta omitida]");

    public static FrameworkElement Create(object source, string property, string title, string summary, bool sanitizeCopy = false, string? summaryProperty = null)
    {
        var panel = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel();
        var heading = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(TextBlock.FontSizeProperty, "UiHeading20"); header.Children.Add(heading);
        var overview = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) };
        overview.SetResourceReference(TextBlock.FontSizeProperty, "UiInstructionFont");
        if (summaryProperty is not null) overview.SetBinding(TextBlock.TextProperty, new Binding(summaryProperty) { Source = source, Mode = BindingMode.OneWay });
        header.Children.Add(overview);
        var headerScroll = new ScrollViewer { Content = header, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true };
        KeyboardNavigation.SetTabIndex(headerScroll, 3);
        KeyboardNavigation.SetIsTabStop(headerScroll, true);
        AutomationProperties.SetName(headerScroll, "Encabezado y descripción · " + title);
        DockPanel.SetDock(headerScroll, Dock.Top); panel.Children.Add(headerScroll);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        text.SetResourceReference(Control.FontSizeProperty, "UiControlFont");
        text.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.OneWay });
        AutomationProperties.SetName(text, title + " · texto seleccionable");
        var footer = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var copy = new Button { Content = sanitizeCopy ? "Copiar diagnóstico saneado" : "Copiar", Margin = new Thickness(0, 0, 8, 4) };
        copy.SetResourceReference(Control.FontSizeProperty, "UiControlFont");
        copy.Click += (_, _) => { try { Clipboard.SetText(sanitizeCopy ? Sanitize(text.Text) : text.Text); } catch (System.Runtime.InteropServices.COMException) { MessageBox.Show("No se pudo copiar. Vuelve a intentarlo; el texto sigue disponible."); } };
        var close = new Button { Content = "Volver", Margin = new Thickness(0, 0, 0, 4) }; close.Click += (_, _) => Window.GetWindow(panel)?.Close();
        close.SetResourceReference(Control.FontSizeProperty, "UiControlFont");
        AutomationProperties.SetAutomationId(copy, "AuxiliaryCopy"); AutomationProperties.SetAutomationId(close, "AuxiliaryBack");
        KeyboardNavigation.SetTabIndex(copy, 0); KeyboardNavigation.SetTabIndex(close, 1); KeyboardNavigation.SetTabIndex(text, 2);
        // Constrain each button as well as the row: a label wider than the window
        // must wrap within its button instead of producing an oversized first row.
        footer.SizeChanged += (_, _) =>
        {
            copy.MaxWidth = Math.Max(0, footer.ActualWidth - copy.Margin.Left - copy.Margin.Right);
            close.MaxWidth = Math.Max(0, footer.ActualWidth - close.Margin.Left - close.Margin.Right);
        };
        panel.SizeChanged += (_, _) => headerScroll.MaxHeight = Math.Max(0, panel.ActualHeight * .35);
        footer.Children.Add(copy); footer.Children.Add(close); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(text); return panel;
    }
}
