# Ruta de desarrollo — Letras en español para Spotify en Windows

Fecha: 7 de octubre de 2026. Estado: F01–F04 y F07 cerradas; F05 y F06 implementadas, pendientes de validación real de OpenAI; F08–F11 pendientes. F08 incorpora la conexión con ChatGPT Plus solicitada el 7 de octubre. Evidencia en [ESTADO_FEATURES.md](ESTADO_FEATURES.md).
Nombre del proyecto: **SongSense**. Repositorio: `song-sense`.

## Objetivo y alcance de la primera versión

Aplicación personal para Windows que detecta la canción reproducida en Spotify, obtiene su letra de LRCLIB y muestra una traducción al español cuando corresponde y una explicación de su significado. Actualiza el contenido al cambiar de canción y reutiliza resultados locales.

La explicación será una interpretación basada en la letra. Esta versión no buscará entrevistas, biografías ni declaraciones del artista; no presentará contexto histórico ni intenciones del autor como hechos comprobados.

La ruta se ejecuta en orden, una feature por petición. Solicitar una feature autoriza solamente esa feature y las correcciones necesarias para cerrarla. No se inicia la siguiente automáticamente. No se hacen commits, pushes ni publicaciones como parte implícita del desarrollo.

## Decisiones fijadas

| Área | Decisión |
|---|---|
| Plataforma inicial | Windows 11, x64; Spotify de escritorio instalado. |
| Aplicación | C#, .NET 10, WPF y MVVM. |
| Detección | Sesiones multimedia de Windows, mediante `Windows.Media.Control`. |
| Letras | API pública de LRCLIB; sin scraping ni endpoints privados de Spotify. |
| IA inicial | OpenAI Responses API mediante HTTPS y salida JSON estructurada. |
| IA con suscripción, F08 | Iniciar sesión con ChatGPT y autorizar el uso incluido de Plus; sin cargos adicionales ni paso automático a API key. |
| Modelo | Identificador configurable obligatorio; sin valor predeterminado ni sustitución silenciosa. F05 valida el modelo configurado. |
| Datos | SQLite en `%LOCALAPPDATA%\SongSense`; migraciones desde F01. |
| Secretos | Clave API cifrada con DPAPI para el usuario actual; nunca en SQLite, repositorio ni logs. |
| Interfaz | Español, tema oscuro, ventana redimensionable y dos pestañas: Traducción y Significado. |
| Red | Consultas a LRCLIB y OpenAI; sin backend propio, cuentas propias, telemetría ni sincronización. |
| Entrega | ZIP portable autocontenido para Windows x64; sin instalación de .NET en el equipo de uso. |

La elección de LRCLIB no acredita derechos adicionales sobre las letras: la documentación consultada confirma acceso técnico gratuito, pero no establece expresamente permiso para traducción o procesamiento con IA. Esa incertidumbre sigue abierta y no se describirá el producto como una integración con licencias verificadas. El alcance es un prototipo personal, sin distribución pública del catálogo. No se añadirá una pantalla que afirme que aceptar un aviso resuelve los derechos.

La ruta inicial F05/F06 usa una clave con acceso y facturación para la API, cobrada aparte de ChatGPT Plus. F08 añade una alternativa mediante la cuenta ChatGPT que se usa en Codex, con uso incluido compartido y bloqueo al agotarse, bajo las condiciones de su contrato. No es una nueva suscripción «Codex Plus». La aplicación informa antes de activarla de que enviará la letra confirmada al proveedor; F06 evita enviar título, artista y álbum. Cambiar este plan no conecta cuentas ni realiza llamadas de IA.

## Contrato transversal

