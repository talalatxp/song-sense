# Plan de mejoras de UI/UX — Song Sense

Fecha: **21 de octubre de 2026**. Estado: **planificado; implementación no iniciada**.

Fuente: [Auditoría UI/UX del 21 de octubre](AUDITORIA_UI_UX_2026-10-21.md), hallazgos H01–H50 y sus secciones 9–12. Este documento convierte las recomendaciones en entregas individuales, decisiones de producto y criterios verificables.

## 1. Revisión y decisiones

El informe distingue correctamente defectos reproducidos, recomendaciones de diseño y aceptaciones pendientes. Sus 280 pruebas y 47 capturas corresponden a su revisión: no equivalen a una aceptación completa de usabilidad. En esta planificación se ha leído el informe, contrastado su trazabilidad con el checkout y revisado las capturas 09, 19, 22 y 45. No se ha repetido la revisión visual completa ni ejecutado la aplicación, autenticación, borrados o consultas de pago.

El código actual confirma varios problemas centrales:

- `MainViewModel.Presentation.cs` busca «0 tokens» dentro de un mensaje y comprime estados distintos en la misma etiqueta.
- Las condiciones de los comandos y los mensajes de bloqueo se calculan por separado; Significado utiliza una instrucción fija.
- `DeleteCacheAsync`, los comandos que lo exponen y el borrado de clave no tienen una confirmación específica en su recorrido actual.
- `MainWindow.xaml.cs` reinicia el scroll al cambiar de pestaña, oculta metadatos y desplaza el contenedor exterior al expandir ajustes.
- El editor conserva el texto de «durante esta sesión», errores de validación obsoletos y el bloqueo de edición cuando cambia la canción.
- Las plantillas de controles están repartidas entre recursos globales y ventanas. La documentación de diseño acumula reglas de distintas versiones.

Se mantiene **Windows, WPF/MVVM, carbón/dorado y las dos claves de sol**. La mejora comienza por estados veraces y protección del trabajo; después reorganiza lectura y configuración. No requiere rehacer los proveedores, cambiar el logo ni sustituir WPF.

### Decisiones de experiencia objetivo

1. Pestañas estables **Letra** y **Significado**. Dentro de Letra: Original, Traducción o Ambos; predeterminado Ambos cuando hay traducción, Original cuando no la hay.
2. Cabecera compacta con canción, artista, reproducción y seguimiento; una acción principal contextual. Álbum, duración y versión disponibles en «Detalles de la canción».
3. Un viewport de lectura por pestaña; preferencias generales en una ventana independiente. Idioma y tamaño permanecen accesibles junto a la lectura. Abrir ajustes conserva posición y contexto.
4. Original y traducción por estrofas relacionadas en un documento vertical, con etiquetas explícitas. Esta iteración no introduce un segundo modo de columnas paralelas.
5. Mínimo objetivo **360×480 DIP**: región de documento de al menos **160 DIP de alto**, excluidos cabecera y pestañas, sin scroll exterior. En 860×780 DIP: lectura de al menos el **60 % del alto útil del cliente** con preferencias cerradas. Son objetivos de producto que deben medirse. Si no se cumplen, el bloque no se cierra; cambiar el mínimo exige registrar una decisión y revisar las preferencias guardadas.
6. Cuenta con selector de modo visible; ChatGPT en tres pasos: conectar, elegir modelo y habilitar la sesión. El usuario recurrente llega al paso pendiente.
7. Borrados mediante confirmación específica, con Cancelar como opción predeterminada. El editor conserva el borrador al cambiar Spotify y permite copiarlo; cerrar con cambios requiere una decisión explícita.
8. X/Alt+F4 conservan inicialmente el comportamiento de bandeja, con explicación de primera vez. Preferencia explícita para cerrar realmente; «Salir» accesible desde el menú de aplicación.

### Contratos que se preservan

