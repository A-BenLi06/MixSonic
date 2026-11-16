param([string]$CompilerPath = $env:MIXDROME_CSC)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe' }
$outputDirectory = Join-Path $projectRoot '.tmp\phone-connection-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$caliburn = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'packages\Caliburn.Micro.Core.3.2.0\lib') -Filter 'Caliburn.Micro.dll' -Recurse | Sort-Object FullName | Select-Object -First 1
if (-not $caliburn) { throw 'Restore Caliburn.Micro.Core 3.2.0 before running phone source tests.' }
Copy-Item -LiteralPath $caliburn.FullName -Destination $outputDirectory -Force
$sources = Get-Content -LiteralPath (Join-Path $projectRoot 'tools\serializer-tests\sources.txt') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$sources += @(
    'Client.Common\Models\Navidrome\NavidromeModels.cs', 'Client.Common\Results\NavidromeResult.cs',
    'Client.Common\Services\Navidrome\NavidromeApi.cs',
    'Client.Phone\Services\BackgroundAudioMessages.cs',
    'Client.Phone\Services\PhoneSettings.cs', 'Client.Phone\Services\PhoneSubsonicServiceFactory.cs',
    'Client.Phone\Services\WP8SubsonicService.cs',
    'tools\serializer-tests\VisualStateResultStub.cs', 'tools\navidrome-tests\ProtocolServer.cs',
    'tools\phone\ConnectionTests.cs'
)
$sources = $sources | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $projectRoot $_)) } | Select-Object -Unique
$executable = Join-Path $outputDirectory 'phone-connection.exe'
& $CompilerPath /nologo /main:PhoneConnectionTests "/out:$executable" @sources "/r:$($caliburn.FullName)" /r:System.Net.Http.dll /r:System.ServiceModel.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll
if ($LASTEXITCODE -ne 0) { throw 'Production phone connection compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Production phone connection assertions failed.' }

$artworkPageSource = [IO.File]::ReadAllText((Join-Path $projectRoot 'Client.Phone\Views\PlaybackPage.xaml.cs'))
$artworkMethodPattern = '(?ms)^        private static string ResolveArtwork\([^\r\n]*\)\r?\n        \{.*?^        \}'
$artworkMethods = [regex]::Matches($artworkPageSource, $artworkMethodPattern)
if ($artworkMethods.Count -ne 1) { throw 'Expected one production PlaybackPage.ResolveArtwork method.' }
$artworkFixture = [IO.File]::ReadAllText((Join-Path $projectRoot 'tools\phone\ArtworkTests.cs'))
$artworkMarker = '/* PRODUCTION_ARTWORK_METHOD */'
if ($artworkFixture.Split(@($artworkMarker), [StringSplitOptions]::None).Length -ne 2) { throw 'Artwork fixture must contain exactly one method insertion marker.' }
$artworkHarness = Join-Path $outputDirectory 'PhoneArtworkHarness.cs'
[IO.File]::WriteAllText($artworkHarness, $artworkFixture.Replace($artworkMarker, $artworkMethods[0].Value), [Text.UTF8Encoding]::new($false))
$artworkExecutable = Join-Path $outputDirectory 'phone-artwork.exe'
& $CompilerPath /nologo /main:PhoneArtworkTests "/out:$artworkExecutable" @sources $artworkHarness "/r:$($caliburn.FullName)" /r:System.Net.Http.dll /r:System.ServiceModel.dll /r:System.Runtime.Serialization.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:Microsoft.CSharp.dll
if ($LASTEXITCODE -ne 0) { throw 'Production phone artwork compilation failed.' }
& $artworkExecutable
if ($LASTEXITCODE -ne 0) { throw 'Production phone artwork assertions failed.' }
