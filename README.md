# SongSense

Aplicación personal para Windows para entender canciones. **F01–F04 y F07 cerradas; F05 y F06 implementadas, pendientes de prueba real de OpenAI:** detecta Spotify, consulta y corrige letras, traduce y explica con IA y conserva contenido en una caché local privada.

## Requisitos de desarrollo

- Windows 11 x64.
- SDK .NET **10.0.401**, fijado en `global.json`. [Instalación oficial](https://learn.microsoft.com/dotnet/core/install/windows).
- Acceso a NuGet para la primera restauración.

En esta máquina el SDK se preparó en `%LOCALAPPDATA%\SongSenseTools\dotnet`. Para usarlo desde una nueva sesión de PowerShell:

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\SongSenseTools\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
```

Estos cambios solo afectan a esa sesión. Si tienes el SDK exacto instalado y en PATH, no necesitas los dos primeros comandos.

Desde la raíz del repositorio:

```powershell
dotnet restore SongSense.sln --locked-mode
dotnet build SongSense.sln -c Release --no-restore
dotnet test SongSense.sln -c Release --no-build --no-restore
dotnet run --project src/SongSense.App -c Release --no-build --no-restore
```

Las dependencias directas tienen versiones explícitas y cada proyecto conserva `packages.lock.json` con las dependencias transitivas. Los cambios deliberados de dependencias requieren regenerar y revisar estos archivos. La prueba de conexión y Traducir y explicar son solicitudes explícitas que pueden generar coste.

## Estructura

- `SongSense.App`: WPF, composición de servicios y MVVM; ventana oscura, estados y pestañas.
- `SongSense.Core`: modelos, interfaces cancelables y etiquetas de estados en español.
- `SongSense.Infrastructure`: SQLite, detección con Windows Media Control, LRCLIB, DPAPI y prueba de conexión a OpenAI.
- `SongSense.Tests`: pruebas de almacenamiento, detección, consultas, edición, cifrado, configuración y límites de IA.

## Configurar IA de forma local

**F08 parcialmente implementada (7 de octubre de 2026):** abre **Cuenta y consumo → Continuar con ChatGPT**. Completa el acceso oficial en tu navegador eligiendo la misma cuenta que utilizas en Codex y comprueba el correo mostrado. Song Sense no puede identificar automáticamente la cuenta de Codex ni importa sus credenciales. Puedes conectar/desconectar, cambiar entre registros y consultar los modelos autorizados. El primer inicio utiliza **ChatGPT · solo uso incluido**, incluso si había una API key guardada.

**Las consultas con el plan ChatGPT están bloqueadas.** La documentación oficial explica el permiso de créditos adicionales en Ajustes → Uso, pero no hemos encontrado una interfaz pública documentada para verificar que está desactivado o imponer solo uso incluido por petición. Una casilla local, un login correcto o un listado de modelos no acreditan esa restricción. No se envían generaciones con el plan ni se pasa a pago automáticamente. F08 no se considera cerrada; falta verificar la política y ejecutar login/logout y análisis reales. [Contrato y pendientes](docs/RUTA_PROYECTO.md#f08--cuenta-chatgpt-plus-y-uso-incluido-sin-cargos-extra).

Las credenciales OAuth y los registros de cuenta se guardan con DPAPI `CurrentUser`, reemplazo atómico y permisos exclusivos del usuario en `%LOCALAPPDATA%\SongSense\ChatGptAuth\session.dpapi`, fuera de SQLite y Git. No se solicitan contraseñas en la app. Las renovaciones se serializan entre procesos; se guarda el refresh token rotado antes de utilizarlo. Desconectar intenta revocar la sesión en OpenAI y elimina los tokens locales; si la revocación remota falla, se indica que debes desconectar la app en ChatGPT. Se conserva la identidad del registro para reconectar. La protección local no impide el acceso de otros procesos que operen como tu mismo usuario de Windows; no hay una garantía de seguridad del 100 %.

Cada análisis y prueba de conexión de API muestra **tokens de entrada, salida y total comunicados por OpenAI**. Un fallo con consumo comunicado también lo muestra. Si faltan datos, se indica **Consumo no comunicado**, nunca 0; las lecturas de caché muestran **0 tokens nuevos**. No se calcula un porcentaje del plan a partir de tokens: sus límites son compartidos y el proveedor no comunica esa equivalencia. **Gestionar uso en ChatGPT** abre el panel oficial. No se almacenan tokens de autenticación en las claves de caché; los análisis se separan por modo y registro de cuenta mediante una identidad hash.

Para usar API de pago, elige explícitamente **Cuenta y consumo → Elegir API key · de pago → Ajustes de API key · de pago**. Introduce tu clave únicamente en el campo enmascarado de la app, el identificador exacto del modelo y un límite entero entre 1 y 100 solicitudes por día. Lee el aviso antes de activar IA y guarda. Déjala desactivada si todavía no quieres usarla. Abrir o guardar ajustes no hace llamadas de red. No hay un modelo predeterminado ni sustitución automática si el modelo falla. Elegir API implica facturación de API; conectar ChatGPT no autoriza ese modo.

El campo vacío conserva la clave guardada; una clave nueva la reemplaza. **Eliminar clave y desactivar IA** elimina el archivo cifrado y conserva preferencias y contadores. La clave nunca se precarga en el campo ni se guarda en SQLite. Se usa DPAPI `CurrentUser` en `%LOCALAPPDATA%\SongSense\credentials.dpapi`, con temporales también cifrados y reemplazo atómico. DPAPI vincula el descifrado al usuario de Windows; un proceso con acceso a esa misma cuenta puede acceder a la clave. Durante la autenticación HTTP hay una representación transitoria en memoria.

**Probar conexión · de pago** usa los ajustes ya guardados y envía solo un texto fijo a `https://api.openai.com/v1/responses`, sin canciones ni letras. Valida una salida JSON estricta y puede generar coste según modelo y tokens. Tiene timeout de 60 segundos, sin reintentos automáticos, redirecciones, herramientas, audio ni búsqueda web. El parámetro `store: false` evita solicitar almacenamiento de la respuesta para recuperación; **no garantiza retención cero** del proveedor. Consulta la [documentación oficial sobre datos](https://developers.openai.com/api/docs/guides/your-data) y [salidas estructuradas](https://developers.openai.com/api/docs/guides/structured-outputs).

El contador se reserva en SQLite antes de enviar. Los intentos reservados cuentan aunque fallen o se cancelen, y sobreviven a reinicios, cambios de modelo o eliminación de clave. La fecha es la local de Windows. El límite inicial es 20 solicitudes y se puede aumentar explícitamente hasta 100; cuenta solicitudes, no euros. Una operación de IA a la vez, incluso entre instancias de la app: el archivo local `ai-operation.lock` se mantiene abierto en exclusiva y se libera al terminar o salir del proceso.

Sin una clave y un modelo suministrados para este proyecto, F05 y F06 quedan pendientes de validación real. Las pruebas automatizadas usan un transporte HTTP simulado y credenciales sintéticas, sin llamadas de pago.

## Traducir y explicar

Con la letra confirmada y la IA activada, pulsa **Traducir y explicar**. Una sola solicitud obtiene idioma, traducción y significado. Se envía únicamente la letra numerada, sin título, artista, álbum ni audio. Comparte el límite diario y el bloqueo de la prueba de conexión. Hay un máximo de 16.384 tokens de salida por análisis; el coste depende del modelo y de los tokens de entrada/salida. El contador de solicitudes no limita euros.

La pestaña Letra conserva estrofas, repeticiones y orden. Si es español, muestra **La letra ya está en español**; si es mixta, conserva literalmente las líneas identificadas como españolas y traduce las demás. Significado muestra resumen, temas, metáforas, alternativas y avisos con la etiqueta **Interpretación generada por IA**. El formato y los IDs se validan localmente; eso no garantiza la exactitud lingüística o de la interpretación.

Una respuesta rechazada, incompleta, de idioma desconocido o inválida no muestra resultados parciales ni se reintenta automáticamente. Puedes reintentar explícitamente si queda cuota. Cancelar puede seguir contando y generar coste. Cambiar canción, letra o modelo descarta el resultado mostrado; pausar lo conserva. Desde F07 se guarda el contenido válido en una caché local. El flujo automático corresponde a F09.

## Privacidad y Git

Las credenciales, SQLite y la copia del escritorio viven fuera del repositorio. `.gitignore` excluye bases, credenciales, certificados, dumps, logs, variables de entorno, accesos directos y carpetas de trabajo/artifacts. La app no registra claves, cabeceras de autenticación, letras ni cuerpos de error de OpenAI; los mensajes de fallo son fijos.

Antes de publicar, ejecuta:

```powershell
./scripts/Test-GitPrivacy.ps1
```

El auditor revisa archivos que pueden entrar en Git, contenido del índice, blobs del historial de todas las referencias locales y reglas de exclusión. Si detecta patrones de credenciales o rutas privadas, falla e informa de la ruta y categoría, sin imprimir el valor encontrado. Complementa la revisión manual: no puede reconocer cualquier dato personal ni impedir por sí solo un `git add -f`. No pegues claves en el chat, el código, el campo de modelo o archivos del repositorio.

## Buscar y corregir una letra

Abre Spotify y selecciona una canción. Pulsa **Buscar letra** para consultar LRCLIB. Si hay varias versiones o los metadatos no permiten confirmar una coincidencia, usa **Cambiar letra → Versiones**: revisa título, artista, álbum, duración y vista previa, y pulsa **Confirmar versión**. La selección no modifica la canción de Spotify.

En **Cambiar letra → Manual** puedes introducir o corregir el texto, incluso sin resultados de LRCLIB. **Guardar letra** exige contenido no vacío y hasta 20.000 caracteres; los textos mayores se rechazan sin recortarlos. La ventana principal muestra la fuente Manual o LRCLIB y su ID. Si cambian la canción, la búsqueda o la letra mientras está abierto el editor, debes volver a abrirlo antes de guardar.

Las letras confirmadas y selecciones se guardan localmente desde F07. Cambiar de canción limpia la pantalla antes de buscar contenido local compatible. La pausa conserva la letra. No se suben correcciones a LRCLIB. Cada confirmación cambia la revisión de letra para descartar los análisis de IA anteriores.

## Caché local

La base local guarda letras, origen/ID de la versión elegida y resultados completos en **texto legible**, fuera del repositorio. Se recuperan al reiniciar o cambiar de canción, sin llamadas de red o IA. Buscar letra consulta primero la caché; una ausencia caduca una hora después de la consulta original. Las letras y asociaciones caducan a los 30 días; leerlas no renueva esa caducidad. Si faltan álbum o duración, **Usar letra guardada** exige confirmación explícita antes de mostrar ese contenido.

El análisis solo se reutiliza con la misma letra (normalizando únicamente saltos de línea), idioma destino, proveedor, modelo y versión de prompt/esquema. La firma distingue álbum, duración y etiquetas de versión. Los resultados no caducan por tiempo mientras estén asociados a contenido conservado. La caché admite 200 canciones y expulsa la menos recientemente usada; limpia análisis sin asociaciones.

**Regenerar** hace una nueva solicitud de pago explícita y reemplaza el análisis solo si termina correctamente. **Borrar esta canción** elimina su asociación y sus análisis si no los comparte otra canción. **Borrar caché** elimina todo el contenido; ambas acciones conservan clave, ajustes y contador diario. El borrado es lógico en SQLite: no garantiza borrar restos físicos o copias de recuperación.

Si la base está corrupta, la app muestra un diagnóstico y ofrece **Recuperar base local** con confirmación. Cierra otras instancias antes. Se conserva el original y sus archivos auxiliares en una carpeta local `recovery-*`, y se crea una base limpia con la IA desactivada. Se rescatan preferencias y contadores cuando son legibles; si los contadores no se pueden leer, el original se conserva y se bloquean las solicitudes del día. No se reconstruye ni publica una caché dañada automáticamente. Las copias conservadas pueden contener tus letras y análisis.

## Flujo automático (F09)

**Actualizar automáticamente** está activado inicialmente y su preferencia se conserva al cerrar. Tras 1,5 segundos de estabilidad de la canción, recupera la caché, busca la letra y genera el análisis cuando la configuración, la conexión y el límite diario lo permiten. Una versión ambigua exige confirmación; una canción instrumental no consulta IA. La activación de IA es independiente de este interruptor.

Cambiar de canción cancela el contexto anterior y descarta sus resultados. Si una consulta ya enviada tarda en finalizar, el flujo espera y conserva únicamente la canción pendiente más reciente. Cancelar no garantiza evitar el consumo de una consulta ya enviada. Pausar y reanudar Spotify no repite el análisis.

Desmarcar el interruptor congela el contenido con **Actualización pausada**; reactivarlo procesa la canción actual de Spotify. Un fallo requiere **Reintentar flujo**, un reintento manual o salir de esa canción y volver. No hay reintentos de IA automáticos. La caché muestra **0 tokens nuevos**; las consultas muestran el consumo comunicado por el proveedor o indican que no está disponible.

El modo ChatGPT conserva el bloqueo de F08 y nunca pasa automáticamente a la API de pago. Esta entrega no resuelve el permiso de tu cuenta ni permite generar con el plan mientras no pueda verificarse la política de uso incluido.

## Almacenamiento

La primera apertura crea `%LOCALAPPDATA%\SongSense\songsense.db`. SQLite usa `PRAGMA user_version = 3`; las migraciones añaden las tablas de caché y la preferencia de actualización automática conservando ajustes y contadores. La IA comienza desactivada, sin modelo, con límite inicial de 20 solicitudes.

La base almacena contenido desde F07, pero nunca claves. El contrato de configuración solo permite preferencias no secretas. Una base de una versión más reciente se rechaza sin modificarla. Si falla el acceso, la ventana muestra Error y permite reintentar; la recuperación de corrupción requiere una acción explícita y conserva una copia local del original.

La inicialización se ejecuta fuera del hilo de interfaz. Cerrar la ventana termina la aplicación; la bandeja del sistema corresponde a F10.

## Identidad y acceso de escritorio

Song Sense usa dos claves de sol para las S del nombre, dorado sobre carbón cálido y una interfaz centrada en la lectura. La decisión y los detalles están en [docs/DISENO.md](docs/DISENO.md). La ventana se adapta a anchuras inferiores a 700 DIP y alturas inferiores a 700 DIP; original y traducción se apilan en ventanas estrechas.

El icono está en `src/SongSense.App/Assets`. Para actualizar la copia autocontenida del escritorio después de cambiar el código, cierra la app y ejecuta:

```powershell
.\scripts\Update-Desktop.ps1
```

El script compila la app, actualiza el acceso de escritorio y el icono del anclaje existente. Si aún no está anclada, usa el menú de Windows **Mostrar más opciones → Anclar a la barra de tareas** sobre el acceso. Esta copia es local; no es una publicación ni el cierre de F11.

## Ruta y evidencia del desarrollo

El alcance de cada entrega está en [docs/RUTA_PROYECTO.md](docs/RUTA_PROYECTO.md). El estado y las comprobaciones están en [docs/ESTADO_FEATURES.md](docs/ESTADO_FEATURES.md). Se desarrolla una feature por petición, sin commits ni publicación implícitos.