- Buscar o seguir una canción, cambiar idioma, guardar ajustes, seleccionar modelo o habilitar la sesión **no genera análisis**. IA solo mediante una acción explícita de generar, regenerar o probar conexión.
- Sin cambio automático de cuenta, modelo, proveedor ni paso de ChatGPT a API de pago. La confirmación de créditos sigue siendo explícita y por sesión; la interfaz no promete cargos cero ni verificación de un ajuste externo que no puede comprobar.
- Contador local, tokens comunicados y límites del proveedor son conceptos separados. Desconocido nunca significa cero.
- Cancelación y revisiones de canción/letra/idioma/cuenta/modelo continúan impidiendo aplicar resultados antiguos al documento activo.
- Caché y preferencias personales se conservan. Los tests usan bases y credenciales sintéticas desechables; los secretos no se incorporan a capturas, diagnóstico copiable, logs ni Git.
- Copiar un fragmento es una acción local explícita; no añade exportación completa ni publicación de letras. Se mantiene la incertidumbre de derechos documentada en la ruta original.
- La numeración y las aceptaciones pendientes de F01–F11 se conservan. Completar una mejora UX no cierra por sí solo una prueba real pendiente de una feature anterior.

## 2. Orden de ejecución

Cada bloque se solicita y desarrolla individualmente. El orden es el de la tabla; cada bloque depende del anterior, salvo la preparación documental descrita abajo. No se comienza el siguiente por iniciativa del agente.

Las dependencias de desarrollo requieren que los contratos y pruebas controlables del bloque previo estén implementados y sin defectos críticos abiertos. Una aceptación externa expresamente reservada a UX14 puede quedar registrada como pendiente sin impedir desarrollar el siguiente bloque cuando se solicite; no permite etiquetar como Cerrado lo que siga sin demostrar. Una pérdida de datos, mensaje de consumo falso o regresión funcional pendiente sí detiene sus dependientes.

| Bloque | Entrega | Hallazgos responsables | Prioridad de entrada |
|---|---|---|---|
| UX01 | Consumo fiable y actividad comprensible | H01, H34 | Error económico de presentación |
| UX02 | Componentes compartidos, contraste y foco | H02, H46, H47 | Consentimiento legible y base común |
| UX03 | Estados, bloqueos y acción contextual | H03–H08, H10 | Tareas bloqueadas o mal explicadas |
| UX04 | Reproducción y seguimiento visibles | H09 | Evitar confundir canciones |
| UX05 | Protección de borrados y credenciales | H11, H39 | Evitar pérdidas accidentales |
| UX06 | Editor, borradores y guardado fiable | H40–H45 | Proteger correcciones manuales |
| UX07 | Ventana centrada en lectura | H12–H14, H16 | Resolver el mínimo y el doble scroll |
| UX08 | Navegación, idioma y procedencia | H15, H21, H22 | Continuidad y confianza al leer |
| UX09 | Lectura seleccionable y escala tipográfica | H17–H20 | Comprensión y comodidad |
| UX10 | Jerarquía del significado | H23–H26 | Resultado escaneable y contextualizado |
| UX11 | Conexión guiada y retorno a la sesión | H27–H33 | Alta y habilitación claras |
| UX12 | API: formulario, validación y prueba explícita | H35–H38 | Configuración sin ambigüedad |
| UX13 | Cierre, bandeja y recuperación de ventana | H49 | Ciclo de vida comprensible |
| UX14 | Aceptación, versión y entrega coherente | H48, H50 | Comprobar la experiencia completa |

H34 se presenta también en la ventana de conexión de UX11; H38 se aplica igualmente al pie de conexión en UX11; la convención de cambios sin guardar de H45 se termina de integrar en API en UX12. Estas dependencias se comprueban de nuevo en UX14; no son hallazgos omitidos ni resueltos dos veces.

### Preparación antes de UX01

- Registrar commit si existe, estado sucio del checkout, SDK, hash SHA-256 de la DLL compilada y hash de la copia de escritorio. El informe ya detecta binarios distintos; sus hashes no identifican por sí solos la diferencia funcional.
- Crear una línea base con build, pruebas existentes y auditoría de privacidad, registrando el número obtenido entonces. **280 es la referencia del informe, no un resultado recién ejecutado por este plan.**
- Conservar cambios locales. Tomar escenarios útiles del arnés de auditoría como fixtures reproducibles, con texto propio y sin red. No incorporar las bases, archivos DPAPI, registros o capturas personales del directorio de auditoría a los tests.
- Cada comparación antes/después usa el mismo escenario, dimensiones, fuente y escala, y un binario identificado. Las evidencias se guardan en `artifacts/uiux-improvements/UXxx/`; su resumen reproducible, en documentación.

## 3. Contrato de cada bloque

### UX01 — Consumo fiable y actividad comprensible

