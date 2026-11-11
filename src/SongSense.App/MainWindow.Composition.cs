using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
namespace SongSense.App;

public partial class MainWindow
{
    private bool compositionQueued, metricsQueued, readingContextInControls;
    private void InitializeMainComposition()
    {
        // Keep the prose width stable when a reflowed summary crosses the overflow edge.
        TranslationScroll.VerticalScrollBarVisibility = MeaningScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        ReadingTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, ReadingTabs)) UpdateMainComposition(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                FocusReading(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else HandleReadingToolsEscape(e);
        };
        ReadingToolsFrame.PreviewKeyDown += (_, e) => HandleReadingToolsEscape(e);
        ReadingToolsPopup.PlacementTarget = ReadingToolsButton;
        Loaded += (_, _) => UpdateMainComposition();
    }
    private void QueueMainComposition()
    {
        if (compositionQueued || Dispatcher.HasShutdownStarted) return;
        compositionQueued = true;
        Dispatcher.BeginInvoke(() => { compositionQueued = false; UpdateMainComposition(); }, DispatcherPriority.DataBind);
    }
    private void UpdateMainComposition()
    {
        if (DataContext is not MainViewModel vm || ReadingTabs is null) return;
        bool compact = IsCompactLayout, meaning = ReadingTabs.SelectedIndex == 1;
        bool operation = vm.Preparation.Action is PreparationAction.CancelSearch or PreparationAction.CancelAnalysis;
        bool cancelled = vm.Preparation.Cause == PreparationCause.AiCancelled;
        bool pinnedReading = operation || cancelled;
        bool compactGeneration = compact && vm.Preparation.Cause == PreparationCause.Ready
            && vm.Preparation.Action == PreparationAction.Generate && vm.HasReadableOriginal;
        bool useCompactAction = compact && pinnedReading || compactGeneration;
        if (readingContextInControls != (pinnedReading || compactGeneration))
        {
            readingContextInControls = pinnedReading || compactGeneration;
            TranslationScroll.PreserveReadingPosition(BuildLyricsDocument);
        }
        bool toolsHaveFocus = ReadingToolsFrame.IsKeyboardFocusWithin, menuHadFocus = ApplicationMenuButton.IsKeyboardFocused;
        var previousFocus = Keyboard.FocusedElement;
        bool movedMenu = false, movedTools = false;
        UiLayout.SetIsCompact(this, compact);
        WideNavigation.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        BrandText.Visibility = compact || vm.CurrentTrack is null ? Visibility.Collapsed : Visibility.Visible;
        TrackContext.Visibility = compact || vm.CurrentTrack is null ? Visibility.Collapsed : Visibility.Visible;
        TrackTitleText.FontSize = (double)FindResource(compact ? "UiHeading18" : "UiHeading20");
        TrackTitleText.LineHeight = TrackTitleText.FontSize * 1.2;
        TrackTitleText.MaxHeight = TrackTitleText.LineHeight * (pinnedReading || compactGeneration ? 1 : 2);
        TrackTitleText.TextWrapping = pinnedReading || compactGeneration ? TextWrapping.NoWrap : TextWrapping.Wrap;
        bool pinnedPause = (pinnedReading || compactGeneration) && vm.ShowTrackingPause;
        ArtistText.Visibility = pinnedPause || vm.CurrentTrack is null ? Visibility.Collapsed : Visibility.Visible;
        TrackingPauseDetail.Visibility = pinnedPause ? Visibility.Collapsed : Visibility.Visible;
        if (pinnedPause) TrackingPauseNotice.SetCurrentValue(TextBlock.TextProperty, "En pausa");
        else TrackingPauseNotice.GetBindingExpression(TextBlock.TextProperty)?.UpdateTarget();
        TrackingPauseNotice.ToolTip = vm.TrackingPauseContext;
        if (compact)
        {
            if (!ReferenceEquals(ApplicationMenuButton.Parent, NavigationBlock))
            {
                movedMenu = true;
                WideNavigation.Children.Remove(ApplicationMenuButton);
                NavigationBlock.Children.Add(ApplicationMenuButton);
            }
            if (ReadingToolsPopup.Child is null)
            {
                InlineReadingTools.Content = null;
                ReadingToolsPopup.Child = ReadingToolsFrame;
                movedTools = true;
            }
        }
        else
        {
            if (!WideNavigation.Children.Contains(ApplicationMenuButton))
            {
                movedMenu = true;
                NavigationBlock.Children.Remove(ApplicationMenuButton);
                WideNavigation.Children.Add(ApplicationMenuButton);
            }
            if (!ReferenceEquals(InlineReadingTools.Content, ReadingToolsFrame))
            {
                movedTools = true;
                ReadingToolsPopup.IsOpen = false;
                ReadingToolsPopup.Child = null;
                InlineReadingTools.Content = ReadingToolsFrame;
            }
        }
        ReadingToolsButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        InlineReadingTools.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        var language = SongSense.Core.OutputLanguages.Get(vm.TargetLanguage);
        var mode = meaning ? "Lectura" : (ReadingMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Original";
        ReadingToolsLabel.Text = $"{language.Code.ToUpperInvariant()} · {mode}";
        System.Windows.Automation.AutomationProperties.SetName(ReadingToolsButton,
            $"Opciones de lectura. Idioma: {language.Name}. " + (meaning ? "Significado." : $"Modo: {mode}."));
        var navigationFits = ActualWidth - 48 >= (double)FindResource("UiControlFont") * 30 + 120;
        foreach (var button in WideNavigation.Children.OfType<Button>().Where(b => !ReferenceEquals(b, ApplicationMenuButton))) button.Visibility = navigationFits ? Visibility.Visible : Visibility.Collapsed;
        if (menuHadFocus && movedMenu)
            Dispatcher.BeginInvoke(() => ApplicationMenuButton.Focus(), DispatcherPriority.ContextIdle);
        if (movedTools && compact && toolsHaveFocus)
            Dispatcher.BeginInvoke(() => ReadingToolsButton.Focus(), DispatcherPriority.Input);
        if (movedTools && !compact && (toolsHaveFocus || ReferenceEquals(previousFocus, ReadingToolsButton)))
            Dispatcher.BeginInvoke(() => (toolsHaveFocus ? previousFocus : ReadingLanguage)?.Focus(), DispatcherPriority.Input);
        ReadingToolsFrame.Padding = new Thickness(compact ? 12 : 0); ReadingToolsFrame.BorderThickness = new Thickness(compact ? 1 : 0);
        ReadingToolsFrame.Width = compact ? Math.Max(260, ActualWidth - 40) : double.NaN;
        if (movedTools) ReadingToolsFrame.SetResourceReference(Border.BackgroundProperty, compact ? "Surface" : "Canvas");
        ReadingModeGroup.Visibility = meaning ? Visibility.Collapsed : Visibility.Visible;
        TranslationScroll.ContentInset = MeaningScroll.ContentInset = compact ? 12 : 16;
        LyricsEmpty.Margin = MeaningEmpty.Margin = new Thickness(compact ? 12 : 16);
        LyricsEmpty.Visibility = vm.HasReadableOriginal ? Visibility.Collapsed : Visibility.Visible;
        TranslationScroll.Visibility = vm.HasReadableOriginal ? Visibility.Visible : Visibility.Collapsed;
        MeaningEmpty.Visibility = vm.HasInsight ? Visibility.Collapsed : Visibility.Visible;
        MeaningScroll.Visibility = vm.HasInsight ? Visibility.Visible : Visibility.Collapsed;
        ReadOriginalButton.Visibility = vm.HasReadableOriginal && !vm.HasInsight ? Visibility.Visible : Visibility.Collapsed;
        // During an operation the whole composition is bounded by the client viewport.
        // Only the document scrolls, so cancellation stays reachable while reading.
        OperationPanel.Visibility = OperationDetailsMenuItem.Visibility = pinnedReading ? Visibility.Visible : Visibility.Collapsed;
        OperationActionHost.Visibility = useCompactAction ? Visibility.Collapsed : Visibility.Visible;
        CompactOperationActionHost.Visibility = useCompactAction ? Visibility.Visible : Visibility.Collapsed;
        IdentityBlock.Margin = new Thickness(0, 0, 0, useCompactAction ? 8 : 12);
        TrackingPausePanel.Margin = new Thickness(0, 0, 0, useCompactAction ? 4 : 12);
        OperationSummary.Text = vm.OperationFeedback;
        OperationSummary.ToolTip = vm.Preparation.Message;
        System.Windows.Automation.AutomationProperties.SetHelpText(OperationSummary, vm.Preparation.Message + " " + vm.UsageMessage);
        MainOuterScroll.VerticalScrollBarVisibility = pinnedReading ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        if (pinnedReading) { RootGrid.MinHeight = 0; MainOuterScroll.ScrollToTop(); }
        ReadingActions.Visibility = pinnedReading ? Visibility.Collapsed : Visibility.Visible;
        if (pinnedReading) UsageFooter.Visibility = Visibility.Collapsed;
        else UsageFooter.ClearValue(VisibilityProperty);
        MeaningEmptyHeading.Visibility = MeaningEmptyHelp.Visibility = pinnedReading ? Visibility.Collapsed : Visibility.Visible;
        bool empty = meaning ? !vm.HasInsight : !vm.HasReadableOriginal;
        bool recovery = !pinnedReading && vm.ShowPreparationNotice && vm.PreparationPresentation == NoticePresentation.Attention;
        if (vm.CurrentTrack is null && recovery)
        {
            LyricsEmpty.Visibility = Visibility.Collapsed;
            MeaningEmpty.Visibility = Visibility.Collapsed;
        }
        PreparationNotice.Visibility = recovery ? Visibility.Visible : Visibility.Collapsed;
        LyricsEmptyHelp.Visibility = recovery || pinnedReading ? Visibility.Collapsed : Visibility.Visible;
        if (recovery) MeaningEmptyHelp.Visibility = Visibility.Collapsed;
        bool languageRecovery = vm.Preparation.Action == PreparationAction.RetryLanguage;
        var target = pinnedReading || useCompactAction ? null : languageRecovery ? LanguageStatus : recovery ? PreparationNotice : null;
        var emptyTarget = !pinnedReading && !useCompactAction && target is null && empty ? (meaning ? MeaningEmptyActionHost : LyricsEmptyActionHost) : null;
        // The same command button changes presentation owner; there are no equivalent clones.
        var actionHost = useCompactAction ? CompactOperationActionHost : pinnedReading ? OperationActionHost : null;
        var correctlyPlaced = actionHost is not null ? ReferenceEquals(actionHost.Content, PrimaryActionButton) : target is not null ? ReferenceEquals(target.ActionContent, PrimaryActionButton) : emptyTarget is not null ? ReferenceEquals(emptyTarget.Content, PrimaryActionButton) : TaskActions.Children.Contains(PrimaryActionButton);
        if (!correctlyPlaced)
        {
            bool actionHadFocus = PrimaryActionButton.IsKeyboardFocused;
            DetachPrimaryAction();
            if (actionHost is not null) actionHost.Content = PrimaryActionButton;
            else if (target is not null) target.ActionContent = PrimaryActionButton;
            else if (emptyTarget is not null) emptyTarget.Content = PrimaryActionButton;
            else TaskActions.Children.Insert(0, PrimaryActionButton);
            if (actionHadFocus) Dispatcher.BeginInvoke(() => PrimaryActionButton.Focus(), DispatcherPriority.Input);
        }
        bool actionable = vm.ShowPrimaryAction && !(vm.Preparation.Action == PreparationAction.Diagnostic && vm.CurrentTrack is null);
        PrimaryActionButton.Visibility = actionable ? Visibility.Visible : Visibility.Collapsed;
        PrimaryActionButton.Content = operation ? "Cancelar" : useCompactAction ? "Analizar" : vm.Preparation.Cause is PreparationCause.DailyLimit or PreparationCause.AiWait ? "Ver cuándo se restablece" : vm.PrimaryActionLabel;
        var actionName = useCompactAction && !operation ? "Analizar. " + vm.PrimaryActionLabel : vm.PrimaryActionLabel;
        System.Windows.Automation.AutomationProperties.SetName(PrimaryActionButton, actionName);
        PrimaryActionButton.MaxWidth = Math.Max(180, ActualWidth - (compact ? 80 : 120));
        AiDecisionButton.Visibility = actionable && !pinnedReading && vm.Preparation.Action is PreparationAction.Generate or PreparationAction.Regenerate ? Visibility.Visible : Visibility.Collapsed;
        AiDecisionButton.MaxWidth = Math.Max(180, ActualWidth - (compact ? 40 : 120));
        if (target == PreparationNotice && vm.Preparation.Action is PreparationAction.OpenApi or PreparationAction.OpenConnection or PreparationAction.ChooseModel or PreparationAction.ReviewPlan)
            PreparationNotice.Explanation = vm.AiDecisionLabel;
        else PreparationNotice.Explanation = "";
        TaskActions.Visibility = TaskActions.Children.OfType<FrameworkElement>().Any(c => c.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        QueueCompositionMetrics();
    }
    private void DetachPrimaryAction()
    {
        if (PrimaryActionButton.Parent is Panel p) p.Children.Remove(PrimaryActionButton);
        if (ReferenceEquals(OperationActionHost.Content, PrimaryActionButton)) OperationActionHost.Content = null;
        if (ReferenceEquals(CompactOperationActionHost.Content, PrimaryActionButton)) CompactOperationActionHost.Content = null;
        if (ReferenceEquals(PreparationNotice.ActionContent, PrimaryActionButton)) PreparationNotice.ActionContent = null;
        if (ReferenceEquals(LanguageStatus.ActionContent, PrimaryActionButton)) LanguageStatus.ActionContent = null;
        if (ReferenceEquals(LyricsEmptyActionHost.Content, PrimaryActionButton)) LyricsEmptyActionHost.Content = null;
        if (ReferenceEquals(MeaningEmptyActionHost.Content, PrimaryActionButton)) MeaningEmptyActionHost.Content = null;
    }
    private void QueueCompositionMetrics()
    {
        if (metricsQueued || !IsLoaded || Dispatcher.HasShutdownStarted) return;
        metricsQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            metricsQueued = false;
            if (DataContext is MainViewModel model && (model.Preparation.Action is PreparationAction.CancelSearch or PreparationAction.CancelAnalysis || model.Preparation.Cause == PreparationCause.AiCancelled)) { RootGrid.MinHeight = 0; return; }
            // Measure only intrinsic chrome; an unrealized selected surface can temporarily
            // report zero, so subtracting it from the allocated tab height forms a feedback loop.
            var bar = (ReadingTabs.Template.FindName("ReadingBar", ReadingTabs) as FrameworkElement)?.ActualHeight ?? 0;
            var min = IdentityBlock.ActualHeight + IdentityBlock.Margin.Top + IdentityBlock.Margin.Bottom + TaskArea.ActualHeight + TrackingPausePanel.ActualHeight + (TrackingPausePanel.IsVisible ? TrackingPausePanel.Margin.Bottom : 0) + ReadingActions.ActualHeight + UsageFooter.ActualHeight + (UsageFooter.IsVisible ? 8 : 0) + 12 + bar + 220;
            if (Math.Abs(RootGrid.MinHeight - min) > 1) RootGrid.MinHeight = min;
        }, DispatcherPriority.Loaded);
    }
    private void ToggleReadingTools(object sender, RoutedEventArgs args)
    {
        if (!IsCompactLayout)
        {
            ReadingLanguage.Focus();
            return;
        }
        if (ReadingToolsPopup.IsOpen)
        {
            CloseReadingTools();
            return;
        }
        ReadingToolsPopup.IsOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (ReadingToolsPopup.IsOpen && !ReadingLanguage.Focus()) ReadingMode.Focus();
        }, DispatcherPriority.Input);
    }
    private void HandleReadingToolsEscape(KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !ReadingToolsPopup.IsOpen || ReadingLanguage.IsDropDownOpen || ReadingMode.IsDropDownOpen) return;
        CloseReadingTools();
        args.Handled = true;
    }
    private void CloseReadingTools()
    {
        ReadingToolsPopup.IsOpen = false;
        ReadingToolsButton.Focus();
    }
    private void ReadOriginal(object sender, RoutedEventArgs args) { ReadingTabs.SelectedIndex = 0; FocusReading(sender, args); }
    private void FocusReading(object sender, RoutedEventArgs args)
    {
        ReadingToolsPopup.IsOpen = false;
        var document = ReadingTabs.SelectedIndex == 1 ? MeaningScroll : TranslationScroll;
        document.BringIntoView(); document.Focus();
    }
}
