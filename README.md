# SongSense

Aplicación personal para Windows para entender canciones. **F01–F04 cerradas; F05 y F06 implementadas, pendientes de prueba real de OpenAI:** detecta Spotify, consulta y corrige letras, y permite traducirlas y explicar su significado con configuración segura de IA.

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

Abre **Ajustes de IA** en la parte inferior de la ventana. Introduce tu clave únicamente en el campo enmascarado de la app, el identificador exacto del modelo y un límite entero entre 1 y 100 solicitudes por día. Lee el aviso antes de activar IA y guarda. Déjala desactivada si todavía no quieres usarla. Abrir o guardar ajustes no hace llamadas de red. No hay un modelo predeterminado ni sustitución automática si el modelo falla.

El campo vacío conserva la clave guardada; una clave nueva la reemplaza. **Eliminar clave y desactivar IA** elimina el archivo cifrado y conserva preferencias y contadores. La clave nunca se precarga en el campo ni se guarda en SQLite. Se usa DPAPI `CurrentUser` en `%LOCALAPPDATA%\SongSense\credentials.dpapi`, con temporales también cifrados y reemplazo atómico. DPAPI vincula el descifrado al usuario de Windows; un proceso con acceso a esa misma cuenta puede acceder a la clave. Durante la autenticación HTTP hay una representación transitoria en memoria.

**Probar conexión · de pago** usa los ajustes ya guardados y envía solo un texto fijo a `https://api.openai.com/v1/responses`, sin canciones ni letras. Valida una salida JSON estricta y puede generar coste según modelo y tokens. Tiene timeout de 60 segundos, sin reintentos automáticos, redirecciones, herramientas, audio ni búsqueda web. El parámetro `store: false` evita solicitar almacenamiento de la respuesta para recuperación; **no garantiza retención cero** del proveedor. Consulta la [documentación oficial sobre datos](https://developers.openai.com/api/docs/guides/your-data) y [salidas estructuradas](https://developers.openai.com/api/docs/guides/structured-outputs).

El contador se reserva en SQLite antes de enviar. Los intentos reservados cuentan aunque fallen o se cancelen, y sobreviven a reinicios, cambios de modelo o eliminación de clave. La fecha es la local de Windows. El límite inicial es 20 solicitudes y se puede aumentar explícitamente hasta 100; cuenta solicitudes, no euros. Una operación de IA a la vez, incluso entre instancias de la app: el archivo local `ai-operation.lock` se mantiene abierto en exclusiva y se libera al terminar o salir del proceso.

Sin una clave y un modelo suministrados para este proyecto, F05 y F06 quedan pendientes de validación real. Las pruebas automatizadas usan un transporte HTTP simulado y credenciales sintéticas, sin llamadas de pago.

## Traducir y explicar

Con la letra confirmada y la IA activada, pulsa **Traducir y explicar**. Una sola solicitud obtiene idioma, traducción y significado. Se envía únicamente la letra numerada, sin título, artista, álbum ni audio. Comparte el límite diario y el bloqueo de la prueba de conexión. Hay un máximo de 16.384 tokens de salida por análisis; el coste depende del modelo y de los tokens de entrada/salida. El contador de solicitudes no limita euros.

La pestaña Letra conserva estrofas, repeticiones y orden. Si es español, muestra **La letra ya está en español**; si es mixta, conserva literalmente las líneas identificadas como españolas y traduce las demás. Significado muestra resumen, temas, metáforas, alternativas y avisos con la etiqueta **Interpretación generada por IA**. El formato y los IDs se validan localmente; eso no garantiza la exactitud lingüística o de la interpretación.

Una respuesta rechazada, incompleta, de idioma desconocido o inválida no muestra resultados parciales ni se reintenta automáticamente. Puedes reintentar explícitamente si queda cuota. Cancelar puede seguir contando y generar coste. Cambiar canción, letra o modelo descarta el resultado anterior; pausar conserva el análisis. Letras y resultados solo viven en memoria, sin caché ni guardado en esta feature. El flujo automático corresponde a F08.

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

Las letras y selecciones viven en memoria y se limpian al cambiar de canción o cerrar la app. La pausa conserva la letra. No se suben correcciones a LRCLIB. Cada confirmación cambia la revisión de letra para descartar los análisis de IA anteriores.

## Almacenamiento

La primera apertura crea `%LOCALAPPDATA%\SongSense\songsense.db`. SQLite usa `PRAGMA user_version = 1`; solo hay tablas `app_settings` y `daily_request_counts`. La IA comienza desactivada, sin modelo, con límite inicial de 20 solicitudes. F05 utiliza el contador existente sin migrar el esquema.

La base no almacena claves, letras ni resultados hasta F06. El contrato de configuración solo permite preferencias no secretas. Una base de una versión más reciente se rechaza sin modificarla. Si falla el acceso, la ventana muestra Error y permite reintentar tras corregir el acceso o usar una versión compatible; nunca borra la base para recuperarse.

La inicialización se ejecuta fuera del hilo de interfaz. Cerrar la ventana termina la aplicación; la bandeja del sistema corresponde a F09.

## Identidad y acceso de escritorio

Song Sense usa dos claves de sol para las S del nombre, dorado sobre carbón cálido y una interfaz centrada en la lectura. La decisión y los detalles están en [docs/DISENO.md](docs/DISENO.md). La ventana se adapta a anchuras inferiores a 700 DIP y alturas inferiores a 700 DIP; original y traducción se apilan en ventanas estrechas.

El icono está en `src/SongSense.App/Assets`. Para actualizar la copia autocontenida del escritorio después de cambiar el código, cierra la app y ejecuta:

```powershell
.\scripts\Update-Desktop.ps1
```

El script compila la app, actualiza el acceso de escritorio y el icono del anclaje existente. Si aún no está anclada, usa el menú de Windows **Mostrar más opciones → Anclar a la barra de tareas** sobre el acceso. Esta copia es local; no es una publicación ni el cierre de F10.

## Ruta y evidencia del desarrollo

El alcance de cada entrega está en [docs/RUTA_PROYECTO.md](docs/RUTA_PROYECTO.md). El estado y las comprobaciones están en [docs/ESTADO_FEATURES.md](docs/ESTADO_FEATURES.md). Se desarrolla una feature por petición, sin commits ni publicación implícitos.
