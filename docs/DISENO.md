# Identidad y experiencia de Song Sense

Decisión de la usuaria del 6 de octubre de 2026: **A — dos claves de sol y estilo dorado**.

## Identidad

- Marca visible: Song Sense; identificadores técnicos y repositorio conservan SongSense/song-sense.
- Las dos S del nombre se representan con claves de sol en el encabezado. El nombre accesible sigue siendo «Song Sense».
- Georgia para marca y lectura; Segoe UI para botones, estados y metadatos; Segoe UI Symbol para las claves.
- Fondo `#11110F`, paneles `#1B1A16`, superficies elevadas `#23221C`, oro `#E8C882`, texto `#F5F0E5`, secundario `#BEB7A6`, bordes `#484236`.
- PNG de icono generado con ImageGen; ICO empaquetado en 16, 24, 32, 48, 64, 128 y 256 px con `scripts/Create-Icon.ps1`. El ejecutable y las ventanas incorporan el icono.
- Prompt del icono: dos claves de sol doradas, gruesas y legibles, en un cuadrado de carbón con esquinas redondeadas, fondo exterior transparente, sin texto ni pentagrama. Generación con herramienta integrada, sin llamadas a una API de pago configurada para el proyecto.

## Interfaz

- Ventana inicial de 860 × 780 DIP; mínimo 360 × 480 conservado.
- Cabecera de marca; tarjeta compacta de canción y estado; acción primaria Buscar letra; Cambiar letra secundaria; Cancelar solo durante búsquedas.
- Pestaña **Letra**, en lugar de anunciar una traducción que todavía no existe. Conserva su AutomationId para compatibilidad.
- Desde 700 DIP de ancho: original y espacio futuro de traducción en paralelo. Ese espacio indica expresamente «Traducción pendiente» y no muestra traducciones simuladas.
- Por debajo de 700 DIP: un solo panel de letra. El significado conserva su aviso de función pendiente.
- Desde F06, por debajo de 700 DIP de altura o 700 DIP de anchura: cabecera compacta, sin lema ni álbum/duración, márgenes menores. Título y artista se recortan con puntos suspensivos y mantienen el texto completo en su tooltip. En modo compacto se permite desplazar la ventana completa y se mantiene una zona de lectura con altura útil; original y traducción se apilan.
- Mayor tamaño de letra, interlineado y scroll independiente; instrucciones iniciales desaparecen al preparar una letra.
- Selector de versiones y editor manual comparten colores, foco de teclado y acciones principales.
- Barra de título nativa oscura, manteniendo controles y comportamiento estándar de Windows.

## Escritorio y barra de tareas

- Acceso de escritorio apuntando a un ejecutable autocontenido de uso local en la carpeta DesktopApp de SongSense; sin lanzador VBS.
- Anclaje realizado mediante el menú de Windows. El entorno de Codex puede redirigir LocalAppData al LocalCache de su paquete; verificar la ruta real del acceso al actualizar.
- Esta copia local se debe volver a publicar cuando cambie la app. No se considera una release pública ni el cierre de F11.
- El anclaje y el icono se realizan por esta petición; no implica implementar bandeja, preferencias persistentes ni las demás funciones de F10.

## Validación

- Comprobación final: restore con `--locked-mode`, build Release sin errores ni advertencias y 66 pruebas existentes correctas, sin fallos ni omitidas.
- Publicación local autocontenida correcta. El acceso se abrió realmente y la app detectó la canción de Spotify sin modificar su reproducción.
- Revisión visual real del tema dorado, las dos claves en el nombre, icono de ventana, ambas pestañas, selector de versiones y editor manual. No se guardó una letra de prueba ni se consultó la IA.
- Ventana reducida hasta ancho y altura mínimos; se corrigió una primera versión cuya cabecera agotaba el espacio vertical. Se verificó scroll en la vista compacta y ocultación del panel futuro de traducción en ancho reducido.
- Anclaje creado mediante el menú nativo «Anclar a la barra de tareas», con un acceso SongSense.lnk que apunta al ejecutable local. Iconos de escritorio y anclaje actualizados al ICO dorado.
- Las escalas 150 % y múltiples monitores no se validaron en este cambio; siguen perteneciendo a F10. F05–F11 permanecen pendientes.
- No se hizo commit, push ni publicación externa.

Referencia para el marco oscuro de Windows: https://learn.microsoft.com/windows/apps/desktop/modernize/ui/apply-windows-themes

## Cuenta y consumo — F08 parcial

El acceso a la configuración pasa a Cuenta y consumo. El modo seguro inicial es ChatGPT · solo uso incluido; el panel explica el bloqueo de generaciones hasta poder verificar la política de créditos y ofrece el login oficial en navegador. La cuenta se reconoce por su correo y una etiqueta estable del registro, sin mostrar tokens. Los modelos se eligen entre los que comunica OpenAI. API key · de pago se activa expresamente y tiene sus ajustes separados.

El consumo de cada análisis aparece en la ventana principal: entrada, salida y total, o un aviso de consumo desconocido. Las recuperaciones de caché indican 0 tokens nuevos. Los mensajes vacíos no ocupan espacio y las acciones de caché y cuenta comparten fila, conservando más altura de lectura. El panel de cuenta conserva colores, icono, foco y scroll a tamaño mínimo. F08 no acredita un login real ni genera con el plan; la evidencia y pendientes están en ESTADO_FEATURES.md.
