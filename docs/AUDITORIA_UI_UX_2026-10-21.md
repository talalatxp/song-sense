# Auditoría crítica de UI/UX — Song Sense

**Fecha: 21 de octubre de 2026 · Windows · Estado local de trabajo**

## Dictamen

Song Sense tiene una identidad visual reconocible y una base funcional considerable, pero todavía exige al usuario entender demasiadas dependencias internas para realizar su tarea principal: leer y comprender una canción. El problema más importante es la distancia entre lo que la interfaz promete, lo que permite hacer y lo que explica cuando una acción está bloqueada.

La aplicación resulta más coherente en una captura estática que durante un recorrido completo. Los problemas aparecen al iniciar sin canción, entrar en Significado sin consultas habilitadas, gestionar la cuenta, cambiar el idioma, encontrarse con una instrumental, alcanzar la cuota o reducir la ventana. La pantalla feliz no representa adecuadamente esos escenarios.

**Mi valoración profesional: necesita una revisión de arquitectura de interacción y presentación de estados antes de una ronda de embellecimiento.** Mantendría la dirección carbón/dorado y las dos claves de sol. Replantearía la jerarquía de la ventana, el flujo de configuración y los mensajes contextuales. No recomiendo resolverlo añadiendo más tarjetas, más iconos o más texto explicativo al diseño actual.

Hay defectos verificables que deben corregirse antes de considerar la experiencia lista: consumo desconocido mostrado como cero, una confirmación casi ilegible, acciones primarias imposibles de ejecutar sin explicación suficiente, instrumentales tratadas como letras listas y borrado de contenido sin confirmación ni deshacer en el código actual.

## 1. Qué se ha revisado y qué significa la cobertura

### Método y entornos

Se han combinado cuatro fuentes de evidencia:

1. **Aplicación de escritorio real:** observación de la ventana abierta, detección de Spotify, letra cargada, bloqueo de IA, pestañas, ajustes y diagnósticos. No se modificó la reproducción de Spotify.
2. **Ventanas reales compiladas desde el checkout actual:** MainWindow, LyricsEditorWindow, ConnectionWindow y AiSettingsWindow. Se ejecutaron en un arnés aislado con los recursos de tema actuales, proveedores sintéticos y datos originales de prueba. La interacción visual se hizo sobre WPF, no sobre maquetas ni imágenes reconstruidas.
3. **Inspección del código:** XAML, plantillas, reglas de visibilidad, comandos, persistencia, recuperación, bandeja y pruebas. Permite señalar comportamientos que no sería responsable ejecutar sobre datos personales.
4. **Validación automatizada existente:** compilación Release correcta y **280 pruebas superadas, 0 fallos, 0 omitidas**. Estas pruebas respaldan lógica y contratos; no prueban que una persona comprenda la interfaz.

Las respuestas de IA del arnés son textos de prueba. No evalúan calidad lingüística, latencia real, autorización real ni fiabilidad del proveedor. El transporte del arnés rechaza las conexiones de red; los estados de cuenta conectada utilizan credenciales sintéticas sin validez. La caché de revisión es independiente de la personal.

La copia abierta del escritorio y la DLL compilada desde el código tienen hashes distintos:

| Entorno | SHA-256 de SongSense.dll |
|---|---|
| Copia de escritorio observada | `69D3C3CF46DD43414998C51D53B353AD42CF4D5AC45A114FA4ABA4CAA1E65C3B` |
| Compilación actual usada por el arnés | `2C4B6C2A79830BFFC4F165BE4D37C246C9559589D823DD15B8B9011C5543B41A` |

Esto confirma que son binarios diferentes; el hash, por sí solo, no demuestra qué cambios contiene cada uno. Los hallazgos principales se apoyan en el código actual y en las ventanas de revisión. Las observaciones de la copia de escritorio se identifican como tales.

### Matriz de cobertura

**Leyenda:** V = observado visualmente e interactuado; S = estado sintético mostrado en las ventanas reales; C = revisado en código o pruebas; P = pendiente de comprobación real. No son etiquetas de calidad.

| Área o recorrido | Cobertura | Evidencia y límite |
|---|---|---|
| Ventana principal con Spotify y letra | V | Instancia personal, sin consulta de IA |
| Letra y traducción completas | S | Estrofas bilingües, texto original de prueba |
| Significado, temas y tres desplegables | V/S | Resumen largo, metáforas, otras lecturas y matices abiertos |
| Sin canción / sin Spotify | S/C | Estado inicial del ViewModel; no se cerró Spotify personal |
| Letra lista con IA desactivada | V/S | Botón bloqueado e instrucciones de ambas pestañas |
| Ambigüedad de versiones | V/S | Lista, selección, metadatos y vista previa |
| Editor manual | V/S | Vacío, precargado, 21.000 caracteres y validaciones |
| Editor invalidado por cambio de canción | S | Texto conservado, edición y guardado bloqueados |
| Resultado ya en idioma destino | S/C | Original sin duplicar; mensaje de idioma no visible |
| Selector de seis idiomas y cambio de idioma | V/S | Lista abierta; análisis retirado sin explicación contextual |
| Búsqueda y análisis en curso | S | Proveedores retenidos artificialmente |
| Cancelación de búsqueda | V/S | Regreso al estado preparado para reintentar |
| Letra no encontrada / sin conexión | S/C | Comparación de mensajes visibles y diagnóstico interno |
| Fallo de IA sin consumo comunicado | S/C | Reproducción de falso «Guardado · 0 tokens nuevos» |
| Instrumental | S/C | Reproducción de «Letra lista» y CTA imposible |
| Cuota local agotada | S/C | 20/20 en almacén de prueba; CTA bloqueado sin aviso directo |
| Reproducción pausada / seguimiento desmarcado | V/S/C | Falta de indicación prominente en la presentación |
| Caché recuperada | S/C | SQLite aislada, recuperación de contenido y etiqueta de cero tokens |
| Borrar canción / borrar toda la caché | C | Se revisó el comando completo; no se borraron datos personales |
| Almacenamiento inaccesible | S/C | Error de inicialización sintético y acceso a reintento |
| Corrupción y diálogo de recuperación | C/P | Texto y rutas revisados; no se dañó ni recuperó la base personal |
| Cuenta sin sesión | V/S | Ventana completa, parte superior e inferior |
| Cuenta conectada pero no habilitada | S | Registro sintético con modelo; sin autenticación externa |
| Acceso rechazado / registro pendiente | S/C | Diagnóstico y opciones de reconexión |
| Modelos de cuenta y habilitación real | C/P | Controles inspeccionados; catálogo real y autorización no ejecutados |
| Ajustes de API | V/S/C | Campos, aviso, botones, scroll; sin introducir secretos ni hacer pagos |
| Geometría normal y mínima | V/S | 860×780 y 360×480 DIP solicitados; capturas del área visible |
| Texto 16/20/24 pt | V/S/C | 20 pt en instancia personal; 16/24 pt en revisión |
| Ventana amplia | S/C | 1200×900 solicitados; captura limitada al área visible disponible |
| Teclado y accesibilidad estructural | V/C | Foco, Escape en menú y Ctrl+End en lectura; árbol UI Automation |
| Narrador, alto contraste y escalado real | P | No se hicieron pruebas completas con tecnología asistiva ni cambios del sistema |
| Bandeja, cerrar/abrir y monitores | C/P | Implementación revisada; menú nativo y cambio físico de monitores pendientes |
| OAuth real, cuota remota y cobro | P | No se alteraron permisos ni se enviaron solicitudes de pago |

**Cobertura honesta:** se han mostrado las cuatro ventanas principales y los dos modos del editor, y se han revisado los componentes definidos en su XAML. Se conservan **47 capturas** de evidencia. No es una certificación del 100 % de combinaciones, servicios externos, escalas de Windows y tecnologías de asistencia. Afirmar ese 100 % sería falso. Las filas P identifican el trabajo concreto restante, no defectos supuestos.

### Cómo leer los hallazgos

- **P1 — Alta:** impide completar una tarea, informa de forma incorrecta, facilita una pérdida relevante o bloquea la comprensión de una decisión importante.
- **P2 — Media:** produce fricción recurrente, lectura deficiente, navegación innecesaria o dudas evitables.
- **P3 — Baja:** consistencia, acabado o mejora menos urgente.
- **V/S/C** conserva el significado de la leyenda anterior. Una recomendación de diseño no se presenta como fallo funcional.

No se asignan porcentajes de usabilidad ni puntuaciones numéricas: harían falta pruebas con usuarios y tareas comparables para justificarlos.

## 2. Hallazgos prioritarios de confianza, estados y orientación

### H01 · P1 · Consumo desconocido presentado como una lectura guardada de cero tokens · S/C

