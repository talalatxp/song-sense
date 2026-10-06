# Estado por feature

Actualizado: 6 de octubre de 2026.

## Identidad visual y acceso de Windows — 6 de octubre de 2026

Cambio solicitado antes de F05: opción A, dos claves de sol y estilo dorado. Tema compartido, lectura con layout adaptable, icono integrado, acceso de escritorio autocontenido y anclaje a barra de tareas. Detalles y validación en [DISENO.md](DISENO.md). No cierra F09 ni F10 y no implementa IA. Las cuatro primeras features conservan su estado.

| Feature | Estado |
|---|---|
| F01 — Base ejecutable | Cerrada |
| F02 — Canción actual de Spotify | Cerrada |
| F03 — Consulta de letras a LRCLIB | Cerrada |
| F04 — Selección y corrección de letra | Cerrada |
| F05 — Configuración segura de IA | Implementada; pendiente de conexión real |
| F06 — Traducción y significado | Implementada; pendiente de prueba real de OpenAI |
| F07 — Caché local | Pendiente |
| F08 — Flujo automático | Pendiente |
| F09 — Ventana de uso diario | Pendiente |
| F10 — Validación y entrega | Pendiente |

## F01 — Implementación

- Solución con App, Core, Infrastructure y Tests; SDK 10.0.401 y dependencias fijadas con archivos de bloqueo.
- Ventana WPF oscura de 480 × 720 DIP, mínimo 360 × 480, título, artista, estado y pestañas Traducción/Significado con scroll y foco visible.
- Contratos `CurrentTrack`, `LyricsCandidate`, `ResolvedLyrics`, `SongInsight`; interfaces para detección, letras, IA y configuración. Revisiones de canción y tokens de cancelación en los contratos. No hay implementaciones ficticias de proveedores.
- SQLite con migración transaccional, versión 1, configuración y contador diario. Sin tablas de contenido. Inicialización repetible, protección ante versión futura y reintento ante error.
- Archivos: solución, configuración de SDK/build/NuGet, `.gitignore`, proyectos y código en `src/`, pruebas en `tests/`, README y esta evidencia.

## F01 — Validación

Pruebas ejecutadas realmente en Windows el 5 de octubre de 2026:

- `dotnet restore SongSense.sln`: correcto; archivos de bloqueo generados.
- `dotnet build SongSense.sln -c Release --no-restore`: correcto, 0 advertencias y 0 errores.
- `dotnet test SongSense.sln -c Release --no-build --no-restore`: **7 pruebas correctas**, 0 fallos, 0 omitidas.
- Las pruebas usan bases temporales: esquema inicial y valores seguros, conservación de ajustes y contador tras dos reinicializaciones, rechazo de esquema futuro sin perder configuración, límites inválidos (dos casos), cancelación y etiquetas de todos los estados.
- Ejecución real de la app Release: ventana abierta y almacenamiento listo; se creó una base de 16.384 bytes en la ruta local de SongSense.
- Inspección visual de ambas pestañas; cambio con ratón y teclado; reducción de ancho y altura al mínimo. Los textos se ajustan y el contenido de las pestañas permanece accesible mediante scroll.
- Cierre mediante el botón de Windows: desapareció la ventana y terminó el proceso. Segunda apertura con la base existente: ventana y almacenamiento listos, sin errores visibles.
- Persistencia de valores modificados verificada automáticamente, sin introducir datos de prueba en la base de uso de la app.

Limitaciones: F01 valida la base ejecutable. No prueba Spotify, LRCLIB, IA, bandeja ni entrega portable. Las escalas 100 %/150 % y escenarios de varios monitores se validarán en F09; no se declaran probados aquí.

## F02 — Implementación y evidencia

