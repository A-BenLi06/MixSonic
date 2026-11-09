param(
    [string]$BinaryPath,
    [string]$MusicPath,
    [string]$FFmpegPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixtureRoot = Join-Path $projectRoot '.tmp\navidrome-fixture'
if (-not $BinaryPath) { $BinaryPath = Join-Path $fixtureRoot 'bin\navidrome.exe' }
if (-not $MusicPath) { $MusicPath = Join-Path $fixtureRoot 'test-music' }
$BinaryPath = (Resolve-Path -LiteralPath $BinaryPath).Path
$MusicPath = (Resolve-Path -LiteralPath $MusicPath).Path
if (-not $MusicPath.StartsWith($fixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'A fixture music folder must be under the ignored project fixture directory.'
}
$ffmpegConfiguration = ''
if ($FFmpegPath) {
    $FFmpegPath = (Resolve-Path -LiteralPath $FFmpegPath).Path
    if (-not $FFmpegPath.StartsWith($fixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The explicit fixture FFmpeg must be under the ignored project fixture directory.'
    }
    $ffmpegConfiguration = 'FFmpegPath = ' + (ConvertTo-Json -InputObject $FFmpegPath -Compress)
}
$runRoot = Join-Path $fixtureRoot ('run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$dataFolder = Join-Path $runRoot 'data'
New-Item -ItemType Directory -Path $dataFolder -Force | Out-Null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$configPath = Join-Path $runRoot 'navidrome.toml'
@"
Address = '127.0.0.1'
Port = $port
MusicFolder = '$MusicPath'
DataFolder = '$dataFolder'
EnableSharing = true
ScanSchedule = '0'
LogLevel = 'debug'
$ffmpegConfiguration
"@ | Set-Content -LiteralPath $configPath -Encoding utf8
$process = Start-Process -FilePath $BinaryPath -ArgumentList @('--configfile', ('"' + $configPath + '"')) -WorkingDirectory $runRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runRoot 'server.log') -RedirectStandardError (Join-Path $runRoot 'server-error.log') -PassThru
$state = [ordered]@{
    root = $runRoot
    port = $port
    processId = $process.Id
    binarySha256 = (Get-FileHash -LiteralPath $BinaryPath -Algorithm SHA256).Hash
    ffmpegSha256 = $(if ($FFmpegPath) { (Get-FileHash -LiteralPath $FFmpegPath -Algorithm SHA256).Hash } else { $null })
    username = 'fixture-listener'
    password = 'Fixture-' + [guid]::NewGuid().ToString('N')
}
$state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixtureRoot 'current.json') -Encoding utf8
$listening = $false
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    if ($process.HasExited) { throw 'The disposable server exited before opening its listener.' }
    $connection = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($connection -and @($connection | Where-Object OwningProcess -eq $process.Id).Count -gt 0) { $listening = $true; break }
    Start-Sleep -Milliseconds 500
}
if (-not $listening) { throw 'The fixture did not open its own loopback listener.' }
$body = @{ username=$state.username; password=$state.password } | ConvertTo-Json
$admin = Invoke-RestMethod -Method Post -Uri ("http://127.0.0.1:$port/auth/createAdmin") -ContentType 'application/json' -Body $body -TimeoutSec 10
if ($admin.username -ne $state.username -or -not $admin.isAdmin) { throw 'Fixture account initialization failed.' }
[pscustomobject]$state | Select-Object root,port,processId,binarySha256 | ConvertTo-Json