**Observación.** Un fallo de IA sin información de consumo deja el mensaje interno «Consumo no comunicado por OpenAI; no equivale a 0 tokens». `CompactUsage` busca primero si la cadena contiene «0 tokens» y muestra «Guardado · 0 tokens nuevos». La captura 22 reproduce exactamente esa contradicción. No se ha demostrado que el proveedor consuma en ese caso; lo demostrado es que la UI afirma cero y caché sin tener esa información.

**Impacto.** Hace creer al usuario que no hubo una consulta nueva y puede inducir reintentos basados en una premisa económica falsa. Es más grave que una etiqueta poco elegante.

**Cambio recomendado.** Modelar el consumo con estados explícitos: recuperado de caché, comunicado, desconocido y no solicitado. Derivar el texto del estado, nunca buscar palabras dentro del mensaje localizado.

**Aceptación.** Un fallo con consumo desconocido muestra «Consumo no disponible»; solo una recuperación real muestra cero tokens nuevos; un error con consumo comunicado conserva la cifra. Añadir una prueba específica de presentación para la frase que hoy falla.

### H02 · P1 · La confirmación de créditos apenas se puede leer · V/S/C

**Observación.** En Cuenta y consumo, la frase junto a «He desactivado el uso de créditos…» aparece negra sobre el panel oscuro. El CheckBox no declara el Foreground que sí se declara en otros controles equivalentes. La captura 19 muestra el defecto en un control habilitado.

**Impacto.** La persona debe confirmar una decisión importante sin poder leer cómodamente su significado. No es un problema de controles desactivados que puedan quedar visualmente atenuados.

**Cambio recomendado.** Aplicar un estilo global de CheckBox que defina texto, indicador, foco, hover, selección y estado desactivado. Mantener el aviso breve y legible al lado de la casilla.

**Aceptación.** La frase se lee en todos sus estados sobre el tema oscuro y con alto contraste. Medir el color finalmente renderizado; el negro puro sobre `#1B1A16` sería aproximadamente 1,21:1, una referencia ilustrativa, no una medición de los píxeles de la captura.

### H03 · P1 · Significado pide una acción bloqueada o inexistente · V/S/C

**Observación.** Sin IA habilitada, Significado dice «Pulsa Traducir y explicar para descubrirlo», mientras el botón está desactivado. Si faltan letras o se están buscando, ese mismo texto puede nombrar una acción que ni siquiera está visible. La pestaña de lectura conoce parte del bloqueo; Significado utiliza un vacío genérico.

**Impacto.** El usuario recibe una instrucción imposible y no sabe si falta cuenta, modelo, cuota o letra. La aplicación parece averiada.

**Cambio recomendado.** Compartir entre pestañas un estado de preparación con causa y siguiente acción: «Conecta tu cuenta», «Elige una versión», «Esperando la letra», «Consultas pausadas» o «Límite alcanzado». Ofrecer el enlace o botón correspondiente dentro del vacío.

**Aceptación.** Ningún estado recomienda pulsar una acción oculta o desactivada. Cada bloqueo permite descubrir su causa sin entrar en Detalles.

### H04 · P1 · El primer inicio no explica cómo conseguir una canción · S/C

**Observación.** Sin canción se ve «Todavía no hay una canción», un Buscar letra desactivado y «Busca la letra para empezar». Existe un `DetectionMessage` que indica abrir Spotify, pero no está enlazado en la ventana principal actual. Véase captura 03.

**Impacto.** Una persona nueva no recibe la condición necesaria para comenzar. Pedir buscar mientras el botón está inactivo convierte el vacío en un callejón sin salida.

**Cambio recomendado.** Mostrar «Abre Spotify y reproduce una canción» con una breve explicación de la detección. Distinguir Spotify no detectado, Spotify sin metadatos y error de detección. Eliminar la tarjeta que simula metadatos pendientes cuando no aporta una acción.

**Aceptación.** Una persona que abra Song Sense por primera vez puede describir el siguiente paso sin leer README ni abrir ajustes.

### H05 · P1 · Una instrumental se presenta como una letra preparada · S/C

**Observación.** Una resolución instrumental produce «Letra lista», muestra Traducir y explicar desactivado y conserva «Pulsa Traducir y explicar para ver la traducción». El diagnóstico correcto existe internamente, pero no protagoniza la pantalla. Captura 23.

**Impacto.** Parece que falta contenido o que una función está rota. Se oculta precisamente la información que explica por qué no hay letra.

**Cambio recomendado.** Estado dedicado «Instrumental · esta versión no contiene letra», sin CTA de traducción. Mantener «Elegir otra versión» si la detección fuera incorrecta. Significado debe explicar por qué no se analiza una letra inexistente.

**Aceptación.** No aparece «Letra lista» ni una invitación a traducir en una versión instrumental. La pantalla no queda como un panel vacío inexplicado.

### H06 · P1 · La cuota agotada puede parecer una desactivación arbitraria · S/C

**Observación.** Con 20/20 solicitudes locales desde la inicialización, se desactiva el CTA por `HasQuota`, pero el aviso de límite depende de `insightState == DailyLimit`. No es necesario que ese estado exista para que el botón se bloquee. La captura 26 muestra «Letra lista» y una invitación a traducir pese al bloqueo.

**Impacto.** La persona no distingue un límite del día de una mala configuración y puede reconectar o cambiar ajustes innecesariamente.

**Cambio recomendado.** Derivar la explicación directamente de las condiciones que desactivan el comando. Mostrar «Límite local de hoy: 20/20» y explicar cuándo vuelve a haber disponibilidad local, diferenciándola de cualquier límite del proveedor.

**Aceptación.** Abrir con la cuota ya agotada ofrece la misma claridad que agotarla durante la sesión. No se presenta un porcentaje inventado del plan ni se confunde el contador con euros.

### H07 · P1 · No encontrar letra y no poder consultar la red se comunican igual · S/C

**Observación.** `DisplayNotice` agrupa Error, Offline y NoLyrics bajo «No se encontró la letra». Las capturas 24 y 25 resultan prácticamente idénticas aunque sus causas sean diferentes.

**Impacto.** La persona puede concluir que una canción no existe en el catálogo cuando en realidad no se pudo realizar la consulta. Se fomenta trabajo manual innecesario y se oculta la posibilidad de reintentar al recuperar conexión.

**Cambio recomendado.** Separar «No hemos encontrado una coincidencia» de «No se pudo conectar con el servicio de letras», «El servicio está limitando las consultas» y «La respuesta no pudo leerse». Mantener detalle técnico secundario.

**Aceptación.** La pantalla permite identificar causa y acción útil en cada caso, sin exponer mensajes brutos del proveedor.

### H08 · P1 · Los errores envían a una zona oculta en lugar de ofrecer recuperación · S/C

**Observación.** Tras el fallo de análisis se ve «Revisar» y «Ver detalles en Ajustes». Para entender la causa hay que abrir Ajustes, desplazarse y abrir Detalles. El mensaje de error no es un enlace. Captura 22.

**Impacto.** La recuperación requiere explorar una sección que parece destinada a preferencias. El usuario abandona su tarea para investigar el funcionamiento interno.

**Cambio recomendado.** Un aviso junto a la acción: causa comprensible, datos conservados y siguiente paso. Ejemplo: «No se pudo conectar. Conservamos la letra. Puedes reintentar». Usar «Ver diagnóstico» para la información secundaria.

**Aceptación.** Los casos habituales se resuelven sin abrir Detalles. Un diagnóstico sigue disponible para soporte, pero no es condición para saber qué hacer.

### H09 · P1 · Pausa de Spotify y contenido congelado carecen de señal prominente · V/S/C

**Observación.** El estado visible prioriza «Letra lista» o «Listo» y no representa la pausa de Spotify. Desmarcar Seguir canción tampoco añade un aviso persistente a la tarjeta. `AutomaticMessage` contiene «Contenido congelado», pero se muestra en Detalles. Capturas 35 y 41.

**Impacto.** Se puede leer una canción anterior creyendo que es la actual. «AHORA SUENA» resulta equívoco durante una pausa o cuando el seguimiento está detenido.

**Cambio recomendado.** Separar estado de reproducción y estado de contenido. Mostrar «Spotify en pausa» o «Seguimiento pausado · mostrando esta canción», con una acción directa para reanudar seguimiento.

**Aceptación.** Con Ajustes cerrado sigue siendo evidente si la canción se actualizará. Reanudar seguimiento no dispara IA ni cambia de proveedor.

### H10 · P1 · La ambigüedad de versiones no cambia la acción principal · S/C

**Observación.** Ante varias letras se muestra «Elige una versión de la letra en Más», pero el botón destacado continúa siendo Buscar letra. Elegir la versión requiere Más → Cambiar letra → Versiones → seleccionar → confirmar. Capturas 11–13.

**Impacto.** La acción más visible repite una búsqueda que ya produjo lo necesario; la acción que desbloquea la tarea queda oculta.

