# Estado por feature

Actualizado: 8 de octubre de 2026.

## Identidad visual y acceso de Windows — 6 de octubre de 2026

Cambio solicitado antes de F05: opción A, dos claves de sol y estilo dorado. Tema compartido, lectura con layout adaptable, icono integrado, acceso de escritorio autocontenido y anclaje a barra de tareas. Detalles y validación en [DISENO.md](DISENO.md). No cierra F10 ni F11 y no implementa IA. Las cuatro primeras features conservan su estado.

| Feature | Estado |
|---|---|
| F01 — Base ejecutable | Cerrada |
| F02 — Canción actual de Spotify | Cerrada |
| F03 — Consulta de letras a LRCLIB | Cerrada |
| F04 — Selección y corrección de letra | Cerrada |
| F05 — Configuración segura de IA | Implementada; pendiente de conexión real |
| F06 — Traducción y significado | Implementada; pendiente de prueba real de OpenAI |
| F07 — Caché local | Cerrada |
| F08 — ChatGPT Plus sin cargos extra | Parcial: OAuth y consumo implementados; generación bloqueada por política no verificable |
| F09 — Flujo automático | Implementada; pendiente de aceptación real con Spotify y proveedor |
| F10 — Ventana de uso diario | Pendiente |
| F11 — Validación y entrega | Pendiente |

## Cambio de ruta — 7 de octubre de 2026