**Alcance:** H01 y H34. Introducir un modelo de presentación de consumo con estados NoSolicitado, EnCurso, Comunicado, NoComunicado y RecuperadoDeCache. Conservar cifras de entrada/salida/total cuando existan, origen real y contexto de solicitud. Los textos se derivan de esos datos y no al revés.

**Comportamiento:** caché real → «Guardado en este dispositivo · 0 tokens nuevos»; respuesta con cifras → mostrarlas; error/cancelación sin cifras → «Consumo no disponible». Comunicado con total cero sigue siendo una respuesta del proveedor, no se convierte en caché. Mostrar última actividad conocida y contador local con fecha; límites del plan mediante enlace externo. No fabricar un historial retrospectivo ni inferir porcentajes o euros.

**Puntos de cambio:** `AiUsage.cs`, `MainViewModel.cs`, `MainViewModel.Presentation.cs`, modelos de presentación y resumen de actividad en las ventanas existentes. UX11 recoloca ese resumen sin recalcularlo.

**Cierre:** tests para no solicitado, pendiente, respuesta, caché, error con cifras, error sin cifras y cancelación; regresión literal del mensaje «no equivale a 0 tokens». Una respuesta tardía no atribuye el consumo al resultado de otra canción. Inspección visual sin consultas de pago.

### UX02 — Componentes compartidos, contraste y foco

**Alcance:** H02, H46 y H47. Extraer recursos semánticos y estilos compartidos para botones, CheckBox, ComboBox, TextBox/PasswordBox, listas, Slider, Expander, menús y barras de scroll. Corregir primero el consentimiento ilegible. Mantener acento dorado; distinguir información, éxito, advertencia, error y progreso mediante texto y forma además del color.

**Implementación:** diccionarios de recursos de App, sin duplicar plantillas por ventana. Cada componente tendrá normal, hover, foco, pulsado/seleccionado, inválido, ocupado y desactivado cuando aplique; conservar teclado, nombres y estados de UI Automation. Alto contraste utiliza recursos del sistema y no fuerza una paleta ilegible.

**Cierre:** galería local de componentes, capturas en las cuatro ventanas, casilla seleccionada/no seleccionada y habilitada/deshabilitada. Medir colores renderizados y opacidades: objetivo 4,5:1 en texto normal esencial y 3:1 en indicadores esenciales de control/foco; decoración y controles inactivos se revisan por función. La comprobación no se presenta como certificación WCAG. Prueba de teclado y anotación de alto contraste real pendiente si no está disponible.

### UX03 — Estados, bloqueos y acción contextual

**Alcance:** H03–H08 y H10. Un único descriptor tipado de preparación determina estado, causa, mensaje, acción principal y recuperación, compartido entre ambas pestañas y las precondiciones de comandos. Debe expresar también modelo ausente, idioma guardándose o con error, sesión pendiente y cancelación.

**Implementación:** separar estado del contenido, disponibilidad de IA y resultado de operaciones. Ante bloqueo, el CTA abre el paso útil; ante ambigüedad, abre directamente Versiones. «Ver diagnóstico» abre un panel secundario desde el aviso, no exige navegar por Ajustes. Un problema de IA conserva la letra. No cambiar lógica de autorización ni reintentar IA automáticamente.

**Cierre:** tests parametrizados para cada fila de la matriz de la sección 4 y para combinaciones conflictivas. Ningún mensaje sugiere una acción oculta o desactivada. Con cuota agotada desde el inicio aparece contador y reinicio local, sin esperar una petición fallida. Diferenciar sin coincidencia, offline, timeout, 429 con espera y formato inválido. Acciones de configurar y revisar habilitación no envían inferencia. Los eventos actualizan también las propiedades de presentación.

### UX04 — Reproducción y seguimiento visibles

**Alcance:** H09. Mostrar por separado «Spotify en pausa» y «Seguimiento pausado · mostrando esta canción», aunque el contenido esté listo. Sustituir «Ahora suena» por «Canción mostrada» cuando corresponda. Añadir reanudar seguimiento cerca del contexto.

**Cierre:** con preferencias cerradas se distingue reproducción, seguimiento y estado de letra/análisis. Tests con pausa/reanudación, cambio de canción durante seguimiento detenido, reapertura y reanudación. Se recupera la observación más reciente y no se genera IA ni cambia de proveedor al reanudar.

### UX05 — Protección de borrados y credenciales

