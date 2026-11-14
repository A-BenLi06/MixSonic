param([string]$Repository)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $projectRoot
try {
    if (-not $Repository) {
        $Repository = & gh repo view --json nameWithOwner --jq .nameWithOwner
        if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the GitHub repository.' }
    }
    & gh api --method PATCH "repos/$Repository" --input '.github/repository-settings.json' --silent
    if ($LASTEXITCODE -ne 0) { throw 'Repository settings update failed. Push main first.' }
    $json = & gh api "repos/$Repository/rulesets"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot list repository rulesets.' }
    $existing = @($json | ConvertFrom-Json)
    foreach ($path in @('.github/main-ruleset.json', '.github/release-tag-ruleset.json')) {
        $rule = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $found = @($existing | Where-Object name -EQ $rule.name)
        if ($found.Count -gt 1) { throw "Duplicate ruleset name: $($rule.name)" }
        $endpoint = "repos/$Repository/rulesets"
        $method = 'POST'
        if ($found.Count -eq 1) { $endpoint += "/$($found[0].id)"; $method = 'PUT' }
        & gh api --method $method $endpoint --input $path --silent
        if ($LASTEXITCODE -ne 0) { throw "Ruleset update failed: $($rule.name)" }
    }
    $scratch = Join-Path $projectRoot '.tmp/ci'
    New-Item -ItemType Directory -Path $scratch -Force | Out-Null
    $environmentInput = Join-Path $scratch 'release-environment.json'
    @{ deployment_branch_policy = @{ protected_branches = $false; custom_branch_policies = $true } } | ConvertTo-Json | Set-Content -LiteralPath $environmentInput
    & gh api --method PUT "repos/$Repository/environments/github-releases" --input $environmentInput --silent
    if ($LASTEXITCODE -ne 0) { throw 'Release environment update failed.' }
    $json = & gh api "repos/$Repository/environments/github-releases/deployment-branch-policies"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read release environment branches.' }
    $policies = @(($json | ConvertFrom-Json).branch_policies)
    if (@($policies | Where-Object { $_.name -eq 'main' -and $_.type -eq 'branch' }).Count -eq 0) {
        & gh api --method POST "repos/$Repository/environments/github-releases/deployment-branch-policies" -f name=main -f type=branch --silent
        if ($LASTEXITCODE -ne 0) { throw 'Release environment main policy creation failed.' }
    }
    foreach ($policy in ($policies | Where-Object { $_.name -ne 'main' -or $_.type -ne 'branch' })) {
        & gh api --method DELETE "repos/$Repository/environments/github-releases/deployment-branch-policies/$($policy.id)" --silent
        if ($LASTEXITCODE -ne 0) { throw 'Release environment policy cleanup failed.' }
    }
    Write-Host 'Applied main protection, immutable release tags and the main-only release environment.'
} finally { Pop-Location }
