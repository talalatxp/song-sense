using System.IO;
using SongSense.App;
using SongSense.Core;
using SongSense.Infrastructure;
using Xunit;

namespace SongSense.Tests;
public sealed class FinalExperienceTests
{
    [Fact]
    public void ClosePreferenceAndOneTimeHintPersistWithoutSongOrSecretData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SongSense.FinalTests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "window.json"); var store = new WindowPreferenceStore(path);
            Assert.True(store.Read().KeepInTray); Assert.False(store.Read().TrayHintShown);
            Assert.True(store.Save(new(KeepInTray: false, TrayHintShown: true)));
            var reopened = new WindowPreferenceStore(path).Read(); Assert.False(reopened.KeepInTray); Assert.True(reopened.TrayHintShown);
            Assert.Equal(new WindowPreferences(), WindowPlacement.Normalize(new(Version: 99)));
            File.WriteAllText(path, "{\"Version\":1,\"Width\":600}");
            Assert.True(store.Read().KeepInTray); Assert.False(store.Read().TrayHintShown); Assert.Equal(600, store.Read().Width);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void LiveStatesDeduplicateSteadyUpdatesAndAnnounceCompletionAfterAnotherOperation()
    {
        var states = new StatusAnnouncements();
        Assert.False(states.Observe(NoticeKind.Progress, "Analizando", "Analysing"));
        Assert.True(states.Observe(NoticeKind.Success, "Resultado disponible", "Result"));
        for (int i = 0; i < 20; i++) Assert.False(states.Observe(NoticeKind.Success, "Resultado disponible", "Result"));
        Assert.False(states.Observe(NoticeKind.Progress, "Analizando", "Analysing"));
        Assert.True(states.Observe(NoticeKind.Success, "Resultado disponible", "Result"));
        Assert.True(states.Observe(NoticeKind.Error, "Sin conexión", "Offline"));
        Assert.False(states.Observe(NoticeKind.Error, "Sin conexión", "Offline"));
    }
    [Fact]
    public void BlockersAreAnnouncedBySemanticCauseAndSongMetadataIsNotLiveSpeech()
    {
        var states = new StatusAnnouncements();
        Assert.False(states.Observe(NoticeKind.Information, "Canción nueva"));
        Assert.True(states.Observe(NoticeKind.Information, "Elige un modelo", "ModelRequired"));
        Assert.False(states.Observe(NoticeKind.Information, "Elige un modelo para otra canción", "ModelRequired"));
        Assert.True(states.Observe(NoticeKind.Warning, "Cuota agotada", "DailyLimit"));
        Assert.False(states.Observe(NoticeKind.Warning, "Cuota agotada; faltan 20 segundos", "DailyLimit"));
    }
    [Fact]
    public void AboutIdentifiesPrereleaseAndBuildWithoutClaimingExternalAcceptance()
    {
        Assert.StartsWith("0.14.0-preview.2", BuildIdentity.ProductVersion);
        Assert.Equal("0.14.0.3", BuildIdentity.FileVersion);
        Assert.Contains("pendiente", BuildIdentity.Description);
        Assert.Contains("Build", BuildIdentity.Description);
    }
}
