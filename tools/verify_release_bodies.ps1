# Программная проверка: загруженные описания релизов должны совпадать с
# эталоном из CHANGELOG.md (построение и сравнение строк в памяти, без
# зависимости от кодировки консоли).
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = 'sivatorov/ConfigurationManagement'

# --- 1. Парсим CHANGELOG как UTF-8 ---
$changelogPath = Join-Path $PSScriptRoot '..\CHANGELOG.md'
$lines = Get-Content -Path $changelogPath -Encoding UTF8
$versionMap = @{}
$current = $null
$currentBody = New-Object System.Text.StringBuilder
function Flush-Current {
    if ($null -ne $current) {
        if (-not $versionMap.ContainsKey($current)) { $versionMap[$current] = $currentBody.ToString().Trim() }
        $current = $null
        [void]$currentBody.Clear()
    }
}
foreach ($line in $lines) {
    if ($line -match '^## \[([^\]]+)\]') { Flush-Current; $current = $Matches[1].Trim() }
    elseif ($line -match '^# ') { Flush-Current }
    else { if ($null -ne $current) { [void]$currentBody.AppendLine($line) } }
}
Flush-Current

# --- 2. Функции сопоставления (как в update_release_bodies.ps1) ---
function Normalize-Version([string]$tag) {
    $m = [regex]::Match($tag, '\d')
    if (-not $m.Success) { return $null }
    $sub = $tag.Substring($m.Index)
    $sub = ($sub -split '\s')[0]
    $sub = $sub -replace '[+:].*$',''
    $sub = $sub.Trim()
    if ($sub -eq '') { return $null } else { return $sub }
}
function Is-SkipTag([string]$tag) {
    if ($tag -in @('new', 'new-releases')) { return $true }
    $ver = Normalize-Version $tag
    if ($null -eq $ver) { return $true }
    if ($ver -match '^[12]\.') { return $true }
    return $false
}
function Resolve-Body([string]$ver) {
    if ($versionMap.ContainsKey($ver)) { $b = $versionMap[$ver]; if ($b) { return $b } }
    $parts = $ver -split '\.'
    while ($parts.Count -gt 1) {
        $parts = $parts[0..($parts.Count - 2)]
        $cand = $parts -join '.'
        if ($versionMap.ContainsKey($cand)) { $b = $versionMap[$cand]; if ($b) { return $b } }
    }
    return $null
}

# --- 3. Получаем релизы и сверяем ---
$headers = @{ 'User-Agent' = 'cm-tool'; 'Accept' = 'application/vnd.github+json' }
$all = @()
$page = 1
do {
    $r = Invoke-WebRequest -Uri "https://api.github.com/repos/$repo/releases?per_page=100&page=$page" `
        -UseBasicParsing -TimeoutSec 20 -Headers $headers
    $items = $r.Content | ConvertFrom-Json
    if ($items.Count -eq 0) { break }
    $all += $items
    $page++
} while ($items.Count -eq 100)

$match = 0
$mismatch = 0
$checked = 0
foreach ($rel in $all) {
    if (Is-SkipTag $rel.tag_name) { continue }
    $ver = Normalize-Version $rel.tag_name
    if ($null -eq $ver) { continue }
    $expected = Resolve-Body $ver
    if ($null -eq $expected) { continue }
    $checked++
    $actual = $rel.body
    if ($actual -and $actual.Trim() -eq $expected.Trim()) { $match++ }
    else { $mismatch++; Write-Host "MISMATCH: $($rel.tag_name)" }
}

Write-Host "Проверено: $checked, совпадений: $match, расхождений: $mismatch"
Write-Host '---DONE---'