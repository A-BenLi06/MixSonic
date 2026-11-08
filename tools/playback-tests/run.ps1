param(
    [string]$CompilerPath = $env:SUBSONIC8_CSC
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CompilerPath) {
    $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe'
}
if (-not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'C# compiler unavailable. Supply -CompilerPath or SUBSONIC8_CSC.'
}
$outputDirectory = Join-Path $projectRoot '.tmp\playback-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$caliburn = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'packages\Caliburn.Micro.Core.3.2.0\lib') -Filter 'Caliburn.Micro.dll' -Recurse |
    Sort-Object FullName | Select-Object -First 1
if (-not $caliburn) { throw 'Restore the Caliburn.Micro.Core package first.' }
Copy-Item -LiteralPath $caliburn.FullName -Destination $outputDirectory -Force
$sources = Get-Content -LiteralPath (Join-Path $projectRoot 'tools\serializer-tests\sources.txt') |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { Join-Path $projectRoot $_ }
$sources += @(
    'Client.Common\Models\PlaybackRepeatMode.cs',
    'Client.Common\EventAggregatorMessages\PlayQueueMessage.cs',
    'Client.Common\Services\PlaylistManagementService.cs',
    'Client.Common\Services\IPlaylistManagementService.cs',
    'Client.Common\Services\DataStructures\PlaylistManagementService\PlaybackState.cs',
    'tools\serializer-tests\VisualStateResultStub.cs',
    'tools\playback-tests\PlaybackTests.cs'
) | ForEach-Object { Join-Path $projectRoot $_ }
$sources = $sources | Select-Object -Unique
$executable = Join-Path $outputDirectory 'playback-tests.exe'
& $CompilerPath /nologo /main:PlaybackTests "/out:$executable" @sources "/r:$($caliburn.FullName)" /r:System.Net.Http.dll /r:System.ServiceModel.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll
if ($LASTEXITCODE -ne 0) { throw 'Playback regression compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Playback regressions failed.' }