**Cambio recomendado.** Convertir el CTA contextual en «Elegir letra · 5 versiones» y abrir directamente el selector. Mantener «Buscar de nuevo» como secundaria.

**Aceptación.** Resolver la ambigüedad empieza con un único clic desde la pantalla principal. Se mantiene la confirmación explícita de la versión.

### H11 · P1 · Borrar contenido es inmediato y el alcance del nombre es ambiguo · C

**Observación.** `DeleteCacheAsync` borra la asociación de la canción o la caché completa sin confirmación ni deshacer. Los comandos se enlazan directamente desde el menú y Ajustes. «Borrar esta canción» puede parecer que afecta a Spotify o a una biblioteca, cuando elimina contenido guardado por Song Sense.

**Impacto.** Un clic accidental puede eliminar letras manuales y análisis que no se recuperan sin trabajo o nuevas consultas. La pérdida y su alcance no se explican antes de ejecutar.

**Cambio recomendado.** Renombrar a «Eliminar contenido guardado de esta canción»; separar acciones destructivas de las de lectura. Ofrecer confirmación específica cuando no haya deshacer fiable, o un deshacer real que restaure los datos. Para borrado global, indicar qué se elimina y qué se conserva.

**Aceptación.** Ningún borrado irreversible relevante ocurre con un único clic equívoco. La opción segura es la predeterminada. Este hallazgo se comprobó por código: no se ejecutó el borrado sobre datos personales.

## 3. Ventana principal, lectura y navegación

### H12 · P2 · La cabecera ocupa demasiado respecto al propósito de la app · V/S

**Observación.** En la ventana normal la lectura comienza alrededor de y=375, después de marca, tarjeta de canción y pestañas. Incluso cuando ya hay resultado y solo queda Más, la tarjeta conserva una gran altura. Capturas 04 y 05.

**Impacto.** Una aplicación destinada a leer utiliza una parte importante de su altura en contenido que cambia poco. Obliga a desplazarse incluso en letras o explicaciones moderadas.

**Cambio recomendado.** Cabecera compacta de canción de dos líneas, estado y acciones alineadas; reservar la tarjeta expandida para verificar una coincidencia o consultar metadatos. Marca discreta y cuenta con estado breve.

**Aceptación.** En 860×780, con ajustes cerrados, el área útil de lectura ocupa al menos aproximadamente el 60 % de la altura de contenido. Es un objetivo de diseño para validar, no una norma universal.

### H13 · P1 · El tamaño mínimo permite abrir la app pero no leerla cómodamente · S/C

**Observación.** A 360×480 DIP solicitados, marca, tarjeta y pestañas empujan la lectura a la franja inferior. A 24 pt solo entran fragmentos de pocas líneas antes de tener que desplazar el contenedor exterior. Capturas 08 y 09.

**Impacto.** La ventana compacta incumple la expectativa de acompañar a Spotify a un lado de la pantalla. La persona tiene que desplazar la app para llegar a su función principal.

**Cambio recomendado.** Un modo compacto específico: título y artista resumidos, acciones en una fila y lectura como región flexible principal. Si no puede ofrecerse una experiencia útil en 360×480, elevar el mínimo de forma deliberada; no conservarlo solo porque no se produce una excepción.

**Aceptación.** En el mínimo elegido permanecen visibles contexto, navegación y una región de lectura útil sin scroll exterior obligatorio para empezar a leer.

### H14 · P2 · Dos niveles de desplazamiento compiten por el control · V/S/C

**Observación.** Al abrir Ajustes o reducir tamaño aparece un scroll exterior mientras cada lectura mantiene el suyo. Abrir Ajustes ejecuta `ScrollToEnd`, retirando de la vista la canción y sus acciones. La rueda puede mover diferentes regiones según la posición del puntero. Capturas 02 y 32.

**Impacto.** El mismo gesto tiene efectos distintos. Cambiar una preferencia hace perder orientación; volver a la canción requiere otro desplazamiento.

**Cambio recomendado.** Ajustes en un panel o ventana independiente, con cabecera estable y un solo scroll principal por tarea. Las preferencias rápidas de lectura pueden vivir en una pequeña barra sin expandir todo el documento.

**Aceptación.** Abrir o cerrar ajustes no mueve la canción fuera de vista ni altera la posición del texto que se estaba leyendo.

### H15 · P2 · Cambiar de pestaña reinicia la lectura · V/C

**Observación.** `ReadingTabs.SelectionChanged` envía la pestaña seleccionada a `ScrollToTop`. La documentación de diseño afirma que se conserva el desplazamiento por pestaña, por lo que también existe una contradicción de especificación.

**Impacto.** Alternar entre un verso y su interpretación obliga a buscar de nuevo la posición. Es una penalización repetida en el uso central de la app.

**Cambio recomendado.** Conservar el scroll de cada pestaña por canción y revisión de contenido. Reiniciarlo cuando cambie realmente el documento, no por volver a la pestaña. Añadir «Volver al inicio» si es útil.

**Aceptación.** Leer a mitad de Letra, pasar a Significado y volver mantiene la posición. Cambiar de canción no deja el documento nuevo a mitad.

### H16 · P2 · Se ocultan siempre los metadatos útiles para validar la versión · C/V

**Observación.** `ApplyReadingLayout` colapsa TrackMetadata sin condicionar por tamaño. Álbum y duración no reaparecen en ventana ancha, aunque el programa los tenga. Los candidatos sí utilizan esos datos para distinguir versiones.

**Impacto.** Si la letra corresponde a una versión en directo, remix o grabación diferente, el usuario pierde una comprobación sencilla.

**Cambio recomendado.** Mantener título/artista compactos y ofrecer «Detalles de la canción» con álbum, duración y versión de letra. No es necesario añadirlos permanentemente a una cabecera grande.

**Aceptación.** La versión puede verificarse sin ir a Spotify ni abrir un editor cuyo objetivo es cambiarla.

### H17 · P2 · Original y traducción se distinguen por estilo, pero no se identifican · S/C

**Observación.** Se alternan original en Georgia y color secundario con traducción en Segoe UI y color claro. Cuando hay análisis se oculta ReadingHint, que podría explicar idioma original y destino. Captura 04.

**Impacto.** La relación debe deducirse por idioma y tipografía. En canciones mixtas o idiomas próximos, esa deducción es menos obvia. El original parece visualmente menos importante aunque pueda ser lo que la persona quiere leer.

**Cambio recomendado.** Mostrar una leyenda persistente y breve «Original · Inglés / Traducción · Español»; permitir solo original, solo traducción o ambos. Conservar la alineación por estrofas, que es una buena base.

**Aceptación.** Los roles se entienden sin depender del color ni conocer los idiomas. El lector puede priorizar su modo preferido sin modificar el contenido.

### H18 · P2 · El tamaño de fuente y el interlineado no forman un sistema · S/C

**Observación.** La preferencia se expresa en puntos y se convierte correctamente a DIP, pero las líneas principales mantienen `LineHeight=30`. A 24 pt la fuente llega a 32 DIP, por lo que ese valor fijo deja de ser una elección proporcional. Temas, matices y otras etiquetas siguen tamaños independientes.

**Impacto.** Ampliar puede hacer el texto más denso en lugar de más cómodo. Parte del contenido importante queda pequeña mientras el resumen crece. La prueba no demuestra un solapamiento universal; sí demuestra que el espaciado no escala de forma coherente.

**Cambio recomendado.** Calcular interlineado y espaciado de estrofas a partir del tamaño de lectura. Separar escala de lectura de escala de controles y ofrecer valores cómodos, sin confundir aumento de texto con aumento del sistema.

**Aceptación.** Revisar 12, 16, 20 y 24 pt con versos largos y resumen denso: sin colisiones, sin recortes y con jerarquía legible en todas las secciones.

### H19 · P2 · En tamaño amplio falta controlar la longitud de línea · S/C

**Observación.** El contenido se estira con la ventana y no hay un ancho máximo para los párrafos. La prueba de 1200×900 estaba limitada por el área de pantalla disponible y no se utiliza como prueba de un fallo de colocación de la app de producción. La ausencia de restricción sí se confirma en XAML.

**Impacto.** Los párrafos largos pueden convertirse en líneas excesivas y difíciles de seguir, mientras letras cortas quedan pegadas a un lateral de una tarjeta muy ancha.

**Cambio recomendado.** Limitar el ancho de la columna de prosa y centrar el documento; explorar dos columnas original/traducción solo cuando el ancho permita mantener estrofas relacionadas. No añadir columnas por defecto en tamaños intermedios.

**Aceptación.** Validar una medida de lectura aproximada de 55–80 caracteres en prosa, ajustándola con usuarios. Mantener anchos cómodos aunque se maximice la ventana.

### H20 · P2 · No hay una forma sencilla de seleccionar y copiar fragmentos · C

