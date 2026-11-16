param(
    [string]$FixtureState,
    [string]$CompilerPath = $env:MIXDROME_CSC
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $FixtureState) { $FixtureState = Join-Path $projectRoot '.tmp\navidrome-fixture\current.json' }
$FixtureState = (Resolve-Path -LiteralPath $FixtureState).Path
$state = Get-Content -LiteralPath $FixtureState -Raw | ConvertFrom-Json
$expectedRoot = Join-Path $projectRoot '.tmp\navidrome-fixture'
if (-not $state.root.StartsWith($expectedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Integration mutations require a disposable project fixture.' }
$process = Get-Process -Id $state.processId -ErrorAction Stop
if ($process.ProcessName -ne 'navidrome') { throw 'The fixture process identity changed.' }
if (-not $CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe' }
$outputDirectory = Join-Path $projectRoot '.tmp\navidrome-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$caliburn = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'packages\Caliburn.Micro.Core.3.2.0\lib') -Filter 'Caliburn.Micro.dll' -Recurse | Sort-Object FullName | Select-Object -First 1
Copy-Item -LiteralPath $caliburn.FullName -Destination $outputDirectory -Force
$sources = Get-Content -LiteralPath (Join-Path $projectRoot 'tools\serializer-tests\sources.txt') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$sources += @('Client.Common\Models\Navidrome\NavidromeModels.cs', 'Client.Common\Results\NavidromeResult.cs', 'Client.Common\Services\Navidrome\NavidromeApi.cs', 'Client.Common\Services\Navidrome\SubsonicXml.cs', 'tools\serializer-tests\VisualStateResultStub.cs', 'tools\navidrome-tests\IntegrationTests.cs')
$sources += 'Client.Common\Services\Navidrome\MediaDownload.cs'
$sources = $sources | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $projectRoot $_)) } | Select-Object -Unique
$executable = Join-Path $outputDirectory 'navidrome-integration.exe'
& $CompilerPath /nologo /main:NavidromeIntegrationTests "/out:$executable" @sources "/r:$($caliburn.FullName)" /r:System.Net.Http.dll /r:System.ServiceModel.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll
if ($LASTEXITCODE -ne 0) { throw 'Integration compilation failed.' }
& $executable $FixtureState
if ($LASTEXITCODE -ne 0) { throw 'Navidrome fixture integration failed.' }
