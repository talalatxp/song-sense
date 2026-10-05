# SongSense

Aplicación personal para Windows para entender canciones. **F01 — Base ejecutable: cerrada.** La detección de Spotify y las consultas de letras/IA aún no están implementadas. La ventana muestra textos de espera, sin canciones ni resultados ficticios.

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

Las dependencias directas tienen versiones explícitas y cada proyecto conserva `packages.lock.json` con las dependencias transitivas. Los cambios deliberados de dependencias requieren regenerar y revisar estos archivos. No hay integración de IA ni solicitudes de pago en F01.

## Estructura

- `SongSense.App`: WPF, composición de servicios y MVVM; ventana oscura, estados y pestañas.
- `SongSense.Core`: modelos, interfaces cancelables y etiquetas de estados en español.
- `SongSense.Infrastructure`: SQLite y migración transaccional del esquema inicial. Las implementaciones de Windows, HTTP e IA se incorporarán en sus features.
- `SongSense.Tests`: pruebas de esquema, persistencia, cancelación y compatibilidad.

## Almacenamiento

La primera apertura crea `%LOCALAPPDATA%\SongSense\songsense.db`. SQLite usa `PRAGMA user_version = 1`; solo hay tablas `app_settings` y `daily_request_counts`. La IA comienza desactivada, sin modelo, con límite inicial de 20 solicitudes. Todavía no se realizan ni reservan solicitudes.

F01 no almacena claves, letras ni resultados. El contrato de configuración solo permite preferencias no secretas. Una base de una versión más reciente se rechaza sin modificarla. Si falla el acceso, la ventana muestra Error y permite reintentar tras corregir el acceso o usar una versión compatible; nunca borra la base para recuperarse.

La inicialización se ejecuta fuera del hilo de interfaz. Cerrar la ventana termina la aplicación; la bandeja del sistema corresponde a F09.

## Ruta y evidencia

El alcance de cada entrega está en [docs/RUTA_PROYECTO.md](docs/RUTA_PROYECTO.md). El estado y las comprobaciones de F01 están en [docs/ESTADO_FEATURES.md](docs/ESTADO_FEATURES.md). Se desarrolla una feature por petición, sin commits ni publicación implícitos.
