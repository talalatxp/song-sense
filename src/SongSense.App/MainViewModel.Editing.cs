using SongSense.Core;
namespace SongSense.App;
public sealed record LyricsSaveResult(bool Success, bool Persisted, string Message);
public sealed partial class MainViewModel
{
    public async Task<LyricsSaveResult> SaveEditedLyricsAsync(LyricsEditContext context, string? text, long? candidateId = null)
    {
        if (!IsEditCurrent(context)) return new(false, false, "La canción o la letra ha cambiado. Conserva o copia el borrador; no se aplica a la canción nueva.");
        ResolvedLyrics lyrics;
        if (candidateId is { } id)
        {
            var candidate = Candidates.FirstOrDefault(c => c.Id == id);
            if (candidate is null) return new(false, false, "Selecciona una versión disponible.");
            var value = LyricsResolution.TextOf(candidate);
            if (!candidate.IsInstrumental && string.IsNullOrWhiteSpace(value)) return new(false, false, "La versión no tiene letra disponible.");
            lyrics = new(context.TrackRevision, candidate.IsInstrumental ? "" : value, LyricsOrigin.Lrclib, id, candidate.IsInstrumental);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 20_000) return new(false, false, "Introduce una letra de 1 a 20.000 caracteres, sin recortarla.");
            lyrics = new(context.TrackRevision, text, LyricsOrigin.Manual, null, false);
        }
        var track = CurrentTrack!; var epoch = cacheEpoch;
        if (cache is not null)
        {
            try
            {
                var written = await CacheIoAsync(async () =>
                {
                    if (!IsEditCurrent(context) || epoch != cacheEpoch) return false;
                    await cache.SaveLyricsAsync(track, lyrics, lifetime); return true;
                }, lifetime);
                if (!written || epoch != cacheEpoch)
                    return new(false, false, "El contexto cambió durante el guardado. El borrador sigue aquí; no se aplica a la canción nueva.");
                if (!IsEditCurrent(context)) return new(false, true, "La letra se escribió para la canción original. El contexto cambió; conserva el borrador, que no se aplica a la canción nueva.");
            }
            catch (Exception) { return new(false, false, "No se pudo guardar en este dispositivo. El borrador se conserva; puedes reintentar o copiarlo."); }
        }
        ConfirmLyrics(lyrics, persist: false);
        lyricsSaveState = cache is null ? ContentSaveState.SessionOnly : ContentSaveState.Saved; NotifyProvenance();
        cacheMessage = cache is null ? "Letra aplicada solo a esta sesión; no hay almacenamiento disponible." : "Letra guardada en este dispositivo. La caché puede caducar o eliminarse.";
        OnChanged(nameof(CacheMessage)); return new(true, cache is not null, cacheMessage);
    }
}
