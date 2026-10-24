using SongSense.Core;

namespace SongSense.App;

public enum ContentReadiness { NoTrack, NoLyrics, Versions, Instrumental, Lyrics, Result }
public enum PreparationCause { Storage, Initializing, Stopped, Searching, Analysing, NoSpotify, NoTrack, Detection,
    Versions, CachedLyrics, NotFound, LyricsOffline, LyricsTimeout, LyricsRateLimited, LyricsInvalid, LyricsError,
    Instrumental, LanguageSaving, LanguageFailed, SessionUnavailable, SessionPending, SessionMissing, Permission,
    ModelMissing, PlanPaused, ApiDisabled, ProviderMissing, CounterUnavailable, DailyLimit,
    AiFailed, AiWait, LyricsCancelled, AiCancelled, Ready, Result }
public enum PreparationAction { None, Search, CancelSearch, CancelAnalysis, ChooseVersion, EditLyrics, ConfirmCachedLyrics,
    OpenConnection, ChooseModel, ReviewPlan, OpenApi, RetryLanguage, RefreshCounter, RetryStorage, RecoverStorage, Generate, Regenerate, Diagnostic }
public sealed record PreparationBlocker(PreparationCause Cause, string Message, PreparationAction Action);
public sealed record PreparationDescriptor(ContentReadiness Content, PreparationCause Cause, string Status, string Message,
    NoticeKind Kind, PreparationAction Action, string ActionLabel, bool CanSearch, bool CanGenerate, bool CanRegenerate,
    bool CanEdit, IReadOnlyList<PreparationBlocker> Blockers)
{
    public bool HasAction => Action != PreparationAction.None;
}

public sealed record PreparationSnapshot
{
    public bool StorageError { get; init; }
    public bool Initializing { get; init; }
    public bool CanRecover { get; init; }
    public bool Stopped { get; init; }
    public bool HasTrack { get; init; }
    public bool TrackingPaused { get; init; }
    public bool Deleting { get; init; }
    public ApplicationState Detection { get; init; }
    public bool Searching { get; init; }
    public bool Analysing { get; init; }
    public bool HasLyrics { get; init; }
    public bool Instrumental { get; init; }
    public bool HasResult { get; init; }
    public int Candidates { get; init; }
    public bool PendingCache { get; init; }
    public bool NotFound { get; init; }
    public LyricsFailure? LyricsFailure { get; init; }
    public AiFailure? AiFailure { get; init; }
    public bool LyricsCancelled { get; init; }
    public bool AiCancelled { get; init; }
    public DateTimeOffset Now { get; init; }
    public DateTimeOffset? LyricsRetryAt { get; init; }
    public DateTimeOffset? AiRetryAt { get; init; }
    public bool LanguageSaving { get; init; }
    public bool LanguageFailed { get; init; }
    public string Language { get; init; } = "Español";
    public AiConnectionMode Mode { get; init; }
    public AnalysisSessionState Session { get; init; } = AnalysisSessionState.Ready;
    public bool Authorized { get; init; }
    public bool ModelValid { get; init; }
    public bool HasProvider { get; init; }
    public bool CounterFailed { get; init; }
    public bool HasQuota { get; init; }
    public int Used { get; init; }
    public int Limit { get; init; }
    public DateOnly Date { get; init; }
    public bool EditAllowed { get; init; }
}

