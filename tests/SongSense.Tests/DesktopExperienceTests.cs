using System.IO;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class DesktopExperienceTests
{
    [Fact]
    public void DisconnectedMonitorRestoresEntireWindowInsideAvailableWorkArea()
    {
        var result = WindowPlacement.Fit(new(Left: 2400, Top: -200, Width: 1000, Height: 900), [new(0, 0, 1280, 720)]);
        Assert.Equal(280, result.Left); Assert.Equal(0, result.Top); Assert.Equal(720, result.Height);
        Assert.True(result.Left + result.Width <= 1280);
    }
    [Fact]
    public void NegativeMonitorCoordinatesRemainValidAndNearestOverlapIsPreserved()
    {
        var result = WindowPlacement.Fit(new(Left: -1000, Top: 100, Width: 600, Height: 500), [new(-1920, 0, 1920, 1080), new(0, 0, 1920, 1080)]);
        Assert.Equal(-1000, result.Left); Assert.Equal(100, result.Top);
    }
    [Theory]
    [InlineData(-10, 12)] [InlineData(50, 24)] [InlineData(double.NaN, 16)]
    public void TextSizeAndInvalidGeometryAreBounded(double size, double expected)
    {
        var result = WindowPlacement.Normalize(new(Left: double.PositiveInfinity, Width: double.NaN, Height: 10, Tab: 9, TextSize: size));
        Assert.Equal(expected, result.TextSize); Assert.Null(result.Left); Assert.Equal(860, result.Width); Assert.Equal(480, result.Height); Assert.Equal(0, result.Tab);
    }
    [Fact]
    public void PreferencesRoundTripWithoutSongOrAuthenticationDataAndCorruptionUsesDefaults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SongSense.DesktopTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "window.json"); var store = new WindowPreferenceStore(path);
            Assert.Equal(new WindowPreferences(), store.Read());
            var preferences = new WindowPreferences(Left: -860, Top: 42, Width: 400, Height: 600, Tab: 1, TextSize: 24, AlwaysOnTop: true);
            Assert.True(store.Save(preferences)); Assert.Equal(preferences, new WindowPreferenceStore(path).Read());
            Assert.False(File.Exists(path + ".tmp"));
            File.WriteAllText(path, "{broken"); Assert.Equal(new WindowPreferences(), store.Read());
            File.WriteAllText(path, "{\"Version\":99,\"AlwaysOnTop\":true}"); Assert.Equal(new WindowPreferences(), store.Read());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task SecondInstanceSignalsFirstAndNextLaunchWorksAfterBothExit()
    {
        var name = Guid.NewGuid().ToString("N");
        using (var first = new SingleInstance(name))
        using (var second = new SingleInstance(name))
        {
            Assert.True(first.IsFirst); Assert.False(second.IsFirst);
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            first.Listen(() => opened.TrySetResult());
            Assert.True(await second.ActivateFirstAsync()); await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await first.ActivateFirstAsync());
        }
        using var restarted = new SingleInstance(name); Assert.True(restarted.IsFirst);
    }
    [Theory]
    [InlineData("en", true)] [InlineData("mixed", true)] [InlineData("es", false)]
    public async Task ReadingKeepsStanzasAlignedAndSpanishDoesNotDuplicateOriginal(string language, bool translated)
    {
        var track = new CurrentTrack(1, "Own text", "Test", null, null, PlaybackState.Playing);
        var provider = new Provider(language); var vm = new MainViewModel(new Store(), provider, insightProvider: provider);
        await vm.InitializeAsync(); vm.ApplyDetection(new(ApplicationState.Playing, track, null));
        Assert.Null(vm.SaveManual(vm.BeginLyricsEdit()!, "I open a window\r\n\r\nA new day"));
        await vm.GenerateInsightAsync();
        Assert.Equal(2, vm.ReadingBlocks.Count); Assert.Equal("A new day", vm.ReadingBlocks[1].Original);
        Assert.All(vm.ReadingBlocks, block => Assert.Equal(translated, block.HasTranslation));
        Assert.Equal(translated ? "Un nuevo día" : "", vm.ReadingBlocks[1].Translation); await vm.StopAsync();
    }
    private sealed class Store : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken token = default) => Task.FromResult(new AppSettings(true, "chosen-model"));
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken token = default) => Task.FromResult(0);
    }
    private sealed class Provider(string language) : ILyricsProvider, IInsightProvider
    {
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token) => throw new InvalidOperationException("No network in desktop tests");
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token) => Task.FromResult(new SongInsight(track.Revision, language,
            language == "es" ? [] : [new(1, "Abro una ventana", "en"), new(3, "Un nuevo día", "en")], "Resumen propio", ["Cambio"], [], [], []));
    }
}
