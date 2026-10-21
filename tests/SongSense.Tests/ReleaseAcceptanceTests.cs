using System.IO;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;

public sealed class ReleaseAcceptanceTests
{
    [Theory]
    [InlineData("en", "I open a window\n\nA new day")]
    [InlineData("es", "Abro una ventana\n\nUn nuevo día")]
    [InlineData("mixed", "I open a window\n\nUn nuevo día")]
    public async Task OfflineRestartReadsRealDatabaseButNewSongFailsWithoutSpending(string language, string text)
    {
        // Original material; actual SQLite persistence, with a deliberately offline transport.
        var directory = Path.Combine(Path.GetTempPath(), "SongSense.ReleaseTests", Guid.NewGuid().ToString("N"));
        MainViewModel? vm = null;
        try
        {
            var database = Path.Combine(directory, "songsense.db");
            var store = new SqliteSettingsStore(database);
            await store.InitializeAsync();
            await store.SaveSettingsAsync(new(false, "chosen-model"));
            var track = new CurrentTrack(1, "Own release song", "Test artist", "Test album", TimeSpan.FromSeconds(180), PlaybackState.Playing);
            var lyrics = new ResolvedLyrics(track.Revision, text, LyricsOrigin.Manual, null, false);
            var result = new SongInsight(track.Revision, language,
                language == "es" ? [] : [new(1, "Abro una ventana", "en"), new(3, "Un nuevo día", language == "mixed" ? "es" : "en")],
                string.Join(' ', Enumerable.Repeat("La ventana podría representar una oportunidad de cambio mientras la repetición sugiere una decisión todavía incierta.", 5)),
                ["Cambio"], [], ["Una acción cotidiana es otra lectura posible."], ["La lectura es tentativa."]);
            await new SqliteCacheStore(database).SaveInsightAsync(track, lyrics, "chosen-model", CacheIdentity.Version, result);
            var offline = new OfflineProvider();
            vm = new MainViewModel(new SqliteSettingsStore(database), offline, insightProvider: offline, cache: new SqliteCacheStore(database));
            vm.ApplyDetection(new(ApplicationState.Playing, track with { Revision = 99 }, null));
            await vm.InitializeAsync();
            await vm.WaitForCacheAsync();
            Assert.True(vm.HasInsight);
            Assert.Equal(99, vm.Insight!.TrackRevision);
            Assert.Equal(text, vm.OriginalLyrics);
            Assert.Equal(2, vm.ReadingBlocks.Count);
            Assert.All(vm.ReadingBlocks, block => Assert.Equal(language != "es", block.HasTranslation));
            Assert.Contains("0 tokens", vm.UsageMessage);
            Assert.Equal(0, offline.LyricsCalls);
            Assert.Equal(0, offline.InsightCalls);
            vm.ApplyDetection(new(ApplicationState.Playing, track with { Revision = 100, Title = "New uncached song" }, null));
            await vm.WaitForCacheAsync();
            await vm.SearchLyricsAsync();
            Assert.Null(vm.PreparedLyrics);
            Assert.False(vm.HasInsight);
            Assert.False(vm.GenerateInsightCommand.CanExecute(null));
            Assert.Equal(1, offline.LyricsCalls);
            Assert.Equal(0, offline.InsightCalls);
            Assert.Equal(0, await store.ReadRequestCountAsync(DateOnly.FromDateTime(DateTime.Now)));
        }
        finally
        {
            if (vm is not null) await vm.StopAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class OfflineProvider : ILyricsProvider, IInsightProvider
    {
        public int LyricsCalls { get; private set; }
        public int InsightCalls { get; private set; }
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken token)
        {
            LyricsCalls++;
            throw new LyricsProviderException(LyricsFailure.Offline);
        }
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken token)
        {
            InsightCalls++;
            throw new AiException(AiFailure.Offline);
        }
    }
}