**Alcance:** H11 y H39. Renombrar «Borrar esta canción» a «Eliminar contenido guardado de esta canción». Separar estas acciones del menú de lectura. Confirmación específica para una canción, toda la caché y clave; Cancelar predeterminado. Clave ausente → acción de eliminar oculta; pausar consultas sigue disponible sin eliminarla.

**Confirmación de caché:** indicar letra manual, selección y análisis afectados, con nombre de canción cuando sea individual. Explicar que la operación es local, qué datos conserva y que no modifica Spotify. Identificar el objetivo al abrir el diálogo; si el contexto cambia antes de confirmar, cancelar y pedir revisar de nuevo. El borrado global conserva claves, preferencias y contador. Eliminar clave desactiva su modo conforme al contrato actual, sin habilitar otro.

**Cierre:** bases/secretos sintéticos para cancelar, confirmar, fallo, cambio de canción y eliminación concurrente con una escritura. Nada se borra al abrir/cerrar el aviso; una escritura tardía no resucita contenido eliminado. Ninguna prueba toca caché o credenciales personales.

### UX06 — Editor, borradores y guardado fiable

**Alcance:** H40–H45. Abrir Versiones cuando haya selección pendiente y Manual cuando se corrige una letra sin candidatos. Lista compacta y vista previa de una estrofa completa; en ancho suficiente, lista lateral y documento; en compacto, selección y vista previa completa antes de confirmar.

**Borradores:** mantener el texto editable y vinculado a la canción/revisión original cuando Spotify cambie. Desactivar solo la aplicación a un contexto obsoleto; ofrecer «Copiar borrador». Conservar el contenido en la ventana hasta guardar correctamente o descartar de forma explícita. Copiar no guarda en la canción nueva. Al cerrar con cambios: continuar editando como opción segura o descartar; copiar permite conservar el trabajo antes de decidir. Sin cambios, cierre inmediato. Este bloque no añade autosalvado permanente de borradores ni excepciones a la protección de revisiones.

**Validación y persistencia:** recalcular vacío/exceso al escribir; contador accesible y aviso al superar 20.000, sin truncar. El guardado explica «en este dispositivo» únicamente tras confirmar la escritura. Si falla, mantener borrador y permitir reintentar/copiar; si no hay almacenamiento en un arnés, indicar que el resultado es solo de sesión. Documentar caducidad/evicción real de caché; no prometer conservación indefinida.

**Cierre:** vacío → contenido válido → exceso → corrección; 20.000/20.001 caracteres; escritura fallida; cambio de canción y de revisión durante edición; cierre limpio/sucio; reinicio con una letra realmente guardada. Las credenciales no forman parte de estos borradores. La misma convención Guardar/Cancelar para API se aplica en UX12.

### UX07 — Ventana centrada en lectura

**Alcance:** H12–H14 y H16. Rehacer geometría con cabecera compacta, acción contextual, pestañas estables, barra breve de lectura y región central flexible. Retirar el scroll exterior y el `ScrollToEnd` al abrir ajustes. Crear una ventana de preferencias independiente para lectura, seguimiento, siempre encima y opciones generales. Conexión/API conservan sus ventanas.

**Detalles de canción:** álbum, duración, versión y fuente en acceso explícito, sin obligar a abrir el editor ni Spotify. En ancho reducido, controles secundarios en menú; contexto, pestañas y acceso al CTA permanecen visibles. Ajustes no ocultan ni reinician el documento.

**Cierre:** medir viewport ≥160 DIP en 360×480 y lectura ≥60 % del alto útil en 860×780; comprobar 600×700 y maximizada, textos largos y 24 pt. Un único scroll de documento, preferencias con su propio scroll separado. No modificar el mínimo silenciosamente. Capturas y medidas del área visible, sin presentar una ventana cortada por la pantalla como una prueba satisfactoria.

### UX08 — Navegación, idioma y procedencia

**Alcance:** H15, H21 y H22. Conservar scroll independiente de cada documento/pestaña y revisión. Cambio de canción/letra/resultado inicia el documento nuevo arriba; volver a una pestaña del mismo resultado conserva la posición. Idioma en barra de lectura y transición explícita.

**Idioma:** «Idioma cambiado a Inglés. No se ha enviado una consulta». Explicar que un resultado anterior compatible sigue guardado cuando ese guardado esté confirmado. Volver a ese idioma puede recuperarlo de caché con su etiqueta; no mostrar el resultado antiguo como si perteneciera al destino nuevo. Si falla guardar idioma, aviso contextual y reintento, sin generación.