**Observación.** Letra, traducción y análisis se presentan como TextBlock sin selección estándar ni acciones de copia expuestas. No es un fallo de renderizado, sino una limitación del flujo de comprensión y consulta.

**Impacto.** Buscar una palabra, contrastar un verso o conservar una idea exige transcribir. La solución actual trata el contenido como una superficie de exposición pasiva.

**Cambio recomendado.** Texto seleccionable y copia de fragmentos, conservando la maquetación. Una acción «Copiar» debe indicar qué copia: estrofa, traducción o interpretación. La exportación completa sería una decisión de producto aparte.

**Aceptación.** Puede seleccionarse un verso con ratón y teclado y copiarse con Ctrl+C sin copiar accidentalmente todo el análisis.

### H21 · P2 · Cambiar el idioma retira el análisis sin explicar la transición · V/S/C

**Observación.** Al elegir Inglés después de un resultado en Español, desaparecen traducción y significado. La invalidación es deliberada para no mostrar resultados del idioma equivocado, pero la UI solo vuelve a «Pulsa Traducir y explicar». Además, el CTA queda fuera de vista porque Ajustes está abierto. Captura 34.

**Impacto.** La persona puede interpretar que ha perdido el análisis guardado o que cambiar el selector ya solicitó uno nuevo.

**Cambio recomendado.** Mostrar «Idioma cambiado a Inglés. No se ha enviado una consulta. Genera el resultado en este idioma». Mantener accesible el resultado anterior con su etiqueta o explicar su disponibilidad, sin mezclar idiomas ni lanzar IA automáticamente.

**Aceptación.** Tras el cambio se entiende qué cambió, qué se conserva y qué acción genera una nueva consulta. Recuperar una caché compatible se comunica como tal.

### H22 · P2 · El origen de la letra y del análisis queda demasiado escondido · V/S/C

**Observación.** Manual/LRCLIB, ID, configuración de IA y diagnóstico están reunidos en Detalles. La lectura principal muestra solo el contenido y un «Listo». El consumo tampoco dice necesariamente cuándo o con qué modelo se produjo el resultado.

**Impacto.** Se dificulta evaluar si la versión es correcta, si la corrección propia quedó guardada o si se está leyendo un análisis antiguo.

**Cambio recomendado.** Una línea discreta junto al contenido: «Letra manual · guardada en este dispositivo» o «Letra de LRCLIB · cambiar versión», y otra procedencia de análisis expandible con idioma, proveedor y modelo. No exponer hashes ni detalles internos por defecto.

**Aceptación.** La procedencia se descubre desde el documento sin abrir preferencias generales. Nunca se atribuye al proveedor una garantía de exactitud que no tiene.

## 4. Presentación del significado

### H23 · P2 · El análisis se convierte fácilmente en un bloque de texto · S

**Observación.** Un resumen largo ocupa toda la altura de lectura; temas, metáforas y otras lecturas quedan debajo del primer pliegue. Se utilizó un texto repetitivo deliberadamente para probar densidad, no para juzgar la salida real del modelo. Captura 05.

**Impacto.** El usuario no ve la estructura del resultado ni sabe cuánto más hay. La aplicación pierde capacidad de explicar de forma progresiva.

**Cambio recomendado.** Un resumen breve inicial, temas claramente titulados y secciones con jerarquía. Si el proveedor devuelve un resumen largo, mantenerlo accesible mediante «Leer explicación completa» sin reemplazarlo ni inventar otro texto.

**Aceptación.** En una ventana normal se reconoce que hay resumen, temas y detalles sin tener que llegar al final del párrafo. Validar con resultados breves y extensos reales.

### H24 · P2 · Los nombres de secciones no explican bien el valor de abrirlas · V/S

**Observación.** «Metáforas», «Otras lecturas» y «Matices» son homogéneos y discretos, sin número de elementos ni breve adelanto. «Matices» es especialmente ambiguo para avisos de incertidumbre. Capturas 06, 46 y 47.

**Impacto.** Se reduce el descubrimiento de contenido importante y se exige abrir cada sección para conocer su propósito.

**Cambio recomendado.** Etiquetas concretas: «Imágenes y metáforas · 2», «Interpretaciones alternativas · 1» y «Límites de esta interpretación». Mostrar un indicio de contenido y evitar desplegables vacíos cuando las listas no aportan nada.

**Aceptación.** Cada encabezado permite predecir qué contiene. Una lista vacía se omite o se explica sin un control que promete contenido inexistente.

### H25 · P2 · Los límites de la interpretación aparecen demasiado tarde · S/C

**Observación.** «Interpretación de IA» está al final, en tamaño 11, y los avisos dentro de Matices empiezan plegados. Un lector puede consumir todo el resumen sin ver la naturaleza tentativa de la explicación.

**Impacto.** El tono visual puede conferir al análisis más autoridad que la que tiene. No se trata de añadir un aviso largo, sino de encuadrar adecuadamente el contenido.

**Cambio recomendado.** Etiqueta breve y permanente al inicio: «Una interpretación posible, generada por IA». Mantener matices concretos junto a las afirmaciones que cualifican cuando el esquema lo permita.

**Aceptación.** La condición interpretativa es visible antes de leer el resultado y sigue siendo comprensible sin abrir un desplegable.

### H26 · P3 · Las listas y las citas no aprovechan una jerarquía editorial consistente · V/S/C

**Observación.** Los temas son una cadena con viñetas en dorado; metáforas y explicaciones comparten gran parte del tratamiento; las alternativas usan otro bloque concatenado. No hay una separación visual consistente entre cita, explicación y tema.

**Impacto.** Escanear el análisis exige leer más. El dorado termina haciendo de marcador genérico de cualquier texto especial.

**Cambio recomendado.** Temas en una lista claramente titulada, citas originales diferenciadas de explicaciones y espaciado uniforme entre ideas. Usar el acento para selección y foco, con menos énfasis en todos los temas a la vez.

**Aceptación.** Un vistazo permite identificar qué es verso citado, qué es explicación y qué es categoría temática. El contenido sigue siendo accesible como texto estructurado.

## 5. Cuenta, modelos y configuración de IA

### H27 · P1 · El alta de cuenta expone demasiadas etapas simultáneas · V/S

**Observación.** La misma página presenta proveedor, cuenta, selección de cuenta, conexión, otra cuenta, registro pendiente, consulta de modelos, selección, guardado, ajuste externo de créditos, habilitación y consumo. Muchos controles aparecen desactivados antes de que sean relevantes. Capturas 18, 19 y 37–39.

**Impacto.** La tarea parece técnica y frágil. La persona debe descubrir el orden correcto leyendo párrafos repartidos en una página larga.

**Cambio recomendado.** Flujo de tres etapas visibles: conectar cuenta, elegir modelo y habilitar consultas. Mostrar solo la acción necesaria para la etapa actual, con un resumen claro de las anteriores. Dejar varias cuentas y reparación de registros como opciones avanzadas.

**Aceptación.** Un usuario nuevo identifica una única siguiente acción principal en cada etapa. Volver a abrir la cuenta muestra el progreso real, no la misma página inicial completa.

### H28 · P1 · Los dos modos de IA no funcionan visualmente como una selección · V/S/C

**Observación.** «Usar mi plan de ChatGPT» y «Elegir API key · de pago» son botones con tratamiento similar. No hay un estado seleccionado evidente. La tarjeta ChatGPT permanece visible aunque se pueda cambiar el modo, y el acceso final a API está al pie de la página.

**Impacto.** La persona puede no saber qué proveedor está activo o si basta con abrir una opción para haber cambiado de modo. En una decisión que puede implicar coste, la ambigüedad es especialmente inconveniente.

**Cambio recomendado.** Selector explícito con modo activo, descripción breve y contenido condicionado al modo. API de pago debe exigir una elección consciente y conservar esa distinción en la pantalla principal.

**Aceptación.** Antes de generar se puede identificar el modo activo sin desplazarse por configuración. No hay cambio automático de ChatGPT a API.

### H29 · P2 · Se muestran selectores vacíos y acciones sin precondición útil · V/S/C

**Observación.** Sin cuentas hay un ComboBox vacío y «Usar cuenta seleccionada» habilitado. Otros botones se desactivan, pero siguen ocupando espacio. Sin catálogo, el selector de modelo es una caja vacía sin contenido ilustrativo. Captura 18.

**Impacto.** Se invita a interactuar con controles que no pueden aportar nada y se dificulta distinguir «no hay datos», «aún no se han cargado» y «hay un problema».

**Cambio recomendado.** Ocultar la selección de cuentas cuando no existe ninguna y cuando hay una sola mostrarla como resumen. Usar estados explícitos en el catálogo: pendiente de cargar, cargando, sin modelos y error recuperable.

