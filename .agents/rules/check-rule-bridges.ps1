param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

$sourceRoot = Join-Path $RepoRoot ".agents/rules"
$antigravityBridgeRoot = Join-Path $RepoRoot ".agent/rules"
$claudeBridgeRoot = Join-Path $RepoRoot ".claude/rules"
$cursorBridgeRoot = Join-Path $RepoRoot ".cursor/rules"

function Get-RuleData {
    param([string]$RulePath)

    $raw = Get-Content $RulePath -Raw
    $fmMatch = [regex]::Match($raw, "(?ms)^---\r?\n(.*?)\r?\n---")
    if (-not $fmMatch.Success) {
        throw "No YAML frontmatter in $RulePath"
    }

    $frontmatter = $fmMatch.Groups[1].Value
    $body = $raw.Substring($fmMatch.Index + $fmMatch.Length).Trim()
    $trigger = [regex]::Match($frontmatter, "(?m)^trigger:\s*(.+)$").Groups[1].Value.Trim()
    $sourceRule = [regex]::Match(
        $frontmatter,
        "(?m)^\s*source_rule:\s*`"?([^`"\r\n]+)`"?\s*$"
    ).Groups[1].Value.Trim()

    return @{
        raw = $raw
        body = $body
        trigger = $trigger
        source_rule = $sourceRule
    }
}

if (-not (Test-Path $sourceRoot)) {
    throw "Source rules path not found: $sourceRoot"
}
if (-not (Test-Path $antigravityBridgeRoot)) {
    throw "Antigravity bridge rules path not found: $antigravityBridgeRoot"
}
if (-not (Test-Path $claudeBridgeRoot)) {
    throw "Claude bridge rules path not found: $claudeBridgeRoot"
}
if (-not (Test-Path $cursorBridgeRoot)) {
    throw "Cursor bridge rules path not found: $cursorBridgeRoot"
}

$errors = New-Object System.Collections.Generic.List[string]

$sourceRules = Get-ChildItem $sourceRoot -File -Filter "*.md" |
    Where-Object { $_.Name -ne "AUTHORING_POLICY.md" } |
    Sort-Object Name
$antigravityBridgeRules = Get-ChildItem $antigravityBridgeRoot -File -Filter "*.md" |
    Sort-Object Name
$claudeBridgeRules = Get-ChildItem $claudeBridgeRoot -File -Filter "*.md" |
    Sort-Object Name
$cursorBridgeRules = Get-ChildItem $cursorBridgeRoot -File -Filter "*.md" |
    Sort-Object Name

$sourceNames = @($sourceRules.Name)
$antigravityBridgeNames = @($antigravityBridgeRules.Name)
$claudeBridgeNames = @($claudeBridgeRules.Name)
$cursorBridgeNames = @($cursorBridgeRules.Name)

foreach ($source in $sourceRules) {
    $name = $source.Name
    $sourceRuleMd = $source.FullName
    $antigravityBridgeRuleMd = Join-Path $antigravityBridgeRoot $name
    $claudeBridgeRuleMd = Join-Path $claudeBridgeRoot $name
    $cursorBridgeRuleMd = Join-Path $cursorBridgeRoot $name

    if (-not (Test-Path $antigravityBridgeRuleMd)) {
        $errors.Add("Missing Antigravity bridge rule for '$name'.")
    }
    if (-not (Test-Path $claudeBridgeRuleMd)) {
        $errors.Add("Missing Claude bridge rule for '$name'.")
    }
    if (-not (Test-Path $cursorBridgeRuleMd)) {
        $errors.Add("Missing Cursor bridge rule for '$name'.")
    }
    if ((-not (Test-Path $antigravityBridgeRuleMd)) -or (-not (Test-Path $claudeBridgeRuleMd)) -or (-not (Test-Path $cursorBridgeRuleMd))) {
        continue
    }

    $sourceData = Get-RuleData -RulePath $sourceRuleMd
    $antigravityData = Get-RuleData -RulePath $antigravityBridgeRuleMd
    $claudeData = Get-RuleData -RulePath $claudeBridgeRuleMd
    $cursorData = Get-RuleData -RulePath $cursorBridgeRuleMd

    $expectedSourceRule = "../../../.agents/rules/$name"

    if ($antigravityData.trigger -ne $sourceData.trigger) {
        $errors.Add("Antigravity bridge trigger mismatch for '$name'.")
    }
    if ($antigravityData.source_rule -ne $expectedSourceRule) {
        $errors.Add(
            "Antigravity bridge source_rule mismatch for '$name': '$($antigravityData.source_rule)'"
        )
    }
    if ($antigravityData.body -notmatch [regex]::Escape($expectedSourceRule)) {
        $errors.Add("Antigravity bridge reference mismatch for '$name'.")
    }

    if ($claudeData.trigger -ne $sourceData.trigger) {
        $errors.Add("Claude bridge trigger mismatch for '$name'.")
    }
    if ($claudeData.body -notmatch [regex]::Escape($expectedSourceRule)) {
        $errors.Add("Claude bridge reference mismatch for '$name'.")
    }

    if ($cursorData.trigger -ne $sourceData.trigger) {
        $errors.Add("Cursor bridge trigger mismatch for '$name'.")
    }
    if ($cursorData.body -notmatch [regex]::Escape($expectedSourceRule)) {
        $errors.Add("Cursor bridge reference mismatch for '$name'.")
    }
}

foreach ($bridgeName in $antigravityBridgeNames) {
    if ($sourceNames -notcontains $bridgeName) {
        $errors.Add("Antigravity bridge rule exists without source rule: '$bridgeName'.")
    }
}

foreach ($bridgeName in $claudeBridgeNames) {
    if ($sourceNames -notcontains $bridgeName) {
        $errors.Add("Claude bridge rule exists without source rule: '$bridgeName'.")
    }
}

foreach ($bridgeName in $cursorBridgeNames) {
    if ($sourceNames -notcontains $bridgeName) {
        $errors.Add("Cursor bridge rule exists without source rule: '$bridgeName'.")
    }
}

foreach ($sourceName in $sourceNames) {
    if ($antigravityBridgeNames -notcontains $sourceName) {
        $errors.Add("Source rule missing in Antigravity bridge tree: '$sourceName'.")
    }
    if ($claudeBridgeNames -notcontains $sourceName) {
        $errors.Add("Source rule missing in Claude bridge tree: '$sourceName'.")
    }
    if ($cursorBridgeNames -notcontains $sourceName) {
        $errors.Add("Source rule missing in Cursor bridge tree: '$sourceName'.")
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Rule bridge check failed with $($errors.Count) issue(s):" -ForegroundColor Red
    foreach ($errorItem in $errors) {
        Write-Host "- $errorItem"
    }
    exit 1
}

Write-Host "Rule bridge check passed:"
Write-Host "- source rules: $($sourceRules.Count)"
Write-Host "- antigravity bridges: $($antigravityBridgeRules.Count)"
Write-Host "- claude bridges: $($claudeBridgeRules.Count)"
Write-Host "- cursor bridges: $($cursorBridgeRules.Count)"
exit 0
