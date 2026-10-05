# Estado por feature

Actualizado: 5 de octubre de 2026.

| Feature | Estado |
|---|---|
| F01 — Base ejecutable | Cerrada |
| F02 — Canción actual de Spotify | Cerrada |
| F03 — Consulta de letras a LRCLIB | En curso |
| F04 — Selección y corrección de letra | Pendiente |
| F05 — Configuración segura de IA | Pendiente |
| F06 — Traducción y significado | Pendiente |
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
