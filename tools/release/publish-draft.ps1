param([Parameter(Mandatory)][string]$Directory)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$Directory = (Resolve-Path -LiteralPath $Directory).Path
$metadata = Get-Content -LiteralPath (Join-Path $Directory 'release-manifest.json') -Raw | ConvertFrom-Json
Push-Location $projectRoot
try {
    $repo = & gh repo view --json nameWithOwner --jq .nameWithOwner
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the GitHub repository.' }
    $pin = & gh variable get RELEASE_CERTIFICATE_SHA256 --repo $repo --json value --jq .value
    if ($LASTEXITCODE -ne 0) { throw 'Configure RELEASE_CERTIFICATE_SHA256 from your release certificate first.' }
    & python tools/release/release.py verify --directory $Directory --tag $metadata.tag --commit $metadata.commit --certificate-sha256 $pin
    if ($LASTEXITCODE -ne 0) { throw 'Release validation failed.' }
    & git fetch origin main --tags
    if ($LASTEXITCODE -ne 0) { throw 'Cannot refresh the main branch and tags.' }
    & git merge-base --is-ancestor $metadata.commit origin/main
    if ($LASTEXITCODE -ne 0) { throw 'Only commits merged into main can be released.' }
    $existingTag = & git rev-parse --verify --quiet "$($metadata.tag)^{commit}"
    if ($LASTEXITCODE -eq 0) {
        if ($existingTag -ne $metadata.commit) { throw 'The existing tag points to another commit.' }
    } else {
        & git tag -a $metadata.tag $metadata.commit -m "Release $($metadata.version)"
        if ($LASTEXITCODE -ne 0) { throw 'Tag creation failed.' }
    }
    & git push origin "refs/tags/$($metadata.tag)"
    if ($LASTEXITCODE -ne 0) { throw 'Release tag push failed.' }
    $notes = Join-Path $Directory '../release-notes.md'
    @"
Windows 8.1 AppX release $($metadata.version), built locally from commit $($metadata.commit).

Download $($metadata.asset) and check its SHA-256 against release-manifest.json. Installation and verification instructions: https://github.com/$repo/blob/$($metadata.tag)/docs/releasing.md

Cloud CI checks CLR regressions, source and tooling. Native UI/audio acceptance remains a separate local check.
"@ | Set-Content -LiteralPath $notes -Encoding utf8
    & gh release create $metadata.tag (Join-Path $Directory $metadata.asset) (Join-Path $Directory 'release-manifest.json') --repo $repo --verify-tag --draft --title $metadata.tag --notes-file $notes
    if ($LASTEXITCODE -ne 0) { throw 'Draft creation failed; an existing release is never overwritten.' }
    & gh workflow run release.yml --repo $repo --ref main -f "tag=$($metadata.tag)"
    if ($LASTEXITCODE -ne 0) { throw 'The draft is retained; manually run Publish release on main when CI is ready.' }
} finally { Pop-Location }
