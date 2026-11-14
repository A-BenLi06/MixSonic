param(
    [Parameter(Mandatory)][string]$CertificateThumbprint,
    [ValidateSet('AnyCPU', 'x86', 'x64', 'ARM')][string]$Architecture = 'AnyCPU',
    [string]$MSBuildPath = $env:SUBSONIC8_MSBUILD,
    [string]$SignToolPath = $env:SUBSONIC8_SIGNTOOL,
    [string]$TimestampUrl
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $projectRoot
try {
    $dirty = & git status --porcelain
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Release builds require a clean committed checkout.' }
    $commit = & git rev-parse HEAD
    $sourceManifest = [xml](Get-Content -LiteralPath 'Client/Package.appxmanifest' -Raw)
    $version = $sourceManifest.Package.Identity.Version
    $project = [xml](Get-Content -LiteralPath 'Client/Client.csproj' -Raw)
    $temporaryThumbprint = $project.SelectSingleNode('//*[local-name()="PackageCertificateThumbprint"]').InnerText
    $CertificateThumbprint = $CertificateThumbprint.Replace(' ', '')
    if ($CertificateThumbprint -eq $temporaryThumbprint) { throw 'Use a dedicated release certificate, not the tracked temporary key.' }
    $certificate = Get-Item -LiteralPath "Cert:/CurrentUser/My/$CertificateThumbprint"
    if (-not $certificate.HasPrivateKey -or $certificate.Subject -ne $sourceManifest.Package.Identity.Publisher) {
        throw 'The signing certificate needs a private key and must match the manifest Publisher.'
    }
    if (-not $MSBuildPath) { $MSBuildPath = Join-Path ${env:ProgramFiles(x86)} 'MSBuild/14.0/Bin/MSBuild.exe' }
    if (-not $SignToolPath) {
        $SignToolPath = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin') -Filter signtool.exe -Recurse |
            Where-Object FullName -Match '[\\/]x64[\\/]signtool.exe$' | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not (Test-Path -LiteralPath $MSBuildPath) -or -not $SignToolPath) { throw 'Supply the legacy MSBuild and Windows SDK SignTool paths.' }
    $scratch = Join-Path $projectRoot ('.tmp/release-build/' + [Guid]::NewGuid().ToString('N'))
    $packages = Join-Path $scratch 'AppPackages'
    New-Item -ItemType Directory -Path $packages -Force | Out-Null
    & $MSBuildPath 'Client/Client.csproj' /t:Rebuild /p:Configuration=Release "/p:Platform=$Architecture" "/p:AppxPackageDir=$packages\" /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false /p:AppxAutoIncrementPackageRevision=false /p:GenerateAppxPackageOnBuild=true /p:NativeUiProbe=false /p:NativeUiObserve=false /p:NativePlaybackProbe=false /m /nologo /v:minimal /fl "/flp:logfile=$scratch/build.log;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { throw "Legacy AppX build failed; see $scratch/build.log" }
    $candidates = @(Get-ChildItem -LiteralPath $packages -Filter '*.appx' -Recurse | Where-Object FullName -NotMatch '[\\/]Dependencies[\\/]')
    if ($candidates.Count -ne 1) { throw 'Expected exactly one freshly built application AppX.' }
    $package = $candidates[0]
    $signArguments = @('sign', '/sha1', $CertificateThumbprint, '/s', 'My', '/fd', 'SHA256')
    if ($TimestampUrl) { $signArguments += @('/tr', $TimestampUrl, '/td', 'SHA256') }
    & $SignToolPath @signArguments $package.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Release signing failed.' }
    $publicCertificate = Join-Path $scratch 'SigningCertificate.cer'
    [IO.File]::WriteAllBytes($publicCertificate, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    $output = Join-Path $projectRoot ".tmp/releases/v$version-$Architecture"
    $stageArguments = @('tools/release/release.py', 'prepare', '--package', $package.FullName, '--certificate', $publicCertificate, '--output', $output)
    $dependencies = Join-Path $package.DirectoryName 'Dependencies'
    if (Test-Path -LiteralPath $dependencies) { $stageArguments += @('--dependencies', $dependencies) }
    & python @stageArguments
    if ($LASTEXITCODE -ne 0) { throw 'Release preparation failed.' }
    $pin = (Get-FileHash -LiteralPath $publicCertificate -Algorithm SHA256).Hash.ToLowerInvariant()
    $verified = Join-Path $scratch 'verified'
    & python tools/release/release.py verify --directory $output --tag "v$version" --commit $commit --certificate-sha256 $pin --extract $verified
    if ($LASTEXITCODE -ne 0) { throw 'Prepared release validation failed.' }
    & (Join-Path $PSScriptRoot 'verify-signatures.ps1') -Directory $verified -CertificateSha256 $pin -SignToolPath $SignToolPath
    Write-Host "Release assets: $output"
    Write-Host "Repository variable RELEASE_CERTIFICATE_SHA256: $pin"
} finally { Pop-Location }
