param([string]$CompilerPath = $env:SUBSONIC8_CSC)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe' }
$outputDirectory = Join-Path $projectRoot '.tmp\navidrome-audio-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$caliburn = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'packages\Caliburn.Micro.Core.3.2.0\lib') -Filter 'Caliburn.Micro.dll' -Recurse | Sort-Object FullName | Select-Object -First 1
Copy-Item -LiteralPath $caliburn.FullName -Destination $outputDirectory -Force
$sources = Get-Content -LiteralPath (Join-Path $projectRoot 'tools\serializer-tests\sources.txt') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$sources += @(
    'Client.Common\Services\DataStructures\PlayerManagementService\IPlayer.cs',
    'Client\AudioPlayback\AudioPlayerViewModel.cs', 'Client\AudioPlayback\IAudioPlayerViewModel.cs', 'Client\AudioPlayback\IPlaybackReporter.cs',
    'Client\Navidrome\NavidromeStreamService.cs', 'Client\Navidrome\NavidromeSessionService.cs',
    'Client\Navidrome\NavidromeText.cs',
    'Client.Common\Models\Navidrome\NavidromeModels.cs', 'Client.Common\Results\NavidromeResult.cs',
    'Client.Common\Services\Navidrome\NavidromeApi.cs', 'Client.Common\Services\Navidrome\SubsonicXml.cs',
    'Client\Framework\Interfaces\IPlayerControls.cs', 'Client\Framework\Interfaces\IExtendedPlayerControls.cs',
    'tools\serializer-tests\VisualStateResultStub.cs', 'tools\navidrome-tests\StorageAdapters.cs',
    'tools\navidrome-tests\AudioAdapters.cs', 'tools\navidrome-tests\AudioTests.cs'
)
$sources = $sources | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $projectRoot $_)) } | Select-Object -Unique
$executable = Join-Path $outputDirectory 'navidrome-audio.exe'
& $CompilerPath /nologo /define:NATIVE_PLAYER_TESTS /main:NavidromeAudioTests "/out:$executable" @sources "/r:$($caliburn.FullName)" /r:System.Net.Http.dll /r:System.ServiceModel.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll
if ($LASTEXITCODE -ne 0) { throw 'Production audio compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Production audio assertions failed.' }
