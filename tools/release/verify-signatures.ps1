param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$CertificateSha256,
    [string]$SignToolPath = $env:MIXDROME_SIGNTOOL,
    [switch]$TrustPinnedCertificate
)

$ErrorActionPreference = 'Stop'
if (-not $SignToolPath) {
    $SignToolPath = Get-ChildItem -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin') -Filter signtool.exe -Recurse |
        Where-Object FullName -Match '[\\/]x64[\\/]signtool.exe$' | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $SignToolPath -or -not (Test-Path -LiteralPath $SignToolPath)) { throw 'Supply MIXDROME_SIGNTOOL or -SignToolPath.' }
$certificatePath = Join-Path $Directory 'SigningCertificate.cer'
Write-Host 'Checking the configured certificate pin and embedded AppX signer.'
if ((Get-FileHash -LiteralPath $certificatePath -Algorithm SHA256).Hash -ne $CertificateSha256) { throw 'Signing certificate pin differs.' }
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
Add-Type -AssemblyName System.Security.Cryptography.Pkcs
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $Directory 'Mixdrome.appx'))
try {
    $stream = $archive.GetEntry('AppxSignature.p7x').Open()
    $buffer = [IO.MemoryStream]::new()
    try { $stream.CopyTo($buffer); $signature = $buffer.ToArray() }
    finally { $stream.Dispose(); $buffer.Dispose() }
    if ([Text.Encoding]::ASCII.GetString($signature, 0, 4) -ne 'PKCX') { throw 'Invalid AppX signature header.' }
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode([byte[]]$signature[4..($signature.Length - 1)])
    if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $certificate.Thumbprint) {
        throw 'The AppX was not signed by the pinned certificate.'
    }
    Write-Host 'The embedded signer matches the pin; SignTool will verify the complete cryptographic signature and package integrity.'
} finally { $archive.Dispose() }
$addedTrust = $false
$store = [Security.Cryptography.X509Certificates.X509Store]::new('Root', 'LocalMachine')
try {
    # Only disposable CI verification opts in. Never trust an unpinned uploaded key.
    if ($TrustPinnedCertificate -and $certificate.Subject -eq $certificate.Issuer) {
        # CurrentUser Root writes invoke Windows consent UI. The disposable hosted
        # runner is elevated, so it can use LocalMachine Root through standard APIs.
        if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Temporary root trust is allowed only on disposable GitHub Actions runners.' }
        $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'The hosted signature verifier needs an elevated runner.' }
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        Write-Host 'Opened the temporary LocalMachine Root store on the disposable runner.'
        if ($store.Certificates.Find('FindByThumbprint', $certificate.Thumbprint, $false).Count -eq 0) {
            $store.Add($certificate)
            $addedTrust = $true
        }
        Write-Host 'Pinned certificate trust is ready for SignTool verification.'
    }
    foreach ($package in (Get-ChildItem -LiteralPath $Directory -Filter '*.appx' -Recurse)) {
        & $SignToolPath verify /pa /all /v $package.FullName
        if ($LASTEXITCODE -ne 0) { throw "AppX signature verification failed: $($package.Name)" }
    }
} finally {
    if ($addedTrust) { $store.Remove($certificate) }
    $store.Close()
    $certificate.Dispose()
}
Write-Host 'PASS: pinned AppX signer, package integrity and dependency signatures.'
