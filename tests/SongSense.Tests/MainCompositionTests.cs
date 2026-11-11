using System.IO;
using SongSense.App;
using SongSense.Core;
using Xunit;
namespace SongSense.Tests;

public sealed class MainCompositionTests
{
 [Fact] public async Task FailedLocalSaveRetriesWithoutAnotherInferenceOrChangingReadingContext()
 {
  var cache=new Cache();var provider=new Provider();var vm=await Create(cache,provider);
  vm.SaveManual(vm.BeginLyricsEdit()!,"Una letra propia");await vm.WaitForCacheAsync();await vm.GenerateInsightAsync();
  Assert.True(vm.HasFailedLocalSave);var result=vm.Insight;var revision=vm.LyricsRevision;var requests=vm.Activity.Counter;
  cache.Fail=false;await vm.RetryLocalSaveAsync();
  Assert.False(vm.HasFailedLocalSave);Assert.Same(result,vm.Insight);Assert.Equal(revision,vm.LyricsRevision);Assert.Equal(requests,vm.Activity.Counter);Assert.Equal(1,provider.Calls);
  Assert.Equal(ContentSaveState.Saved,vm.LyricsSaveState);Assert.Equal(ContentSaveState.Saved,vm.InsightSaveState);await vm.StopAsync();
 }
 [Fact] public async Task SaveRetryThatFinishesAfterSongChangeDoesNotMarkTheNewSongSaved()
 {
  var cache=new Cache();var provider=new Provider();var vm=await Create(cache,provider);vm.SaveManual(vm.BeginLyricsEdit()!,"Letra fijada");await vm.WaitForCacheAsync();
  cache.Fail=false;cache.Block=true;var retry=vm.RetryLocalSaveAsync();await cache.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
  vm.ApplyDetection(Track(2));cache.Release.TrySetResult();await retry;
  Assert.Equal(2,vm.CurrentTrack!.Revision);Assert.Null(vm.PreparedLyrics);Assert.NotEqual(ContentSaveState.Saved,vm.LyricsSaveState);Assert.Equal(0,provider.Calls);await vm.StopAsync();
 }
 [Fact] public async Task NoSongNoLyricsAndInstrumentalHaveDifferentEmptyReasons()
 {
  var p=new Provider();var vm=new MainViewModel(new Store(),p,insightProvider:p);await vm.InitializeAsync();
  Assert.Equal("Esperando una canción",vm.LyricsEmptyTitle);Assert.False(vm.HasReadableOriginal);
  vm.ApplyDetection(Track(1));await vm.SearchLyricsAsync();Assert.Equal("Letra no disponible",vm.LyricsEmptyTitle);
  p.Instrumental=true;await vm.SearchLyricsAsync();Assert.Equal("Pieza instrumental",vm.LyricsEmptyTitle);Assert.False(vm.HasReadableOriginal);Assert.Equal(0,p.Calls);await vm.StopAsync();
 }
 [Fact] public async Task FailedRetryKeepsTheResultReadableAndCanBeRetriedAgain()
 {
  var cache=new Cache();var p=new Provider();var vm=await Create(cache,p);vm.SaveManual(vm.BeginLyricsEdit()!,"Texto propio");await vm.WaitForCacheAsync();
  await vm.RetryLocalSaveAsync();Assert.True(vm.CanRetryLocalSave);Assert.True(vm.HasReadableOriginal);Assert.Contains("sesión",vm.SaveRecoveryMessage);Assert.Equal(0,p.Calls);await vm.StopAsync();
 }
 static TrackDetection Track(long n)=>new(ApplicationState.Playing,new(n,"Canción propia "+n,"Artista","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null);
 static async Task<MainViewModel> Create(Cache c,Provider p){var vm=new MainViewModel(new Store(),p,insightProvider:p,cache:c);await vm.InitializeAsync();vm.ApplyDetection(Track(1));await vm.WaitForCacheAsync();return vm;}
 sealed class Store:ISettingsStore
 {
  public Task InitializeAsync(CancellationToken t=default)=>Task.CompletedTask;
  public Task<AppSettings> ReadSettingsAsync(CancellationToken t=default)=>Task.FromResult(new AppSettings(true,"fixture-model",20));
  public Task SaveSettingsAsync(AppSettings s,CancellationToken t=default)=>Task.CompletedTask;
  public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken t=default)=>Task.FromResult(0);
 }
 sealed class Provider:ILyricsProvider,IInsightProvider
 {
  public int Calls;public bool Instrumental;
  public Task<LyricsSearchResult> FindAsync(CurrentTrack track,CancellationToken t)=>Task.FromResult(new LyricsSearchResult(track.Revision,Instrumental?LyricsSearchKind.Instrumental:LyricsSearchKind.NotFound,Instrumental?new(track.Revision,"",LyricsOrigin.Lrclib,1,true):null,[]));
  public Task<SongInsight> GenerateAsync(CurrentTrack track,ResolvedLyrics l,CancellationToken t){Calls++;return Task.FromResult(new SongInsight(track.Revision,"es",[],"Interpretación propia.",["Cambio"],[],[],[]));}
 }
 sealed class Cache:ICacheStore
 {
  public bool Fail=true,Block;public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task<CachedSong?> ReadSongAsync(CurrentTrack t,CancellationToken c=default)=>Task.FromResult<CachedSong?>(null);
  public async Task SaveLyricsAsync(CurrentTrack t,ResolvedLyrics? l,CancellationToken c=default){if(Block){Started.TrySetResult();await Release.Task.WaitAsync(c);}if(Fail)throw new IOException("synthetic cache failure");}
  public Task<SongInsight?> ReadInsightAsync(ResolvedLyrics l,string m,string v,CancellationToken c=default,CurrentTrack? t=null,string scope="api-key",string targetLanguage="es")=>Task.FromResult<SongInsight?>(null);
  public Task SaveInsightAsync(CurrentTrack t,ResolvedLyrics l,string m,string v,SongInsight s,CancellationToken c=default,string scope="api-key",string targetLanguage="es")=>Fail?Task.FromException(new IOException("synthetic cache failure")):Task.CompletedTask;
  public Task DeleteSongAsync(CurrentTrack t,CancellationToken c=default)=>Task.CompletedTask;public Task ClearAsync(CancellationToken c=default)=>Task.CompletedTask;
 }
}