**Aceptación.** No se puede pulsar «Usar cuenta seleccionada» sin selección válida. Un selector nunca parece haber perdido su texto por un fallo gráfico.

### H30 · P2 · La identidad de cuenta añade códigos internos innecesarios · S/C

**Observación.** La etiqueta conectada incluye correo, un fragmento hash de ocho caracteres y «Conectada», y se repite encima y dentro del selector. Captura 37.

**Impacto.** Se refuerza la sensación de herramienta técnica. El identificador puede parecer un error o algo que el usuario deba verificar.

**Cambio recomendado.** Identificar la cuenta con correo y, si hace falta, espacio o nombre distinguible. Reservar el código para soporte o detalle avanzado. Mostrar una única cuenta activa sin duplicación textual.

**Aceptación.** Se distingue entre cuentas de forma humana. Los códigos solo aparecen al pedir un diagnóstico, sin comprometer la separación interna de identidades.

### H31 · P2 · La relación con Codex condiciona innecesariamente el lenguaje de producto · V/S/C

**Observación.** Se pide elegir «la misma cuenta que utilizas en Codex» y se habla de Git, SQLite y DPAPI en la configuración normal. Parte de esa información es útil técnicamente, pero no explica la tarea musical.

**Impacto.** Una persona que conozca ChatGPT y Spotify pero no Codex puede pensar que necesita otra aplicación o un requisito adicional.

**Cambio recomendado.** Hablar de «tu cuenta de ChatGPT» y explicar consecuencias prácticas: dónde se abre el acceso, qué se envía y cómo se guarda. Solo conservar una relación con Codex si es un requisito real del producto y entonces explicarlo de forma precisa.

**Aceptación.** La configuración puede entenderse sin saber qué son Git, DPAPI, SQLite o Codex. La información técnica sigue disponible en ayuda avanzada.

### H32 · P2 · Los errores de autenticación mezclan ayuda con diagnóstico de protocolo · S/C

**Observación.** El fallo sintético de acceso muestra en el mensaje principal «HTTP 403; etapa Consent; código None». Captura 38. Estos valores no permiten decidir qué hacer mejor que un mensaje humano breve.

**Impacto.** Aumenta la sensación de fallo grave y el usuario debe interpretar términos internos. «None» incluso puede parecer que no existe diagnóstico.

**Cambio recomendado.** Primera línea: qué pasó y acción posible. En un desplegable: etapa y código, con un botón para copiar solo diagnóstico saneado. No mostrar tokens, cuerpos remotos ni URLs de retorno.

**Aceptación.** Un acceso denegado se entiende sin leer códigos HTTP. El soporte conserva datos suficientes sin exponer secretos.

### H33 · P1 · Rehabilitar consultas cada sesión necesita un recorrido mucho más corto · V/S/C

**Observación.** Una cuenta conectada puede quedar con consultas pausadas al iniciar y requiere confirmar un ajuste externo. El motivo está explicado en texto largo, mientras la pantalla principal deja el CTA desactivado.

**Impacto.** El coste de configuración se convierte en fricción de uso diario, no solo de incorporación. «Ya conecté mi cuenta» no implica para la persona «tengo que completar otra página para usarla hoy».

**Cambio recomendado.** Conservar el requisito de seguridad de sesión, pero ofrecer desde el bloqueo un acceso directo a una confirmación breve, con cuenta y modelo ya resumidos. No eliminar ni automatizar la confirmación de créditos; la app no puede verificar el ajuste externo.

**Aceptación.** Una persona que vuelve a la app entiende el motivo del bloqueo y llega directamente al paso pendiente. No se promete cero cargos sin evidencia.

### H34 · P2 · La pantalla llamada «Cuenta y consumo» no ofrece un resumen de uso orientado a tareas · V/S/C

**Observación.** Una gran parte del apartado de consumo explica qué son los tokens y qué no se puede deducir de ellos. El contador diario local está en otra ventana y el uso real del plan exige salir a ChatGPT. El pie principal muestra un número técnico aislado.

**Impacto.** El título promete una visión que la pantalla no puede proporcionar. La persona puede buscar un saldo o presupuesto inexistente.

**Cambio recomendado.** Separar «Conexión» de «Actividad de Song Sense»: últimas consultas y solicitudes locales si existe información fiable. Para el plan, un enlace claro «Ver límites en ChatGPT». No convertir tokens en porcentaje ni presentar el límite local como presupuesto.

**Aceptación.** Se distingue qué mide la app, qué comunica el proveedor y qué solo puede verse fuera. Una ausencia de datos se muestra como desconocida.

### H35 · P2 · Los ajustes de API exigen conocimiento previo del modelo · V/S/C

**Observación.** El campo «ID del modelo» es libre y no tiene ejemplo ni ayuda breve para obtener un identificador válido. La validación técnica existe, pero el formulario no ayuda a introducir el dato correcto. Captura 20.

**Impacto.** Se traslada al usuario la investigación de un detalle técnico y se dificulta distinguir sintaxis válida de compatibilidad real.

**Cambio recomendado.** Ayuda contextual con ejemplo claramente ilustrativo y acceso a la documentación del proveedor; explicar que el modelo debe estar disponible en su cuenta. Un catálogo sería una mejora posible, pero no se debe inventar una selección automática ni sustituir un modelo fallido.

**Aceptación.** El usuario sabe dónde encontrar el dato y recibe un error específico para el campo. Los ejemplos no se guardan ni se usan automáticamente como configuración real.

### H36 · P2 · La validación de API comunica errores globales, no decisiones concretas · C

**Observación.** Límite no entero o fuera de rango, modelo inválido y falta de aceptación pueden acabar como InvalidSettings y un mensaje global. El límite es un TextBox ordinario y la interfaz no vincula claramente el mensaje con el campo que hay que corregir.

**Impacto.** Se vuelve a revisar todo el formulario por un error localizado. La persona puede perder tiempo con la clave cuando el fallo está en otro campo.

**Cambio recomendado.** Error junto al campo, resumen breve si hay varios y foco al primero inválido. Mantener entrada por teclado y pegado, con explicación del rango; un control numérico por sí solo no sustituye la validación.

**Aceptación.** Probar vacío, 0, 101, decimal, letras y espacios. Cada caso indica qué corregir y conserva el resto de preferencias válidas. Este hallazgo se sustenta en código; no se cambió una configuración personal de API.

### H37 · P2 · Guardar y probar no comparten una explicación suficientemente visible · V/S/C

**Observación.** «Probar conexión · de pago» utiliza ajustes guardados, no lo escrito sin guardar; la explicación está en un tooltip. Ambos botones se encuentran al final de un formulario largo y aparecen como acciones relacionadas. Capturas 20–21.

**Impacto.** La persona puede creer que está probando el modelo o la clave que acaba de escribir, cuando aún se usan otros valores. En caso de coste, importa saber qué configuración se va a enviar.

**Cambio recomendado.** Estado visible «Cambios sin guardar». Después de guardar, ofrecer una prueba opcional indicando el modelo guardado y que puede generar coste. No fusionar guardar con una llamada de red implícita.

**Aceptación.** Antes de pulsar Probar se sabe exactamente qué configuración se utilizará. Guardar continúa sin enviar consultas.

### H38 · P2 · Las acciones finales y sus resultados quedan fuera de vista · V/S/C

**Observación.** En API, guardar, probar, cancelar, contador y mensaje están después de avisos extensos. En cuenta, habilitar y pausar se encuentran mucho más abajo que conexión y modelos. No hay una zona estable para la acción de la etapa actual.

**Impacto.** Hay desplazamientos de ida y vuelta para introducir, ejecutar y verificar. Un mensaje nuevo puede quedar fuera del encuadre que la persona está mirando.

**Cambio recomendado.** Formulario con contenido desplazable y pie de acciones estable, ajustado también al mínimo. Estado de guardado cerca de la acción que lo causó; separar ayuda ampliada de los campos.

**Aceptación.** En 600×780 y en el mínimo soportado se ven la acción principal y su resultado sin buscar el final de una página. Corregir también el texto y el orden de foco de cualquier barra fija.

### H39 · P2 · La eliminación de clave aparece incluso cuando no hay ninguna · V/S/C

**Observación.** API muestra «No hay una clave guardada» y, a la vez, «Eliminar clave y desactivar IA» habilitado. `DeleteKey` no pide confirmación. La revisión se hizo con un almacén sintético vacío; no se pulsó el borrado de credenciales.

**Impacto.** Hay ruido de una acción imposible o redundante. Cuando sí hay clave, un clic puede obligar a recuperarla externamente para volver a configurar.

**Cambio recomendado.** Mostrar «Eliminar clave» únicamente cuando existe una. Separar pausa de IA de eliminación de la credencial, y explicar el efecto antes de borrar cuando no haya recuperación sencilla.

