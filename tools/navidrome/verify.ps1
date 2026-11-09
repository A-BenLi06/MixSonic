param([string]$FixtureState)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$auditRoot = Join-Path $projectRoot '.tmp\navidrome-audit'
New-Item -ItemType Directory -Path $auditRoot -Force | Out-Null
function Get-VerificationCodeState {
    $commit = (& git -C $projectRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Verification requires a Git checkout.' }
    & git -C $projectRoot diff --quiet
    $worktreeClean = $LASTEXITCODE -eq 0
    & git -C $projectRoot diff --cached --quiet
    $indexClean = $LASTEXITCODE -eq 0
    $untracked = @(& git -C $projectRoot ls-files --others --exclude-standard)
    return [ordered]@{ commit = $commit; trackedClean = $worktreeClean -and $indexClean; untrackedFiles = $untracked }
}
$initialCode = Get-VerificationCodeState
if (-not $initialCode.trackedClean -or $initialCode.untrackedFiles.Count -gt 0) {
    throw 'Commit the completed changes before generating acceptance evidence for that commit.'
}
if (-not $FixtureState) { $FixtureState = Join-Path $projectRoot '.tmp\navidrome-fixture\current.json' }
$fixture = Get-Content -LiteralPath $FixtureState -Raw | ConvertFrom-Json
$fixtureRoot = [IO.Path]::GetFullPath($fixture.root)
$expectedFixtureRoot = Join-Path $projectRoot '.tmp\navidrome-fixture'
if (-not $fixtureRoot.StartsWith($expectedFixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use the disposable project fixture.' }
$fixtureProcess = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + [int]$fixture.processId)
if (-not $fixtureProcess -or $fixtureProcess.Name -ne 'navidrome.exe' -or
    -not $fixtureProcess.CommandLine.Contains((Join-Path $fixtureRoot 'navidrome.toml')) -or
    (Get-FileHash -LiteralPath $fixtureProcess.ExecutablePath -Algorithm SHA256).Hash -ne $fixture.binarySha256) { throw 'The fixture process/binary identity changed.' }
$serviceParameters = @{ FixtureState = $FixtureState }
$checks = @(
    @{ name = 'build'; script = 'tools\desktop\run.ps1'; parameters = @{ NoLaunch = $true }; proof = 'build:desktop' },
    @{ name = 'protocol'; script = 'tools\navidrome-tests\run.ps1'; parameters = @{} },
    @{ name = 'audio'; script = 'tools\navidrome-tests\run-audio.ps1'; parameters = @{} },
    @{ name = 'connection'; script = 'tools\navidrome-tests\run-connection.ps1'; parameters = @{} },
    @{ name = 'jukebox'; script = 'tools\navidrome-tests\run-jukebox.ps1'; parameters = @{} },
    @{ name = 'services'; script = 'tools\navidrome-tests\run-services.ps1'; parameters = $serviceParameters },
    @{ name = 'integration'; script = 'tools\navidrome-tests\run-integration.ps1'; parameters = $serviceParameters },
    @{ name = 'playback'; script = 'tools\playback-tests\run.ps1'; parameters = @{} },
    @{ name = 'serializer'; script = 'tools\serializer-tests\run.ps1'; parameters = @{}; proof = 'serializer:models' },
    @{ name = 'resources'; executable = 'python'; arguments = @((Join-Path $projectRoot 'tools\navidrome-tests\check-client.py')); proof = 'resources:desktop' },
    @{ name = 'reference'; executable = 'python'; arguments = @((Join-Path $projectRoot 'tools\navidrome\inspect_source.py')); proof = 'reference:source' },
    @{ name = 'coverage'; executable = 'python'; arguments = @((Join-Path $projectRoot 'tools\navidrome\audit_functions.py')); proof = 'coverage:functions' },
    @{ name = 'acceptance-tools'; executable = 'python'; arguments = @((Join-Path $projectRoot 'tools\navidrome\test_acceptance.py')) },
    @{ name = 'quality-tools'; executable = 'python'; arguments = @((Join-Path $projectRoot 'tools\mixradio\test_quality_tools.py')) }
)
$record = [ordered]@{ schemaVersion = 2; startedAt = [DateTimeOffset]::Now.ToString('o'); code = $initialCode;
    featureInventorySha256 = (Get-FileHash -LiteralPath (Join-Path $projectRoot 'tools\navidrome\features.json') -Algorithm SHA256).Hash.ToLowerInvariant();
    fixture = @{ binarySha256 = $fixture.binarySha256; ffmpegSha256 = $fixture.ffmpegSha256 }; checks = @() }
$failure = $null
foreach ($check in $checks) {
    $logPath = Join-Path $auditRoot ('final-' + $check.name + '.log')
    $observation = [ordered]@{ name = $check.name; startedAt = [DateTimeOffset]::Now.ToString('o'); log = $logPath; exitCode = 0 }
    try {
        if ($check.script) {
            $parameters = $check.parameters
            & (Join-Path $projectRoot $check.script) @parameters 2>&1 | Tee-Object -FilePath $logPath | Out-Null
        } else {
            $arguments = $check.arguments
            & $check.executable @arguments 2>&1 | Tee-Object -FilePath $logPath | Out-Null
            if ($LASTEXITCODE -ne 0) { throw ('Verification failed: ' + $check.name) }
        }
        if ($check.proof) { Add-Content -LiteralPath $logPath -Value ('ND-CHECK:' + $check.proof) -Encoding utf8 }
    } catch {
        $observation.exitCode = 1; $observation.error = $_.Exception.Message; $failure = $_
        Add-Content -LiteralPath $logPath -Value $_.Exception.Message -Encoding utf8
    }
    $observation.completedAt = [DateTimeOffset]::Now.ToString('o')
    $observation.sha256 = (Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $observation.proofs = @(Get-Content -LiteralPath $logPath | Where-Object { $_ -match '^ND-CHECK:[a-z-]+:[a-z-]+$' } | ForEach-Object { $_.Substring(9) })
    $record.checks += $observation
    Write-Output ($check.name + ': ' + $(if ($observation.exitCode -eq 0) { 'passed' } else { 'failed' }))
    if ($failure) { break }
}
$record.completedAt = [DateTimeOffset]::Now.ToString('o')
$record.codeAfter = Get-VerificationCodeState
$record.passed = -not $failure -and $record.codeAfter.commit -eq $initialCode.commit -and $record.codeAfter.trackedClean -and $record.codeAfter.untrackedFiles.Count -eq 0
$recordPath = Join-Path $auditRoot 'verification.json'
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
Write-Output $recordPath
if ($failure) { throw $failure }
if (-not $record.passed) { throw 'The code changed during verification; regenerate evidence after committing the final source.' }