- Detección por `Windows.Media.Control`, filtrando identificadores exactos de Spotify; eventos de sesiones, metadatos, reproducción y timeline, más recuperación cada 5 segundos. Lecturas serializadas, señales coalescidas, cancelación y liberación de suscripciones al cerrar.
- Selección de sesión reproduciendo; empates conservan la anterior y, si no existe, usan orden ordinal. Revisión monotónica por canción; pausa/reanudación no la incrementan. Metadatos incompletos o contenido no musical no producen una canción elegible.
- Ventana enlazada a título, artista, álbum, duración y estado reales. Duración ausente: desconocida. Identificador observado: `SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify`.
- Build Release: 0 errores y advertencias. 27 pruebas correctas (incluyendo F01): identificadores, prioridad, empates, revisión, ausencia de canción, duración desconocida, errores, lecturas obsoletas, polling y cierre durante lectura.
- Pruebas reales del 5 de octubre: SongSense abierto después de Spotify; dos cambios (`Nunca Estoy` → `Tú Me Dejaste De Querer` → `Tiranosaurius Rex`), detectados por el mismo servicio en aproximadamente 0,33 y 0,52 segundos. Interfaz revisada con estos metadatos. Pausa y reanudación mantienen revisión.
- Cierre de Spotify mediante Archivo → Salir: SongSense mostró Sin Spotify y limpió título/artista/álbum/duración. Reapertura de Spotify con SongSense funcionando: reconexión sin reiniciar y canción `El Fin del Mundo`, La La Love You, álbum `La la Love You Bonus`, duración 3:08. Esto cubre Spotify iniciado después de SongSense.
- Vídeo real reproducido en Reproductor multimedia de Windows mientras Spotify reproducía. El servicio observó sesiones de Chrome, Spotify y `Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic`; SongSense siguió mostrando la canción de Spotify. El reproductor de prueba se cerró después.
- La comprobación con Chrome se interrumpió en el turno anterior por imposibilidad del controlador de verificar su URL; se completó mediante el reproductor de Windows. No hay login, control de reproducción ni consultas a la API de Spotify en la app. No se garantiza distinguir podcasts que Windows presenta como canciones.

## F03 — Implementación y evidencia

- Cliente HTTP de LRCLIB: coincidencia exacta por metadatos y búsqueda alternativa, User-Agent de la app, normalización de metadatos sin eliminar etiquetas Live/Remix/Remaster. Texto plano preferido; fallback de LRC conserva líneas y elimina marcas temporales/metadatos.
- Estados diferenciados para ausencia, instrumental, candidatos, desconexión, timeout, respuesta inválida y limitación HTTP 429. Consultas serializadas, separación mínima de 300 ms, timeout de 15 segundos y respeto de Retry-After sin reintentos automáticos.
- Botones Buscar letra/Cancelar; revisiones y cancelación impiden aceptar respuestas de canciones anteriores. La pausa conserva el contenido. No hay consultas automáticas ni llamadas de IA.
- El 5 de octubre: compilación Release correcta y 60 pruebas correctas. Consulta real de `El Bolero`, Yami Safdie, álbum `Dije Que No Me Iba a Enamorar`, ID 8561270; letra de 1.571 caracteres. La revisión visual quedó interrumpida por Escape.
- El 6 de octubre se completó la revisión visual: consulta desde SongSense con `MOJABI GHOST`, Tainy, álbum `DATA`, 3:52. Se mostró letra con scroll y fuente LRCLIB, ID 3852796. No se guarda esa letra en el repositorio ni en SQLite.
- Pruebas automáticas cubren coincidencia y ambigüedad, instrumental y ausencia, fallback LRC, codificación HTTP, espaciado, 429, errores, serialización, cancelación y respuestas obsoletas. Todas siguen pasando con F04.

## F04 — Implementación y evidencia

- Cambiar letra abre una ventana vinculada a una revisión de canción y letra. Versiones muestra título, artista, álbum, duración, ID y vista previa; exige selección y Confirmar versión. La ambigüedad nunca prepara una letra automáticamente.
- Manual permite introducir/corregir el texto y exige confirmación. Rechaza texto vacío o de solo espacios y más de 20.000 caracteres, sin truncar. Muestra contador y errores; conserva los saltos de línea. Fuente Manual sin ID, o LRCLIB con ID, en la ventana principal.
- Confirmar incrementa `LyricsRevision`, aunque el título sea igual. Los futuros consumidores de IA deberán validar canción y revisión de letra; F06 aún no existe y no se afirma haber invalidado un análisis real. Una búsqueda nueva, otra confirmación o cambio de canción invalida el editor abierto. Pausa/reanudación lo conservan. Las ventanas se desuscriben al cerrar.
- Selecciones y ediciones solo en memoria; sin cambios al esquema de SQLite, sin publicación a LRCLIB ni integración de IA. Botones con contraste y foco visibles en ambas ventanas.
- Validación del 6 de octubre: build Release final con 0 errores y 0 advertencias; **66 pruebas correctas**, 0 fallos y 0 omitidas. `git diff --check` correcto. Un intento intermedio de recompilación encontró el DLL ocupado por la app; se cerraron las instancias y la compilación final pasó.
- Pruebas nuevas: elegir dos versiones modifica texto/ID sin modificar el título reproducido; rechazo de vacío/espacios; entrada manual tras ausencia; aceptación de 20.000 y rechazo de 20.001 sin recorte; cambio de revisión al editar; rechazo de selector/editor antiguos incluso con el mismo título; búsqueda nueva invalida el editor y pausa no lo invalida.
- Interfaz real: revisión del selector con metadatos de LRCLIB; editor con contador, rechazo visible de texto vacío y guardado de texto propio de dos líneas. Se cerró el editor y la ventana principal mostró Fuente: Manual y el texto confirmado, manteniendo los metadatos de Spotify. El texto de prueba es temporal y la app se cerró al terminar.
- La selección entre dos candidatos y el cambio de canción con editor abierto se verificaron automáticamente; no se declaran reproducidos manualmente con dos versiones reales de LRCLIB. DPI y varios monitores siguen para F09.

