# Controlled negative cases: synthetic files only, never user credentials or databases.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$fixtureBase = [IO.Path]::GetFullPath("$PSScriptRoot\..\artifacts\portable-audit-tests\" + [Guid]::NewGuid().ToString('N'))
$fixtureFolder = Join-Path $fixtureBase 'Fixture-0.14.0-preview.1'
New-Item -ItemType Directory -Path $fixtureFolder -Force | Out-Null
$fixtureAudit = Join-Path $PSScriptRoot 'Test-PortablePackage.ps1'
foreach ($name in @('SongSense.exe','SongSense.dll','SongSense.Core.dll','SongSense.Infrastructure.dll','coreclr.dll',
 'System.Private.CoreLib.dll','PresentationFramework.dll','e_sqlite3.dll')) {
 [IO.File]::WriteAllBytes((Join-Path $fixtureFolder $name), [byte[]](0x4d,0x5a,0x00))
}
foreach ($name in @('LEEME.md','TERCEROS.txt','SongSense.runtimeconfig.json','SongSense.deps.json')) {
 [IO.File]::WriteAllText((Join-Path $fixtureFolder $name), 'Synthetic audit fixture')
}
$fixtureManifest = @{runtime='win-x64';selfContained=$true;trimming=$false;files=@(Get-ChildItem -LiteralPath $fixtureFolder -File | ForEach-Object {
 @{ name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})}
$fixtureManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$fixtureFolder\release-manifest.json" -Encoding utf8
& $fixtureAudit -Path $fixtureFolder | Out-Null
$fixtureZip = Join-Path $fixtureBase 'valid.fixture.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($fixtureFolder,$fixtureZip,[IO.Compression.CompressionLevel]::Optimal,$true)
& $fixtureAudit -Path $fixtureZip | Out-Null
$fixtureCases = @('personal-database','simulated-credential','profile-path','digest-tamper','path-traversal','duplicate','symbols')
foreach ($case in $fixtureCases) {
 $zipPath = Join-Path $fixtureBase ($case + '.fixture.zip')
 Copy-Item -LiteralPath $fixtureZip -Destination $zipPath
 $archive = [IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Update)
 try {
  $name = switch ($case) {
   'personal-database' { 'Fixture-0.14.0-preview.1/songsense.db' }
   'simulated-credential' { $archive.GetEntry('Fixture-0.14.0-preview.1/LEEME.md').Delete(); 'Fixture-0.14.0-preview.1/LEEME.md' }
   'profile-path' { $archive.GetEntry('Fixture-0.14.0-preview.1/LEEME.md').Delete(); 'Fixture-0.14.0-preview.1/LEEME.md' }
   'digest-tamper' { $archive.GetEntry('Fixture-0.14.0-preview.1/LEEME.md').Delete(); 'Fixture-0.14.0-preview.1/LEEME.md' }
   'path-traversal' { 'Fixture-0.14.0-preview.1/../escape.dll' }
   'duplicate' { 'Fixture-0.14.0-preview.1/SONGSENSE.DLL' }
   'symbols' { 'Fixture-0.14.0-preview.1/SongSense.pdb' }
  }
  $entry = $archive.CreateEntry($name)
  $writer = [IO.StreamWriter]::new($entry.Open())
  try {
   $value = switch ($case) {
    'simulated-credential' { 'sk-' + 'proj-' + ('A' * 30) }
    'profile-path' { 'C:' + '\Users\SyntheticUser\source\' }
    default { 'Synthetic rejection case' }
   }
   $writer.Write($value)
  } finally { $writer.Dispose() }
 } finally { $archive.Dispose() }
 $rejected = $false
 try { & $fixtureAudit -Path $zipPath | Out-Null }
 catch { $rejected = $_.Exception.Message.StartsWith('Portable audit:') }
 if (!$rejected) { throw "Portable auditor failed negative case [$case]." }
}
Write-Output 'Portable auditor tests passed: 2 valid fixtures accepted; 7 unsafe/tampered fixtures rejected.'
