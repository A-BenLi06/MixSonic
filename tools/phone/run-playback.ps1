param([string]$CompilerPath = $env:MIXDROME_CSC)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe' }
if (-not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) { throw 'Supply -CompilerPath or MIXDROME_CSC for the C# compiler.' }
$outputDirectory = Join-Path $projectRoot '.tmp\phone-playback-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sources = @(
    'Client.Phone\Services\BackgroundAudioMessages.cs',
    'Client.Phone\Services\AudioQueueState.cs',
    'tools\phone\PlaybackTests.cs'
) | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $projectRoot $_)) }
$executable = Join-Path $outputDirectory 'phone-playback.exe'
& $CompilerPath /nologo /main:PhonePlaybackTests "/out:$executable" @sources /r:System.Runtime.Serialization.dll
if ($LASTEXITCODE -ne 0) { throw 'Production phone audio compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Production phone audio assertions failed.' }