**Procedencia:** fuente Manual/LRCLIB visible junto al texto; estado de guardado veraz, idioma, fecha disponible y proveedor/modelo del análisis en detalle accesible. IDs y hashes técnicos quedan en diagnóstico. No reconstruir procedencia ausente de un resultado antiguo.

**Cierre:** leer a mitad de Letra → Significado → Letra; expandir detalles sin perder posición; cambio real de documento; idioma de ida y vuelta con/sin caché y escritura fallida. Todos los cambios de preferencia producen cero solicitudes de inferencia.

### UX09 — Lectura seleccionable y escala tipográfica

**Alcance:** H17–H20. Tres modos de lectura Original/Traducción/Ambos, conservando el original y la alineación por estrofas. Etiquetas de rol e idioma presentes; origen mixto/desconocido se nombra como tal. Si origen y destino coinciden, original único con explicación, sin traducción duplicada. Cuando no existe traducción, no ofrecer un modo vacío sin causa y acción.

**Representación:** documento seleccionable de solo lectura, por ejemplo RichTextBox con FlowDocument y su scroll propio, en sustitución del viewport anterior; un único sistema de selección y Ctrl+C. Seleccionar y copiar fragmentos con ratón y teclado, sin exportación global añadida. Conservar estructura y nombres accesibles.

**Tipografía:** puntos convertidos a DIP; interlineado inicial 1,5× el tamaño en DIP y separación de estrofas 0,75×. Temas y explicaciones relevantes escalan con la lectura; controles mantienen su escala separada. Columna de prosa centrada con máximo inicial de 36 em, ajustada al ancho disponible; comprobar una medida aproximada de 55–80 caracteres donde quepa, sin forzarla en compacto.

**Cierre:** 12/16/20/24 pt; letra mixta, Unicode, verso muy largo y línea sin espacios, original igual al destino y 20.000 caracteres. Copiar un verso no copia todo el documento ni permite editarlo. Comprobar que selección, foco y scroll conservado de UX08 sobreviven al nuevo control.

### UX10 — Jerarquía del significado

**Alcance:** H23–H26. Abrir con «Una interpretación posible, generada por IA». Mostrar resumen, temas y acceso reconocible a detalles. Si el resumen es largo, vista inicial de unas 6 líneas y «Leer explicación completa» que revela el texto íntegro sin generar otro resumen ni truncarlo en almacenamiento.

**Secciones:** «Imágenes y metáforas · N», «Interpretaciones alternativas · N» y «Límites de esta interpretación», con indicio de contenido. Omitir desplegables vacíos; advertencias relevantes visibles sin depender de abrir un acordeón. Diferenciar cita original, explicación y categoría temática por estructura además de color. Matices ligados a una afirmación solo cuando exista esa relación en los datos; en otro caso mantenerlos como límites generales.

**Cierre:** resultados cortos/largos, cero/uno/varios elementos y avisos extensos; identidad interpretativa visible antes del resultado; contenido completo seleccionable y accesible. No se modifica el contrato del proveedor ni se necesita una nueva consulta para reorganizar un resultado guardado.

### UX11 — Conexión guiada y retorno a la sesión

**Alcance:** H27–H33; integración de H34 y del pie de H38. Selector explícito de modo con estado activo y descripción de pago. En ChatGPT: conectar → confirmar cuenta/elegir modelo → revisar habilitación de sesión. Una sola siguiente acción principal; pasos anteriores resumidos y editables. Entrar desde un bloqueo abre el paso pendiente, no el comienzo de una página larga.

**Estados:** sin cuentas, una, varias, registro pendiente, permiso denegado, catálogo no solicitado/cargando/vacío/error/disponible y sesión expirada. Ocultar selector de cuentas si no aporta elección; acciones sin selección válida bloqueadas con causa. Etiquetas humanas de cuenta, sin hashes visibles; preservar separación interna de identidades. Varias cuentas y reparación en Avanzado.

