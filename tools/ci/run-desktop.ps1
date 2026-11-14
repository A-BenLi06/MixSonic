param([string]$CompilerPath = $env:SUBSONIC8_CSC)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $CompilerPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $CompilerPath = @(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe') | Select-Object -First 1
    }
    if (-not $CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild/14.0/Bin/csc.exe' }
}
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Supply -CompilerPath or SUBSONIC8_CSC.' }
$suites = @(
    'tools/serializer-tests/run.ps1',
    'tools/navidrome-tests/run.ps1',
    'tools/navidrome-tests/run-audio.ps1',
    'tools/navidrome-tests/run-connection.ps1',
    'tools/navidrome-tests/run-jukebox.ps1',
    'tools/navidrome-tests/run-home.ps1',
    'tools/playback-tests/run.ps1'
)
foreach ($suite in $suites) {
    Write-Host "Running $suite"
    & (Join-Path $projectRoot $suite) -CompilerPath $CompilerPath
}
Write-Host 'PASS: all seven CLR regression suites. Native WinRT checks are separate.'
& (Join-Path $projectRoot 'tools/release/test-signatures.ps1') -CompilerPath $CompilerPath
