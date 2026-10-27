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
    private bool updatingReadingMode, restoreReadingFocus;
    private int preferredReadingMode = 2;
    private MeaningPresentation? meaningPresentation;
    private void InitializeReadingNavigation()
    {
        DataContextChanged += (_, _) => AttachReadingModel();
        Loaded += (_, _) => { AttachReadingModel(); RestoreReadingPosition(); };
        Closed += (_, _) => { if (readingModel is not null) readingModel.PropertyChanged -= ReadingModelChanged; meaningPresentation?.Dispose();  };
        ReadingTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, ReadingTabs)) RestoreReadingPosition(); };
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
        TranslationScroll.ResetReadingPosition(); MeaningScroll.ResetReadingPosition();
    }
    private void RestoreReadingPosition()
    {
        TranslationScroll.RestoreBookmark(); MeaningScroll.RestoreBookmark();
        if (restoreReadingFocus) { (ReadingTabs.SelectedIndex == 1 ? MeaningScroll : TranslationScroll).Focus(); restoreReadingFocus = false; }
    }
    public int PreferredReadingMode => preferredReadingMode;
    private void ChangeReadingMode(object sender, SelectionChangedEventArgs e)
    {
        if (updatingReadingMode || readingModel is null || !ReferenceEquals(e.OriginalSource, ReadingMode)) return;
        preferredReadingMode = Math.Clamp(ReadingMode.SelectedIndex, 0, 2);
        TranslationScroll.PreserveReadingPosition(BuildLyricsDocument);
        QueuePreferences();
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
        if (label) { paragraph.Tag = "metadata"; paragraph.FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); paragraph.FontSize = 12; paragraph.LineHeight = 18; paragraph.SetResourceReference(ForegroundProperty, "Muted"); }
        view.Document.Blocks.Add(paragraph);
        return paragraph;
    }
    private void AddHeader(ReadingDocument view)
    {
        if (readingModel is not { } vm) return;
        if (vm.HasPreparedLyrics) AddSource(view, vm);
        if (!vm.RetryLanguageCommand.CanExecute(null)) return;
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
        TranslationScroll.Document.Blocks.Clear();
        var translated = vm.ReadingBlocks.Any(b => b.HasTranslation);
        updatingReadingMode = true;
        for (var i = 1; i < 3; i++) ((ComboBoxItem)ReadingMode.Items[i]).IsEnabled = translated;
        ReadingMode.SelectedIndex = translated ? preferredReadingMode : 0;
        updatingReadingMode = false;
        var identity = ReadingMode.SelectedIndex switch {
            1 => $"Traducción · {OutputLanguages.Get(vm.TargetLanguage).Name}",
            2 => $"Original · {SourceLabel(vm.IdentifiedSourceLanguage)} / Traducción · {OutputLanguages.Get(vm.TargetLanguage).Name}",
            _ => $"Original · {SourceLabel(vm.IdentifiedSourceLanguage)}" };
        AddText(TranslationScroll, identity, true).Tag = "metadata";
        var stanza = 0;
        foreach (var block in vm.ReadingBlocks)
        {
            if (ReadingMode.SelectedIndex != 1)
            {
                var original = AddText(TranslationScroll, block.Original); original.Tag = "lyrics-body"; ReadingAnchors.SetKey(original, "lyrics:original:" + stanza);
            }
            if (ReadingMode.SelectedIndex != 0 && block.HasTranslation)
            {
                var target = AddText(TranslationScroll, block.Translation); target.Tag = "lyrics-body"; ReadingAnchors.SetKey(target, "lyrics:translation:" + stanza);
                if (ReadingMode.SelectedIndex == 2) { target.BorderThickness = new Thickness(2, 0, 0, 0); target.Padding = new Thickness(8, 0, 0, 0); target.SetResourceReference(Block.BorderBrushProperty, "Gold"); }
            }
            stanza++;
        }
        if (!translated && vm.Insight is { } insight && insight.Language == insight.TargetLanguage)
            AddText(TranslationScroll, "El original ya está en el idioma de destino; se muestra una sola vez.", true).Tag = "metadata";
        if (!vm.HasPreparedLyrics) AddReadingHint(TranslationScroll);
        if (vm.HasPreparedLyrics) AddSource(TranslationScroll, vm);
        TranslationScroll.Selection.Select(TranslationScroll.Document.ContentStart, TranslationScroll.Document.ContentStart);
        UpdateReadingTypography();
    }
    private void AddSource(ReadingDocument view, MainViewModel vm)
    {
        var paragraph = AddText(view, "", true);
        BindingOperations.SetBinding(paragraph.Inlines.FirstInline!, Run.TextProperty, new Binding(nameof(MainViewModel.ReadingSource)) { Source = vm, Mode = BindingMode.OneWay });
    }
    private void AddReadingHint(ReadingDocument view)
    {
        var paragraph = AddText(view, "", true);
        BindingOperations.SetBinding(paragraph.Inlines.FirstInline!, Run.TextProperty, new Binding(nameof(MainViewModel.ReadingContextHint)) { Source = readingModel, Mode = BindingMode.OneWay });
    }
    private void UpdateReadingTypography()
    {
        TranslationScroll?.SetPointSize(ReadingFontSize); MeaningScroll?.SetPointSize(ReadingFontSize);
    }
}