**Retorno:** cuenta y modelo conocidos se resumen encima de la confirmación explícita de créditos. No se recuerda esa aceptación como si fuera permanente ni se marca automáticamente. Lenguaje de producto comprensible sin Git, SQLite, DPAPI o Codex; ayuda técnica disponible. Errores con causa y recuperación; diagnóstico copiable saneado y sin tokens/cuerpos/URLs de retorno. Pie de acciones y resultado estable. Resumen de actividad procede de UX01 y el límite externo se consulta mediante enlace.

**Cierre:** escenarios controlados de todas las etapas; volver a abrir, cambiar cuenta y caducar sesión; guardar modelo/activar modo/habilitar producen cero análisis. Una elección de API de pago es explícita, reconocible antes de generar y nunca un fallback. OAuth y permisos reales mantienen su aceptación externa pendiente si no se ejecutan con una cuenta preparada.

### UX12 — API: formulario, validación y prueba explícita

**Alcance:** H35–H38; integración del cierre con cambios de H45. Ayuda breve para localizar el ID de modelo en la documentación del proveedor, con ejemplo marcado como ilustrativo que no se guarda automáticamente. Compatibilidad y disponibilidad se explican sin sustituir modelos.

**Formulario:** error por campo y foco al primero inválido. Límite entero 1–100, modelo explícito, clave y aceptación con motivos separados. Preservar valores válidos. Mostrar «Cambios sin guardar» al editar; Guardar no envía red. Probar deshabilitado mientras haya cambios sin guardar, con explicación; tras guardar mostrar modelo y modo efectivos y coste antes del botón. En curso: progreso y cancelación visibles. Pie estable con acción y resultado.

**Cierre:** sin cambios cerrar directamente; con cambios, continuar editando o descartar explícitamente. Una clave recién escrita nunca se persiste como borrador recuperable en texto legible; se limpia conforme al recorrido elegido. Eliminar clave sigue UX05.

**Cierre del bloque:** tests de vacío, 0, 101, decimal, letras, espacios, modelo inválido, clave ausente y aviso no aceptado; probar sin guardar no llama al servicio. Guardar y volver a abrir conserva preferencias; cancelación de cierre conserva la edición en la ventana. Pie utilizable en tamaño normal y mínimo; pruebas de transporte sin coste.

### UX13 — Cierre, bandeja y recuperación de ventana

**Alcance:** H49. Preferencia «Al cerrar: mantener en bandeja / salir», con explicación breve la primera vez que se elige ocultar. La notificación de primera vez ofrece Abrir y explica dónde salir; una preferencia persistente evita repetirla. Salir accesible desde el menú de aplicación, sin desplegar preferencias.

**Implementación:** mantener instancia única, preferencias y recuperación de posición; comprobar el menú real de bandeja. Salir cancela y libera tareas; informar si una solicitud ya enviada puede haber consumido. Ocultar sigue el contrato actual de continuidad y no habilita análisis por sí solo.

**Cierre:** X, Alt+F4, ocultar, doble clic en bandeja, menú Abrir/Pausar/Reanudar/Salir, segunda apertura y salida durante operación retenida en un proveedor sintético. No quedan procesos huérfanos; preferencia se conserva al reiniciar. Revisión real del menú de bandeja, no solo su código.

### UX14 — Aceptación, versión y entrega coherente

**Alcance:** H48 y H50; regresión de H01–H50. Completar anuncios accesibles de finalización/error/bloqueo, sin narrar cada cambio de canción repetidamente. Revisar Narrador, foco tras cerrar ventanas, Tab/Shift+Tab, flechas, atajos y selección de texto. Versionado y Acerca de con versión de producto/build identificable; hash completo en evidencia técnica.

**Documentación:** consolidar `DISENO.md` como especificación del estado entregado, retirando contradicciones; registrar historial aparte. Actualizar matriz de estados/pantallas y `ESTADO_FEATURES.md` sin convertir aceptaciones externas pendientes en cerradas. Marcar este plan como terminado solo cuando se satisfagan sus criterios, no por haber programado todos los controles.

**Cierre:** matriz técnica y tareas de personas de la sección 6. Informar explícitamente cualquier prueba no disponible. La copia de escritorio se actualiza como operación local separada, con cierre de instancias que bloqueen archivos, verificación de versión y hash tras publicar y lanzamiento real. Comprobar acceso e icono anclado. Un ZIP actualizado requiere su propia auditoría de privacidad y licencia; no se publica nada en GitHub por cerrar UX14.

## 4. Matriz de estados que sustituye las reglas dispersas

