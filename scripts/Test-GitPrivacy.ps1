# Read-only audit. Reports identities/categories/counts, never matched secret values.
# Exit 0: CLEAN or REVIEWED_FIXTURES (explicitly not a clean audit). Other findings throw.
# Packaging callers must preserve this output, including the reviewed-fixture distinction.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath("$PSScriptRoot\..")
$gitExecutable = (Get-Command git -ErrorAction Stop).Source
$findings = [Collections.Generic.List[string]]::new()
$reviewedFindings = [Collections.Generic.List[string]]::new()
# Reviewed content identities are versioned alongside the scanner, never in local docs.
$reviews = @(Get-Content -LiteralPath "$PSScriptRoot/PrivacyReviews.json" -Raw | ConvertFrom-Json)
foreach ($review in $reviews) {
 if ($review.path -notmatch '^[A-Za-z0-9_./-]+$' -or $review.sha256 -notmatch '^[0-9a-f]{64}$' -or $review.blob -notmatch '^[0-9a-f]{40,64}$' -or $review.count -lt 1 -or !$review.reason -or !$review.provenance) { throw 'Invalid privacy review registry.' }
}
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
function Test-Content([byte[]]$bytes, [string]$label, [string]$name, [string]$objectId = '') {
 # Count every match across the same encodings as before; never emit matched values.
 $sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
 if (!$objectId) {
  $header = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)" + [char]0)
  $objectId = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData([byte[]]($header + $bytes))).ToLowerInvariant()
 }
 foreach ($category in $secretPatterns.Keys) {
  $count = 0
  foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode, [Text.Encoding]::BigEndianUnicode)) {
   $count += [regex]::Matches($encoding.GetString($bytes), $secretPatterns[$category]).Count
  }
  if (!$count) { continue }
  $review = @($reviews | Where-Object { $_.path -ceq $name -and $_.sha256 -ceq $sha256 -and $_.blob -ceq $objectId -and $_.category -ceq $category -and $_.count -eq $count })
  $description = "$label [$category; matches=$count; blob=$objectId; sha256=$sha256]"
  if ($review.Count -eq 1) { $reviewedFindings.Add("REVIEWED synthetic fixture: $description") }
  else { $findings.Add($description) }
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
  if (Test-Path -LiteralPath $resolved -PathType Leaf) { Test-Content ([IO.File]::ReadAllBytes($resolved)) "worktree: $name" $name }
 }
 # Audit staged snapshots too, even if their working-tree file was edited afterwards.
 $index = & $gitExecutable -c core.quotepath=false ls-files -s
 if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate index.' }
 foreach ($entry in $index) {
  if ($entry -match '^\d+ ([0-9a-f]+) \d+\t(.+)$') { Test-Content (Get-BlobBytes $Matches[1]) "index: $($Matches[2])" $Matches[2] $Matches[1] }
 }
 # Enumerate each historical path/blob pair, including renames and merge parents.
 # rev-list --objects alone keeps only one path when identical bytes were reused.
 $history = & $gitExecutable -c core.quotepath=false log --all --full-history -m --root --format= --raw --no-abbrev --no-renames
 if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate history.' }
 $objects = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
 foreach ($entry in $history) {
  if ($entry -match '^:[0-7]+ [0-7]+ [0-9a-f]+ ([0-9a-f]+) [A-Z][0-9]*\t(.+)$' -and $Matches[1] -notmatch '^0+$') {
   [void]$objects.Add("$($Matches[1]) $($Matches[2])")
  }
 }
 $blobCount = 0
 foreach ($entry in $objects) {
  if ($entry -notmatch '^([0-9a-f]+) (.+)$') { continue }
  $objectId = $Matches[1]; $name = $Matches[2]
  $type = & $gitExecutable cat-file -t $objectId
  if ($LASTEXITCODE -ne 0) { throw 'Could not inspect object type.' }
  if ($type -ne 'blob') { continue }
  $blobCount++
  if (Test-PrivatePath $name) { $findings.Add("history: $name [private path]") }
  Test-Content (Get-BlobBytes $objectId) "history: $name" $name $objectId
 }
 $expectedIgnored = @('.env', 'credentials.dpapi', 'credentials.dpapi.test.tmp', 'session.dpapi.lock', 'auth.json', 'oauth-export.json', 'tokens.json', 'songsense.db', 'secrets.json', 'sample.pem', 'crash.dmp', 'artifacts/probe.txt', 'work/private.txt')
 $ignored = @(& $gitExecutable check-ignore --no-index -- $expectedIgnored)
 foreach ($name in $expectedIgnored) { if ($name -notin $ignored) { $findings.Add("ignore missing: $name") } }
 $reviewedFindings | Sort-Object -Unique | Write-Output
 if ($findings.Count -gt 0) {
  $findings | Sort-Object -Unique | Write-Output
  throw 'Privacy audit failed. Review the reported paths before publishing.'
 }
 if ($reviewedFindings.Count -gt 0) {
  Write-Output "Privacy audit REVIEWED_FIXTURES: no new blocking findings; contains reviewed synthetic fixture. $($files.Count) eligible files, $blobCount historical path/blob pairs; index and ignore rules checked."
 } else {
  Write-Output "Privacy audit CLEAN: $($files.Count) eligible files, $blobCount historical path/blob pairs; index and ignore rules checked."
 }
 Write-Output 'Pattern scanning supplements manual review; it cannot identify every kind of personal data.'
} finally { Pop-Location }
