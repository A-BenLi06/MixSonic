param([string]$FixtureState)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $FixtureState) { $FixtureState = Join-Path $projectRoot '.tmp\navidrome-fixture\current.json' }
$state = Get-Content -LiteralPath $FixtureState -Raw | ConvertFrom-Json
$expectedRoot = Join-Path $projectRoot '.tmp\navidrome-fixture'
$runRoot = [IO.Path]::GetFullPath($state.root)
if (-not $runRoot.StartsWith($expectedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The manifest is not a disposable project fixture.' }
$process = Get-CimInstance Win32_Process -Filter ("ProcessId = " + [int]$state.processId)
if (-not $process) { Write-Output 'The fixture has already stopped.'; return }
$configPath = Join-Path $runRoot 'navidrome.toml'
if ($process.Name -ne 'navidrome.exe' -or -not $process.CommandLine.Contains($configPath) -or
    (Get-FileHash -LiteralPath $process.ExecutablePath -Algorithm SHA256).Hash -ne $state.binarySha256) { throw 'Process identity does not match the disposable fixture.' }
Stop-Process -Id $process.ProcessId -ErrorAction Stop
Write-Output 'Stopped the disposable fixture. Its generated music, database and logs remain available.'