Las dimensiones reproducción/seguimiento, preparación y consumo son independientes. Un error de red no elimina una letra ya confirmada; un límite no impide leer un resultado existente.

| Situación | Mensaje principal | Acción útil |
|---|---|---|
| Spotify no detectado | Abre Spotify y reproduce una canción | Instrucción; sin búsqueda inejecutable |
| Spotify sin título/artista | No podemos identificar esta reproducción | Instrucción contextual y diagnóstico |
| Detección fallida | No se pudo leer la reproducción | Recuperación de detección/diagnóstico |
| Letra consultándose | Buscando letra… | Cancelar búsqueda |
| Versiones pendientes | Elige la letra correcta · N versiones | Elegir versión, acceso directo |
| Sin coincidencia | No encontramos letra para esta versión | Añadir letra; volver a buscar secundaria |
| Sin conexión/timeout | No se pudo contactar con el servicio de letras | Reintentar manualmente |
| Limitación del servicio | El servicio pide esperar | Plazo visible; reintentar al vencer |
| Respuesta inválida | No pudimos leer la respuesta del servicio | Reintentar/diagnóstico |
| Instrumental | Esta versión es instrumental y no contiene letra | Elegir otra versión; sin CTA de IA |
| Letra lista, falta conexión | Configura IA para obtener la explicación | Conexión en el paso pendiente |
| Falta modelo/habilitación | Modelo pendiente / Consultas pausadas en esta sesión | Elegir modelo / Revisar habilitación |
| Cuota local agotada | Límite local de hoy: usados/límite | Detalle local y próxima fecha disponible |
| Fallo al guardar idioma | No se pudo guardar el idioma | Reintentar guardado; generación bloqueada |
| Todo preparado | Puedes generar en el idioma elegido | Traducir y explicar |
| Analizando | Analizando la letra… | Cancelar; consumo pendiente/desconocido fiel |
| Resultado válido disponible | Resultado listo, con procedencia real | Leer; regeneración secundaria explícita |
| Análisis fallido | Causa legible; letra conservada | Recuperación que corresponda; sin reintento automático |
| Spotify pausado | Spotify en pausa, más estado del contenido | Mantener lectura |
| Seguimiento desactivado | Mostrando esta canción · seguimiento pausado | Reanudar seguimiento, sin IA |

Precedencia: error de almacenamiento relevante → operación activa → falta de canción → versión instrumental/ambigua/sin letra → resultado compatible existente → preparación de IA y cuota. Reproducción/seguimiento se muestran aparte. Ante varios bloqueos de generación, mostrar el primero resoluble en el recorrido y mantener visibles los demás en detalle de preparación. Resultado compatible no se oculta solo por agotar cuota o pausar consultas. Cada comando conserva sus guardas de negocio además de la presentación.

## 5. Trazabilidad completa

Cada fila tiene un responsable de cierre; las integraciones indicadas arriba se revisan también antes del cierre global.

| Hallazgo | Bloque |
|---|---|
| H01 | UX01 |
| H02 | UX02 |
| H03 | UX03 |
| H04 | UX03 |
| H05 | UX03 |
| H06 | UX03 |
| H07 | UX03 |
| H08 | UX03 |
| H09 | UX04 |
| H10 | UX03 |
| H11 | UX05 |
| H12 | UX07 |
| H13 | UX07 |
| H14 | UX07 |
| H15 | UX08 |
| H16 | UX07 |
| H17 | UX09 |
| H18 | UX09 |
| H19 | UX09 |
| H20 | UX09 |
| H21 | UX08 |
| H22 | UX08 |
| H23 | UX10 |
| H24 | UX10 |
| H25 | UX10 |
| H26 | UX10 |
| H27 | UX11 |
| H28 | UX11 |
| H29 | UX11 |
| H30 | UX11 |
| H31 | UX11 |
| H32 | UX11 |
| H33 | UX11 |
| H34 | UX01 |
| H35 | UX12 |
| H36 | UX12 |
| H37 | UX12 |
| H38 | UX12 |
| H39 | UX05 |
| H40 | UX06 |
| H41 | UX06 |
| H42 | UX06 |
| H43 | UX06 |
| H44 | UX06 |
| H45 | UX06 |
| H46 | UX02 |
| H47 | UX02 |
| H48 | UX14 |
| H49 | UX13 |
| H50 | UX14 |

## 6. Validación y aceptación global

### Por bloque