## F05 — Implementación y evidencia

- Ajustes accesibles desde la ventana principal: clave en `PasswordBox`, reemplazo/eliminación, modelo explícito, IA inicialmente desactivada y límite entero 1–100 con valor inicial 20. Aviso previo de envío de datos y coste; activación requiere aceptarlo. Abrir y guardar ajustes no realiza llamadas HTTP. Claves de más de 512 caracteres, modelos de más de 120 y límites inválidos se rechazan al guardar sin recortar los campos.
- DPAPI `CurrentUser`, fuera del repositorio, en `%LOCALAPPDATA%\SongSense\credentials.dpapi`. El campo nunca recupera la clave guardada. Guardado mediante temporal cifrado y reemplazo atómico; buffers de caracteres/bytes se limpian. La clave transitoria usada en la cabecera HTTP sigue existiendo en memoria mientras se autentica: no se afirma aislamiento frente a procesos de la misma cuenta o un equipo comprometido.
- Preferencias y contador permanecen en SQLite, esquema 1. Se rechaza pegar una clave con prefijo `sk-` en el campo del modelo. Reserva con transacción inmediata y comprobación del límite guardado; sobrevive a reinicio y cambio de modelo. Los intentos reservados no se reembolsan tras errores/cancelación. Fecha local de Windows; día nuevo o aumento explícito del límite permiten más solicitudes.
- Una operación por servicio y bloqueo exclusivo de archivo entre instancias de la app. Guardar/reemplazar/eliminar queda bloqueado durante otra operación. El bloqueo se libera al finalizar y con la salida del proceso. Eliminar la clave primero desactiva IA y conserva el contador.
- Prueba explícita a Responses API con un texto fijo, sin letras ni metadatos de Spotify; máximo 256 tokens de salida, esquema JSON estricto, `store: false`, sin herramientas/audio/web, sin redirecciones y sin reintentos automáticos. Timeout de 60 segundos. Mensajes estáticos para 401/403, modelo/configuración rechazada, 429, timeout, desconexión y respuesta inválida/incompleta/rechazada; no se muestran cuerpos de error ni claves. Nunca se cambia el modelo automáticamente.
- `store: false` no garantiza retención cero. Fuentes oficiales consultadas el 6 de octubre de 2026: [datos del proveedor](https://developers.openai.com/api/docs/guides/your-data) y [salidas estructuradas](https://developers.openai.com/api/docs/guides/structured-outputs).
- Validación automática real en Windows: restore bloqueado correcto; build Release con **0 errores y 0 advertencias**; **106 pruebas correctas**, 0 fallos y 0 omitidas. Cifrado DPAPI real y lectura/reemplazo/eliminación, preservación ante reemplazo inválido, límites de entrada sin truncar, ausencia de clave en SQLite, cero HTTP al abrir/guardar, precondiciones, request HTTP, errores, salida estricta, cancelación, timeout controlado de 60 segundos, contador persistente, día local, aumento de límite, bloqueo entre instancias y reservas concurrentes de SQLite. HTTP simulado y credenciales sintéticas, sin peticiones de pago.
- La prueba concurrente detectó inicialmente un bloqueo en SQLite; se corrigió con transacción inmediata y pasó. La revisión visual detectó contraste incorrecto en la ventana nueva y un cierre recursivo cuando `StopAsync` terminaba sin espera; se corrigieron y se repitió la comprobación.
- Interfaz real: apertura de ajustes, campo de clave vacío/enmascarado, modelo sin valor inicial, límite 20, aviso legible, botones con coste explícito, scroll hasta contador (0/20) y mensajes. Cerrar ajustes conserva la ventana principal. Se cerró la app al terminar; no se introdujeron claves ni se activó IA en la configuración de uso. Los cambios de configuración y llamadas se verificaron en pruebas aisladas.
- Copia autocontenida del escritorio actualizada con `scripts/Update-Desktop.ps1`. El acceso directo abre esta versión; no incorpora la base, credenciales ni contador, que viven en la carpeta local superior.
- Auditoría de privacidad: `scripts/Test-GitPrivacy.ps1` inspecciona archivos elegibles, índice y blobs de todas las referencias locales, sin imprimir secretos. Verifica exclusiones de claves, DPAPI y temporales, bases, certificados, dumps y artifacts/work. Resultado sin hallazgos; fixture aislada con patrón sintético confirmó detección. El escáner complementa la revisión manual y no garantiza reconocer todo dato personal ni bloquea por sí solo una publicación.
- `main` remoto verificado mediante `git ls-remote`: permanece en el commit existente `1a37c24`. Sin staging, commit ni push. Se conservan los cambios locales previos de diseño/F04. No se ha implementado F06.

**Pendiente:** conexión real con una clave y un modelo suministrados para el proyecto. Por contrato, F05 no se marca cerrada hasta esa validación. La clave debe introducirse localmente en la app, no en el chat ni en Git.

## F06 — Implementación y evidencia

- Traducir y explicar exige letra confirmada no instrumental, IA activada, modelo válido y cuota. Una sola solicitud Responses obtiene idioma, traducción y significado con esquema estricto, `store: false`, sin herramientas ni metadatos de canción. Comparte el coordinador, timeout de 60 segundos, reserva SQLite y bloqueo entre instancias de F05; no hay reintentos automáticos. Máximo de salida: 16.384 tokens; el límite diario cuenta solicitudes, no euros.
- Validador local rechaza campos extra/duplicados, tipos incorrectos, resumen fuera de 80–150 palabras, cardinalidades inválidas, metáforas no citadas del texto y traducciones con IDs faltantes, duplicados, inventados o desordenados. Cada entrada ocupa una línea. Conserva vacíos, estrofas y repeticiones al reconstruir la traducción. Español no se traduce; en mixtas las líneas identificadas como españolas deben conservarse literalmente.
- Prompt versionado `songsense_insight_v1`: letra como datos numerados, instrucciones incrustadas sin autoridad, solo interpretación tentativa del texto, sin biografía, intención del artista ni fuentes externas. Etiqueta Interpretación generada por IA. La validación estructural no garantiza detectar idioma, fidelidad o veracidad semántica; falta evaluar el modelo real.
- Rechazo, idioma desconocido, salida incompleta e inválida generan errores recuperables sin mostrar parciales. Cancelar, cambiar canción/letra/modelo/desactivar IA o perder Spotify invalida la respuesta aunque el proveedor ignore la cancelación. Pausa conserva el resultado. Una generación completada no vuelve a solicitarse con doble clic; cambiar contenido permite generar de nuevo explícitamente.
- Letra y resultado solo en memoria; sin tablas ni migración de contenido, caché, automatización ni desarrollo de F07. No se escriben respuestas a logs o Git. Las credenciales mantienen DPAPI fuera del repositorio.
- Restore bloqueado y compilación Release correctos, 0 errores y 0 advertencias. **139 pruebas correctas**, 0 fallos y 0 omitidas, incluyendo todas las anteriores. Casos nuevos: español/inglés/mixta, resumen y citas de texto propio, líneas inválidas, rechazo/incompleto/JSON inválido, petición sin metadatos/clave en cuerpo, reserva previa y cuota compartida con la prueba de conexión, instrumental y revisión equivocada sin HTTP, respuestas tardías ante cambios de contexto, pausa, doble clic y errores sin reintentos.
- Revisión visual mediante ventana WPF aislada con texto propio y proveedor controlado, dentro de artifacts ignorados; sin clave ni petición de pago. Original, traducción, pestaña Significado, etiqueta de IA y scroll inspeccionados. Se detectó falta de espacio a 380 × 600 DIP y se ajustó el modo compacto: desplazamiento de ventana completa y altura útil de lectura. Se verificó el acceso a original, traducción y ajustes después del cambio. No equivale a una validación real del modelo ni a validar escalas/varios monitores de F09.
- Copia autocontenida y acceso de escritorio actualizados mediante `scripts/Update-Desktop.ps1`, conservando el almacenamiento y las credenciales fuera de esa copia. DLL publicada con hash idéntico a la compilación final. Arranque real comprobado: Spotify detectado, almacenamiento listo y Traducir y explicar desactivado sin IA configurada. Se deja abierta la app de uso, cerradas las ventanas de revisión.
- Auditoría Git sin hallazgos: archivos elegibles, índice, historial local y exclusiones comprobados. Sin staging, commit ni push. Se preservan los cambios locales anteriores.

**Pendiente:** prueba real completa con la clave y modelo de F05, introducidos por el usuario en la app, y revisión lingüística de ejemplos propios. Por esa condición F06 no se declara cerrada todavía. La implementación de F06 fue solicitada explícitamente mientras la prueba real de F05 seguía pendiente.