Por petición del usuario, F08 pasa a ser la conexión oficial con ChatGPT Plus desde Song Sense: uso incluido compartido, protección de tokens, sin API key ni créditos adicionales y bloqueo de generación cuando se agote o no pueda comprobarse la cuota. La política de cero cargos debe demostrarse con el proveedor antes de habilitar el modo. Detalles, dependencias, fuentes y aceptación en [RUTA_PROYECTO.md](RUTA_PROYECTO.md#f08--cuenta-chatgpt-plus-y-uso-incluido-sin-cargos-extra).

La anterior F08 (flujo automático) pasa a F09; F09 (ventana de uso diario), a F10; y F10 (validación y entrega), a F11. Las referencias de esta documentación usan la numeración actual. El cambio del plan no declara cerradas las pruebas reales pendientes. La implementación parcial de F08 se documenta al final. F07 conserva su alcance.

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

Limitaciones: F01 valida la base ejecutable. No prueba Spotify, LRCLIB, IA, bandeja ni entrega portable. Las escalas 100 %/150 % y escenarios de varios monitores se validarán en F10; no se declaran probados aquí.

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
- La selección entre dos candidatos y el cambio de canción con editor abierto se verificaron automáticamente; no se declaran reproducidos manualmente con dos versiones reales de LRCLIB. DPI y varios monitores siguen para F10.

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
- Revisión visual mediante ventana WPF aislada con texto propio y proveedor controlado, dentro de artifacts ignorados; sin clave ni petición de pago. Original, traducción, pestaña Significado, etiqueta de IA y scroll inspeccionados. Se detectó falta de espacio a 380 × 600 DIP y se ajustó el modo compacto: desplazamiento de ventana completa y altura útil de lectura. Se verificó el acceso a original, traducción y ajustes después del cambio. No equivale a una validación real del modelo ni a validar escalas/varios monitores de F10.
- Copia autocontenida y acceso de escritorio actualizados mediante `scripts/Update-Desktop.ps1`, conservando el almacenamiento y las credenciales fuera de esa copia. DLL publicada con hash idéntico a la compilación final. Arranque real comprobado: Spotify detectado, almacenamiento listo y Traducir y explicar desactivado sin IA configurada. Se deja abierta la app de uso, cerradas las ventanas de revisión.
- Auditoría Git sin hallazgos: archivos elegibles, índice, historial local y exclusiones comprobados. Sin staging, commit ni push. Se preservan los cambios locales anteriores.

**Pendiente:** prueba real completa con la clave y modelo de F05, introducidos por el usuario en la app, y revisión lingüística de ejemplos propios. Por esa condición F06 no se declara cerrada todavía. La implementación de F06 fue solicitada explícitamente mientras la prueba real de F05 seguía pendiente.

## F07 — Persistencia y caché — 7 de octubre de 2026

- Migración transaccional de esquema 1 a 2: añade `song_cache`, `insight_cache` y `song_insights`, índices de acceso y de relaciones, sin cambiar preferencias ni contadores. Letras, origen, ID elegido y resultados completos se guardan en texto legible en la base local fuera de Git; el aviso de ajustes lo explica. No se guardan claves en estas tablas.
- Firma SHA-256 de título/artista/álbum normalizados y duración, conservando etiquetas de versión. Si faltan álbum o duración, la asociación exige Usar letra guardada antes de preparar contenido. Clave de análisis SHA-256 de texto con saltos normalizados pero espacios/estrofas/repeticiones intactos, destino español, proveedor OpenAI, modelo y versión de prompt/esquema.
- Al reiniciar o cambiar de canción se consulta únicamente almacenamiento local. Buscar letra consulta caché antes de LRCLIB. Los resultados se vuelven a validar y se ligan a revisiones actuales. Cambiar texto, modelo o versión impide reutilizar una clave diferente. F07 no automatiza solicitudes de red ni generaciones y no inicia F08.
- Caducidad de letras/asociaciones a 30 días y ausencias a una hora desde la escritura original; las lecturas no renuevan el TTL. Resultados sin caducidad temporal mientras tengan asociaciones conservadas. Límite de 200 canciones por acceso reciente, expulsión y limpieza de resultados huérfanos. Una asociación nueva puede compartir un análisis exacto; borrar otra canción no elimina ese resultado compartido.
- Regenerar exige una acción explícita y las precondiciones/cuota de F06, omite la caché y reemplaza solo un resultado completo válido. Borrar esta canción y Borrar caché conservan preferencias, clave y contador diario. Se invalidan generaciones y lecturas pendientes para que un resultado antiguo no repueble contenido borrado. El borrado es lógico; no garantiza eliminar restos físicos de SQLite o copias de recuperación.
- Escrituras de letra, análisis y relaciones con transacción inmediata, comprobación de cancelación antes del commit y claves foráneas. JSON inválido no se guarda ni se muestra como completo. Fallos de persistencia se informan sin ocultar el contenido válido mostrado.
- Integridad comprobada al abrir. Corrupción muestra diagnóstico y Recuperar base local, con confirmación explícita y cierre de otras instancias. Se conservan el original y auxiliares en una carpeta local `recovery-*`; se prepara una base nueva con IA desactivada. Se conservan contadores legibles; si no se pueden leer, el original permanece disponible y se bloquea la cuota del día con un contador de 100. No se borra ni recupera automáticamente una base dañada. Una versión futura o un error de acceso no se trata como corrupción.
- Validación final: restore bloqueado correcto, build Release **0 errores y 0 advertencias**, **153 pruebas correctas**, 0 fallos y omitidas. Incluye migración repetible conservando datos, recuperación tras reiniciar sin proveedores ni gasto, clave exacta, TTL controlado, asociación ambigua, LRU/huérfanos, contenido compartido, borrado conservando contador/archivo de credencial, escritura interrumpida mediante trigger que fuerza rollback, resultado/JSON inválidos, original corrupto conservado byte por byte, edición persistente, generación tardía tras borrado, búsqueda repetida sin red, regeneración explícita, cambio/restauración de modelo y rechazo de metadatos de modelo/prompt incompatibles con la clave de caché.
- Revisión visual a 380 × 600 DIP mediante app WPF aislada, texto propio y proveedor simulado: segunda instancia del ViewModel recuperó letra, traducción y significado desde SQLite. Estado de caché, Regenerar, Borrar esta canción, Borrar caché y ajustes accesibles con scroll. No se pulsaron controles de borrado sobre datos de uso ni se hicieron solicitudes de IA reales; el comportamiento de las mutaciones se verificó en pruebas aisladas.
- Copia de escritorio actualizada tras guardar/cerrar los ajustes pendientes por el usuario. Hash de DLL publicada idéntico al de la compilación final; arranque real y almacenamiento listo comprobados. App de uso abierta, ventana de revisión cerrada. No se reiniciaron preferencias, clave ni contadores.
- Auditoría `Test-GitPrivacy.ps1` correcta: 62 archivos elegibles y 98 blobs históricos, índice y exclusiones comprobados, sin hallazgos. `git diff --check` correcto. Sin staging, commit ni push de esta feature; se preservan los cambios de documentación de la nueva ruta ChatGPT Plus. El remoto main consultado permanece en `1a37c24`.

F07 queda cerrada por sus comprobaciones de almacenamiento y reutilización controlada. F05/F06 conservan su validación real pendiente; F07 no acredita una conexión real de OpenAI ni implementa la nueva F08.

## F08 — Implementación parcial segura — 7 de octubre de 2026

Estado: **parcial; generación con el plan deshabilitada**. No se declara seguridad al 100 % ni se acredita una sesión real por haber superado pruebas controladas.

Implementado:

- Ventana Cuenta y consumo con modos independientes ChatGPT · solo uso incluido y API key · de pago. Primer inicio en ChatGPT, aunque F05 tenga una clave guardada. El pago se elige explícitamente; ninguna operación del modo ChatGPT usa la clave como alternativa.
- OAuth oficial: navegador del usuario, registro dinámico, host estable, loopback 127.0.0.1 en /auth/callback, estado y nonce aleatorios, PKCE S256, client ID emitido, permiso separado del plan. Validación de firma RSA con Microsoft.IdentityModel.JsonWebTokens 8.23.0, JWKS oficial, emisor, audiencia, caducidad, nonce e identidad del registro al reconectar. No se importan archivos de autenticación de Codex ni se interpretan sus metadatos opacos.
- Registros separados incluso con igual correo, etiquetas estables, selección explícita y consulta manual de modelos autorizados con selección de slug, sin heredar el modelo de API. Una identidad validada sin permiso del plan no puede listar modelos ni generar.
- Credenciales y registros protegidos mediante DPAPI y ACL del usuario en un directorio dedicado fuera del repositorio y SQLite. Archivos temporales cifrados y sustitución atómica. Exclusión entre procesos para login, renovación, cambio de modo y solicitudes de API. Renovación rotatoria persistida antes del uso; invalid_grant elimina la sesión inutilizable. Errores temporales conservan credenciales, sin reintentos automáticos. Desconectar intenta revocar y elimina tokens locales incluso si falla la revocación remota, indicando ese límite. Se conserva identidad/client ID para reconectar.
- El cambio de conexión cancela análisis y pruebas de API en curso e invalida el contexto. La configuración del modo de pago se comprueba de nuevo en disco antes de leer su clave o reservar presupuesto. La caché de análisis se separa por ruta y registro mediante hash de identidad/client ID; no contiene credenciales.
- Tokens comunicados por Responses: entrada, salida y total por análisis y por prueba de API. Se muestran también ante respuesta incompleta o resultado inválido si el proveedor los comunicó. Datos ausentes, malformados, cancelación o corte se señalan como consumo no comunicado, nunca cero. Caché: 0 tokens nuevos, sin reutilizar el consumo anterior como una consulta nueva. No se convierte el número de tokens en porcentaje del plan ni se utiliza la cuota de Codex como cuota independiente de Song Sense.
- Auditor de Git ampliado para JWT, refresh/ID tokens, callbacks OAuth y exportaciones de autenticación. Los mensajes de error no contienen cuerpos HTTP, encabezados, tokens ni códigos de acceso.

Bloqueo obligatorio:

La documentación pública consultada explica la opción de permitir créditos adicionales, pero no documenta un mecanismo de consulta de esa opción ni una restricción por request de solo uso incluido. **El bloqueo está en el proveedor y en la interfaz, no en una casilla de aceptación.** Login, permisos concedidos y modelos disponibles no habilitan inferencia con ChatGPT. Una reapertura conserva el bloqueo. No se usa ningún endpoint privado ni se realizan peticiones de generación para sondear cuota. [Flujo oficial](https://developers.openai.com/siwc/token-sharing-open-source/sign-in), [credenciales y renovación](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions), [uso y créditos](https://learn.chatgpt.com/docs/sign-in-with-chatgpt), [tokens de respuestas](https://developers.openai.com/api/docs/guides/token-counting).

Validación local:

- Restore con --locked-mode, compilación Release sin errores ni advertencias y **190 pruebas correctas**, cero fallos u omitidas. Incluye firma/emisor/audiencia/nonce/caducidad erróneos; callback con estado incorrecto, consentimiento denegado, client ID ausente y cuenta distinta; protección DPAPI, sesión corrupta conservada, renovación y revocación fallidas, rotación durable, permisos/modelos y ausencia de fallback; consumo comunicado/ausente/malformado y aislamiento de caché por cuenta/ruta.
- Consulta de vulnerabilidades NuGet de las dependencias de Infrastructure, incluidas transitivas: ninguna reportada por el feed en esta comprobación. No constituye una garantía de ausencia de vulnerabilidades.
- Auditoría de privacidad de archivos elegibles, índice e historial y git diff --check. No se hace commit, push ni publicación externa.
- Copia autocontenida local y acceso del escritorio actualizados. Revisión real de la pantalla principal, controles de generación deshabilitados, almacenamiento listo y panel Cuenta y consumo. Revisión aislada a 360 × 480 con cuenta/modelo sintéticos y scroll hasta el final. Se corrigieron el selector que mostraba propiedades internas, su contraste y la referencia de icono del panel. Datos y artefactos de revisión permanecen ignorados y separados de la cuenta real.

Pendientes para cerrar F08:

1. Login/logout real y confirmación por el usuario de que el correo y el registro corresponden a su cuenta de Codex. No existe en esta implementación un puente automático entre la identidad de Codex y la identidad OAuth de Song Sense.
2. Mecanismo oficial verificable que impida consumir créditos adicionales, incluido ante cambios posteriores de ajustes; hasta entonces, cero generaciones en el modo ChatGPT.
3. Habilitar después el transporte Responses streaming de ChatGPT, confirmar JSON Schema en un modelo autorizado, manejar finalización/fallo/cuota según los códigos oficiales y ejecutar una traducción propia real con evidencia de uso incluido. Este transporte no se considera implementado ni validado en esta entrega; el cliente JSON de F05/F06 continúa reservado para el modo API key de pago.
4. Validación real de revocación, renovación, elegibilidad, cuotas y consumo del plan. Las pruebas controladas y la interfaz no sustituyen esta evidencia.

Intento real de acceso observado en la app: rechazado por el flujo de OpenAI, sin sesión guardada. Se añadió diagnóstico por etapa mediante valores enumerados (sin cuerpo HTTP, códigos ni tokens), compatibilidad del tipo Bearer sin distinguir mayúsculas/minúsculas y conservación cifrada del client ID pendiente para reautorizar con un código nuevo. El nombre de registro solo se envía durante el alta inicial, como exige la documentación. La identidad de un registro pendiente todavía no se considera verificada. El login real continúa pendiente de un intento exitoso del usuario.

Revisión del 7 de octubre: el navegador respondía antes del intercambio y la validación, y el diagnóstico desaparecía al cerrar Cuenta y consumo. Ahora la respuesta de éxito espera a la validación y la escritura protegida de la sesión. Los fallos responden con un mensaje genérico y conservan únicamente códigos enumerados del resultado y etapa en DPAPI; la app los muestra al reabrir. Una escritura fallida vuelve a leer el estado durable antes de activar una cuenta. Añadir otra cuenta queda bloqueado con un registro pendiente; Continuar reutiliza su client ID. Dos pruebas nuevas cubren el éxito después de guardar y el fallo de escritura sin confirmación falsa; se amplió la prueba del intercambio rechazado para comprobar persistencia del diagnóstico y bloqueo de altas duplicadas. La revisión local encontró cero registros y cero sesiones guardadas; no permite determinar cuántos registros existen en ChatGPT. La causa del intento anterior no se puede recuperar porque no se conservaba su diagnóstico.

Nuevo intento real: diagnóstico protegido Authentication / TokenExchange y client ID pendiente conservado, sin sesión activa. El estado HTTP y el código específico del servidor no se guardaban en esa versión y no pueden recuperarse. Se amplió el diagnóstico con estado HTTP y un enum de errores OAuth permitidos; los cuerpos, descripciones y códigos desconocidos nunca se persisten. Seis casos controlados cubren errores simples, anidados, HTML y texto desconocido privado, y su exclusión del diagnóstico guardado. La comprobación del endpoint con códigos deliberadamente inválidos no crea ni renueva sesiones y no confirma que el acceso real funcione. No se ha corregido ni identificado aún la causa concreta del rechazo real; se necesita un código nuevo para probar la reautorización con el registro retenido.

## F09 — Flujo automático — 8 de octubre de 2026

Estado: **implementada; pendiente de aceptación real**. Por petición del usuario se implementa la siguiente feature conservando los bloqueos y pendientes de F08. No se habilita el modo ChatGPT ni se inicia F10.

- Interruptor Actualizar automáticamente, activado inicialmente, independiente de la activación de IA y persistido localmente. Migración transaccional a SQLite v3: nueva columna automatic_updates, sin modificar preferencias de IA, contadores o caché. Guardar ajustes de IA no sobrescribe esta preferencia.
- Espera cancelable de 1,5 segundos desde el último cambio de canción. Recuperación de caché, búsqueda de letra y análisis cuando hay letra confirmada, modelo válido, conexión autorizada y cuota diaria. Instrumentales, ausencia de letra y selección ambigua detienen la generación.
- Cambios de canción invalidan búsquedas y análisis anteriores. El trabajador espera la finalización real de una consulta que ignore cancelación y conserva únicamente la canción pendiente más reciente. No procesa una cola histórica. Una solicitud enviada puede consumir tokens aunque se descarte su resultado.
- Pausa/reanudación y eventos repetidos de la misma revisión no duplican consultas. Desactivar congela el contenido y cancela el contexto; reactivar procesa la última observación. Los fallos no se reintentan automáticamente. Reintentar flujo es explícito y respeta caché, conexión y cuota.
- Consumo de cada análisis comunicado por el proveedor; caché con 0 tokens nuevos. Se mantiene la indicación de consumo no comunicado cuando corresponde y no se convierte a porcentaje del plan. Cero fallback desde ChatGPT a API de pago.

Validación:

- Compilación Release sin errores; suite completa: **200 pruebas correctas**, cero fallos u omitidas. Tras el último ajuste de estado sin canción, se repitieron las **10 pruebas de F09**, todas correctas.
- Casos controlados: cinco cambios rápidos, debounce antes de la consulta, eventos de reproducción repetidos, generación que ignora cancelación, rechazo del resultado antiguo y procesamiento exclusivo de la última canción; pausa del flujo y persistencia; fallo con reintento explícito; instrumental, ausencia y candidatos; bloqueo de ChatGPT, cuota diaria y ausencia de fallback; volver a una canción almacenada sin llamadas nuevas ni tokens.
- Revisión de la app real: almacenamiento listo, estado Sin Spotify, nuevos controles y generación deshabilitada. Corregido el contraste del interruptor. Revisión aislada de 360 × 480 con texto propio, proveedor simulado y desplazamiento hasta los controles inferiores y el consumo; no usa credenciales reales ni red.
- Copia autocontenida local y acceso del escritorio actualizados. Los artefactos de revisión permanecen ignorados. Auditoría de privacidad de Git y diff --check; sin staging, commit, push o publicación externa.

Pendientes: secuencia real de cinco cambios en Spotify, pausa/reanudación y regreso a caché con un proveedor operativo; validación real de límites y resultados. Spotify no estaba abierto durante la revisión. La generación real con ChatGPT sigue impedida por F08; las pruebas controladas no acreditan elegibilidad, conexión ni consumo incluido. F09 no se declara cerrada hasta completar esa aceptación.
