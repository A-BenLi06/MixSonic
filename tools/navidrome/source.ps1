param(
    [string]$Destination,
    [switch]$Inspect
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$sourceLock = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source-lock.json') -Raw | ConvertFrom-Json
if (-not $Destination) { $Destination = Join-Path $projectRoot '.tmp\navidrome' }
$Destination = [IO.Path]::GetFullPath($Destination)
if (-not (Test-Path -LiteralPath $Destination)) {
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Destination)) -Force | Out-Null
    & git clone --no-checkout --filter=blob:none $sourceLock.repository $Destination
    if ($LASTEXITCODE -ne 0) { throw 'Navidrome clone failed.' }
}
$remote = & git -C $Destination remote get-url origin
if ($LASTEXITCODE -ne 0 -or $remote -ne $sourceLock.repository) { throw 'Destination is not the expected Navidrome checkout.' }
$sourceChanges = & git -C $Destination status --porcelain
if ($LASTEXITCODE -ne 0 -or $sourceChanges) { throw 'Preserve local reference edits before selecting a different revision.' }
& git -C $Destination cat-file -e ($sourceLock.commit + '^{commit}') 2>$null
if ($LASTEXITCODE -ne 0) {
    & git -C $Destination fetch --depth 1 origin $sourceLock.commit
    if ($LASTEXITCODE -ne 0) { throw 'Cannot retrieve the locked Navidrome revision.' }
}
& git -C $Destination checkout --detach $sourceLock.commit
if ($LASTEXITCODE -ne 0) { throw 'Cannot select the locked Navidrome revision.' }
if ($Inspect) {
    & python (Join-Path $PSScriptRoot 'inspect_source.py') --source $Destination
    if ($LASTEXITCODE -ne 0) { throw 'Navidrome reference inspection failed.' }
}
Write-Output $Destination
