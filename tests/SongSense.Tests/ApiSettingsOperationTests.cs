using System.Net.Http;
using System.Security;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;
public sealed class ApiSettingsOperationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LocalSaveHasDistinctActivityAndStopsOnCompletionOrCancellation(bool cancel)
    {
        var store = new Store(); using var http = new HttpClient(new NoNetwork());
        var service = new AiConfigurationService(store, new Secrets(), store, new OpenAiConnectionProbe(http));
        using var vm = new AiSettingsViewModel(service, CancellationToken.None); await vm.InitializeAsync();
        vm.Model = "updated-model"; store.WriteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var empty = new SecureString(); var pending = vm.SaveAsync(empty);
        Assert.True(vm.IsOperating); Assert.Equal(ApiSettingsOperation.Saving, vm.Operation);
        Assert.Contains("este dispositivo", vm.OperationMessage); Assert.True(vm.CanCancel);
        if (cancel) vm.Cancel(); else store.WriteGate.SetResult(); await pending;
        Assert.False(vm.IsOperating); Assert.Equal(ApiSettingsOperation.None, vm.Operation); Assert.Empty(vm.OperationMessage);
        Assert.Equal(cancel ? "fixture-model" : "updated-model", store.Value.Model);
        Assert.Equal(cancel, vm.HasChanges); Assert.Equal(0, store.Reservations);
    }
    [Fact]
    public async Task LoadingProgressOnlyLastsForTheLocalRead()
    {
        var store = new Store { ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var http = new HttpClient(new NoNetwork());
        var service = new AiConfigurationService(store, new Secrets(), store, new OpenAiConnectionProbe(http));
        using var vm = new AiSettingsViewModel(service, CancellationToken.None); var pending = vm.InitializeAsync();
        Assert.True(vm.IsOperating); Assert.Equal(ApiSettingsOperation.Loading, vm.Operation); Assert.Contains("locales", vm.OperationMessage);
        store.ReadGate.SetResult(); await pending;
        Assert.False(vm.IsOperating); Assert.True(vm.CanManage); Assert.Equal(ApiSettingsOperation.None, vm.Operation); Assert.Equal(0, store.Reservations);
    }
    private sealed class Store : ISettingsStore, IRequestBudget
    {
        public AppSettings Value = new(false, "fixture-model", 20);
        public TaskCompletionSource? ReadGate, WriteGate;
        public int Reservations;
        public Task InitializeAsync(CancellationToken token = default) => Task.CompletedTask;
        public async Task<AppSettings> ReadSettingsAsync(CancellationToken token = default) { if (ReadGate is not null) await ReadGate.Task.WaitAsync(token); return Value; }
        public async Task SaveSettingsAsync(AppSettings value, CancellationToken token = default) { if (WriteGate is not null) await WriteGate.Task.WaitAsync(token); Value = value; }
        public Task<int> ReadRequestCountAsync(DateOnly date, CancellationToken token = default) => Task.FromResult(0);
        public Task<bool> TryReserveRequestAsync(DateOnly date, CancellationToken token = default) { Reservations++; return Task.FromResult(true); }
    }
    private sealed class Secrets : ISecretStore
    {
        public Task<bool> HasKeyAsync(CancellationToken token = default) => Task.FromResult(false);
        public Task<string?> ReadKeyAsync(CancellationToken token = default) => Task.FromResult<string?>(null);
        public Task SaveKeyAsync(SecureString key, CancellationToken token = default) => Task.CompletedTask;
        public Task DeleteKeyAsync(CancellationToken token = default) => Task.CompletedTask;
    }
    private sealed class NoNetwork : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("Local operations must not use HTTP"); }
}
