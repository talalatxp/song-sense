# Read-only inspection of the actual release directory or ZIP. Never prints matched content.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$portablePath = [IO.Path]::GetFullPath($Path)
$portablePatterns = @{
 'API credential' = '(?<![A-Za-z0-9])sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{20,}'
 'GitHub credential' = '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})'
 'OAuth credential' = '(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}'
 'OAuth callback' = '(?i)[?&](?:code|access_token|refresh_token)=[A-Za-z0-9_%.-]{20,}'
 'Private key material' = '-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----\s+[A-Za-z0-9+/=\r\n]{64,}'
 'Literal credential' = '(?i)(?:api[_-]?key|client[_-]?secret|password|access[_-]?token|refresh[_-]?token|id[_-]?token)\s*[:=]\s*["''][A-Za-z0-9/+_=.-]{20,}["'']'
 'Developer profile path' = '(?i)[A-Z]:[\\/]Users[\\/][^\\/\s\x00]{2,}[\\/]'
}
$portableNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$portableCount = 0
$portableTotalBytes = 0L
$portableHashes = @{}
$portableManifestData = $null
function Test-PortableEntry([string]$name, [byte[]]$bytes) {
 $script:portableTotalBytes += $bytes.LongLength
 if ($portableTotalBytes -gt 1GB) { throw 'Portable audit: unexpectedly large package.' }
 if (!$portableNames.Add($name)) { throw 'Portable audit: duplicate entry.' }
 if ($name -match '[\\:]|(^|/)\.\.?(/|$)' -or $name.StartsWith('/')) { throw 'Portable audit: unsafe entry path.' }
 # Fresh publish only: assemblies, runtime metadata, icon, user guide and third-party notices.
 $allowed = $name -match '\A(?:[a-z]{2}(?:-[A-Za-z]{2,4})?/)?[A-Za-z0-9_.-]+\.dll\z' -or
  $name -in @('SongSense.exe','createdump.exe','SongSense.ico','SongSense.deps.json','SongSense.runtimeconfig.json',
   'LEEME.md','TERCEROS.txt','release-manifest.json') -or $name -match '\ALicenses/[A-Za-z0-9_.-]+\.txt\z'
 if (!$allowed) { throw 'Portable audit: entry outside release allowlist (data, logs, symbols, sources and credentials are forbidden).' }
 if ($name -match '\.(?:dll|exe)$' -and ($bytes.Length -lt 2 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a)) { throw 'Portable audit: invalid executable header.' }
 foreach ($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode,[Text.Encoding]::BigEndianUnicode)) {
  $content = $encoding.GetString($bytes)
  foreach ($category in $portablePatterns.Keys) {
   if ($content -match $portablePatterns[$category]) { throw "Portable audit: blocked content category [$category]. No matched content is printed." }
  }
 }
 $portableHashes[$name] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
 if ($name -eq 'release-manifest.json') {
  try { $script:portableManifestData = [Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xfeff) | ConvertFrom-Json -AsHashtable }
  catch { throw 'Portable audit: invalid manifest.' }
 }
 $script:portableCount++
}
if (Test-Path -LiteralPath $portablePath -PathType Container) {
 if ((Get-Item -LiteralPath $portablePath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Portable audit: root symbolic link forbidden.' }
 $portableFiles = @(Get-ChildItem -LiteralPath $portablePath -Recurse -File -Force)
 if (@(Get-ChildItem -LiteralPath $portablePath -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) { throw 'Portable audit: symbolic link forbidden.' }
 if ($portableFiles.Count -gt 1000) { throw 'Portable audit: too many entries.' }
 foreach ($file in $portableFiles) {
  if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Portable audit: symbolic link forbidden.' }
  if ($file.Length -gt 100MB) { throw 'Portable audit: unexpectedly large file.' }
  $name = [IO.Path]::GetRelativePath($portablePath,$file.FullName).Replace('\','/')
  Test-PortableEntry $name ([IO.File]::ReadAllBytes($file.FullName))
 }
} else {
 $portableArchive = [IO.Compression.ZipFile]::OpenRead($portablePath)
 try {
  $portableRoot = $null
  if ($portableArchive.Entries.Count -gt 1000) { throw 'Portable audit: too many entries.' }
  foreach ($entry in $portableArchive.Entries) {
   if ($entry.FullName -notmatch '\A([A-Za-z0-9][A-Za-z0-9_.-]*)/(.+)\z') { throw 'Portable audit: ZIP requires a single release folder.' }
   if ($null -eq $portableRoot) { $portableRoot = $Matches[1] }
   if ($Matches[1] -ne $portableRoot) { throw 'Portable audit: multiple ZIP roots.' }
   $name = $Matches[2]
   if ($entry.Length -gt 100MB) { throw 'Portable audit: unexpectedly large entry.' }
   if ($name.EndsWith('/')) { throw 'Portable audit: explicit directory entries are not expected.' }
   $stream = $entry.Open(); $memory = [IO.MemoryStream]::new()
   try {
    $chunk = [byte[]]::new(65536)
    while (($read = $stream.Read($chunk,0,$chunk.Length)) -gt 0) {
     if ($memory.Length + $read -gt 100MB) { throw 'Portable audit: oversized decompressed entry.' }
     $memory.Write($chunk,0,$read)
    }
    Test-PortableEntry $name ($memory.ToArray())
   }
   finally { $memory.Dispose(); $stream.Dispose() }
  }
 } finally { $portableArchive.Dispose() }
}
foreach ($required in @('SongSense.exe','SongSense.dll','SongSense.Core.dll','SongSense.Infrastructure.dll','coreclr.dll',
 'System.Private.CoreLib.dll','PresentationFramework.dll','e_sqlite3.dll','SongSense.runtimeconfig.json','SongSense.deps.json',
 'LEEME.md','TERCEROS.txt','release-manifest.json')) {
 if (!$portableNames.Contains($required)) { throw "Portable audit: missing required release component [$required]." }
}
if ($null -eq $portableManifestData -or $portableManifestData.runtime -ne 'win-x64' -or $portableManifestData.selfContained -ne $true -or $portableManifestData.trimming -ne $false) { throw 'Portable audit: invalid delivery manifest.' }
$portableDeclared = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in $portableManifestData.files) {
 if (!$portableDeclared.Add($item.name) -or $item.name -eq 'release-manifest.json' -or !$portableHashes.ContainsKey($item.name) -or $item.sha256 -ne $portableHashes[$item.name]) { throw 'Portable audit: manifest/file digest mismatch.' }
}
if ($portableDeclared.Count -ne $portableCount - 1) { throw 'Portable audit: undeclared file.' }
Write-Output "Portable audit passed: $portableCount files; paths, allowlist and credential/profile patterns checked."
Write-Output 'This scan supplements review; it is not a guarantee of absolute security.'