- Cada canción activa tiene una revisión monotónica. Toda consulta conserva esa revisión; sus resultados solo pueden actualizar la pantalla si siguen correspondiendo a la revisión activa.
- Solo se analiza una letra identificada. Si no hay letra o hay coincidencia ambigua, no se inventa ni se pide a la IA que la reconstruya.
- Pausar y reanudar la reproducción conserva el contenido y no crea solicitudes nuevas.
- Si cambia la canción, se limpian de inmediato traducción y explicación anteriores. No se muestra texto de otra canción bajo el nuevo título.
- Instrumentales confirmadas: «Esta canción es instrumental»; cero solicitudes de IA.
- Las canciones en español reciben explicación; no una traducción redundante. Las mixtas conservan las líneas españolas y traducen las restantes.
- Toda operación de red es asíncrona y cancelable; ninguna bloquea la interfaz.
- Las letras son datos no confiables: no pueden modificar instrucciones de IA, ejecutar comandos ni abrir enlaces automáticamente.
- Los errores se muestran con una acción de recuperación concreta. Logs sin letras, respuestas completas, claves ni cabeceras de autorización.
- Estados mínimos: Sin Spotify, Sin canción, En pausa, Buscando letra, Elegir versión, Sin letra, IA sin configurar, Procesando, Listo, Sin conexión, Límite diario y Error.

## Orden de features

| ID | Feature | Resultado al cerrar |
|---|---|---|
| F01 | Base ejecutable | Aplicación abre y tiene estructura, estados y almacenamiento inicial. |
| F02 | Canción actual de Spotify | Detecta cambios reales sin usar la API de Spotify. |
| F03 | Consulta de letras a LRCLIB | Recupera letras y clasifica ausencia, instrumental y candidatos. |
| F04 | Selección y corrección de letra | Resuelve versiones ambiguas y permite corrección manual. |
| F05 | Configuración segura de IA | Clave, modelo, activación y control de solicitudes funcionan. |
| F06 | Traducción y significado | Produce y muestra resultados completos y validados. |
| F07 | Caché local | Reutiliza resultados y permite borrarlos o regenerarlos. |
| F08 | ChatGPT Plus sin cargos extra | Inicio de sesión desde la app y generación con uso incluido; bloqueo al agotarse y ninguna vía de cobro alternativa. |
| F09 | Flujo automático | Cambiar canción dispara el flujo completo sin resultados obsoletos. |
| F10 | Ventana de uso diario | Ventana flotante, bandeja y preferencias persistentes. |
| F11 | Validación y entrega | ZIP portable probado con Spotify real y guía de uso. |

Todas las features dependen del cierre de la inmediatamente anterior. Hasta F09, las consultas y generaciones se disparan mediante botones; F09 incorpora la automatización. Esto permite verificar cada integración por separado.

## F01 — Base ejecutable

**Objetivo:** disponer de una base que compile y abra, sin implementar integraciones.

**Incluye:**

- Solución con proyectos App (WPF), Core (contratos y reglas), Infrastructure (Windows, HTTP y SQLite) y Tests.
- Configuración de compilación reproducible con versión del SDK registrada y versiones de dependencias fijadas.
- Ventana oscura inicialmente de 480 × 720 DIP, ampliada a 860 × 780 DIP por la decisión visual del 6 de octubre de 2026; mínimo 360 × 480 conservado. Identidad dorada con dos claves de sol y layout adaptable, según `docs/DISENO.md`.
- Contratos `CurrentTrack`, `LyricsCandidate`, `ResolvedLyrics` y `SongInsight`; interfaces de detección, letras, IA y persistencia.
- Inicialización de SQLite con versión de esquema y migraciones. No tablas de contenido hasta F07; sí configuración y contador de solicitudes necesarios en F05.
- README con requisitos y comandos de compilación, ejecución y pruebas; documento de estado por feature.

**No incluye:** Spotify, LRCLIB, IA ni resultados simulados presentados como reales.

**Aceptación:** compilación Release correcta; abre y cierra sin excepciones; pestañas y redimensionado funcionan; primera apertura crea la base y las siguientes no pierden datos. Se revisa visualmente en Windows; si no es posible ejecutarla, F01 se informa como incompleta.

## F02 — Detección de Spotify

**Objetivo:** mostrar título, artista y estado de reproducción reales.

**Incluye:**

- Enumerar sesiones multimedia y reconocer la sesión de Spotify por su identificador de aplicación. Registrar el identificador observado durante la validación; no elegir indiscriminadamente la sesión actual del sistema.
- Leer título, artista, álbum y duración cuando Windows los entregue. Duración ausente se representa como desconocida, no cero.
- Suscribirse a cambios de sesión, propiedades y reproducción. Reenumerar cada 5 segundos como recuperación; liberar suscripciones al cerrar.
- Si hay más de una sesión de Spotify, priorizar la que está reproduciendo; entre empates conservar la seleccionada, o elegir por identificador ordenado si no existe selección previa.
- Distinguir Spotify cerrado, sesión sin canción y reproducción en pausa. Podcasts y anuncios sin metadatos identificables no disparan el flujo de letras.
- Al desaparecer Spotify, limpiar la canción activa; al volver, reconectar sin reiniciar SongSense.

