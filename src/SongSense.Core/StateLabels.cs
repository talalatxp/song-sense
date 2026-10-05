namespace SongSense.Core;

public static class StateLabels
{
    public static string InSpanish(this ApplicationState state) => state switch
    {
        ApplicationState.NoSpotify => "Sin Spotify",
        ApplicationState.NoTrack => "Sin canción",
        ApplicationState.Paused => "En pausa",
        ApplicationState.SearchingLyrics => "Buscando letra",
        ApplicationState.ChoosingVersion => "Elegir versión",
        ApplicationState.NoLyrics => "Sin letra",
        ApplicationState.AiNotConfigured => "IA sin configurar",
        ApplicationState.Processing => "Procesando",
        ApplicationState.Ready => "Listo",
        ApplicationState.Offline => "Sin conexión",
        ApplicationState.DailyLimit => "Límite diario",
        ApplicationState.Error => "Error",
        ApplicationState.Playing => "Reproduciendo",
        ApplicationState.Stopped => "Reproducción detenida",
        ApplicationState.PlaybackUnknown => "Estado de reproducción desconocido",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
