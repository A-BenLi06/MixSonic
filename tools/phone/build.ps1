param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidateSet('x86', 'ARM')][string]$Architecture = 'x86',
    [string]$MSBuildPath = $env:MIXDROME_MSBUILD,
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
$phoneProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$phoneProjectPath = Join-Path $phoneProjectRoot 'Client.Phone\Mixdrome.Phone.csproj'
if (-not $MSBuildPath) {
    $MSBuildPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild\14.0\Bin\MSBuild.exe'
}
if (-not (Test-Path -LiteralPath $MSBuildPath -PathType Leaf)) {
    throw 'Supply -MSBuildPath or MIXDROME_MSBUILD for MSBuild 14 with the Windows Phone 8.1 SDK.'
}
$phoneOutputRoot = Join-Path $phoneProjectRoot '.tmp\phone'
New-Item -ItemType Directory -Path $phoneOutputRoot -Force | Out-Null
$phoneBuildLog = Join-Path $phoneOutputRoot "build-$Architecture-$Configuration.log"
$phoneBuildTarget = if ($Rebuild) { 'Rebuild' } else { 'Build' }
Push-Location $phoneProjectRoot
try {
    & $MSBuildPath $phoneProjectPath "/t:$phoneBuildTarget" "/p:Configuration=$Configuration" `
        "/p:Platform=$Architecture" /p:AppxPackageSigningEnabled=false /p:AppxBundle=Never `
        /m /v:quiet /nologo /fl "/flp:logfile=$phoneBuildLog;verbosity=normal"
    if ($LASTEXITCODE -ne 0) {
        throw "Phone $Architecture $Configuration build failed. See $phoneBuildLog"
    }
    $phoneBuiltRoot = Join-Path $phoneProjectRoot "Client.Phone\bin\$Architecture\$Configuration"
    $phoneBuiltManifest = Join-Path $phoneBuiltRoot 'AppxManifest.xml'
    if (-not (Test-Path -LiteralPath $phoneBuiltManifest -PathType Leaf)) {
        throw "Phone build completed without an AppxManifest.xml at $phoneBuiltRoot"
    }
    $phoneBuiltIdentity = ([xml](Get-Content -LiteralPath $phoneBuiltManifest -Raw)).Package.Identity
    if ($phoneBuiltIdentity.ProcessorArchitecture -ne $Architecture.ToLowerInvariant()) {
        throw 'Built phone manifest architecture differs from the requested target.'
    }
    [pscustomobject]@{
        Project = $phoneProjectPath
        Configuration = $Configuration
        Architecture = $Architecture
        BuildLog = $phoneBuildLog
        Manifest = $phoneBuiltManifest
        PackageSigningEnabled = $false
    } | ConvertTo-Json
} finally {
    Pop-Location
}