public static class PreparationRules
{
    public static PreparationDescriptor Evaluate(PreparationSnapshot s)
    {
        var content = !s.HasTrack ? ContentReadiness.NoTrack : s.Instrumental ? ContentReadiness.Instrumental :
            s.PendingCache || s.Candidates > 0 && !s.HasLyrics ? ContentReadiness.Versions :
            s.HasResult ? ContentReadiness.Result : s.HasLyrics ? ContentReadiness.Lyrics : ContentReadiness.NoLyrics;
        var blockers = new List<PreparationBlocker>();
        void Block(PreparationCause cause, string message, PreparationAction action) => blockers.Add(new(cause, message, action));
        if (s.LanguageSaving) Block(PreparationCause.LanguageSaving, "Guardando el idioma. Espera a que termine; no se ha enviado una consulta.", PreparationAction.None);
        if (s.LanguageFailed) Block(PreparationCause.LanguageFailed, "No se pudo guardar el idioma. Reintenta el guardado antes de generar.", PreparationAction.RetryLanguage);
        if (s.Session == AnalysisSessionState.Unavailable) Block(PreparationCause.SessionUnavailable, "La conexión de IA no está disponible. Revisa la cuenta y su almacenamiento.", PreparationAction.OpenConnection);
        if (s.Mode == AiConnectionMode.ChatGptIncluded)
        {
            if (s.Session == AnalysisSessionState.PendingRegistration) Block(PreparationCause.SessionPending, "El acceso a ChatGPT está pendiente. Continúa la conexión en Cuenta.", PreparationAction.OpenConnection);
            else if (s.Session == AnalysisSessionState.Disconnected) Block(PreparationCause.SessionMissing, "Conecta tu cuenta de ChatGPT para obtener la explicación.", PreparationAction.OpenConnection);
            else if (s.Session == AnalysisSessionState.PermissionRequired) Block(PreparationCause.Permission, "La cuenta necesita permiso de uso del plan. Revisa la conexión.", PreparationAction.OpenConnection);
        }
        if (!s.ModelValid) Block(PreparationCause.ModelMissing, "Falta un modelo válido. Elige y guarda un modelo antes de generar.", s.Mode == AiConnectionMode.ChatGptIncluded ? PreparationAction.ChooseModel : PreparationAction.OpenApi);
        if (!s.Authorized && !blockers.Any(b => b.Cause is PreparationCause.SessionMissing or PreparationCause.SessionPending or PreparationCause.Permission or PreparationCause.SessionUnavailable))
            Block(s.Mode == AiConnectionMode.ChatGptIncluded ? PreparationCause.PlanPaused : PreparationCause.ApiDisabled,
                s.Mode == AiConnectionMode.ChatGptIncluded ? "Consultas pausadas en esta sesión. Revisa la habilitación y el ajuste de créditos en Cuenta." : "La IA de API está desactivada. Revisa sus ajustes y el aviso de coste.",
                s.Mode == AiConnectionMode.ChatGptIncluded ? PreparationAction.ReviewPlan : PreparationAction.OpenApi);
        if (!s.HasProvider) Block(PreparationCause.ProviderMissing, "El servicio de análisis no está disponible en esta instancia.", PreparationAction.Diagnostic);
        if (s.AiRetryAt > s.Now) Block(PreparationCause.AiWait, $"Las consultas están en espera hasta {s.AiRetryAt.Value.ToLocalTime():dd/MM HH:mm:ss}. Esta pausa local no genera reintentos automáticos.", PreparationAction.Diagnostic);
        if (s.CounterFailed) Block(PreparationCause.CounterUnavailable, "No se pudo leer el contador local. Actualízalo antes de generar.", PreparationAction.RefreshCounter);
        if (!s.HasQuota) Block(PreparationCause.DailyLimit, $"Puedes seguir leyendo el contenido disponible. Generación bloqueada por el límite local de hoy ({s.Date:dd/MM/yyyy}): {s.Used}/{s.Limit} solicitudes. Próxima fecha disponible: {s.Date.AddDays(1):dd/MM/yyyy}, a las 00:00, hora local de Windows. No representa la cuota del plan.", PreparationAction.Diagnostic);
        var active = s.StorageError || s.Initializing || s.Deleting || s.Stopped || s.Searching || s.Analysing;
        var canSearch = s.HasTrack && !active && !(s.LyricsRetryAt > s.Now);
        var canEdit = s.EditAllowed && !s.Searching && !s.Initializing && !s.Deleting && !s.Stopped && !s.StorageError;
        var aiRecovery = s.AiFailure is AiFailure.Authentication or AiFailure.SessionExpired or AiFailure.MissingKey or AiFailure.IncompatibleModel or AiFailure.InvalidSettings or AiFailure.PlanSafetyUnverified;
        var canRequest = s.HasTrack && s.HasLyrics && content is ContentReadiness.Lyrics or ContentReadiness.Result && !active && blockers.Count == 0 && !aiRecovery && !(s.AiRetryAt > s.Now);
        PreparationDescriptor D(PreparationCause cause, string status, string message, PreparationAction action = PreparationAction.None, NoticeKind kind = NoticeKind.Information) =>
            new(content, cause, status, message, kind, action, action == PreparationAction.EditLyrics && s.HasLyrics ? "Revisar letra" : Label(action), canSearch, canRequest && !s.HasResult, canRequest && s.HasResult, canEdit, blockers);
        if (s.StorageError && !s.Initializing) return D(PreparationCause.Storage, "Error local", "No se pudo abrir el almacenamiento. Tus datos se conservan.", s.CanRecover ? PreparationAction.RecoverStorage : PreparationAction.RetryStorage, NoticeKind.Error);
        if (s.Initializing) return D(PreparationCause.Initializing, "Preparando…", "Preparando el almacenamiento local…", kind: NoticeKind.Progress);
        if (s.Stopped) return D(PreparationCause.Stopped, "Cerrando", "La aplicación está terminando sus operaciones.");
        if (s.Deleting) return D(PreparationCause.Initializing, "Eliminando…", "Eliminando contenido guardado en este dispositivo…", kind: NoticeKind.Progress);
        if (s.Analysing) return D(PreparationCause.Analysing, "Analizando…", "Analizando la letra… Puedes cancelar; una solicitud ya enviada puede consumir tokens.", PreparationAction.CancelAnalysis, NoticeKind.Progress);
        if (s.Searching) return D(PreparationCause.Searching, "Buscando…", "Buscando letra… No se ha solicitado IA.", PreparationAction.CancelSearch, NoticeKind.Progress);
        if (!s.HasTrack && s.TrackingPaused) return D(PreparationCause.NoTrack, "Sin canción mostrada",
            "No hay una canción mostrada. Reanuda el seguimiento para recuperar la última observación de Spotify.");
        if (!s.HasTrack) return s.Detection switch
        {
            ApplicationState.NoSpotify => D(PreparationCause.NoSpotify, "Sin Spotify", "Abre Spotify y reproduce una canción para empezar."),
            ApplicationState.Error => D(PreparationCause.Detection, "Detección fallida", "No se pudo leer la reproducción. La detección se revisa cada 5 segundos; si persiste, reinicia Spotify.", PreparationAction.Diagnostic, NoticeKind.Error),
            _ => D(PreparationCause.NoTrack, "Sin canción", "Spotify no ofrece título y artista para identificar esta reproducción. Elige una canción.", PreparationAction.Diagnostic)
        };
        if (s.Instrumental) return D(PreparationCause.Instrumental, "Instrumental", "Esta versión es instrumental y no contiene letra. Puedes revisar otra versión.", canEdit ? PreparationAction.ChooseVersion : PreparationAction.None);
        if (s.PendingCache) return D(PreparationCause.CachedLyrics, "Confirmar letra", "Hay letra guardada con metadatos incompletos. Confirma explícitamente que corresponde a esta canción.", canEdit ? PreparationAction.ConfirmCachedLyrics : PreparationAction.None, NoticeKind.Warning);
        if (content == ContentReadiness.Versions) return D(PreparationCause.Versions, "Elegir versión", $"Elige la letra correcta · {s.Candidates} versiones disponibles.", canEdit ? PreparationAction.ChooseVersion : PreparationAction.None, NoticeKind.Warning);
        if (!s.HasLyrics)
        {
            if (s.LyricsRetryAt > s.Now) return D(PreparationCause.LyricsRateLimited, "Espera del servicio", $"El servicio de letras pide esperar hasta {s.LyricsRetryAt.Value.ToLocalTime():dd/MM HH:mm:ss}. Después podrás reintentar manualmente.", canEdit ? PreparationAction.EditLyrics : PreparationAction.None, NoticeKind.Warning);
            if (s.LyricsFailure is { } failure) return D(failure switch { LyricsFailure.Offline => PreparationCause.LyricsOffline, LyricsFailure.Timeout => PreparationCause.LyricsTimeout, LyricsFailure.RateLimited => PreparationCause.LyricsRateLimited, LyricsFailure.InvalidResponse => PreparationCause.LyricsInvalid, _ => PreparationCause.LyricsError },
                "Revisar letra", failure switch { LyricsFailure.Offline => "Sin conexión con el servicio de letras. Comprueba la red y reintenta.", LyricsFailure.Timeout => "El servicio de letras agotó el plazo de 15 segundos. Puedes reintentar manualmente.", LyricsFailure.RateLimited => "Ha terminado la espera del servicio de letras. Ya puedes reintentar.", LyricsFailure.InvalidResponse => "La respuesta del servicio de letras tiene un formato inválido. Puedes reintentar o revisar el diagnóstico.", _ => "El servicio de letras devolvió un error. Puedes reintentar manualmente." }, canSearch ? PreparationAction.Search : PreparationAction.None, NoticeKind.Error);
            if (s.NotFound) return D(PreparationCause.NotFound, "Sin letra", "No encontramos letra para esta versión. Puedes introducirla manualmente; volver a buscar está en Más.", canEdit ? PreparationAction.EditLyrics : PreparationAction.None, NoticeKind.Warning);
            if (s.LyricsCancelled) return D(PreparationCause.LyricsCancelled, "Búsqueda cancelada", "Búsqueda cancelada. Puedes volver a buscar cuando quieras.", canSearch ? PreparationAction.Search : PreparationAction.None);
            return D(PreparationCause.Ready, "Canción detectada", "Busca la letra para preparar esta canción. Esta búsqueda no solicita IA.", canSearch ? PreparationAction.Search : PreparationAction.None);
        }
        if (s.HasResult) return D(PreparationCause.Result, "Listo", s.AiFailure is null ? "Resultado disponible. Puedes leerlo; regenerar exige una acción explícita." : "El resultado anterior sigue disponible. La última consulta falló; revisa el diagnóstico antes de reintentar.", s.AiFailure is null ? PreparationAction.None : PreparationAction.Diagnostic, s.AiFailure is null ? NoticeKind.Success : NoticeKind.Warning);
        if (s.AiFailure is { } error)
        {
            var recovery = error switch
            {
                AiFailure.DailyLimit => blockers.Any(b => b.Cause == PreparationCause.DailyLimit) ? PreparationAction.Diagnostic : PreparationAction.RefreshCounter,
                AiFailure.Authentication or AiFailure.SessionExpired or AiFailure.PlanSafetyUnverified => s.Mode == AiConnectionMode.ChatGptIncluded ? PreparationAction.OpenConnection : PreparationAction.OpenApi,
                AiFailure.IncompatibleModel or AiFailure.InvalidSettings or AiFailure.MissingKey or AiFailure.Disabled => s.Mode == AiConnectionMode.ChatGptIncluded ? PreparationAction.ChooseModel : PreparationAction.OpenApi,
                AiFailure.InvalidLyrics or AiFailure.UnknownLanguage or AiFailure.Refused => canEdit ? PreparationAction.EditLyrics : PreparationAction.Diagnostic,
                _ => blockers.FirstOrDefault()?.Action ?? (s.AiRetryAt > s.Now ? PreparationAction.Diagnostic : PreparationAction.Generate)
            };
            var message = AiSettingsViewModel.Describe(error) + " La letra se conserva.";
            if (s.AiRetryAt > s.Now) message += $" Espera hasta {s.AiRetryAt.Value.ToLocalTime():dd/MM HH:mm:ss}; no se reintentará automáticamente.";
            if (error == AiFailure.DailyLimit) message = blockers.FirstOrDefault(b => b.Cause == PreparationCause.DailyLimit)?.Message ?? message;
            return D(PreparationCause.AiFailed, "Análisis pendiente", message, recovery, NoticeKind.Error);
        }
        if (blockers.FirstOrDefault() is { } blocker) return D(blocker.Cause, blocker.Cause switch
            { PreparationCause.DailyLimit => "Límite local", PreparationCause.ModelMissing => "Modelo pendiente", PreparationCause.LanguageSaving => "Guardando idioma…",
                PreparationCause.LanguageFailed => "Idioma sin guardar", PreparationCause.SessionPending => "Conexión pendiente", PreparationCause.SessionMissing => "Sin sesión",
                PreparationCause.PlanPaused or PreparationCause.ApiDisabled => "Consultas pausadas", PreparationCause.CounterUnavailable => "Revisar contador", _ => "Letra lista" }, blocker.Message, blocker.Action,
            blocker.Cause == PreparationCause.LanguageSaving ? NoticeKind.Progress : NoticeKind.Warning);
        if (s.AiCancelled) return D(PreparationCause.AiCancelled, "Análisis cancelado", "Análisis cancelado. La letra se conserva; consulta el consumo comunicado antes de decidir otro intento.", PreparationAction.Generate);
        return D(PreparationCause.Ready, "Preparado", $"Puedes generar la traducción y explicación en {s.Language}. Solo se envía al pulsar Traducir y explicar.", PreparationAction.Generate, NoticeKind.Success);
    }
    private static string Label(PreparationAction action) => action switch
    {
        PreparationAction.Search => "Buscar letra", PreparationAction.CancelSearch => "Cancelar búsqueda", PreparationAction.CancelAnalysis => "Cancelar análisis",
        PreparationAction.ChooseVersion => "Elegir versión", PreparationAction.EditLyrics => "Añadir letra", PreparationAction.ConfirmCachedLyrics => "Usar letra guardada",
        PreparationAction.OpenConnection => "Revisar conexión", PreparationAction.ChooseModel => "Elegir modelo", PreparationAction.ReviewPlan => "Revisar habilitación", PreparationAction.OpenApi => "Configurar API",
        PreparationAction.RetryLanguage => "Reintentar guardado", PreparationAction.RefreshCounter => "Actualizar contador", PreparationAction.RetryStorage => "Reintentar almacenamiento", PreparationAction.RecoverStorage => "Recuperar base local",
        PreparationAction.Generate => "Traducir y explicar", PreparationAction.Regenerate => "Regenerar análisis", PreparationAction.Diagnostic => "Ver diagnóstico", _ => ""
    };
}