**No incluye:** control de reproducción, login Spotify, API oficial de Spotify ni detección garantizada de podcasts que Windows presente como canciones.

**Aceptación:** prueba real con Spotify: inicio antes y después de SongSense, dos cambios de canción, pausa/reanudación y cierre/reapertura. Cambios reflejados en menos de 5 segundos con ambas apps activas. Reproducción paralela de un vídeo no sustituye la canción de Spotify. Debe quedar evidencia de los metadatos disponibles en esa instalación.

## F03 — Consulta a LRCLIB

**Objetivo:** recuperar letras sin asumir que todo resultado corresponde a la canción.

**Incluye:**

- Botón «Buscar letra» para la canción activa.
- Cliente HTTPS con identificación de la aplicación en `User-Agent`, timeout de 15 segundos y solicitudes secuenciales separadas al menos 300 ms.
- Con álbum y duración conocidos: consultar `/api/get`. Si faltan o no hay resultado, consultar `/api/search` por título y artista.
- Normalizar mayúsculas, Unicode y espacios para comparar; conservar etiquetas de versión como live, remix y remaster.
- Aceptar automáticamente un único candidato con título y artista coincidentes, álbum coincidente cuando sea conocido y duración con diferencia máxima de 2 segundos cuando sea conocida. Si faltan datos o hay variantes, devolver candidatos para F04.
- Usar `plainLyrics`; si está vacía y hay `syncedLyrics`, extraer texto conservando líneas y retirando marcas de tiempo. No implementar seguimiento temporal.
- Gestionar 404, respuestas vacías, instrumental, error HTTP, timeout y desconexión.
- En 429 respetar `Retry-After`; no reintentar antes de ese plazo ni repetir indefinidamente. Sin cabecera válida, cooldown de 60 segundos y reintento manual.

**Aceptación:** pruebas con respuestas controladas para coincidencia, ausencia, instrumental, formato inválido y 429; una consulta real obtiene una letra sin imprimirla en logs. Cancelar o cambiar canción impide aplicar una respuesta antigua. Sin letra no se habilita IA.

## F04 — Selección y corrección

**Objetivo:** evitar traducir una versión incorrecta.

**Incluye:**

- Lista de candidatos con título, artista, álbum y duración; selector obligatorio si F03 devuelve ambigüedad.
- Botón «Cambiar letra» que permite elegir otro candidato, sin alterar el título de la canción reproducida.
- Editor de letra manual asociado a la canción activa. Guardar exige texto no vacío y máximo 20.000 caracteres; rechazar exceso sin truncar.
- Mostrar origen LRCLIB o Manual e identificador LRCLIB cuando corresponda.
- Confirmar una letra la deja preparada para IA. Modificarla invalida los resultados de esa letra, incluso si el título sigue siendo el mismo.

**No incluye:** subida a LRCLIB, scraping, exportación pública ni cambios al catálogo del servicio.

**Aceptación:** seleccionar dos versiones cambia la letra preparada; una búsqueda sin resultados permite entrada manual; editar invalida el análisis anterior; un selector o editor abierto para una canción anterior no puede guardar sobre la nueva. Hasta F07 las selecciones solo duran durante la ejecución actual.

## F05 — Configuración y control de IA

**Objetivo:** configurar el proveedor sin exponer la clave y con gasto acotado por solicitudes.

**Incluye:**

