using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
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
        header.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        var overview = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) };
        if (summaryProperty is not null) overview.SetBinding(TextBlock.TextProperty, new Binding(summaryProperty) { Source = source, Mode = BindingMode.OneWay });
        header.Children.Add(overview);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        text.SetBinding(TextBox.TextProperty, new Binding(property) { Source = source, Mode = BindingMode.OneWay });
        AutomationProperties.SetName(text, title + " · texto seleccionable");
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var copy = new Button { Content = sanitizeCopy ? "Copiar diagnóstico saneado" : "Copiar", Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => { try { Clipboard.SetText(sanitizeCopy ? Sanitize(text.Text) : text.Text); } catch (System.Runtime.InteropServices.COMException) { MessageBox.Show("No se pudo copiar. Vuelve a intentarlo; el texto sigue disponible."); } };
        var close = new Button { Content = "Volver" }; close.Click += (_, _) => Window.GetWindow(panel)?.Close();
        footer.Children.Add(copy); footer.Children.Add(close); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(text); return panel;
    }
}
