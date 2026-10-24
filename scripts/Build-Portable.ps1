[CmdletBinding()]
param([string]$OutputDirectory = "$PSScriptRoot\..\artifacts\releases")
$ErrorActionPreference = 'Stop'
$portableRepo = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$portableOutput = [IO.Path]::GetFullPath($OutputDirectory)
$portableSdk = Join-Path $env:LOCALAPPDATA 'SongSenseTools\dotnet\dotnet.exe'
$portableDotnet = if (Test-Path -LiteralPath $portableSdk) { $portableSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$portableName = 'SongSense-win-x64-0.14.0-preview.1-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$portableFolder = Join-Path $portableOutput $portableName
$portableZip = $portableFolder + '.zip'
if (Test-Path -LiteralPath $portableFolder) { throw 'Release directory already exists. No overwrite is allowed.' }
Push-Location $portableRepo
try {
 & $portableDotnet restore SongSense.sln --locked-mode
 if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
 & $portableDotnet test SongSense.sln -c Release --no-restore
 if ($LASTEXITCODE -ne 0) { throw 'Release tests failed. No ZIP will be created.' }
 & "$PSScriptRoot\Test-GitPrivacy.ps1"
 & "$PSScriptRoot\Test-PortableAudit.ps1"
 New-Item -ItemType Directory -Path $portableOutput -Force | Out-Null
 foreach ($project in @('SongSense.App','SongSense.Core','SongSense.Infrastructure')) {
  $projectDirectory = Join-Path $portableRepo "src\$project"
  Copy-Item -LiteralPath "$projectDirectory\packages.lock.json" -Destination "$projectDirectory\obj\portable.packages.lock.json"
 }
 # Publish to a NEW folder; never copy the installed application or LocalAppData.
 & $portableDotnet publish src/SongSense.App/SongSense.App.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false "-p:PathMap=$portableRepo=/_/SongSense" -p:NuGetLockFilePath=obj/portable.packages.lock.json -o $portableFolder
 if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed. No ZIP will be created.' }
 Copy-Item -LiteralPath "$portableRepo\README.md" -Destination "$portableFolder\LEEME.md"
 Copy-Item -LiteralPath "$portableRepo\THIRD-PARTY-NOTICES.txt" -Destination "$portableFolder\TERCEROS.txt"
 Copy-Item -LiteralPath "$portableRepo\src\SongSense.App\Assets\SongSense.ico" -Destination "$portableFolder\SongSense.ico"
 $portableDependencies = @{}
 foreach ($project in @('SongSense.App','SongSense.Core','SongSense.Infrastructure')) {
  $lock = Get-Content -LiteralPath "$portableRepo\src\$project\packages.lock.json" -Raw | ConvertFrom-Json -AsHashtable
  foreach ($target in $lock.dependencies.Values) { foreach ($dependency in $target.GetEnumerator()) {
   if ($dependency.Value.type -ne 'Project') { $portableDependencies[$dependency.Key] = $dependency.Value.resolved }
  } }
 }
 # Preserve the runtime's supplied notices and each package's license metadata.
 $assets = Get-Content -LiteralPath "$portableRepo\src\SongSense.App\obj\project.assets.json" -Raw | ConvertFrom-Json -AsHashtable
 $packageRoot = @($assets.packageFolders.Keys)[0]
 $licenseDirectory = Join-Path $portableFolder 'Licenses'
 New-Item -ItemType Directory -Path $licenseDirectory | Out-Null
 Copy-Item -LiteralPath "$portableRepo\licenses\Apache-2.0.txt" -Destination "$licenseDirectory\Apache-2.0.txt"
 $notices = [Collections.Generic.List[string]]::new()
 $portableLibraries = @{} + $assets.libraries
 $runtimeConfig = Get-Content -LiteralPath "$portableFolder\SongSense.runtimeconfig.json" -Raw | ConvertFrom-Json -AsHashtable
 foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
  $runtimePackage = $framework.name + '.Runtime.win-x64'
  $portableLibraries[$runtimePackage + '/' + $framework.version] = @{type='package';path=$runtimePackage.ToLowerInvariant() + '/' + $framework.version}
  $portableDependencies[$runtimePackage] = $framework.version
 }
 foreach ($library in $portableLibraries.GetEnumerator()) {
  if ($library.Value.type -ne 'package') { continue }
  $packageDirectory = Join-Path $packageRoot $library.Value.path
  $specFile = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' -File | Select-Object -First 1
  if ($null -eq $specFile) { throw 'Missing package license metadata.' }
  [xml]$spec = Get-Content -LiteralPath $specFile.FullName -Raw
  $metadata = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
  $license = $metadata.SelectSingleNode('*[local-name()="license"]')
  $authors = $metadata.SelectSingleNode('*[local-name()="authors"]')
  $copyright = $metadata.SelectSingleNode('*[local-name()="copyright"]')
  $notices.Add("$($library.Key): license=$($license.InnerText); authors=$($authors.InnerText); $($copyright.InnerText)")
  $index = 0
  foreach ($notice in @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(?:LICENSE(?:\..*)?|NOTICE(?:\..*)?|THIRD-PARTY-NOTICES(?:\..*)?)$' })) {
   $index++
   $licenseName = $library.Key.Replace('/','-') + "-$index.txt"
   Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenseDirectory $licenseName)
  }
 }
 $notices | Set-Content -LiteralPath "$licenseDirectory\PACKAGE-NOTICES.txt" -Encoding utf8
 $portableManifest = [ordered]@{ release=$portableName; runtime='win-x64'; selfContained=$true; trimming=$false; singleFile=$false;
  validation='Prerelease: controlled tests passed; real-provider, accessibility and participant acceptance pending. ChatGPT use requires explicit session confirmation.';
  dependencies=$portableDependencies;
  files=@(Get-ChildItem -LiteralPath $portableFolder -Recurse -File | Sort-Object FullName | ForEach-Object {
   @{ name=[IO.Path]::GetRelativePath($portableFolder,$_.FullName).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
  }) }
 $portableManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$portableFolder\release-manifest.json" -Encoding utf8
 & "$PSScriptRoot\Test-PortablePackage.ps1" -Path $portableFolder
 [IO.Compression.ZipFile]::CreateFromDirectory($portableFolder,$portableZip,[IO.Compression.CompressionLevel]::Optimal,$true)
 & "$PSScriptRoot\Test-PortablePackage.ps1" -Path $portableZip
 $portableHash = (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash
 "$portableHash  $([IO.Path]::GetFileName($portableZip))" | Set-Content -LiteralPath ($portableZip + '.sha256') -Encoding ascii
 Write-Output "Portable directory: $portableFolder"
 Write-Output "Audited ZIP: $portableZip"
 Write-Output "SHA256: $portableHash"
} finally { Pop-Location }