**Aceptación.** Con almacén vacío no se ofrece eliminar nada. Desactivar consultas no exige borrar la clave. El borrado relevante tiene una salvaguarda proporcionada.

## 6. Selección y edición de letras

### H40 · P1 · El editor describe incorrectamente cuánto se conserva la letra · V/S/C

**Observación.** El texto «Los cambios se conservan durante esta sesión» permanece en LyricsEditorWindow, aunque la app actual guarda letras y selecciones en caché local. Capturas 12–17 y 43–45. El arnés sin caché es una configuración de revisión; el problema se confirma comparando el texto con la composición de producción y las escrituras de `ConfirmLyrics`.

**Impacto.** La persona puede creer que perderá su trabajo al cerrar o no entender por qué sigue apareciendo días después. También altera sus expectativas de almacenamiento privado.

**Cambio recomendado.** «La letra se guardará en este dispositivo para esta canción» y estado posterior de guardado o fallo. Explicar por separado la política de caducidad si afecta a la expectativa de conservar una corrección manual.

**Aceptación.** La descripción concuerda con el comportamiento tras reiniciar. No se promete persistencia si una escritura falló.

### H41 · P2 · El editor abre en una lista vacía aunque la tarea sea corregir texto · V/S

**Observación.** Con una letra existente y sin candidatos, Cambiar letra abre Versiones. Se ve una lista vacía grande, una vista previa sin contenido y Confirmar versión desactivado; Manual está al lado. Captura 43.

**Impacto.** Se empieza en una tarea que no puede realizarse. Corregir una letra ya preparada exige descubrir otra pestaña.

**Cambio recomendado.** Abrir Manual cuando no hay candidatos, y Versiones cuando hay una selección pendiente. Renombrar el acceso principal a «Corregir letra» cuando ese sea el contexto.

**Aceptación.** La primera vista abierta es útil en cada contexto y no se reserva la mayor parte del diálogo a una lista vacía.

### H42 · P2 · La comparación de versiones tiene una vista previa demasiado pequeña · V/S

**Observación.** Los candidatos usan una lista con dos partes de altura frente a una parte para la vista previa. Con títulos largos solo entran unos pocos elementos; la letra aparece en una ranura de pocas líneas y con scroll adicional. Capturas 12–13.

**Impacto.** Elegir una versión por su contenido exige desplazarse continuamente entre lista y texto. La información útil queda repartida en dos ventanas de lectura diminutas.

**Cambio recomendado.** Lista más compacta con título, álbum y duración; vista previa claramente titulada y suficientemente alta. En ancho amplio, lista lateral y documento; en compacto, elegir y abrir vista previa completa antes de confirmar.

**Aceptación.** Puede compararse una estrofa entera y verificar álbum/duración sin pelear con dos áreas de scroll pequeñas. Se preservan metadatos completos mediante detalle accesible.

### H43 · P2 · El error de edición se queda obsoleto al corregir el contenido · V/S/C

**Observación.** Guardar vacío muestra «Introduce una letra que no esté vacía». Después de introducir 21.000 caracteres, el mensaje de vacío sigue presente hasta volver a guardar. El contador tampoco cambia a un estado de error visual al superar 20.000. Capturas 15–17.

**Impacto.** La aplicación contradice lo que el usuario acaba de hacer. El formulario muestra un dato correcto y un error que ya no lo es.

**Cambio recomendado.** Limpiar o recalcular el error al editar; contador y explicación de exceso junto al campo. Mantener el texto completo, sin truncarlo silenciosamente. El botón puede bloquearse con una razón legible, o permitir validar si el feedback inmediato ya explica el problema.

**Aceptación.** El error de vacío desaparece al introducir contenido y el exceso se identifica antes de guardar. Un texto mayor nunca se recorta sin decisión de la persona.

### H44 · P1 · Cambiar de canción invalida el editor y deja al usuario sin recuperación de su trabajo · S/C

**Observación.** Con el editor abierto, un cambio de canción desactiva el texto y Guardar letra, y pide cerrar y volver a abrir. El texto permanece visible, lo cual es positivo, pero no hay «Copiar borrador», «Seguir editando esta canción» ni recuperación de borrador. Captura 45.

**Impacto.** En una app que sigue Spotify automáticamente, este cambio puede producirse mientras la persona corrige una estrofa. La protección contra guardar en la canción incorrecta es correcta; el tratamiento del trabajo iniciado es insuficiente.

**Cambio recomendado.** Vincular el borrador a la canción original y conservarlo al quedar obsoleto. Ofrecer copiarlo o continuar en un contexto explícito, sin aplicar cambios a la canción nueva por error. Si se bloquea el guardado, explicar por qué y cómo recuperar el texto.

**Aceptación.** Ninguna edición se pierde por un cambio automático de canción ni se guarda bajo otra canción. El usuario dispone de una acción para conservar el borrador.

### H45 · P2 · Cerrar formularios no contempla claramente cambios sin guardar · C/S

**Observación.** LyricsEditorWindow no implementa comprobación de cambios al cerrar. AiSettingsWindow cancela y limpia la clave al cerrar, pero tampoco comunica diferencias sin guardar. La invalidación del editor se observó; los distintos casos de cierre con borradores deben validarse específicamente con usuarios.

**Impacto.** Cerrar puede significar «he terminado» para una persona acostumbrada a autosalvado, mientras el programa interpreta «descartar». La UI no establece una convención uniforme de guardado.

**Cambio recomendado.** Definir una convención por tipo de dato: preferencias autosalvadas, formulario con Guardar/Cancelar y editor con borrador. Advertir solo cuando hay cambios relevantes, sin multiplicar confirmaciones vacías. No conservar claves nuevas en texto legible para recuperar un formulario.

**Aceptación.** Cerrar sin cambios es inmediato; cerrar con edición nueva tiene un comportamiento explícito y consistente. Las credenciales mantienen sus protecciones.

## 7. Sistema visual, accesibilidad y escritorio

### H46 · P2 · El sistema de componentes está incompleto y diverge entre ventanas · V/S/C

**Observación.** Botones y pestañas tienen plantillas propias; CheckBox y Slider conservan gran parte de su aspecto nativo; los ComboBox se definen por separado en MainWindow y ConnectionWindow; el scroll principal es fino y oscuro, mientras editor y ajustes muestran barras claras nativas. El foco del editor puede aparecer azul mientras otros componentes usan dorado.

**Impacto.** La aplicación parece una suma de implementaciones. Esa duplicación facilita errores como el Foreground del consentimiento y encarece mantener estados consistentes.

**Cambio recomendado.** Recursos compartidos para campos, selectores, casillas, slider, expanders, menús y scroll. Definir estados normal, hover, foco, pulsado, seleccionado, inválido, ocupado y desactivado. Conservar semántica y operabilidad nativas al cambiar la presentación.

**Aceptación.** El mismo componente se reconoce igual en las cuatro ventanas. Una corrección de contraste o foco se aplica desde un único recurso, sin romper teclado o UI Automation.

### H47 · P2 · El contraste del texto base es bueno, pero las formas interactivas son poco distinguibles · V/S/C

**Observación.** Los colores definidos producen aproximadamente 15,32:1 para Ink/Surface, 8,72:1 para Muted/Surface y 10,79:1 para Gold/Surface. En cambio Outline/Surface es 1,75:1 y Raised/Canvas, 1,19:1. Son cálculos sobre los colores fuente, sin antialiasing ni opacidades; no un certificado de contraste completo.

**Impacto.** La lectura base no necesita una paleta más clara, pero bordes de campos, barras de scroll y límites entre superficies son muy sutiles. Además, el dorado sirve simultáneamente para selección, éxito, progreso, error y aviso.

**Cambio recomendado.** Mantener la paleta base, reforzar las señales necesarias de interacción y añadir icono más texto para estados. Medir cada combinación activa real. Como referencia de diseño, WCAG plantea 4,5:1 para texto normal; componentes inactivos y decoración tienen excepciones, y un borde decorativo no se evalúa automáticamente como un control esencial. Véase [W3C sobre contraste](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum).

**Aceptación.** Se distingue un campo editable y un control enfocado sin depender de una diferencia mínima de fondo. Error y éxito no se confunden solo porque ambos estén escritos en dorado. Revisar contraste no textual por función, no aplicando 3:1 indiscriminadamente a todos los bordes.

### H48 · P2 · Los anuncios accesibles y la escala global necesitan una validación propia · C/P

**Observación.** Hay nombres accesibles útiles en varios campos y un foco global, lo cual es una buena base. Sin embargo, solo algunos mensajes declaran anuncios de región viva; los estados principales cambian mediante TextBlock y hay plantillas personalizadas. El slider modifica lectura, no controles, avisos ni toda la interfaz.

**Impacto.** Una persona con lector de pantalla puede no recibir el mismo feedback que una persona que mira la tarjeta. Aumentar la letra de lectura no garantiza que pueda leer el consentimiento o manejar la configuración.

