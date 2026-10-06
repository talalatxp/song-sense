# Run from PowerShell after closing SongSense. Updates the local copy and shortcuts;
# taskbar pinning itself remains a Windows UI action, performed once by the user/agent.
$ErrorActionPreference = 'Stop'
$repoDirectory = [System.IO.Path]::GetFullPath("$PSScriptRoot\..")
$localSdk = Join-Path $env:LOCALAPPDATA 'SongSenseTools\dotnet\dotnet.exe'
$dotnetExecutable = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$applicationDirectory = Join-Path $env:LOCALAPPDATA 'SongSense\DesktopApp'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $repoDirectory
try {
 # Runtime-specific dependency metadata belongs in obj, not in the tracked lock files.
 foreach ($project in @('SongSense.App', 'SongSense.Core', 'SongSense.Infrastructure')) {
  $projectDirectory = Join-Path $repoDirectory "src\$project"
  New-Item -ItemType Directory -Path "$projectDirectory\obj" -Force | Out-Null
  Copy-Item -LiteralPath "$projectDirectory\packages.lock.json" -Destination "$projectDirectory\obj\desktop.packages.lock.json" -Force
 }
 & $dotnetExecutable publish src/SongSense.App/SongSense.App.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:NuGetLockFilePath=obj/desktop.packages.lock.json -o $applicationDirectory
 if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar la copia de escritorio. Cierra SongSense y revisa la salida.' }
} finally { Pop-Location }
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