- Ajustes para clave OpenAI, identificador de modelo, activar/desactivar IA y límite diario entero de 1 a 100; valor inicial 20 solicitudes y IA inicialmente desactivada.
- Guardar la clave cifrada mediante DPAPI; campo enmascarado; reemplazar y eliminar clave. Modelos y preferencias no secretas en SQLite.
- Aviso informativo sobre datos enviados y coste antes de activar IA; sin solicitudes de red al abrir ajustes ni al guardar.
- Botón «Probar conexión» con solicitud mínima y salida estructurada; cuenta para el límite diario y su carácter de pago se indica en el botón o su ayuda.
- Implementar Responses API, timeout de 60 segundos, `store: false` y sin herramientas, audio ni búsqueda web. `store: false` no se describe como garantía de retención cero.
- Modelo vacío o incompatible: error explícito; no selección ni cambio automático de modelo.
- Contador persistente por fecha local; reservar una solicitud antes de enviarla. Intentos enviados cuentan aunque fallen o se cancelen. Cambiar modelo o reiniciar no reinicia el contador.
- Máximo una solicitud IA simultánea. Sin reintentos automáticos de IA. Límite alcanzado bloquea nuevas solicitudes hasta el día siguiente o un aumento explícito del límite en ajustes.

**Aceptación:** pruebas controladas de autenticación inválida, incompatibilidad, timeout, 429 y límite; clave ausente de base, logs y archivos en texto claro; contador sobrevive reinicio. Validación real de conexión solo con clave y modelo suministrados para el proyecto. Sin ellos, la integración queda pendiente de validación real y no se marca completamente cerrada.

## F06 — Traducción y significado

**Objetivo:** generar ambos contenidos con una sola solicitud por letra nueva.

**Incluye:**

- Botón «Traducir y explicar»; letra confirmada, IA activada y cuota disponible como precondiciones.
- Dividir letra en líneas numeradas. Una sola solicitud detecta idioma y devuelve traducción y explicación mediante esquema JSON estricto.
- Resultado: idioma `es`, `mixed`, otro código o `unknown`; traducción con identificadores de línea; resumen de 80–150 palabras; 1–5 temas; 0–6 metáforas con explicación; 0–3 interpretaciones alternativas; advertencias de ambigüedad.
- Español: no devolver traducción, mostrar «La letra ya está en español». Mixta: conservar las líneas españolas y traducir las demás. Otro idioma: traducir todas las líneas con contenido, manteniendo orden, estrofas y repeticiones.
- Traducción de significado, sin adaptación para cantar, sin inventar versos y sin censurar el sentido del original.
- Validación local de JSON y de correspondencia exacta entre IDs de líneas de entrada y salida. No mostrar una traducción parcial como completa.
- Idioma desconocido, rechazo del proveedor, salida incompleta o JSON inválido: estado recuperable, sin resultados falsos ni reintento de pago automático.
- Etiqueta permanente «Interpretación generada por IA». No afirmar intenciones del artista, acontecimientos reales ni fuentes no consultadas.
- Letra como bloque de datos: ignorar posibles instrucciones incrustadas en ella.

**Aceptación:** respuestas controladas para español, inglés, mixta, rechazo, líneas faltantes y salida inválida; revisión de traducción y explicación con ejemplos de texto propio o con permisos adecuados. Prueba real completa con la configuración de F05. Instrumental no hace llamada. Resultado de una canción antigua no puede actualizar la nueva.

## F07 — Persistencia y caché

**Objetivo:** no repetir búsquedas ni generaciones ya resueltas.

**Incluye:**

- Tablas de letras elegidas, resultados IA y asociaciones con firma de canción; migración desde F01.
- Firma de canción: título, artista y álbum normalizados, duración cuando sea conocida y etiqueta de versión conservada. Si no hay firma inequívoca, exigir selección antes de reutilizar una asociación dudosa.
- Clave de análisis: SHA-256 de letra normalizada conservando líneas, idioma destino, proveedor, modelo y versión del prompt/esquema.
- Guardar fecha, origen, selección y resultados validados; no secretos. Letra y resultados en almacenamiento local en texto legible; informar de ello en ajustes.
- Reutilizar análisis solo con clave exacta. Un cambio de modelo, prompt o letra invalida esa coincidencia; no altera el resultado guardado anterior.
- TTL de letras y asociaciones de 30 días. Ausencias guardadas 1 hora. Resultados IA ligados al hash sin caducidad hasta que el usuario borre o cambie configuración.
- Límite de 200 canciones por acceso reciente; expulsar la menos recientemente usada y limpiar resultados huérfanos. Contadores y ajustes no se borran por expulsión.
- Botones «Regenerar» (una nueva llamada explícita), «Borrar esta canción» y «Borrar caché». Borrar caché no elimina clave ni contador diario.
- Almacenar solo resultados completos y validados con transacciones. Recuperación de base corrupta conservando copia local y mostrando diagnóstico; no borrar datos silenciosamente.