**Cambio recomendado.** Auditoría de Narrador con tareas, nombres y estados; anunciar progreso terminado, error y bloqueo sin interrumpir continuamente. Validar foco tras cerrar ventanas, orden Tab/Shift+Tab, cambios de pestaña, flechas y teclas de lectura. Probar escalado de Windows real al 100/125/150/200 % y alto contraste.

**Aceptación.** Completar conectar/configurar, elegir letra, leer y recuperarse de un error sin ratón y con Narrador. No se da por logrado por haber inspeccionado el árbol UI Automation. Esta auditoría deja esa aceptación real pendiente.

### H49 · P1 · Cerrar en bandeja es una decisión importante que la ventana actual no explica · C/V

**Observación.** La composición de App oculta la ventana con X o Alt+F4 y mantiene el proceso. Salir está dentro de Ajustes y en la bandeja. El texto explicativo está en el tooltip del icono; no hay una explicación visible equivalente en el XAML actual. El menú nativo de bandeja se revisó por código, no se validó visualmente en esta sesión.

**Impacto.** El usuario puede creer que la app ha terminado cuando sigue detectando canciones. Si el icono queda bajo el área de iconos ocultos, tampoco resulta obvio cómo recuperarla o salir.

**Cambio recomendado.** Explicar una vez de forma breve «Song Sense seguirá en la bandeja» y ofrecer una preferencia clara de comportamiento de cierre. Mantener Salir localizable desde un menú de aplicación. No mostrar una alerta en cada cierre.

**Aceptación.** La primera experiencia de cierre se entiende; se puede recuperar y terminar la aplicación sin conocer de antemano la bandeja de Windows. Validar el menú real, la segunda apertura y cierre con operaciones activas.

### H50 · P2 · La especificación visual y la entrega local no describen una única experiencia actual · C

**Observación.** DISENO.md contiene reglas antiguas junto a nuevas: pestaña Letra frente a Letra y traducción, columnas paralelas frente a estrofas, preservación de scroll frente a reinicio y explicación del cierre que no aparece en el pie actual. La copia del escritorio y la compilación tampoco coinciden en hash.

**Impacto.** Las revisiones pueden dar por comprobada una experiencia que pertenece a otra versión. Diseñador, desarrollador y usuario no tienen una referencia única para decidir si un cambio mejora o regresa.

**Cambio recomendado.** Consolidar una especificación de estado actual con matriz de pantallas/estados y capturas fechadas. Mostrar versión de aplicación en Acerca de. Vincular cada validación visual a una compilación identificable. Tratar la actualización de la copia de escritorio como una acción separada.

**Aceptación.** Cada regla describe la UI que se entrega y tiene una comprobación reproducible. Actualizar documentación no sustituye corregir el producto ni publicar una nueva versión.

## 8. Galería de evidencias seleccionadas

Las imágenes son capturas de las ventanas reales. El título «UI audit · synthetic» identifica el arnés; no forma parte de la propuesta de producto. Los textos de IA y de canciones sintéticas se usan únicamente para evaluar estructura y estados. La galería principal evita reproducir las letras de canciones observadas en la instancia personal.

### Primer inicio sin una siguiente acción viable

![Inicio sin canción y búsqueda desactivada](../artifacts/uiux-audit-2026-10-21/03-empty.png)

### Lectura bilingüe y proporción de cabecera

![Original y traducción por estrofas](../artifacts/uiux-audit-2026-10-21/04-ready-translation.png)

### Ventana mínima con texto ampliado

![Ventana mínima con texto de 24 puntos](../artifacts/uiux-audit-2026-10-21/09-narrow-text24.png)

### Consumo incorrecto después de un fallo sin datos

![Error de IA con falso cero tokens nuevos](../artifacts/uiux-audit-2026-10-21/22-ai-offline.png)

### Instrumental presentada como letra lista

![Instrumental con instrucción de traducción imposible](../artifacts/uiux-audit-2026-10-21/23-instrumental.png)

### Cuenta: demasiadas etapas al mismo tiempo

![Pantalla inicial de cuenta y consumo](../artifacts/uiux-audit-2026-10-21/18-account-top.png)

### Cuenta: confirmación de créditos casi ilegible

![Parte inferior con texto negro en el consentimiento](../artifacts/uiux-audit-2026-10-21/19-account-bottom.png)

### API: formulario y aviso concentran demasiada información

![Formulario de API sin clave guardada](../artifacts/uiux-audit-2026-10-21/20-api-top.png)

### Editor: selección con previsualización reducida

![Selector de versiones y vista previa](../artifacts/uiux-audit-2026-10-21/13-editor-preview.png)

### Editor: error obsoleto mientras el contenido ya ha cambiado

![Contador superior al máximo con mensaje de vacío anterior](../artifacts/uiux-audit-2026-10-21/16-editor-over-limit.png)

### Editor invalidado al cambiar la canción

![Edición bloqueada sin recuperación explícita de borrador](../artifacts/uiux-audit-2026-10-21/45-editor-stale.png)

## 9. Propuesta de experiencia objetivo

### Ventana de uso diario

La cabecera debe responder en pocos segundos a tres preguntas: qué canción estoy viendo, si se sigue actualizando y qué puedo hacer ahora. Una fila compacta de título y artista, un estado explícito y una acción contextual bastan. El estado de cuenta puede ser un pequeño acceso con «Conectada», «Configurar» o «Consultas pausadas», sin ocupar una tarjeta de explicación.

Debajo, la navegación de lectura permanece estable. «Letra» puede adaptarse a «Letra y traducción» cuando realmente hay ambas, o mantener un nombre estable con un selector interno; hay que probar cuál resulta más predecible. Junto al documento aparecen el idioma destino y las opciones de tamaño. El contenido ocupa el resto de altura y tiene un único scroll.

Los estados de fallo aparecen donde ocurrió la acción. No se obliga a abrir preferencias para reintentar una consulta o corregir una configuración. «Detalles técnicos» permanece accesible, separado de los ajustes de lectura.

### Acción contextual de la canción

| Situación | Estado visible propuesto | Acción principal propuesta |
|---|---|---|
| No hay Spotify/canción | Abre Spotify y reproduce una canción | Instrucción explícita; no una búsqueda bloqueada |
| Se busca la letra | Buscando letra… | Cancelar búsqueda |
| Varias versiones | Elige la letra correcta · 5 versiones | Elegir versión |
| No hay coincidencia | No encontramos una letra para esta versión | Añadir letra; buscar de nuevo como secundaria |
| Fallo de red | No se pudo conectar con el servicio | Reintentar |
| Instrumental | Esta versión es instrumental | Elegir otra versión, si procede |
| Letra lista, cuenta pendiente | Conecta tu cuenta para obtener la explicación | Configurar IA |
| Letra lista, confirmación de sesión pendiente | Consultas pausadas en esta sesión | Revisar habilitación |
| Letra lista y preparada | Puedes generar en Español | Traducir y explicar |
| Consulta activa | Analizando la letra… | Cancelar, con explicación de consumo disponible |
| Resultado guardado | Resultado guardado en este dispositivo | Leer; regenerar en menú con efecto explicado |
| Límite local alcanzado | Límite de hoy: 20/20 | Ver detalle del límite; no prometer cuota remota |
| Seguimiento pausado | Mostrando esta canción · seguimiento pausado | Reanudar seguimiento |

### Cuenta y configuración

La primera configuración se organiza en una secuencia corta. Primero se elige el modo. En ChatGPT, conectar la cuenta lleva a una confirmación de identidad, después a elegir un modelo y finalmente al requisito de habilitación de la sesión. En visitas siguientes solo se muestra el paso pendiente y un resumen de lo ya preparado.

Las advertencias se redactan por decisión: qué se envía, qué puede generar coste, qué significa el contador local y qué no puede verificar Song Sense. El detalle de DPAPI, almacenamiento, retención y protocolos se conserva en ayuda específica. No hace falta ocultar riesgos; hace falta colocarlos donde son comprensibles y relevantes.

La ruta de API conserva una elección explícita de pago, un formulario con errores locales y una prueba opcional de la configuración guardada. Nunca se envía una solicitud al guardar ni se sustituye el modelo por otro sin decisión del usuario.

### Dirección visual

Mantendría fondo cálido oscuro, dorado moderado y tipografía editorial para momentos concretos. Reduciría la cantidad de paneles bordeados y la altura de tarjetas. Segoe UI puede sostener controles y prosa funcional; Georgia puede conservar personalidad en marca, títulos y, si las pruebas lo apoyan, letra original. La identidad no depende de usar serif en toda la lectura.

Una escala inicial para explorar: controles 14–15 DIP, ayudas 12–13 DIP cuando no son esenciales y lectura configurada en puntos con interlineado proporcional. Estos valores deben probarse con escalado real. No se deben copiar de forma rígida si producen controles pequeños a usuarios concretos.

