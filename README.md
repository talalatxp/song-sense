# Song Sense

Aplicación para Windows que detecta la canción de Spotify, busca su letra y permite traducirla y obtener una interpretación con IA. Guarda letras y resultados en el equipo y ofrece traducción y explicaciones en español, inglés, francés, alemán, italiano y portugués.

Versión preliminar: `0.14.0-preview.1`. La integración con proveedores reales y la aceptación completa de accesibilidad siguen pendientes de validación. Las interpretaciones de IA pueden contener errores.

## Ejecutar la aplicación

Requiere **Windows 11 x64**, Spotify y conexión a Internet para buscar letras o usar IA.

Si dispones del paquete portable de Song Sense, extrae el ZIP completo y abre `SongSense.exe`. No requiere instalar .NET. Conserva los archivos de la carpeta, incluidos `Licenses` y `TERCEROS.txt`. Para ejecutarla desde este repositorio, sigue las instrucciones de compilación que aparecen más abajo.

## Primeros pasos

1. Abre Spotify y reproduce una canción. Song Sense utiliza la sesión multimedia de Windows para detectarla; no necesita iniciar sesión en Spotify desde la app.
2. Con el seguimiento automático activo, espera a que busque la letra en LRCLIB. También puedes usar **Buscar letra** en el menú de la canción.
3. Si hay varias coincidencias, abre **Cambiar letra → Versiones** y confirma la correcta. En **Manual** puedes introducir o corregir una letra de hasta 20.000 caracteres.
4. Abre **Cuenta**, configura un proveedor de IA y habilita las consultas.
5. Selecciona el idioma en **Ajustes → Idioma** y pulsa **Traducir y explicar**. Consulta las pestañas **Letra y traducción** y **Significado**.

Cambiar de canción o de idioma no genera IA automáticamente. Los resultados compatibles guardados se recuperan sin una consulta nueva. Si el original ya está en el idioma elegido, no se duplica la traducción.

## Configurar IA y costes

En **Cuenta** puedes elegir uno de estos modos:

- **ChatGPT:** conecta tu cuenta, consulta los modelos disponibles y selecciona uno. Para habilitar consultas, revisa el uso de créditos en **Gestionar uso en ChatGPT** y confirma el ajuste solicitado por la app. La confirmación dura esta sesión; Song Sense no puede verificar el ajuste externo ni garantizar cargos cero. La disponibilidad depende de la cuenta y del proveedor.
- **API key · de pago:** introduce tu clave en el campo enmascarado de la app, el identificador del modelo y el límite diario de solicitudes. Guarda los ajustes y habilita IA. La API tiene facturación propia; una suscripción a ChatGPT no cubre ese consumo.

**Probar conexión**, **Traducir y explicar** y **Regenerar** envían solicitudes al proveedor y pueden consumir cuota o generar coste. Cancelar una solicitud ya enviada no garantiza evitar ese consumo. El límite diario cuenta solicitudes, no euros. No hay compra de créditos ni cambio automático de proveedor.

## Datos y controles

Los datos se guardan en `%LOCALAPPDATA%\SongSense`. Las credenciales se protegen con DPAPI del usuario de Windows; las letras y los resultados de la caché se guardan en texto legible. Para generar un análisis se envía la letra al proveedor de IA elegido. La búsqueda de letras envía los metadatos de la canción a LRCLIB.

Puedes borrar contenido desde la app. Borrar la caché conserva los ajustes y las credenciales; la desconexión de cuenta y la eliminación de API key se realizan por separado. Borrar el ZIP no elimina los datos locales. Evita ejecutar versiones distintas simultáneamente, ya que comparten esos datos.

En **Ajustes** puedes cambiar el tamaño de lectura, activar **Siempre encima** y elegir **Mantener en bandeja al cerrar**. Si esta última opción está activa, cerrar la ventana mantiene la app abierta. Recupérala desde el icono de la bandeja y usa **Salir** para terminar el proceso.

## Compilar desde el repositorio

Instala el **SDK .NET 10.0.401**, fijado en `global.json`. La primera restauración necesita acceso a NuGet. Desde PowerShell, en Windows:

```powershell
git clone https://github.com/talalatxp/song-sense.git
cd song-sense
dotnet restore SongSense.sln --locked-mode
dotnet build SongSense.sln -c Release --no-restore
dotnet run --project src/SongSense.App -c Release --no-build --no-restore
```

Para ejecutar las pruebas:

```powershell
dotnet test SongSense.sln -c Release --no-build --no-restore
```

Para crear un paquete portable autocontenido de Windows x64:

```powershell
./scripts/Build-Portable.ps1
```

El script ejecuta las comprobaciones y crea una carpeta, un ZIP y su SHA256 bajo `artifacts/releases`. No requiere planes, auditorías ni documentación interna. Puedes comprobar una carpeta o un ZIP con `./scripts/Test-PortablePackage.ps1 -Path <ruta>`.

Para crear o actualizar el acceso de escritorio local, cierra Song Sense mediante **Salir** y ejecuta `./scripts/Update-Desktop.ps1`. El anclaje inicial a la barra de tareas se hace desde el menú de Windows del acceso directo.

## Problemas frecuentes

- **No detecta la canción:** comprueba que Spotify está reproduciendo y que la canción aparece en los controles multimedia de Windows.
- **No encuentra letra:** prueba otra versión o introduce el texto manualmente. LRCLIB puede no tener esa canción.
- **IA deshabilitada o sin cuota:** revisa la cuenta, el modelo y la habilitación de consultas. En API, comprueba también la clave y el límite diario. No compartas tu clave en incidencias.
- **La ventana ha desaparecido:** busca Song Sense en la bandeja de Windows, incluida la sección de iconos ocultos.

## Componentes de terceros

Las dependencias se fijan en los archivos `packages.lock.json`. Los avisos de terceros están en `THIRD-PARTY-NOTICES.txt`; el portable incluye las licencias y avisos de los paquetes y runtimes utilizados. Las letras pertenecen a sus titulares; este repositorio no distribuye un catálogo de canciones.