**Aceptación:** tras reiniciar, una canción guardada muestra su resultado sin llamada IA; cambiar una línea, modelo o versión del prompt obliga a regenerar; respetar TTL; borrar caché conserva contador; edición manual persiste; pruebas de escritura interrumpida, expulsión y asociación ambigua.

## F08 — Cuenta ChatGPT Plus y uso incluido sin cargos extra

**Objetivo:** ofrecer desde Song Sense «Continuar con ChatGPT» para usar la cuenta Plus conectada a Codex, sin clave API ni cargos adicionales, y detener nuevas generaciones cuando el proveedor rechace el uso incluido disponible.

**Dependencias:** caché F07 cerrada; generación y validación de F06; flujo oficial habilitado para la cuenta y el cliente local. Implementación parcial el 7 de octubre: OAuth y consumo; no acredita conexión real ni habilita generaciones con el plan mientras la política de créditos no sea verificable. Evidencia en [ESTADO_FEATURES.md](ESTADO_FEATURES.md#f08--implementación-parcial-segura--7-de-octubre-de-2026).

**Incluye:**

- Ajustes con modos separados «ChatGPT · solo uso incluido» y «API key · de pago». Conectar, ver la cuenta seleccionada y desconectar desde la app; login oficial en navegador con OAuth, PKCE y verificación de `state` y permisos. No copiar ni reutilizar credenciales internas de Codex, ni pedir contraseña en Song Sense.
- Autorizar expresamente el uso del plan, además del inicio de sesión. Tokens de acceso/renovación protegidos con DPAPI fuera del repositorio y SQLite; renovación segura y limpieza al desconectar. Sin tokens ni letras en logs, diagnóstico, URLs ajenas al flujo oficial, ZIP o Git.
- Consultar modelos autorizados para esa cuenta y exigir selección. El ID API configurado en F05 no se da por válido en este modo. Requests adaptadas a la ruta oficial de Responses, con streaming y `store: false`; omitir parámetros incompatibles de la ruta API key, incluido `max_output_tokens` según las restricciones actuales. Mantener límites locales de entrada, tamaño y tiempo sin afirmar un presupuesto de tokens que el proveedor no permite fijar.
- Conservar el contrato de idioma, traducción y significado de F06: solo aceptar JSON completo validado tras finalización confirmada del stream. Nada parcial ante corte, rechazo o error. Comprobar con la integración real el soporte del esquema; una incompatibilidad bloquea el modo, sin cambiar silenciosamente de modelo o perder validaciones.
- Mostrar «Usando tu plan ChatGPT» y acceso a «Gestionar uso». Explicar que consume la cuota compartida con ChatGPT/Codex y límites de la app; no mostrar un contador ficticio de tokens restantes. El límite local de solicitudes sigue siendo una protección adicional, independiente del límite del plan.
- Mostrar, en cada análisis y prueba, tokens de entrada, salida y total comunicados por el proveedor. Si el consumo falta o la consulta se cancela antes de conocerlo, indicarlo como desconocido; no estimar porcentaje del plan a partir de tokens. Una recuperación local muestra 0 tokens nuevos y no reproduce un consumo histórico como si fuera una nueva consulta.
- **Política obligatoria de cero cargos adicionales:** prohibir fallback automático a API key, compra/recarga de créditos o uso de saldo adicional. Guiar al usuario para desactivar «Permitir que otras apps usen créditos al alcanzar el límite» en ChatGPT. Habilitar «solo uso incluido» únicamente cuando se haya verificado que el proveedor permite cumplir esa restricción. Si no puede verificarse o deja de cumplirse, bloquear generación y explicar el motivo; una casilla local no demuestra el estado del servidor.
- Al recibir el error oficial de cuota agotada, bloquear Traducir, Regenerar y cualquier prueba de IA en este modo. Sin reintentos automáticos, cola ni solicitudes para sondear continuamente la cuota. Lectura de caché, letras y borrado local siguen disponibles. No inferir que se agotó todo Plus ni inventar fecha de reinicio: puede ser un límite específico de Song Sense. Ofrecer Gestionar uso y comprobación manual de disponibilidad; desbloquear solo con evidencia del proveedor o un restablecimiento comunicado por él.
- Permiso denegado, cuenta no elegible, sesión revocada/caducada o disponibilidad de cuota desconocida: detener nuevas generaciones con una acción concreta, sin sustituir el mecanismo de facturación. Cambiar cuenta o modo cancela solicitudes, invalida contexto y separa las claves de caché por ruta de autenticación, modelo y versión del prompt, sin introducir secretos en esas claves.

**No incluye:** automatización de canciones (F09), cuotas independientes de Plus, promesa de uso ilimitado, scraping de ChatGPT, endpoints privados, extracción de tokens de Codex ni cargos de API autorizados implícitamente por conectar Plus.

**Aceptación:** login/logout real con la cuenta Plus, permisos revisados y una traducción de texto propio completa sin API key; evidencia de consumo incluido y de créditos extra desactivados. Pruebas controladas de denegación, renovación/revocación, stream incompleto, cambio de cuenta/modelo, cuota agotada y cuota no comprobable. En todos los bloqueos: cero nuevas llamadas de generación, cero fallback de pago y caché accesible; reabrir conserva el bloqueo hasta comprobar disponibilidad. Auditoría de secretos en disco, Git y entrega. Si elegibilidad, formato o política de cero cargos no se pueden demostrar, F08 permanece pendiente de validación y el modo queda deshabilitado.

**Fuentes oficiales verificadas el 7 de octubre de 2026:** [flujo para aplicaciones locales](https://developers.openai.com/siwc/token-sharing-open-source), [modelos e inferencia](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference), [restricciones de la integración](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations), [errores y cuota agotada](https://developers.openai.com/siwc/token-sharing-open-source/errors-and-recovery), [uso compartido y permiso de créditos extra](https://learn.chatgpt.com/docs/sign-in-with-chatgpt). Revisar cambios de estas condiciones al implementar.

## F09 — Flujo automático

**Objetivo:** completar el caso de uso original al reproducir o cambiar una canción.

**Incluye:**

- Interruptor «Actualizar automáticamente», inicialmente activado; IA mantiene su activación independiente de F05.
- Al detectar metadatos nuevos, esperar 1,5 segundos de estabilidad y ejecutar: buscar caché → resolver letra → consultar caché de análisis → generar cuando esté permitido → mostrar.
- Si falta selección, configuración, conexión o cuota, detenerse en el estado correspondiente; no generar a ciegas.
- En modo ChatGPT de F08, aplicar el bloqueo por cuota o uso incluido no verificable antes de cualquier generación; sin fallback a API key o créditos extra. Recuperar caché no consume el plan.
- Cambiar de canción cancela operaciones anteriores. Una llamada de pago ya enviada puede seguir contando; cancelar no garantiza evitar el cargo.
- Si una llamada IA anterior sigue en curso, conservar únicamente la última canción pendiente; no acumular cola de canciones saltadas.
- Debounce y deduplicación: eventos repetidos no duplican solicitudes. Pausa/reanudación no vuelve a procesar. Una llamada fallida no se repite hasta «Reintentar» o abandonar esa canción y volver a ella.
- Desactivar actualización automática cancela operaciones y congela el contenido con etiqueta «Actualización pausada». Al reactivarla procesa la canción actual, no una cola histórica.

**Aceptación:** prueba real de cinco cambios rápidos, pausa, reanudación, vuelta a canción guardada y activación/desactivación. Solo la canción vigente actualiza la pantalla; no hay llamadas duplicadas; instrumental, ambigüedad y límite diario detienen el flujo correctamente. Secuencia completa documentada con Spotify y proveedor real.

## F10 — Ventana de uso diario

**Objetivo:** hacer cómoda la aplicación mientras se escucha música o se trabaja.

**Incluye:**

- Interruptor «Siempre encima», inicialmente desactivado; posición, tamaño, pestaña y tamaño de texto persistentes.
- Texto ajustable entre 12 y 24 puntos, valor inicial 16; scroll independiente en cada pestaña.
- Pestaña Traducción muestra original y español por bloques de estrofa; en español solo el original. Pestaña Significado muestra resumen, temas, metáforas y alternativas.
- Bandeja del sistema con Abrir, Pausar/Reanudar actualización y Salir. Cerrar ventana minimiza a bandeja; Salir termina el proceso y libera conexiones.
- Una sola instancia: una segunda apertura activa la primera. Sin inicio automático con Windows en esta versión.
- Uso completo con teclado, foco visible, contraste legible; recuperar ventana dentro de un monitor disponible al desconectarse una pantalla.
- Sin robos de foco ni notificaciones por cada canción. Errores se ven dentro de la ventana.

**Aceptación:** inspección real a 100 % y 150 % de escala, textos largos, ventana mínima, teclado, dos monitores cuando estén disponibles, minimizar/abrir/salir y segunda instancia. Si una configuración no puede probarse se registra expresamente; no se declara validada.

## F11 — Validación y entrega portable

**Objetivo:** entregar una versión personal ejecutable y documentada.

**Incluye:**

- Compilación Release, pruebas de lógica e integraciones con respuestas controladas, y cierre de defectos de las features anteriores.
- Publicación autocontenida `win-x64` en carpeta y ZIP; sin trimming de WPF ni instalador. Mantener una carpeta completa; no prometer un único EXE.
- Guía de primera ejecución, conexión de ChatGPT Plus o configuración explícita de API de pago, modelo, uso compartido y bloqueo sin cargos extra de F08, almacenamiento, borrado y solución de errores.
- Evidencia separada de pruebas automáticas, ejecución visual en Windows y consultas reales. No equiparar compilación con validación de uso.
- Matriz real: inglés, español, letra mixta, instrumental, canción sin letra, versión ambigua y cambio rápido. Casos no disponibles realmente se prueban de forma controlada y se etiquetan así.
- Red desconectada con resultado ya guardado y con canción nueva; cuota local y del plan agotadas; créditos extra desactivados y sin fallback de pago; sesión ChatGPT revocada; clave API inválida; reinicio y recuperación de Spotify.
- Auditoría del ZIP para asegurar que no contiene claves, base personal, letras de prueba, logs sensibles ni credenciales.

**Aceptación:** ZIP abre fuera de la carpeta de desarrollo en Windows x64; Spotify real se detecta; una canción de otro idioma muestra traducción y explicación; repetirla no consume una llamada; español no se traduce; todas las limitaciones y pruebas pendientes se documentan. Ninguna feature pendiente se oculta bajo una entrega «terminada».

## Definición de cierre de cada feature

1. Implementación limitada al alcance definido.
2. Compilación y comprobaciones apropiadas correctas.
3. Criterios de aceptación demostrados; separar pruebas simuladas de reales.
4. Documentación actualizada con archivos cambiados, validación y limitaciones.
5. Estado de la feature actualizado; sin empezar la siguiente.

Estados permitidos: Pendiente, En curso, Implementada pendiente de validación real, Cerrada. Una dependencia solamente se cumple en estado Cerrada.

Solicitud para empezar: **«Desarrolla F01 siguiendo RUTA_PROYECTO.md; no empieces F02».** El mismo patrón se aplica a las siguientes features.

## Fuera de la primera versión

- Android, iOS, navegador y macOS.
- Plugins o modificaciones del cliente de Spotify.
- Letras siguiendo el tiempo de reproducción, karaoke y transcripción del audio.
- Control de reproducción o cuenta de Spotify.
- Scraping de Genius/Musixmatch y endpoints privados.
- Modelos IA locales, múltiples proveedores y backend propio.
- Contexto histórico verificado mediante búsqueda web.
- Compartir, publicar, exportar el catálogo y comercializar.
- Instalador, actualizaciones automáticas y ejecución al iniciar Windows.

## Fuentes técnicas

- Sesiones multimedia: https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession.trygetmediapropertiesasync?view=winrt-26100
- LRCLIB, consultas y comportamiento de la API: https://lrclib.net/docs
- OpenAI, salida estructurada: https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=responses

Las fuentes establecen capacidades técnicas; no acreditan por sí solas derechos de reutilización de todas las letras.
