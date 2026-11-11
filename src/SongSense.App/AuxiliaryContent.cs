using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace SongSense.App;

public static class AuxiliaryContent
{
    public static string Sanitize(string text) => Regex.Replace(
        Regex.Replace(text, @"(?i)\bBearer\s+\S+|\bsk-[\w-]+", "[credencial omitida]"),
        @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "[cuenta omitida]");

    public static FrameworkElement CreatePresentation(object source, string property, string title, string summary, bool sanitizeCopy = false, string? summaryProperty = null)
    {
        var panel = (DockPanel)Create(source, property, title, summary, sanitizeCopy, summaryProperty);
        var text = panel.Children.OfType<TextBox>().Single(); panel.Children.Remove(text);
        if (sanitizeCopy)
        {
            text.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            // The new local reading scope follows its disclosure in visual order.
            // The old index belonged to the flat Copy / Back / body / header layout.
            text.ClearValue(KeyboardNavigation.TabIndexProperty);
            var detail = new Expander { Header = "Detalle técnico", Content = text };
            var body = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            KeyboardNavigation.SetTabIndex(body, 2);
            KeyboardNavigation.SetIsTabStop(body, false);
            KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Local);
            panel.Children.Add(body);
        }
        else
        {
            var view = new RichTextBox { IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            KeyboardNavigation.SetTabIndex(view, 2);
            view.SetResourceReference(Control.FontSizeProperty, "UiControlFont"); view.SetResourceReference(Control.ForegroundProperty, "Ink");
            AutomationProperties.SetName(view, title + " · texto seleccionable");
            void Refresh()
            {
                view.Document.Blocks.Clear();
                foreach (var line in text.Text.Replace("\r\n", "\n").Split('\n'))
                {
                    var p = new Paragraph { Margin = new Thickness(0, 0, 0, 12) }; var colon = line.IndexOf(':');
                    if (colon > 0) { p.Inlines.Add(new Bold(new Run(line[..(colon + 1)]))); p.Inlines.Add(new Run(line[(colon + 1)..])); }
                    else p.Inlines.Add(new Run(line));
                    view.Document.Blocks.Add(p);
                }
            }
            text.TextChanged += (_, _) => Refresh(); Refresh(); panel.Children.Add(view);
        }
        panel.SizeChanged += (_, _) => { var window = Window.GetWindow(panel); panel.Margin = new Thickness(window?.ActualWidth < 600 ? 12 : 24); };
        return panel;
    }

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
