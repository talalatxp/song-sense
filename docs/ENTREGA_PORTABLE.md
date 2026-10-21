# Song Sense — entrega personal F11

Windows x64. Extrae **todo el ZIP** a una carpeta de tu elección y abre `SongSense.exe`. No lo ejecutes dentro del ZIP ni copies solamente el EXE. Incluye el runtime de .NET y WPF; no necesita instalar el SDK. Para un acceso sencillo, crea un acceso directo al EXE desde el Explorador. Conserva la carpeta mientras uses ese acceso.

Esta entrega está **implementada pendiente de aceptación real**. Consulta `VALIDACION.md`. El ZIP F11 del 8 de octubre conserva el bloqueo de generación ChatGPT. En compilaciones posteriores al 9 de octubre, la generación está implementada y se habilita por sesión según las instrucciones del README, con confirmación manual de créditos desactivados. No se acredita verificación automática de ese ajuste ni aceptación real de inferencia.

## Primera apertura y uso

1. Si hay otra copia abierta, pulsa **Salir** en ella; cerrar la ventana la deja en la bandeja. Una segunda apertura activa la primera instancia.
2. Abre Spotify para Windows y reproduce una canción. Song Sense observa la sesión multimedia publicada por Windows; no solicita tu contraseña de Spotify ni controla la reproducción. Si falta la sesión, comprueba que usas el cliente de Windows y reinicia Spotify.
3. **Actualizar automáticamente** espera 1,5 segundos de estabilidad y recupera resultados guardados o busca la letra en LRCLIB. Una coincidencia ambigua requiere selección y confirmación. Puedes aportar una letra manual propia. Sin letra o con una instrumental no se analiza.
4. La IA comienza desactivada en una instalación nueva. Antes de activarla, revisa **Cuenta y consumo**, modelo y límite diario en **Ajustes de IA**. Una instalación existente conserva sus preferencias.
5. **Traducción** organiza original y español por estrofas. En español conserva solo el original. **Significado** muestra una interpretación tentativa; no verifica intenciones del artista ni contexto histórico.

Cerrar o Alt+F4 oculta la ventana; **Salir** termina el proceso. El icono dorado de la bandeja ofrece Abrir, Pausar/Reanudar y Salir. No se inicia con Windows. Tamaño de texto, pestaña, posición y Siempre encima se guardan localmente.

## Cuenta, consultas y costes

El inicio de sesión y el permiso para consumir el plan son distintos. El uso autorizado de otras apps comparte límites del plan. ChatGPT permite créditos adicionales mediante un ajuste de Uso. Consulta los [ajustes y permisos oficiales](https://learn.chatgpt.com/docs/sign-in-with-chatgpt). Esta versión de Song Sense no puede verificar de forma suficiente la política de uso exclusivamente incluido: **bloquea las generaciones con ChatGPT, incluso si hay sesión**, y no cambia automáticamente a una API de pago. No necesitas mejorar el plan para abrir la aplicación; hacerlo no demuestra que se resuelva este bloqueo.

La alternativa **API de pago** exige elegirla expresamente, proporcionar tu propia clave y seleccionar un modelo compatible. La [API se factura por separado](https://help.openai.com/es-es/articles/9039756-managing-billing-for-chatgpt-and-the-api-platform); no equivale a utilizar la suscripción de ChatGPT. No hay clave incluida. No actives esta alternativa si no quieres cargos. La aplicación no obtiene credenciales de Codex ni puede garantizar que la cuenta OAuth elegida sea la misma de este chat: debes comprobarla en la pantalla oficial. La clave se protege mediante DPAPI para tu usuario de Windows.

Cada análisis muestra tokens de entrada, salida y total cuando el proveedor los comunica; en otro caso indica consumo no disponible. Un resultado de caché muestra **0 tokens nuevos**. Los tokens no se convierten a un porcentaje del plan. El límite diario local cuenta solicitudes y es una protección adicional, no un límite monetario ni la cuota oficial. Una petición enviada puede consumir aunque canceles o cambies de canción. No hay reintentos de IA automáticos.

## Datos locales y privacidad

Los ejecutables son portables; **los datos personales no viajan en el ZIP**. Todas las copias ejecutadas por el mismo usuario usan `%LOCALAPPDATA%\SongSense`:

- `songsense.db` y sus auxiliares: preferencias, contadores, letras y análisis guardados.
- Archivos protegidos con DPAPI: credenciales, sesión y registro OAuth pendiente. No son transferibles de forma segura a otro usuario o equipo.
- `window-preferences.json`: preferencias de ventana. Carpetas `recovery-*`: originales conservados tras recuperación de corrupción; pueden contener tus letras.

Usa las opciones de caché para borrar una canción o todos los resultados. Desconecta la cuenta o elimina la clave desde sus ajustes. Borrar la caché conserva los contadores de seguridad y la configuración. Para quitar todos los datos, primero pulsa Salir y elimina la carpeta local; también perderás contadores, ajustes y copias de recuperación. No publiques ni compartas esa carpeta.

LRCLIB recibe los metadatos necesarios para buscar la canción. En API de pago, OpenAI recibe la letra confirmada y contexto para analizarla. El ZIP no contiene tu base, claves ni sesiones. La auditoría combina una lista de archivos permitidos, búsqueda de patrones y hashes: no supone una garantía absoluta de seguridad.

## Problemas y comprobación de integridad

| Situación | Acción |
|---|---|
| Sin Spotify | Reproduce en el cliente Windows; revisa sesión multimedia y reinicia Spotify. |
| Sin red, canción guardada | Se puede recuperar su resultado compatible sin consulta nueva. |
| Sin red, canción nueva | Comprueba la conexión y reintenta explícitamente; no se inventa un resultado. |
| Sin letra / varias versiones | Aporta una letra manual propia o confirma la versión correcta. |
| Conexión incompleta / mejorar plan | Vuelve a Cuenta y consumo para diagnóstico. No repitas altas desde el navegador. El bloqueo F08 sigue vigente. |
| Clave API inválida / cuota / tiempo agotado | Revisa la configuración elegida y reintenta explícitamente cuando corresponda. No hay fallback. |
| Almacenamiento dañado | Recuperación explícita conserva el original; si no se recupera el contador, bloquea consultas del día. |
| EXE no abre | Extrae toda la carpeta, usa Windows x64 y consulta el aviso de Windows. No desactives sus protecciones. La entrega no está firmada. |

Compara `Get-FileHash -Algorithm SHA256` sobre el ZIP con el archivo `.zip.sha256` que acompaña la entrega. Un hash detecta cambios respecto a esa copia; no demuestra por sí solo el origen. `release-manifest.json` enumera archivos y hashes internos. Para volver a generar la entrega, ejecuta en el repositorio con `./scripts/Build-Portable.ps1`; no registra claves ni copia datos de usuario. El diseño autocontenido sigue la [documentación de dotnet publish](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish).
