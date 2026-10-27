# Run from PowerShell after using Salir in SongSense (closing may hide, according to preferences).
# taskbar pinning itself remains a Windows UI action, performed once by the user/agent.
[CmdletBinding()]
param([string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'

if (Get-Process -Name SongSense -ErrorAction SilentlyContinue) {
 throw 'Song Sense sigue abierto. Usa Salir en la ventana o en su bandeja; cerrar puede ocultarla según tus preferencias. Después repite la actualización.'
}
$repoDirectory = [System.IO.Path]::GetFullPath("$PSScriptRoot\..")
$localSdk = Join-Path $env:LOCALAPPDATA 'SongSenseTools\dotnet\dotnet.exe'
$dotnetExecutable = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$applicationDirectory = Join-Path $env:LOCALAPPDATA 'SongSense\DesktopApp'
if (!$EvidenceDirectory) { $EvidenceDirectory = Join-Path $repoDirectory ("artifacts\desktop-updates\" + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')) }
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
$publishDirectory = Join-Path $EvidenceDirectory 'publish'
$backupDirectory = Join-Path $EvidenceDirectory 'previous-DesktopApp'
if (Test-Path -LiteralPath $publishDirectory) { throw 'La carpeta de publish ya existe; usa una carpeta de evidencia nueva.' }
if (Test-Path -LiteralPath $backupDirectory) { throw 'La carpeta de respaldo ya existe; usa una carpeta de evidencia nueva.' }
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $repoDirectory
try {
 # Runtime-specific dependency metadata belongs in obj, not in the tracked lock files.
 foreach ($project in @('SongSense.App', 'SongSense.Core', 'SongSense.Infrastructure')) {
  $projectDirectory = Join-Path $repoDirectory "src\$project"
  New-Item -ItemType Directory -Path "$projectDirectory\obj" -Force | Out-Null
  Copy-Item -LiteralPath "$projectDirectory\packages.lock.json" -Destination "$projectDirectory\obj\desktop.packages.lock.json" -Force
 }
 & $dotnetExecutable publish src/SongSense.App/SongSense.App.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:NuGetLockFilePath=obj/desktop.packages.lock.json -o $publishDirectory
 if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar la copia de escritorio. Cierra SongSense y revisa la salida.' }
} finally { Pop-Location }
# Publish must finish before touching the installed copy. The backup contains application files only.
if (Get-Process -Name SongSense -ErrorAction SilentlyContinue) { throw 'Song Sense se abrió durante la preparación; usa Salir antes de copiar.' }
Copy-Item -LiteralPath "$repoDirectory\src\SongSense.App\Assets\SongSense.ico" -Destination (Join-Path $publishDirectory 'SongSense-Gold.ico') -Force
$publishedApp = Get-Item -LiteralPath (Join-Path $publishDirectory 'SongSense.dll')
$publishedFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
 [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('\','/'); Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash; Bytes = $_.Length }
})
$head = & git -C $repoDirectory rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'No se pudo identificar el checkout.' }
$dirty = [bool](& git -C $repoDirectory status --porcelain)
$manifest = [ordered]@{
 SchemaVersion = 1; Kind = 'local-desktop'; CreatedUtc = [DateTime]::UtcNow.ToString('o')
 ProductVersion = $publishedApp.VersionInfo.ProductVersion; FileVersion = $publishedApp.VersionInfo.FileVersion
 GitHead = $head; DirtyWorktree = $dirty; AppSha256 = (Get-FileHash -LiteralPath $publishedApp.FullName).Hash
 PortablePackage = $false; Files = $publishedFiles
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $publishDirectory 'desktop-manifest.json') -Encoding utf8
if (Test-Path -LiteralPath $applicationDirectory) { Copy-Item -LiteralPath $applicationDirectory -Destination $backupDirectory -Recurse }
New-Item -ItemType Directory -Path $applicationDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $publishDirectory -Force | Copy-Item -Destination $applicationDirectory -Recurse -Force
foreach ($file in $publishedFiles) {
 $installedFile = Join-Path $applicationDirectory $file.Path
 if (!(Test-Path -LiteralPath $installedFile) -or (Get-FileHash -LiteralPath $installedFile).Hash -ne $file.Sha256) { throw "La verificación de copia falló: $($file.Path). Conserva el respaldo y revisa la instalación." }
}
if ((Get-FileHash -LiteralPath (Join-Path $applicationDirectory 'desktop-manifest.json')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $publishDirectory 'desktop-manifest.json')).Hash) { throw 'El manifiesto instalado no coincide con el preparado.' }
Copy-Item -LiteralPath (Join-Path $publishDirectory 'desktop-manifest.json') -Destination (Join-Path $EvidenceDirectory 'desktop-manifest.json') -Force
$iconPath = Join-Path $applicationDirectory 'SongSense-Gold.ico'
Copy-Item -LiteralPath "$repoDirectory\src\SongSense.App\Assets\SongSense.ico" -Destination $iconPath -Force
$shortcutShell = New-Object -ComObject WScript.Shell
$pinnedShortcutPath = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\SongSense.lnk'
if (Test-Path -LiteralPath $pinnedShortcutPath) {
 $existingPinned = $shortcutShell.CreateShortcut($pinnedShortcutPath)
 $resolvedIconPath = Join-Path (Split-Path $existingPinned.TargetPath) 'SongSense-Gold.ico'
 if (Test-Path -LiteralPath $resolvedIconPath) { $iconPath = $resolvedIconPath }
}
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SongSense.lnk'
$shortcut = $shortcutShell.CreateShortcut($desktopShortcutPath)
$shortcut.TargetPath = Join-Path $applicationDirectory 'SongSense.exe'
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = $applicationDirectory
$shortcut.IconLocation = "$iconPath,0"
$shortcut.Description = 'Song Sense — Tu música, más significado'
$shortcut.Save()
if (Test-Path -LiteralPath $pinnedShortcutPath) {
 $pinnedShortcut = $shortcutShell.CreateShortcut($pinnedShortcutPath)
 # Preserve the resolved target Windows used when pinning, including any LocalAppData redirection.
 $pinnedShortcut.IconLocation = "$iconPath,0"
 $pinnedShortcut.Description = $shortcut.Description
 $pinnedShortcut.Save()
}
Write-Output "Copia de escritorio actualizada: $applicationDirectory"
