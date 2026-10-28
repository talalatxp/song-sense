using SongSense.App;
using SongSense.Core;
using Xunit;

namespace SongSense.Tests;

public sealed class VersionNavigationTests
{
    private static async Task<(MainViewModel Owner, Provider Provider)> Create(bool candidates = true)
    {
        var provider = new Provider(candidates); var owner = new MainViewModel(new Store(), provider, insightProvider: provider);
        await owner.InitializeAsync(); owner.ApplyDetection(new(ApplicationState.Playing, new(1, "Ventana propia", "Artista", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
        await owner.SearchLyricsAsync(); return (owner, provider);
    }
    [Fact]
    public async Task SelectingEveryOptionStaysInListAndOnlyExplicitConfirmationAppliesLyrics()
    {
        var (owner, provider) = await Create(); using var editor = new LyricsEditorViewModel(owner, owner.BeginLyricsEdit()!);
        try
        {
            Assert.False(editor.OpenSelectedVersion()); Assert.False(editor.CanPreviewVersion);
            foreach (var option in editor.Options)
            {
                editor.Selected = option; Assert.Equal(LyricsVersionsStep.List, editor.VersionsStep);
                Assert.Contains("Selecciona", editor.VersionsMessage); Assert.Null(owner.PreparedLyrics);
                Assert.True(editor.CanPreviewVersion); Assert.Equal(LyricsResolution.TextOf(option.Candidate), editor.Preview);
            }
            Assert.True(editor.OpenSelectedVersion()); Assert.Equal(LyricsVersionsStep.Preview, editor.VersionsStep);
            Assert.Contains("Vista previa", editor.VersionsMessage); Assert.Null(owner.PreparedLyrics);
            editor.ReturnToVersions(); Assert.Contains("Selecciona", editor.VersionsMessage); Assert.Same(editor.Options[4], editor.Selected);
            editor.Selected = editor.Options[1]; Assert.Equal(LyricsVersionsStep.List, editor.VersionsStep);
            Assert.True(editor.OpenSelectedVersion()); await editor.SaveAsync(true);
            Assert.Equal(2, owner.PreparedLyrics!.LrclibId); Assert.Equal("Texto propio de versión 2", owner.OriginalLyrics);
            Assert.Equal(1, provider.Searches); Assert.Equal(0, provider.Generations);
        }
        finally { await owner.StopAsync(); }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WideComparisonDoesNotLoseTheExplicitCompactStepOrSelection(bool preview)
    {
        var (owner, provider) = await Create(); using var editor = new LyricsEditorViewModel(owner, owner.BeginLyricsEdit()!);
        try
        {
            editor.Selected = editor.Options[2]; if (preview) editor.OpenSelectedVersion();
            editor.SetVersionsComparison(true); Assert.Contains("compara", editor.VersionsMessage);
            Assert.Same(editor.Options[2], editor.Selected); Assert.Null(owner.PreparedLyrics);
            editor.SetVersionsComparison(false); Assert.Equal(preview ? LyricsVersionsStep.Preview : LyricsVersionsStep.List, editor.VersionsStep);
            Assert.Contains(preview ? "Vista previa" : "Selecciona", editor.VersionsMessage);
            Assert.Equal(1, provider.Searches); Assert.Equal(0, provider.Generations);
        }
        finally { await owner.StopAsync(); }
    }
    [Fact]
    public async Task ContextChangeKeepsOldDraftAndPreviewReadableButBlocksBothSaveActions()
    {
        var (owner, provider) = await Create(); using var editor = new LyricsEditorViewModel(owner, owner.BeginLyricsEdit()!);
        try
        {
            editor.ManualText = "Borrador propio sin guardar"; editor.Selected = editor.Options[3]; editor.OpenSelectedVersion();
            var preview = editor.Preview;
            owner.ApplyDetection(new(ApplicationState.Playing, new(2, "Otra canción", "Artista", "Álbum", TimeSpan.FromMinutes(3), PlaybackState.Playing), null));
            Assert.False(editor.IsCurrent); Assert.False(editor.ConfirmCommand.CanExecute(null)); Assert.False(editor.SaveCommand.CanExecute(null));
            Assert.True(editor.CanCopyDraft); Assert.True(editor.CanPreviewVersion); Assert.Equal(preview, editor.Preview);
            editor.ReturnToVersions(); editor.OpenSelectedVersion(); await editor.SaveAsync(true); await editor.SaveAsync();
            Assert.Equal("Borrador propio sin guardar", editor.ManualText); Assert.Null(owner.PreparedLyrics);
            Assert.Equal(1, provider.Searches); Assert.Equal(0, provider.Generations);
        }
        finally { await owner.StopAsync(); }
    }
    private sealed class Store : ISettingsStore
    {
        public Task InitializeAsync(CancellationToken t = default) => Task.CompletedTask;
        public Task<AppSettings> ReadSettingsAsync(CancellationToken t = default) => Task.FromResult(new AppSettings());
        public Task SaveSettingsAsync(AppSettings s, CancellationToken t = default) => Task.CompletedTask;
        public Task<int> ReadRequestCountAsync(DateOnly d, CancellationToken t = default) => Task.FromResult(0);
    }
    public sealed class Provider(bool candidates) : ILyricsProvider, IInsightProvider
    {
        public int Searches, Generations;
        public Task<LyricsSearchResult> FindAsync(CurrentTrack track, CancellationToken t)
        {
            Searches++;
            return Task.FromResult(new LyricsSearchResult(track.Revision, LyricsSearchKind.Candidates, null, candidates ? Enumerable.Range(1, 5).Select(i => new LyricsCandidate(i, "Versión parecida " + i, "Artista", "Álbum", TimeSpan.FromMinutes(3), false, "Texto propio de versión " + i, null)).ToArray() : []));
        }
        public Task<SongInsight> GenerateAsync(CurrentTrack track, ResolvedLyrics lyrics, CancellationToken t) { Generations++; throw new InvalidOperationException("No inference expected"); }
    }
}
