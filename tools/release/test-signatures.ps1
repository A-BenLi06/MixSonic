param([Parameter(Mandatory)][string]$CompilerPath)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run signature trust self-tests on the disposable hosted Windows runner.' }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$scratch = Join-Path $projectRoot ('.tmp/ci/signature-test-' + [Guid]::NewGuid().ToString('N'))
$layout = Join-Path $scratch 'layout'
$verified = Join-Path $scratch 'verified'
New-Item -ItemType Directory -Path $layout,$verified -Force | Out-Null
$signTool = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin') -Filter signtool.exe -Recurse |
    Where-Object FullName -Match '[\\/]x64[\\/]signtool.exe$' | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signTool) { throw 'The signature tests require the Windows 10 SDK tools.' }
$makeAppx = Join-Path (Split-Path $signTool) 'makeappx.exe'
$source = Join-Path $scratch 'Fixture.cs'
'class Fixture { static void Main() {} }' | Set-Content -LiteralPath $source
& $CompilerPath /nologo "/out:$layout/Fixture.exe" $source
if ($LASTEXITCODE -ne 0) { throw 'Synthetic payload compilation failed.' }
# This package only tests signing/integrity. It is never installed or run.
@'
<Package xmlns="http://schemas.microsoft.com/appx/2010/manifest">
  <Identity Name="SubSonic8.SignatureFixture" Publisher="CN=SubSonic8.SignatureFixture" Version="1.0.0.0" ProcessorArchitecture="neutral" />
  <Properties><DisplayName>Fixture</DisplayName><PublisherDisplayName>Fixture</PublisherDisplayName><Logo>Logo.png</Logo></Properties>
  <Prerequisites><OSMinVersion>6.3.0</OSMinVersion><OSMaxVersionTested>6.3.0</OSMaxVersionTested></Prerequisites>
  <Resources><Resource Language="en-US" /></Resources>
  <Applications><Application Id="Fixture" Executable="Fixture.exe" EntryPoint="Fixture.App"><VisualElements DisplayName="Fixture" Description="Synthetic signature test" Logo="Logo.png" SmallLogo="Logo.png" ForegroundText="light" BackgroundColor="#000000"><SplashScreen Image="Logo.png" /></VisualElements></Application></Applications>
</Package>
'@ | Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Encoding utf8
$png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1ioAAAAASUVORK5CYII='
[IO.File]::WriteAllBytes((Join-Path $layout 'Logo.png'), [Convert]::FromBase64String($png))
$package = Join-Path $verified 'SubSonic8.appx'
& $makeAppx pack /d $layout /p $package /nv /o
if ($LASTEXITCODE -ne 0) { throw 'Synthetic AppX packing failed.' }
$key = [Security.Cryptography.RSA]::Create(2048)
$request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=SubSonic8.SignatureFixture', $key, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
$usage = [Security.Cryptography.OidCollection]::new()
$usage.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3')) | Out-Null
$request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usage, $false))
$certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(1))
$pfxPath = Join-Path $scratch 'ephemeral.pfx'
$password = [Guid]::NewGuid().ToString('N')
try {
    [IO.File]::WriteAllBytes($pfxPath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password))
    [IO.File]::WriteAllBytes((Join-Path $verified 'SigningCertificate.cer'), $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    & $signTool sign /f $pfxPath /p $password /fd SHA256 $package
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic signing failed.' }
    $pin = (Get-FileHash -LiteralPath (Join-Path $verified 'SigningCertificate.cer') -Algorithm SHA256).Hash
    & (Join-Path $PSScriptRoot 'verify-signatures.ps1') -Directory $verified -CertificateSha256 $pin -SignToolPath $signTool -TrustPinnedCertificate
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('Root', 'LocalMachine')
    try {
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        if ($store.Certificates.Find('FindByThumbprint', $certificate.Thumbprint, $false).Count -ne 0) { throw 'Temporary certificate trust was not removed.' }
    } finally { $store.Close() }
    $archive = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $archive.GetEntry('Fixture.exe').Delete()
        $stream = $archive.CreateEntry('Fixture.exe').Open()
        try { $stream.WriteByte(42) } finally { $stream.Dispose() }
    } finally { $archive.Dispose() }
    $rejected = $false
    try {
        & (Join-Path $PSScriptRoot 'verify-signatures.ps1') -Directory $verified -CertificateSha256 $pin -SignToolPath $signTool -TrustPinnedCertificate
    } catch { $rejected = $true }
    if (-not $rejected) { throw 'A changed AppX payload passed signature verification.' }
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try {
        if ($store.Certificates.Find('FindByThumbprint', $certificate.Thumbprint, $false).Count -ne 0) { throw 'Trust cleanup failed after tamper rejection.' }
    } finally { $store.Close() }
    Write-Host 'PASS: genuine AppX signing, pinned verification, trust cleanup and tamper rejection.'
    # SignTool's expected negative result has been handled by the test.
    $global:LASTEXITCODE = 0
} finally {
    if (Test-Path -LiteralPath $pfxPath) { Remove-Item -LiteralPath $pfxPath -Force }
    $certificate.Dispose()
    $key.Dispose()
}
