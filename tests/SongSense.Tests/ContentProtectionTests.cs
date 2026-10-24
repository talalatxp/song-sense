using SongSense.App;
using System.IO;
using SongSense.Core;
using Xunit;
namespace SongSense.Tests;
public sealed class ContentProtectionTests
{
    static TrackDetection Track(long id) => new(ApplicationState.Playing, new(id, "Canción propia " + id, "Artista", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null);
    static async Task<MainViewModel> Start(Cache cache)
    {
        var vm = new MainViewModel(new Store(), new Provider(), cache: cache); await vm.InitializeAsync(); vm.ApplyDetection(Track(1)); await vm.WaitForCacheAsync(); return vm;
    }
    [Fact]
    public async Task OpeningAndAbandoningDeletionNeverWritesAndStaleTargetCannotDeleteNewSong()
    {
        var cache = new Cache(); var vm = await Start(cache);
        var request = vm.BeginCacheDeletion(false)!; Assert.Contains("Canción propia 1", request.Label); Assert.Equal(0, cache.Deletes);
        vm.ApplyDetection(Track(2)); Assert.False(await vm.ConfirmCacheDeletionAsync(request)); Assert.Equal(0, cache.Deletes);
        Assert.Contains("contexto", vm.CacheMessage); await vm.StopAsync();
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AcceptedDeletionRunsOnceAndFailureNeverClaimsSuccess(bool fail)
    {
        var cache = new Cache { FailDelete = fail }; var vm = await Start(cache);
        Assert.Equal(!fail, await vm.ConfirmCacheDeletionAsync(vm.BeginCacheDeletion(true)!)); Assert.Equal(1, cache.Deletes);
        Assert.Contains(fail ? "No se pudo" : "contador conservados", vm.CacheMessage); await vm.StopAsync();
    }
    [Fact]
    public async Task WriteAlreadyInFlightFinishesBeforeDeletionAndCannotResurrectContent()
    {
        var cache = new Cache { HoldWrite = true }; var vm = await Start(cache);
        vm.SaveManual(vm.BeginLyricsEdit()!, "Texto propio"); await cache.Started.Task;
        var deletion = vm.ConfirmCacheDeletionAsync(vm.BeginCacheDeletion(false)!);
        Assert.False(vm.CanEditLyrics); cache.Release.TrySetResult();
        Assert.True(await deletion); await vm.WaitForCacheAsync(); Assert.Null(cache.Saved); Assert.Equal(1, cache.Deletes); await vm.StopAsync();
    }
    internal sealed class Store : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings());
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(0);
    }
    [Fact]
    public async Task LateFailedLyricsWriteCannotMarkTheNewSongAsFailed()
    {
        var cache = new Cache { HoldWrite = true, FailWrite = true }; var vm = await Start(cache);
        vm.SaveManual(vm.BeginLyricsEdit()!, "Texto de la canción original"); await cache.Started.Task;
        vm.ApplyDetection(Track(2)); cache.Release.TrySetResult(); await vm.WaitForCacheAsync();
        Assert.Equal(2, vm.CurrentTrack!.Revision); Assert.Empty(vm.CacheMessage); Assert.Empty(vm.ReadingSource); await vm.StopAsync();
    }
    [Fact]
    public async Task DraftValidationIsReactiveAndNeverTruncates()
    {
        var vm = await Start(new()); using var editor = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!);
        Assert.False(editor.SaveCommand.CanExecute(null)); editor.ManualText = "Texto válido";
        Assert.True(editor.SaveCommand.CanExecute(null)); Assert.Empty(editor.Error);
        editor.ManualText = new string('a', 20_001); Assert.False(editor.SaveCommand.CanExecute(null)); Assert.Contains("20.000", editor.Error);
        editor.ManualText = new string('a', 20_000); Assert.True(editor.SaveCommand.CanExecute(null));
        await editor.SaveAsync(); Assert.Equal(20_000, vm.OriginalLyrics.Length); Assert.False(editor.HasChanges); await vm.StopAsync();
    }
    [Fact]
    public async Task FailedWriteRetainsDraftAndCanBeRetriedWithoutApplyingFailedContent()
    {
        var cache = new Cache { FailWrite = true }; var vm = await Start(cache); using var editor = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!);
        editor.ManualText = "Texto propio completo"; var committed = 0; editor.Committed += (_, _) => committed++;
        await editor.SaveAsync(); Assert.Equal(0, committed); Assert.Null(vm.PreparedLyrics); Assert.True(editor.HasChanges);
        Assert.Equal("Texto propio completo", editor.ManualText); Assert.Contains("reintentar", editor.Error); Assert.True(editor.SaveCommand.CanExecute(null));
        cache.FailWrite = false; await editor.SaveAsync(); Assert.Equal(1, committed); Assert.Equal(editor.ManualText, cache.Saved!.Text);
        Assert.Contains("este dispositivo", editor.SaveMessage); await vm.StopAsync();
    }
    [Fact]
    public async Task ChangedSongOrRevisionKeepsDraftEditableButCannotApplyIt()
    {
        var vm = await Start(new()); using var editor = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!);
        editor.ManualText = "Borrador original"; vm.ApplyDetection(Track(2)); editor.ManualText += " corregido";
        Assert.True(editor.CanEditDraft); Assert.False(editor.IsCurrent); Assert.False(editor.SaveCommand.CanExecute(null));
        await editor.SaveAsync(); Assert.Null(vm.PreparedLyrics); Assert.Equal("Borrador original corregido", editor.ManualText);
        using var current = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!); current.ManualText = "Otro borrador";
        vm.SaveManual(vm.BeginLyricsEdit()!, "Otra revisión"); Assert.False(current.IsCurrent); Assert.True(current.CanEditDraft); await vm.StopAsync();
    }
    [Fact]
    public async Task TrackChangeDuringWriteCannotApplyDraftToNewTrack()
    {
        var cache = new Cache { HoldWrite = true }; var vm = await Start(cache); using var editor = new LyricsEditorViewModel(vm, vm.BeginLyricsEdit()!);
        editor.ManualText = "Borrador de canción 1"; var save = editor.SaveAsync(); await cache.Started.Task;
        vm.ApplyDetection(Track(2)); cache.Release.TrySetResult(); await save;
        Assert.False(editor.IsCurrent); Assert.True(editor.HasChanges); Assert.Contains("Borrador", editor.ManualText); Assert.Equal(2, vm.CurrentTrack!.Revision);
        await vm.StopAsync();
    }
    internal sealed class Provider : ILyricsProvider
    { public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken t) => Task.FromResult(new LyricsSearchResult(track.Revision, LyricsSearchKind.NotFound, null, [])); }
    internal sealed class Cache : ICacheStore
    {
        public int Deletes; public bool FailDelete, HoldWrite; public bool FailWrite { get; set; } public ResolvedLyrics? Saved;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CachedSong?> ReadSongAsync(CurrentTrack track, CancellationToken t = default) => Task.FromResult<CachedSong?>(Saved is null ? null : new(Saved with { TrackRevision = track.Revision }, false, false));
        public async Task SaveLyricsAsync(CurrentTrack track, ResolvedLyrics? lyrics, CancellationToken t = default)
        { Started.TrySetResult(); if (HoldWrite) await Release.Task; if (FailWrite) throw new IOException(); Saved = lyrics; }
        public Task<SongInsight?> ReadInsightAsync(ResolvedLyrics l, string m, string v, CancellationToken t = default, CurrentTrack? track = null, string scope = "api-key", string targetLanguage = "es") => Task.FromResult<SongInsight?>(null);
        public Task SaveInsightAsync(CurrentTrack track, ResolvedLyrics l, string m, string v, SongInsight i, CancellationToken t = default, string scope = "api-key", string targetLanguage = "es") => Task.CompletedTask;
        public Task DeleteSongAsync(CurrentTrack track, CancellationToken t = default) => ClearAsync(t);
        public Task ClearAsync(CancellationToken t = default) { Deletes++; if (FailDelete) throw new IOException(); Saved = null; return Task.CompletedTask; }
    }
}
