using SongSense.App;
using SongSense.Core;
using Xunit;

namespace SongSense.Tests;

public sealed class AiActivityTests
{
    [Fact]
    public void ActivityIsSessionOnlyAndCounterHasExplicitDateAndCanBecomeUnknown()
    {
        var vm = new AiActivityViewModel();
        Assert.Contains("Sin actividad", vm.LastActivity);
        Assert.Contains("no disponible", vm.Counter);
        vm.UpdateCounter(new(2026, 10, 21), 3, 20);
        Assert.Contains("21/10/2026: 3 / 20", vm.Counter);
        Assert.Contains("no representa los límites del plan ni euros", vm.Counter);
        vm.UpdateCounter(new(2026, 10, 22), null, 20);
        Assert.Equal("Contador local no disponible.", vm.Counter);
        Assert.Null(new AiActivityViewModel().Last);
    }

    [Fact]
    public void EqualTimestampsDoNotAllowOlderOperationToReplaceNewerActivity()
    {
        var vm = new AiActivityViewModel();
        var at = DateTimeOffset.Parse("2026-10-21T12:00:00+02:00");
        var old = new AiUsagePresentation(AiUsageState.EnCurso, At: at);
        var latest = new AiUsagePresentation(AiUsageState.EnCurso, At: at);
        vm.Record(old); vm.Record(latest);
        vm.Record(old with { State = AiUsageState.Comunicado, Tokens = new(1, 2, 3) });
        Assert.Equal(latest, vm.Last);
    }
}
