param([string]$CompilerPath = $env:MIXDROME_CSC)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe' }
$outputDirectory = Join-Path $projectRoot '.tmp\navidrome-jukebox-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$caliburn = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'packages\Caliburn.Micro.Core.3.2.0\lib') -Filter 'Caliburn.Micro.dll' -Recurse | Sort-Object FullName | Select-Object -First 1
if (-not $caliburn) { throw 'Restore Caliburn.Micro.Core first.' }
Copy-Item -LiteralPath $caliburn.FullName -Destination $outputDirectory -Force
$sources = Get-Content -LiteralPath (Join-Path $projectRoot 'tools\serializer-tests\sources.txt') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$sources += @(
    'Client.Common\Models\Navidrome\NavidromeModels.cs', 'Client.Common\Results\NavidromeResult.cs',
    'Client.Common\Services\Navidrome\NavidromeApi.cs', 'Client.Common\Services\Navidrome\SubsonicXml.cs',
    'Client.Common\Services\PlaylistManagementService.cs', 'Client.Common\Services\IPlaylistManagementService.cs',
    'Client.Common\Services\DataStructures\PlaylistManagementService\PlaybackState.cs',
    'Client\Navidrome\NavidromeSessionService.cs', 'Client\Navidrome\NavidromePageViewModel.cs',
    'Client\Navidrome\NavidromeJukeboxViewModel.cs', 'Client\Navidrome\NavidromeDialogContracts.cs',
    'Client\Navidrome\NavidromeItem.cs', 'Client\Navidrome\NavidromeText.cs', 'Client\Navidrome\OfflineDownloadItem.cs',
    'Client.Common\Models\Navidrome\OfflineCatalog.cs', 'Client.Common\Services\Navidrome\MediaDownload.cs',
    'tools\serializer-tests\VisualStateResultStub.cs', 'tools\navidrome-tests\StorageAdapters.cs',
    'tools\navidrome-tests\JukeboxAdapters.cs', 'tools\navidrome-tests\JukeboxTests.cs'
)
$sources = $sources | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $projectRoot $_)) } | Select-Object -Unique
$executable = Join-Path $outputDirectory 'navidrome-jukebox.exe'
& $CompilerPath /nologo /main:NavidromeJukeboxTests "/out:$executable" @sources "/r:$($caliburn.FullName)" /r:System.Net.Http.dll /r:System.ServiceModel.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll
if ($LASTEXITCODE -ne 0) { throw 'Production jukebox page compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Production jukebox assertions failed.' }