Los estados necesitan forma y palabras: éxito con marca y texto, advertencia con explicación, error con acción de recuperación, progreso con indicador indeterminado y etiqueta. El color complementa, pero no sustituye, el significado. Las animaciones deben servir para continuidad, respetar preferencias del sistema y no retrasar una app de lectura.

## 10. Orden de ejecución recomendado

| Fase | Alcance | Hallazgos principales | Condición para cerrar |
|---|---|---|---|
| 1. Veracidad y protección | Consumo, contraste crítico, estados imposibles y borrado | H01–H11, H40, H44 | Ningún caso de la matriz informa de forma incorrecta o invita a una acción imposible; borrados y borradores protegidos |
| 2. Tarea diaria | Cabecera compacta, lectura, scroll e idioma | H12–H22 | Se puede leer y alternar pestañas cómodamente en normal y compacto, conservando contexto y posición |
| 3. Incorporación y cuenta | Secuencia de configuración y formularios | H27–H39 | Usuario nuevo y usuario recurrente completan su paso pendiente sin instrucciones externas |
| 4. Calidad editorial y componentes | Significado, controles compartidos y estados visuales | H23–H26, H46–H47 | Jerarquía coherente y todos los componentes con estados completos |
| 5. Aceptación de escritorio | Narrador, escala, bandeja, monitores y versión | H48–H50 | Evidencia real de las combinaciones de entorno acordadas y documentación consolidada |

No empezaría por cambiar la paleta ni por redibujar el logo. Tampoco lanzaría un rediseño de todas las pantallas sin cerrar primero el modelo de estados. De lo contrario, una interfaz más atractiva seguirá mostrando mensajes incorrectos.

## 11. Plan de aceptación que falta para hablar de experiencia completa

### Pruebas con personas

Realizar una ronda formativa inicial con aproximadamente 5–6 participantes, combinando personas que conocen herramientas de IA con otras que solo usan Spotify. Esa muestra sirve para descubrir problemas, no para representar estadísticamente a todos los usuarios. Evitar explicarles previamente la arquitectura de la aplicación.

Tareas propuestas:

1. Abrir por primera vez sin canción y conseguir ver una letra.
2. Comprender una canción con cuenta aún pendiente de habilitación.
3. Elegir entre varias versiones y verificar que corresponde a su grabación.
4. Corregir un verso y explicar qué ocurrirá con la corrección al cerrar.
5. Leer una traducción, consultar Significado y volver al mismo verso.
6. Cambiar el idioma y explicar si se ha enviado una nueva consulta.
7. Interpretar un fallo sin consumo comunicado y decidir si reintentar.
8. Pausar seguimiento, cambiar canción en Spotify y reconocer qué se está leyendo.
9. Cerrar, recuperar y terminar la aplicación.
10. Localizar cómo eliminar contenido guardado y explicar qué se perdería, sin ejecutar el borrado sobre datos valiosos.

Registrar éxito sin ayuda, tiempo hasta la primera acción correcta, intentos fallidos, retrocesos, comprensión del estado y percepción de esfuerzo. Un objetivo razonable para la siguiente iteración es que la gran mayoría complete las tareas principales sin explicación del moderador; fijar umbrales concretos después de una línea base. No inventar métricas de la versión actual: esta revisión no es una prueba con usuarios.

### Matriz técnica de aceptación visual

| Dimensión | Combinaciones pendientes o a repetir tras corregir |
|---|---|
| Escalado | Windows 100 %, 125 %, 150 % y 200 % reales; no simularlos solo con tamaño de fuente |
| Tamaño | Mínimo elegido, 600×700, 860×780, maximizada y acoplada a un lateral |
| Monitores | Un monitor, dos con escalas diferentes, desconexión del secundario y restauración |
| Accesibilidad | Solo teclado, Narrador, alto contraste y texto del sistema ampliado |
| Contenido | Títulos/artistas largos, estrofas largas, una línea sin espacios, Unicode y letra de 20.000 caracteres |
| Idiomas | Resultados reales en seis idiomas, letras mixtas y original igual al destino; validar traducción y alineación por separado |
| Cuenta | Cuenta nueva, conectada, sin permiso, confirmación pendiente, catálogo vacío, error de catálogo, cambio de cuenta y sesión expirada |
| Red | Sin conexión antes de consultar, pérdida durante consulta, timeout, limitación, fallo remoto y recuperación |
| Consumo | Caché, consumo completo, desconocido, error con consumo comunicado y cancelación tras envío |
| Persistencia | Reinicio con letra manual, resultado guardado, cuota agotada, error de preferencias y fallo de escritura de idioma |
| Datos | Borrado cancelado, confirmado y error de borrado en fixtures; recuperación de corrupción en copia desechable |
| Escritorio | X, Alt+F4, menú real de bandeja, abrir segunda instancia, salir con consulta pendiente y sin procesos huérfanos |

Los fallos remotos y las operaciones destructivas se prueban primero con transportes simulados y bases desechables. Las aceptaciones reales de cuenta, autorización y consumo requieren un entorno y una cuenta preparados para ello. No hace falta gastar una solicitud real para comprobar un botón, un contraste o el layout de un error.

## 12. Trazabilidad para implementación

| Grupo de hallazgos | Archivos y puntos principales |
|---|---|
| H01, H03–H10 | `MainViewModel.Presentation.cs`: DisplayStatus, DisplayNotice, CompactUsage, ReadingHint y ShowGenerate; `MainViewModel.cs`: condiciones de comandos y DescribeUsage |
| H02, H27–H34 | `ConnectionWindow.xaml`: bloque de proveedor, cuenta, modelos y CheckBox CreditsDisabledConfirmed; `ConnectionViewModel.cs`: precondiciones y mensajes |
| H11, H40 | `MainViewModel.cs`: ConfirmLyrics y DeleteCacheAsync; `App.xaml.cs`: composición con SqliteCacheStore |
| H12–H19, H21–H26 | `MainWindow.xaml` y `MainWindow.xaml.cs`: ApplyReadingLayout, ReflowOptions, SelectionChanged y las plantillas de lectura |
| H20 | TextBlocks de lectura en `MainWindow.xaml`; evaluar representación seleccionable sin perder estructura |
| H35–H39, H45 | `AiSettingsWindow.xaml`, `AiSettingsWindow.xaml.cs`, `AiSettingsViewModel.cs` y AiConfigurationService |
| H40–H45 | `LyricsEditorWindow.xaml`, `LyricsEditorWindow.xaml.cs`, `LyricsEditorViewModel.cs` y contratos LyricsEditContext |
| H46–H48 | `App.xaml`, recursos locales de MainWindow/ConnectionWindow y `MainWindow.Preferences.cs` |
| H49 | `App.xaml.cs`: Closing; `DesktopTray.cs`; `MainWindow.Preferences.cs`: ExitApplication y OpenFromTray |
| H50 | `docs/DISENO.md`, `docs/ESTADO_FEATURES.md`, versión mostrada y proceso de copia local |

El arnés de revisión está en `artifacts/uiux-audit-2026-10-21/harness`. No modifica la aplicación de producción. Carga sus recursos visuales y referencia el proyecto de la app; sustituye servicios y almacenamiento de revisión. Su archivo command.txt permite preparar escenarios. Es una herramienta puntual de auditoría, no una nueva feature ni un sustituto de pruebas E2E con servicios reales.

Los árboles de accesibilidad acompañan algunas capturas. En operaciones rápidas, UI Automation puede reflejar el estado anterior mientras la imagen ya muestra el nuevo; se reobservó antes de usar índices para decisiones posteriores. Las capturas posteriores a la animación de menús son las usadas para valorar su aspecto. No se interpreta una captura transitoria como un defecto permanente.

### Referencias externas consultadas

Se han usado como criterios de contraste y comportamiento, no como un certificado de conformidad de esta aplicación WPF:

- [W3C: Understanding Contrast Minimum](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum): referencia para contraste de texto y sus excepciones.
- [Microsoft: Dialogs and flyouts](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/): distinguir mensajes contextuales de interrupciones modales.
- [Microsoft: Dialog controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs): acciones explícitas para decisiones que necesitan confirmación. Sus APIs WinUI no se proponen como reemplazo directo de WPF.

### Entregables y límites de esta intervención

Se entrega este informe, una versión HTML navegable, una galería local de 47 capturas y el arnés usado para reproducir estados. Se conservaron los cambios locales existentes. No se implementaron los rediseños, no se actualizaron ejecutables de escritorio, no se introdujeron claves reales ni se hicieron consultas de IA de pago. No se ejecutaron borrados sobre la base personal ni acciones de autenticación externas. Las observaciones sobre bandeja, Narrador, escalado y servicios reales mantienen los límites indicados en la matriz.
