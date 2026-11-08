param([string]$CompilerPath = $env:SUBSONIC8_CSC)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $CompilerPath) { $CompilerPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\csc.exe' }
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Supply -CompilerPath or SUBSONIC8_CSC for the desktop C# compiler.' }
$outputDirectory = Join-Path $projectRoot '.tmp\serializer-tests'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$caliburn = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'packages\Caliburn.Micro.Core.3.2.0\lib') -Filter 'Caliburn.Micro.dll' -Recurse |
    Sort-Object FullName | Select-Object -First 1
if (-not $caliburn) { throw 'Restore the Caliburn.Micro.Core package first.' }
Copy-Item -LiteralPath $caliburn.FullName -Destination $outputDirectory -Force
$sources = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'sources.txt') |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { Join-Path $projectRoot $_ }
$sources += Join-Path $PSScriptRoot 'VisualStateResultStub.cs'
$references = @("/r:$($caliburn.FullName)", '/r:System.Net.Http.dll', '/r:System.ServiceModel.dll', '/r:System.Runtime.Serialization.dll', '/r:System.Xml.dll', '/r:System.Xml.Linq.dll', '/r:Microsoft.CSharp.dll')
$library = Join-Path $outputDirectory 'ClientCommonDesktop.dll'
& $CompilerPath /nologo /target:library "/out:$library" @sources @references
if ($LASTEXITCODE -ne 0) { throw 'Current serializer model sources failed to compile.' }
$executable = Join-Path $outputDirectory 'serializer-tests.exe'
& $CompilerPath /nologo /main:SerializerTests "/out:$executable" (Join-Path $PSScriptRoot 'SerializerTests.cs') "/r:$library" @references
if ($LASTEXITCODE -ne 0) { throw 'Serializer tests failed to compile.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Serializer regressions failed.' }
