using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SongSense.App;
using Xunit;

namespace SongSense.Tests;

public sealed class ControlThemeTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void ModeRadioUsesReadableThemeColorAndNativeSelection(bool highContrast, bool selected) => Sta(() =>
    {
        var radio = new RadioButton { Content = "API de pago", IsChecked = selected };
        var window = Host(radio); ThemeResources.Apply(window.Resources, highContrast); window.UpdateLayout();
        Assert.Equal(((SolidColorBrush)window.FindResource("Ink")).Color, ((SolidColorBrush)radio.Foreground).Color);
        var provider = Assert.IsAssignableFrom<ISelectionItemProvider>(new RadioButtonAutomationPeer(radio).GetPattern(PatternInterface.SelectionItem));
        Assert.Equal(selected, provider.IsSelected);
    });
    private static void Sta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static Window Host(Control control)
    {
        var window = new Window { Width = 500, Height = 300, Content = control };
        ThemeResources.Apply(window.Resources, false);
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SongSense;component/Themes/Controls.xaml") });
        window.Measure(new Size(500, 300)); window.Arrange(new Rect(0, 0, 500, 300)); window.UpdateLayout();
        control.ApplyTemplate();
        control.Measure(new Size(450, 250)); control.Arrange(new Rect(0, 0, 450, 250)); control.UpdateLayout();
        return window;
    }
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void ConsentRetainsReadableTextAndNativeToggleState(bool enabled, bool selected) => Sta(() =>
    {
        var text = new TextBlock { Text = "He desactivado el uso de créditos para esta cuenta y Song Sense" };
        var check = new CheckBox { Content = text, IsEnabled = enabled, IsChecked = selected };
        var window = Host(check);
        Assert.Equal(((SolidColorBrush)window.FindResource(enabled ? "Ink" : "DisabledText")).Color, ((SolidColorBrush)text.Foreground).Color);
        Assert.Equal(1, check.Opacity);
        var peer = new CheckBoxAutomationPeer(check);
        var toggle = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
        Assert.Equal(selected ? ToggleState.On : ToggleState.Off, toggle.ToggleState);
        Assert.Equal(enabled, peer.IsEnabled());
    });
    [Fact]
    public void SystemPaletteReplacesColorsWithoutRecreatingControls() => Sta(() =>
    {
        var field = new TextBox { Text = "Valor de prueba" };
        var window = Host(field);
        Assert.Equal(Color.FromRgb(0xF5, 0xF0, 0xE5), ((SolidColorBrush)field.Foreground).Color);
        ThemeResources.Apply(window.Resources, true); window.UpdateLayout();
        Assert.Equal(SystemColors.WindowTextColor, ((SolidColorBrush)field.Foreground).Color);
        Assert.Equal(SystemColors.WindowColor, ((SolidColorBrush)field.Background).Color);
        Assert.Equal(2, window.Resources.MergedDictionaries.Count);
        ThemeResources.Apply(window.Resources, false); window.UpdateLayout();
        Assert.Equal(Color.FromRgb(0xF5, 0xF0, 0xE5), ((SolidColorBrush)field.Foreground).Color);
    });
    [Fact]
    public void SelectorsRetainNonStringDisplayMembersAndNativeSelectionPatterns() => Sta(() =>
    {
        var combo = new ComboBox { ItemsSource = new[] { new Entry("Español"), new Entry("Inglés") }, DisplayMemberPath = nameof(Entry.Label), SelectedIndex = 1 };
        Host(combo);
        var peer = new ComboBoxAutomationPeer(combo);
        Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
        var selection = Assert.IsAssignableFrom<ISelectionProvider>(peer.GetPattern(PatternInterface.Selection));
        Assert.Single(selection.GetSelection());
        Assert.Equal("Inglés", ((Entry)combo.SelectedItem).Label);
        var presenter = Descendants(combo).OfType<ContentPresenter>().First(item => item.Content is Entry);
        presenter.ApplyTemplate();
        Assert.Contains(Descendants(presenter).OfType<TextBlock>(), item => item.Text == "Inglés");
    });
    [Fact]
    public void ListItemsKeepTheirContentAndSelectionWhenTheirSharedTemplateIsApplied() => Sta(() =>
    {
        var list = new ListBox { ItemsSource = new[] { "Primera versión", "Segunda versión" }, SelectedIndex = 1 };
        var window = Host(list); window.UpdateLayout();
        Assert.Contains(Descendants(list).OfType<TextBlock>(), text => text.Text == "Segunda versión");
        var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
        Assert.Equal(((SolidColorBrush)window.FindResource("SelectionInk")).Color, ((SolidColorBrush)item.Foreground).Color);
        Assert.IsAssignableFrom<ISelectionProvider>(new ListBoxAutomationPeer(list).GetPattern(PatternInterface.Selection));
    });
    [Fact]
    public void TextAndPasswordFieldsKeepNativeEditingHostsAndPasswordPrivacy() => Sta(() =>
    {
        var text = new TextBox { Text = "Texto editable" }; Host(text);
        Assert.NotNull(text.Template.FindName("PART_ContentHost", text));
        Assert.IsAssignableFrom<IValueProvider>(new TextBoxAutomationPeer(text).GetPattern(PatternInterface.Value));
        var password = new PasswordBox { Password = "synthetic" }; Host(password);
        Assert.NotNull(password.Template.FindName("PART_ContentHost", password));
        Assert.True(new PasswordBoxAutomationPeer(password).IsPassword());
    });
    [Theory]
    [InlineData(NoticeKind.Information, "Información")] [InlineData(NoticeKind.Success, "Listo")]
    [InlineData(NoticeKind.Warning, "Atención")] [InlineData(NoticeKind.Error, "Error")] [InlineData(NoticeKind.Progress, "En curso")]
    public void FeedbackHasAnAccessibleRoleLabelAndDistinctVisibleSymbol(NoticeKind kind, string label) => Sta(() =>
    {
        var notice = new StatusNotice { Kind = kind, Message = "Mensaje de prueba" }; Host(notice);
        var peer = UIElementAutomationPeer.CreatePeerForElement(notice)!;
        Assert.Equal(label + ": Mensaje de prueba", peer.GetName());
        Assert.Contains(Descendants(notice).OfType<TextBlock>(), item => item.Name == "Symbol" && item.Text.Length > 0);
    });
    [Fact]
    public void LongNoticeUpdatesItsPrefixAndMessageAsOneTextValue() => Sta(() =>
    {
        var notice = new StatusNotice { Kind = NoticeKind.Warning, Message = new string('x', 200) }; Host(notice);
        Assert.Equal("Atención: " + notice.Message, notice.DisplayText);
        notice.Kind = NoticeKind.Error; notice.Message = "Otro mensaje";
        Assert.Equal("Error: Otro mensaje", notice.DisplayText);
    });
    [Fact]
    public void LongButtonWrapsWithoutLosingNativeNameOrInvoke() => Sta(() =>
    {
        var button = new Button { Content = "Eliminar contenido de esta canción", Width = 180, HorizontalContentAlignment = HorizontalAlignment.Left };
        var window = Host(button); window.UpdateLayout();
        var peer = new ButtonAutomationPeer(button);
        Assert.Equal("Eliminar contenido de esta canción", peer.GetName());
        Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
        var text = Assert.Single(Descendants(button).OfType<AccessText>());
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        Assert.True(text.ActualHeight > button.FontSize * 1.5);
    });
    [Fact]
    public void NoticeKeepsFullAccessibleMessageAndExpandableDetail() => Sta(() =>
    {
        var notice = new StatusNotice { Message = "No se pudo guardar por un error local. Tu borrador se conserva y puedes reintentar.", Summary = "El guardado falló. Conservas el borrador; reintenta.", IsCompact = true };
        Host(notice); Assert.True(notice.HasDetails);
        Assert.Contains(notice.Message, UIElementAutomationPeer.CreatePeerForElement(notice)!.GetName());
        var text = (TextBlock)notice.Template.FindName("NoticeText", notice);
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping); Assert.Equal(TextTrimming.None, text.TextTrimming);
        var detail = (Expander)notice.Template.FindName("NoticeDetail", notice);
        Assert.Equal(Visibility.Visible, detail.Visibility);
        Assert.IsAssignableFrom<IExpandCollapseProvider>(new ExpanderAutomationPeer(detail).GetPattern(PatternInterface.ExpandCollapse));
        detail.IsExpanded = true; notice.UpdateLayout();
        Assert.Contains(Descendants(detail).OfType<TextBox>(), box => box.IsReadOnly && box.Text == notice.Message);
    });
    [Fact]
    public void SystemTextScaleUpdatesUiTokensAndExistingControls() => Sta(() =>
    {
        var resources = new ResourceDictionary(); UiTypography.Apply(resources, 1.57);
        Assert.Equal(14 * 1.57, (double)resources["UiControlFont"], 3);
        Assert.Equal(13 * 1.57, (double)resources["UiInstructionFont"], 3);
        var button = new Button { Content = "Guardar" }; var window = Host(button);
        UiTypography.Apply(window.Resources, 1.57); window.UpdateLayout();
        Assert.Equal(14 * 1.57, button.FontSize, 3);
    });
    [Fact]
    public void MenuRetainsCheckmarkSubmenuArrowAndNativeExpandPattern() => Sta(() =>
    {
        var check = new MenuItem { Header = "Opción", IsCheckable = true, IsChecked = true }; Host(check);
        Assert.Equal(Visibility.Visible, ((System.Windows.Shapes.Path)check.Template.FindName("MenuCheck", check)).Visibility);
        var item = new MenuItem { Header = "Más opciones" };
        item.Items.Add(new MenuItem { Header = "Detalle" });
        var menu = new Menu(); menu.Items.Add(item); Host(menu); item.ApplyTemplate();
        Assert.Equal(Visibility.Visible, ((System.Windows.Shapes.Path)item.Template.FindName("SubmenuArrow", item)).Visibility);
        Assert.IsAssignableFrom<IExpandCollapseProvider>(new MenuItemAutomationPeer(item).GetPattern(PatternInterface.ExpandCollapse));
    });
    [Fact]
    public void HorizontalScrollAndSliderRetainDirectionAndNativeRangeValue() => Sta(() =>
    {
        var scroll = new ScrollBar { Orientation = Orientation.Horizontal, Minimum = 0, Maximum = 100, Value = 20 }; Host(scroll);
        var track = (Track)scroll.Template.FindName("PART_Track", scroll);
        Assert.Equal(Orientation.Horizontal, track.Orientation);
        Assert.Equal(ScrollBar.PageLeftCommand, track.DecreaseRepeatButton.Command);
        Assert.Equal(ScrollBar.PageRightCommand, track.IncreaseRepeatButton.Command);
        var slider = new Slider { Minimum = 12, Maximum = 24, Value = 16 }; Host(slider);
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(new SliderAutomationPeer(slider).GetPattern(PatternInterface.RangeValue));
        range.SetValue(17); Assert.Equal(17, slider.Value);
        Assert.Equal(17, ((Track)slider.Template.FindName("PART_Track", slider)).Value);
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    public sealed record Entry(string Label);
}
