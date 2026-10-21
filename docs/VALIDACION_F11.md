# F11 — evidencia y límites de aceptación

Estado: **implementada pendiente de aceptación real**. Fecha: 8 de octubre de 2026. Las comprobaciones controladas usan letras propias, transportes simulados y bases temporales; no consumen API de pago. Las filas reales pendientes impiden declarar cerrada F11.

La suite Release pasó con **213 pruebas, cero fallos y cero omitidas**. El auditor aceptó dos fixtures válidos y rechazó siete: base personal, credencial simulada, ruta de perfil, hash alterado, recorrido de directorios, duplicado y símbolos. Se comprobó la carpeta publicada y su ZIP. NuGet no informó vulnerabilidades conocidas para las dependencias de la aplicación en la revisión de su fuente oficial; ese resultado no garantiza ausencia de vulnerabilidades.

## Matriz de aceptación

| Caso | Evidencia controlada | Evidencia real / pendiente |
|---|---|---|
| Inglés: original, traducción y significado | InsightTests, DesktopExperienceTests; validación estructurada y estrofas | Consulta real pendiente F05/F06/F08. |
| Español sin traducción duplicada | InsightTests, DesktopExperienceTests | Canción real y análisis pendiente. |
| Letra mixta, español conservado | InsightTests, DesktopExperienceTests | Canción real pendiente. |
| Instrumental / ausencia de letra | LrclibProviderTests, AutomaticWorkflowTests: ninguna generación | Spotify real pendiente. |
| Versión ambigua | LyricsResolutionTests, CacheTests: confirmación obligatoria | Selección real pendiente. |
| Cambios rápidos, cancelación y resultado antiguo | AutomaticWorkflowTests: solo última revisión, sin cola ni reintento automático | Secuencia real Spotify pendiente F09. |
| Resultado guardado sin red; canción nueva sin red | CacheTests, LrclibProviderTests y ReleaseAcceptanceTests con SQLite real y proveedor desconectado | Red físicamente desconectada + Spotify pendiente. |
| Límite diario agotado | AiConfigurationTests, AutomaticWorkflowTests: bloqueo sin envío | Agotamiento real deliberado no realizado. |
| Cuota del plan / créditos adicionales desactivados | ChatGptTests: política no verificada bloquea antes del transporte, sin fallback | Transporte del plan bloqueado F08; no se afirma validar cuota real. |
| Sesión ChatGPT revocada / renovación | ChatGptTests: rechazo, limpieza local y persistencia protegida | Sesión válida real no disponible; pendiente. |
| Clave API inválida / errores de proveedor | InsightTests, AiConfigurationTests: errores controlados, no autorreintento | No se utiliza ni solicita clave real para la entrega. |
| Reinicio / pérdida de sesión de Spotify | SpotifyDetectorTests, SpotifyRulesTests | Reinicio real y reaparición de sesión pendiente. |
| Caché, borrado y recuperación | CacheTests, StorageTests: transacciones, versiones, corrupción y bloqueo de presupuesto desconocido | Recuperación real de una base personal no provocada. |
| Windows, lectura, teclado y bandeja | DesktopExperienceTests; revisión F10 a 100 % y tamaño mínimo documentada en ESTADO_FEATURES.md | 150 %, desconexión de monitores y menú nativo de bandeja pendientes F10. |
| Portable fuera del repositorio | Publicación Release autocontenida, hashes, auditoría de carpeta y ZIP | Resultado de apertura del ZIP se registra en ESTADO_FEATURES.md tras la comprobación. |

## Protocolo de entrega

`Build-Portable.ps1` restaura dependencias bloqueadas, ejecuta toda la suite Release, audita archivos candidatos a Git, índice e historial y publica en una carpeta nueva. Incluye documentación y avisos de terceros; excluye símbolos y fuentes. Audita la carpeta, crea un ZIP con una sola raíz, vuelve a auditar el ZIP y genera SHA256. No incluye datos de LocalAppData ni cambia la copia instalada. Las pruebas negativas del auditor comprueban rechazo de datos privados, credenciales simuladas, alteraciones, rutas inseguras y entradas duplicadas.

El manifiesto acredita el contenido creado y comprobado; no prueba una conexión real ni representa una firma del editor. Los resultados concretos, número de pruebas, revisión de vulnerabilidades y apertura en Windows se registran en `docs/ESTADO_FEATURES.md` del repositorio. Conserva también el SHA256 externo de tu entrega.

## Pendientes que se conservan

- F05/F06: consulta real con modelo y acceso autorizado; traducción y explicación reales.
- F08: permiso de cuenta y política oficial verificable de uso exclusivamente incluido; transporte ChatGPT no implementado para generación. El modo queda bloqueado y no tiene fallback.
- F09: matriz y flujo con Spotify real.
- F10: pruebas visuales de configuraciones no disponibles en la revisión anterior.
- F11: aceptación real completa de inglés, español, repetición sin consumo, sesión Spotify y fallos de servicios. Compilar, auditar y abrir el ZIP no sustituye estas pruebas.
