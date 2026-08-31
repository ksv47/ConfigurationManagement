# Анализ: сопоставление релизов GitHub с разделами CHANGELOG.md
# Ничего не изменяет — только строит отчёт.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo = 'sivatorov/ConfigurationManagement'

# ---- 1. Парсим CHANGELOG.md в карту version -> body ----
$changelogPath = Join-Path $PSScriptRoot '..\CHANGELOG.md'
$lines = Get-Content -Path $changelogPath
$versionMap = @{}          # version -> body text
$order = New-Object System.Collections.Generic.List[string]
$current = $null
$currentBody = New-Object System.Text.StringBuilder

function Flush-Current {
    if ($null -ne $current) {
        $text = $currentBody.ToString().Trim()
        if (-not $versionMap.ContainsKey($current)) {
            $versionMap[$current] = $text
            $order.Add($current)
        }
        $current = $null
        [void]$currentBody.Clear()
    }
}

foreach ($line in $lines) {
    if ($line -match '^## \[([^\]]+)\]') {
        Flush-Current
        $current = $Matches[1].Trim()
    }
    elseif ($line -match '^# ') {
        Flush-Current
    }
    else {
        if ($null -ne $current) { [void]$currentBody.AppendLine($line) }
    }
}
Flush-Current

Write-Host "CHANGELOG versions: $($versionMap.Count)"

# ---- 2. Получаем все релизы ----
$headers = @{ 'User-Agent' = 'cm-tool'; 'Accept' = 'application/vnd.github+json' }
$all = @()
$page = 1
do {
    $url = "https://api.github.com/repos/$repo/releases?per_page=100&page=$page"
    $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 20 -Headers $headers
    $items = $r.Content | ConvertFrom-Json
    if ($items.Count -eq 0) { break }
    $all += $items
    $page++
} while ($items.Count -eq 100)

# ---- 3. Сопоставляем ----
function Normalize-Version([string]$tag) {
    # new-0.3.5.88 -> 0.3.5.88 ; v0.3.5 -> 0.3.5 ; извлекаем подстроку с первой цифры
    $t = $tag
    $m = [regex]::Match($t, '\d')
    if (-not $m.Success) { return $null }
    $start = $m.Index
    $sub = $t.Substring($start)
    $sub = $sub -split '\s' | Select-Object -First 1
    $sub = $sub -replace '[+:].*$',''
    $sub = $sub.Trim()
    if ($sub -eq '') { return $null }
    return $sub
}

$report = foreach ($rel in $all) {
    $ver = Normalize-Version $rel.tag_name
    $hasBody = $rel.body -and $rel.body.Length -gt 0
    $match = if ($ver -and $versionMap.ContainsKey($ver)) { 'EXACT' } else { '' }
    [PSCustomObject]@{
        tag = $rel.tag_name
        id  = $rel.id
        ver = $ver
        has_body = $hasBody
        match = $match
    }
}

$report | Sort-Object ver | Format-Table -AutoSize | Out-String -Width 200

$exact = $report | Where-Object { $_.match -eq 'EXACT' }
$noexact = $report | Where-Object { $_.match -ne 'EXACT' }
Write-Host "EXACT matches: $($exact.Count)"
Write-Host "No exact match (or no body): $($noexact.Count)"
Write-Host '---DONE---'