1. Alcance implementado y hallazgos asociados demostrados antes/después.
2. Build y pruebas apropiadas, incluidas regresiones de comportamiento relevante; auditoría de privacidad si se modifica almacenamiento, diagnóstico o empaquetado.
3. Inspección visual del binario actual en tamaño normal y mínimo, con teclado. Una prueba lógica no cierra un criterio visual.
4. Evidencia con escenario, versión/hash, escala real, dimensiones y resultado; diferenciar sintético, real y no ejecutado.
5. Actualizar estado sin empezar el bloque siguiente. Estados: Pendiente, En curso, Implementado pendiente de aceptación, Cerrado.

### Matriz técnica de UX14

| Área | Casos obligatorios |
|---|---|
| Tamaño y escala | Mínimo, 600×700, 860×780, maximizada y acoplada; Windows 100/125/150/200 % reales |
| Tipografía | 12/16/20/24 pt; todos en normal y mínimo al 100 %; repetir 24 pt y mínimo en las demás escalas |
| Pantallas | Un monitor, dos con distinta escala, desconectar secundario, restaurar ubicación; registrar área útil y evitar capturas cortadas |
| Accesibilidad | Sin ratón, Narrador, alto contraste y texto del sistema ampliado; nombres, foco, selección y anuncios de estado |
| Contenido | Títulos/artistas extensos, estrofas largas, línea sin espacios, Unicode, 20.000 caracteres, listas vacías y resumen largo |
| Idiomas | Seis destinos, original igual al destino y mezcla; alineación controlada más calidad real del proveedor como aceptación separada |
| Cuenta | Nueva, una/varias, sin permiso, pendiente, catálogo vacío/error, cambio de cuenta, sesión expirada y retorno al paso pendiente |
| Operaciones | Offline antes/durante, timeout, 429, error remoto y cancelación; sin respuestas antiguas en el documento actual |
| Uso | Caché, cifras comunicadas, desconocido, error con cifras, cancelación tras envío y cuota agotada desde inicio |
| Datos | Reinicio con letra manual, fallo de escritura, idioma sin guardar, borrados cancelados/confirmados/fallidos y corrupción en copia desechable |
| Escritorio | X, Alt+F4, bandeja real, segunda instancia, salida durante operación, versión de copia local y acceso anclado |

Es una cobertura por escenarios y riesgos, no el producto cartesiano de todas las filas. Estados críticos de bloqueo/borrado/consumo se prueban íntegros de forma controlada; recorridos principales se repiten con ventanas reales. No se gasta una consulta para verificar layout o mensajes. Cuenta, permisos y calidad real requieren un entorno preparado; su ausencia se registra como pendiente y evita afirmar aceptación completa.

### Ronda formativa con personas

Preparar **6 participantes** combinando usuarios de Spotify con y sin experiencia en IA, sin explicarles la arquitectura. Esto es una actividad futura del plan: no se contacta ni se envía nada a terceros por crear este documento.

Tareas: (1) primer inicio y letra; (2) habilitar cuenta ya conectada; (3) elegir versión; (4) corregir y explicar persistencia; (5) volver al verso tras Significado; (6) cambiar idioma sin confundirlo con una consulta; (7) interpretar consumo desconocido y decidir un reintento; (8) reconocer seguimiento pausado; (9) cerrar/recuperar/salir; (10) localizar borrado y entender su alcance en datos de prueba.

Registrar éxito sin ayuda, primera acción correcta, errores, retrocesos, tiempo y comprensión. Tomar primero una línea base, sin atribuir métricas inventadas al estado actual. Objetivo propuesto de la iteración: al menos 5 de 6 completan cada tarea principal sin guía; cualquier confusión grave sobre cobro, canción objetivo o eliminación exige corrección y nueva prueba aunque se alcance esa cifra. La muestra descubre problemas; no certifica usabilidad poblacional. Si no se realiza esta ronda, queda pendiente y no se sustituye por la opinión del implementador.

## 7. Estado y siguiente solicitud

Todos los bloques **UX01–UX14 están Pendientes**. Este documento y sus enlaces son el único cambio de esta intervención: no se ha implementado el rediseño ni actualizado la copia del escritorio.

Para empezar: **«Desarrolla UX01 siguiendo docs/PLAN_MEJORAS_UI_UX.md. Completa la preparación y UX01; no empieces UX02».**
