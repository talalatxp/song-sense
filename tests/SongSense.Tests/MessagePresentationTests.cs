using SongSense.App;
using SongSense.Core;
using Xunit;
namespace SongSense.Tests;

public sealed class MessagePresentationTests
{
 [Fact]
 public async Task ExistingResultDoesNotHideANewGenerationBlockerOrPresentItAsSuccess()
 {
  var provider=new Provider(true);var vm=new MainViewModel(new Store(),provider,insightProvider:provider);
  await vm.InitializeAsync();vm.ApplyDetection(new(ApplicationState.Playing,new(1,"Canción propia","Artista","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));
  vm.SaveManual(vm.BeginLyricsEdit()!,"Letra propia");await vm.GenerateInsightAsync();var result=vm.Insight;
  vm.ApplyAiSettings(new(false,"fixture-model",20));
  Assert.Same(result,vm.Insight);Assert.Equal(PreparationCause.Result,vm.Preparation.Cause);Assert.NotEmpty(vm.Preparation.Blockers);
  Assert.Equal(NoticePresentation.Attention,vm.PreparationPresentation);Assert.Equal(NoticeKind.Warning,vm.DisplayNoticeKind);
  Assert.True(vm.ShowPreparationNotice);Assert.False(vm.CanPrimaryAction);Assert.Equal(1,provider.Calls);await vm.StopAsync();
 }
 [Theory]
 [InlineData(false)] [InlineData(true)]
 public async Task CancellingRetainsLyricsAndDistinguishesCommunicatedUsageFromUnknown(bool known)
 {
  var provider=new Provider(known);var cache=new Cache();var vm=new MainViewModel(new Store(),provider,insightProvider:provider,cache:cache);
  await vm.InitializeAsync();vm.ApplyDetection(new(ApplicationState.Playing,new(1,"Canción propia","Artista","Álbum",TimeSpan.FromMinutes(3),PlaybackState.Playing),null));
  vm.SaveManual(vm.BeginLyricsEdit()!,"Letra propia que se conserva");await vm.WaitForCacheAsync();
  var operation=vm.GenerateInsightAsync();await (known?cache.Started.Task:provider.Started.Task).WaitAsync(TimeSpan.FromSeconds(5));
  Assert.Equal(NoticePresentation.Contextual,vm.PreparationPresentation);Assert.Contains("puede consumir",vm.OperationFeedback);
  var counter=vm.Activity.Counter;vm.CancelInsight();await operation;
  Assert.Equal(PreparationCause.AiCancelled,vm.Preparation.Cause);Assert.Equal(NoticePresentation.Contextual,vm.PreparationPresentation);
  Assert.Contains("letra conservada",vm.OperationFeedback);Assert.Equal(counter,vm.Activity.Counter);Assert.Equal("Letra propia que se conserva",vm.OriginalLyrics);
  if(known){Assert.Equal(AiUsageState.Comunicado,vm.Usage.State);Assert.Contains("7 tokens comunicados",vm.OperationFeedback);}
  else {Assert.Equal(AiUsageState.NoComunicado,vm.Usage.State);Assert.Contains("Consumo no comunicado",vm.OperationFeedback);}
  Assert.Equal(1,provider.Calls);await vm.StopAsync();
 }
 sealed class Provider(bool known):ILyricsProvider,IInsightProvider
 {
  public int Calls;public TaskCompletionSource Started=new();
  public Task<LyricsSearchResult> FindAsync(CurrentTrack t,CancellationToken c)=>throw new InvalidOperationException("No implicit search");
  public async Task<SongInsight> GenerateAsync(CurrentTrack t,ResolvedLyrics l,CancellationToken c)
  {
   Calls++;Started.TrySetResult();if(!known)await Task.Delay(Timeout.Infinite,c);
   return new(t.Revision,"es",[],"Interpretación propia",[],[],[],[]){Usage=new(5,2,7)};
  }
 }
 sealed class Store:ISettingsStore
 {
  public Task InitializeAsync(CancellationToken c=default)=>Task.CompletedTask;
  public Task<AppSettings> ReadSettingsAsync(CancellationToken c=default)=>Task.FromResult(new AppSettings(true,"fixture-model",20));
  public Task SaveSettingsAsync(AppSettings s,CancellationToken c=default)=>Task.CompletedTask;
  public Task<int> ReadRequestCountAsync(DateOnly d,CancellationToken c=default)=>Task.FromResult(0);
 }
 sealed class Cache:ICacheStore
 {
  public TaskCompletionSource Started=new();
  public Task<CachedSong?> ReadSongAsync(CurrentTrack t,CancellationToken c=default)=>Task.FromResult<CachedSong?>(null);
  public Task SaveLyricsAsync(CurrentTrack t,ResolvedLyrics? l,CancellationToken c=default)=>Task.CompletedTask;
  public Task<SongInsight?> ReadInsightAsync(ResolvedLyrics l,string m,string v,CancellationToken c=default,CurrentTrack? t=null,string scope="api-key",string targetLanguage="es")=>Task.FromResult<SongInsight?>(null);
  public async Task SaveInsightAsync(CurrentTrack t,ResolvedLyrics l,string m,string v,SongInsight s,CancellationToken c=default,string scope="api-key",string targetLanguage="es"){Started.TrySetResult();await Task.Delay(Timeout.Infinite,c);}
  public Task DeleteSongAsync(CurrentTrack t,CancellationToken c=default)=>Task.CompletedTask;
  public Task ClearAsync(CancellationToken c=default)=>Task.CompletedTask;
 }
}
