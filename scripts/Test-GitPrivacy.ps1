# Read-only audit. Reports paths/categories, never matched secret values.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$gitExecutable = (Get-Command git -ErrorAction Stop).Source
$findings = [Collections.Generic.List[string]]::new()
$secretPatterns = @{
 'OpenAI credential' = '(?<![A-Za-z0-9])sk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{20,}'
 'GitHub credential' = '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})'
 'Slack credential' = 'xox[baprs]-[A-Za-z0-9-]{20,}'
 'Private key' = '-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----'
 'JWT credential' = '(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}'
 'OAuth callback credential' = '(?i)(?:[?&](?:code|id_token_hint|access_token|refresh_token)=)[A-Za-z0-9_%.-]{20,}'
 'Literal secret' = '(?i)(?:api[_-]?key|client[_-]?secret|password|access[_-]?token|refresh[_-]?token|id[_-]?token)\s*[:=]\s*["''][A-Za-z0-9/+_=.-]{20,}["'']'
}
function Test-PrivatePath([string]$name) {
 if ($name -match '(?i)(^|/)(?:auth|oauth[^/]*|token[^/]*)\.json$') { return $true }
 if ($name -match '(^|/)(?:\.env(?:\..*)?|credentials[^/]*|secrets[^/]*|appsettings\.Local\.json)$' -and $name -notmatch '(^|/)\.env\.example$') { return $true }
 return $name -match '(?i)\.(?:db|db-shm|db-wal|dpapi(?:\.lock|\.[^/]*\.tmp)?|key|pem|pfx|p12|dmp|log|lnk)$' -or $name -match '(^|/)(?:bin|obj|artifacts|work|TestResults)/'
}
function Test-Content([byte[]]$bytes, [string]$label) {
 # Inspect UTF-8/ASCII and UTF-16; binary assets can otherwise hide ASCII strings.
 foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode, [Text.Encoding]::BigEndianUnicode)) {
  $content = $encoding.GetString($bytes)
  foreach ($category in $secretPatterns.Keys) {
   if ($content -match $secretPatterns[$category]) { $findings.Add("$label [$category]") }
  }
 }
}
function Get-BlobBytes([string]$objectId) {
 if ($objectId -notmatch '\A[0-9a-f]{40,64}\z') { throw 'Invalid Git object ID.' }
 $start = [Diagnostics.ProcessStartInfo]::new()
 $start.FileName = $gitExecutable
 $start.WorkingDirectory = $repoDirectory
 $start.ArgumentList.Add('cat-file'); $start.ArgumentList.Add('blob'); $start.ArgumentList.Add($objectId)
 $start.UseShellExecute = $false
 $start.RedirectStandardOutput = $true
 $start.RedirectStandardError = $true
 $start.CreateNoWindow = $true
 $process = [Diagnostics.Process]::Start($start)
 $memory = [IO.MemoryStream]::new()
 try {
  $process.StandardOutput.BaseStream.CopyTo($memory)
  $process.WaitForExit()
  if ($process.ExitCode -ne 0) { throw 'Could not read Git blob.' }
  return ,$memory.ToArray()
 } finally { $memory.Dispose(); $process.Dispose() }
}
Push-Location $repoDirectory
try {
 $eligible = (& $gitExecutable -c core.quotepath=false ls-files --cached --others --exclude-standard -z) -join "`n"
 if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate Git files.' }
 $files = @($eligible.Split([char]0, [StringSplitOptions]::RemoveEmptyEntries))
 foreach ($name in $files) {
  if (Test-PrivatePath $name) { $findings.Add("worktree: $name [private path]") }
  $resolved = [IO.Path]::GetFullPath((Join-Path $repoDirectory $name))
  if (!$resolved.StartsWith($repoDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Path outside repository.' }
  if (Test-Path -LiteralPath $resolved -PathType Leaf) { Test-Content ([IO.File]::ReadAllBytes($resolved)) "worktree: $name" }
 }
 # Audit staged snapshots too, even if their working-tree file was edited afterwards.
 $index = & $gitExecutable -c core.quotepath=false ls-files -s
 if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate index.' }
 foreach ($entry in $index) {
  if ($entry -match '^\d+ ([0-9a-f]+) \d+\t(.+)$') { Test-Content (Get-BlobBytes $Matches[1]) "index: $($Matches[2])" }
 }
 $objects = & $gitExecutable -c core.quotepath=false rev-list --objects --all
 if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate history.' }
 $blobCount = 0
 foreach ($entry in $objects) {
  if ($entry -notmatch '^([0-9a-f]+) (.+)$') { continue }
  $objectId = $Matches[1]; $name = $Matches[2]
  $type = & $gitExecutable cat-file -t $objectId
  if ($LASTEXITCODE -ne 0) { throw 'Could not inspect object type.' }
  if ($type -ne 'blob') { continue }
  $blobCount++
  if (Test-PrivatePath $name) { $findings.Add("history: $name [private path]") }
  Test-Content (Get-BlobBytes $objectId) "history: $name ($($objectId.Substring(0,8)))"
 }
 $expectedIgnored = @('.env', 'credentials.dpapi', 'credentials.dpapi.test.tmp', 'session.dpapi.lock', 'auth.json', 'oauth-export.json', 'tokens.json', 'songsense.db', 'secrets.json', 'sample.pem', 'crash.dmp', 'artifacts/probe.txt', 'work/private.txt')
 $ignored = @(& $gitExecutable check-ignore --no-index -- $expectedIgnored)
 foreach ($name in $expectedIgnored) { if ($name -notin $ignored) { $findings.Add("ignore missing: $name") } }
 if ($findings.Count -gt 0) {
  $findings | Sort-Object -Unique | Write-Output
  throw 'Privacy audit failed. Review the reported paths before publishing.'
 }
 Write-Output "Privacy audit passed: $($files.Count) eligible files, $blobCount historical blobs; index and ignore rules checked."
 Write-Output 'Pattern scanning supplements manual review; it cannot identify every kind of personal data.'
} finally { Pop-Location }
