using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Automation;
using SongSense.Core;
namespace SongSense.App;
public partial class MainWindow
{
    private MainViewModel? readingModel;
    private long? readingTrack;
    private long readingLyricsRevision;
    private string? readingLanguage;
    private SongInsight? readingResult;
    private readonly double[] readingOffsets = [0, 0];
    private bool restoringReading;
    private long readingNavigationVersion;
    private bool updatingReadingMode, restoreReadingFocus;
    private int preferredReadingMode = 2;
    private MeaningPresentation? meaningPresentation;
    private void InitializeReadingNavigation()
    {
        DataContextChanged += (_, _) => AttachReadingModel();
        Loaded += (_, _) => { AttachReadingModel(); RestoreReadingPosition(); };
        Closed += (_, _) => { if (readingModel is not null) readingModel.PropertyChanged -= ReadingModelChanged; meaningPresentation?.Dispose(); readingNavigationVersion++; };
        ReadingTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, ReadingTabs)) RestoreReadingPosition(); };
        TranslationScroll.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => RecordReadingPosition(0, TranslationScroll, e)));
        MeaningScroll.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => RecordReadingPosition(1, MeaningScroll, e)));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && (TranslationScroll.IsKeyboardFocusWithin || MeaningScroll.IsKeyboardFocusWithin)) restoreReadingFocus = true; };
    }
    private void AttachReadingModel()
    {
        if (ReferenceEquals(readingModel, DataContext)) return;
        if (readingModel is not null) readingModel.PropertyChanged -= ReadingModelChanged;
        readingModel = DataContext as MainViewModel;
        if (readingModel is not null) readingModel.PropertyChanged += ReadingModelChanged;
        readingTrack = null; readingLyricsRevision = -1; readingLanguage = null; readingResult = null;
        UpdateReadingDocument();
    }
    private void ReadingModelChanged(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentTrack) or nameof(MainViewModel.LyricsRevision) or nameof(MainViewModel.TargetLanguage) or nameof(MainViewModel.Insight) or nameof(MainViewModel.ReadingBlocks)) UpdateReadingDocument();
    }
    private void UpdateReadingDocument()
    {
        if (readingModel is not { } vm) return;
        if (readingTrack == vm.CurrentTrack?.Revision && readingLyricsRevision == vm.LyricsRevision && readingLanguage == vm.TargetLanguage && ReferenceEquals(readingResult, vm.Insight)) return;
        readingTrack = vm.CurrentTrack?.Revision; readingLyricsRevision = vm.LyricsRevision; readingLanguage = vm.TargetLanguage; readingResult = vm.Insight;
        BuildReadingDocuments();
        readingOffsets[0] = readingOffsets[1] = 0; RestoreReadingPosition();
    }
    private void RecordReadingPosition(int tab, ReadingDocument scroll, ScrollChangedEventArgs e)
    {
        if (!restoringReading && scroll.IsLoaded && ReadingTabs.SelectedIndex == tab && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
            readingOffsets[tab] = scroll.VerticalOffset;
    }
    private void RestoreReadingPosition()
    {
        var tab = ReadingTabs.SelectedIndex == 1 ? 1 : 0;
        var version = ++readingNavigationVersion; restoringReading = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (version != readingNavigationVersion) return;
            if (restoreReadingFocus) { (tab == 1 ? MeaningScroll : TranslationScroll).Focus(); restoreReadingFocus = false; }
            (tab == 1 ? MeaningScroll : TranslationScroll).ScrollToVerticalOffset(readingOffsets[tab]);
            Dispatcher.BeginInvoke(() => { if (version == readingNavigationVersion) restoringReading = false; }, DispatcherPriority.ContextIdle);
        }, DispatcherPriority.ContextIdle);
    }

    private void ChangeReadingMode(object sender, SelectionChangedEventArgs e)
    {
        if (updatingReadingMode || readingModel is null || !ReferenceEquals(e.OriginalSource, ReadingMode)) return;
        preferredReadingMode = Math.Max(0, ReadingMode.SelectedIndex);
        var offset = TranslationScroll.VerticalOffset;
        BuildLyricsDocument();
        readingOffsets[0] = offset;
        RestoreReadingPosition();
    }
    private static string SourceLabel(string? code) => code switch
    {
        null or "" or "unknown" or "und" => "Idioma desconocido",
        "mixed" => "Idiomas mixtos",
        _ => OutputLanguages.All.FirstOrDefault(l => l.Code == code)?.Name ?? $"Idioma: {code}"
    };
    private Paragraph AddText(ReadingDocument view, string text, bool label = false)
    {
        var paragraph = new Paragraph(new Run(text));
        if (label) { paragraph.FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); paragraph.FontSize = 12; paragraph.LineHeight = 18; paragraph.SetResourceReference(ForegroundProperty, "Muted"); }
        view.Document.Blocks.Add(paragraph);
        return paragraph;
    }
    private void AddHeader(ReadingDocument view)
    {
        if (readingModel is not { } vm) return;
        foreach (var property in new[] { nameof(MainViewModel.ReadingSource), nameof(MainViewModel.LanguageMessage) })
        {
            var paragraph = new Paragraph(new Run()) { Tag = "metadata", FontFamily = new System.Windows.Media.FontFamily("Segoe UI") };
            var binding = new Binding(property) { Source = vm, Mode = BindingMode.OneWay };
            BindingOperations.SetBinding(paragraph.Inlines.FirstInline!, Run.TextProperty, binding);
            paragraph.SetResourceReference(ForegroundProperty, "Muted");
            var headerStyle = new Style(typeof(Paragraph));
            headerStyle.Setters.Add(new Setter(TextElement.FontSizeProperty, 12d));
            headerStyle.Setters.Add(new Setter(Block.LineHeightProperty, 18d));
            var empty = new DataTrigger { Binding = binding, Value = "" };
            empty.Setters.Add(new Setter(TextElement.FontSizeProperty, .1d));
            empty.Setters.Add(new Setter(Block.LineHeightProperty, .1d));
            headerStyle.Triggers.Add(empty); paragraph.Style = headerStyle;
            view.Document.Blocks.Add(paragraph);
        }
        var retry = new Hyperlink { Command = vm.RetryLanguageCommand };
        AutomationProperties.SetName(retry, "Reintentar guardado del idioma");
        var retryText = new Run(); retry.Inlines.Add(retryText);
        var textStyle = new Style(typeof(Run)); textStyle.Setters.Add(new Setter(Run.TextProperty, "Reintentar idioma"));
        var disabledText = new DataTrigger { Binding = new Binding(nameof(Hyperlink.IsEnabled)) { Source = retry }, Value = false };
        disabledText.Setters.Add(new Setter(Run.TextProperty, "")); textStyle.Triggers.Add(disabledText); retryText.Style = textStyle;
        var retryParagraph = new Paragraph(retry) { Tag = "metadata" };
        var retryStyle = new Style(typeof(Paragraph)); retryStyle.Setters.Add(new Setter(Block.LineHeightProperty, 18d));
        retryStyle.Setters.Add(new Setter(TextElement.FontSizeProperty, 12d));
        var disabledLine = new DataTrigger { Binding = new Binding(nameof(Hyperlink.IsEnabled)) { Source = retry }, Value = false };
        disabledLine.Setters.Add(new Setter(Block.LineHeightProperty, .1d)); retryStyle.Triggers.Add(disabledLine);
        retryParagraph.Style = retryStyle; view.Document.Blocks.Add(retryParagraph);
    }
    private void BuildReadingDocuments()
    {
        BuildLyricsDocument();
        meaningPresentation?.Dispose(); meaningPresentation = null;
        MeaningScroll.Document.Blocks.Clear();
        if (readingModel is not { } vm) return;
        if (!vm.HasInsight) { AddHeader(MeaningScroll); AddReadingHint(MeaningScroll); }
        else
        {
            var identity = AddText(MeaningScroll, "Una interpretación posible, generada por IA", true);
            identity.FontSize = 14; identity.LineHeight = 21; identity.FontWeight = FontWeights.SemiBold;
            identity.SetResourceReference(TextElement.ForegroundProperty, "Ink");
            AddHeader(MeaningScroll);
            meaningPresentation = new MeaningPresentation(MeaningScroll, vm.Insight!);
        }
        MeaningScroll.Selection.Select(MeaningScroll.Document.ContentStart, MeaningScroll.Document.ContentStart);
        UpdateReadingTypography();
    }
    private void BuildLyricsDocument()
    {
        if (readingModel is not { } vm) return;
        TranslationScroll.Document.Blocks.Clear(); AddHeader(TranslationScroll);
        var translated = vm.ReadingBlocks.Any(b => b.HasTranslation);
        updatingReadingMode = true;
        for (var i = 1; i < 3; i++) ((ComboBoxItem)ReadingMode.Items[i]).IsEnabled = translated;
        ReadingMode.SelectedIndex = translated ? preferredReadingMode : 0;
        updatingReadingMode = false;
        if (!translated && vm.Insight is { } insight && insight.Language == insight.TargetLanguage)
            AddText(TranslationScroll, "El original ya está en el idioma de destino; se muestra una sola vez.", true);
        if (!vm.HasInsight) AddReadingHint(TranslationScroll);
        foreach (var block in vm.ReadingBlocks)
        {
            if (ReadingMode.SelectedIndex != 1)
            {
                AddText(TranslationScroll, $"Original · {SourceLabel(vm.Insight?.Language)}", true);
                AddText(TranslationScroll, block.Original);
            }
            if (ReadingMode.SelectedIndex != 0 && block.HasTranslation)
            {
                AddText(TranslationScroll, $"Traducción · {OutputLanguages.Get(vm.TargetLanguage).Name}", true);
                AddText(TranslationScroll, block.Translation);
            }
        }
        TranslationScroll.Selection.Select(TranslationScroll.Document.ContentStart, TranslationScroll.Document.ContentStart);
        UpdateReadingTypography();
    }
    private void AddReadingHint(ReadingDocument view)
    {
        var paragraph = AddText(view, "", true);
        BindingOperations.SetBinding(paragraph.Inlines.FirstInline!, Run.TextProperty, new Binding(nameof(MainViewModel.ReadingHint)) { Source = readingModel, Mode = BindingMode.OneWay });
    }
    private void UpdateReadingTypography()
    {
        TranslationScroll?.SetPointSize(ReadingFontSize); MeaningScroll?.SetPointSize(ReadingFontSize);
    }
}
