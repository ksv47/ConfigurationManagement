# Заполняет описания (body) релизов GitHub на основе CHANGELOG.md.
#
# Правила:
#   * Точное совпадение тега с версией в CHANGELOG -> текст этого раздела.
#   * Иначе микро-версия (напр. 0.2.5.92) -> сводный раздел основной версии (0.2.5)
#     по принципу отбрасывания последнего числового компонента.
#   * Служебные/старые релизы (new, new-releases, 1.x, 2.x) пропускаются.
#   * Обновляются только релизы с ПУСТЫМ описанием; при -Overwrite —
#     перезаписывает и уже заполненные (например, для исправления кодировки).
#
# Без -DryRun выполняет реальный PATCH каждого релиза через сохранённый
# OAuth-токен из git credential manager (пользователь, у которого есть права).
param(
    [switch]$DryRun,
    [switch]$Overwrite
)

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repo = 'sivatorov/ConfigurationManagement'

# ---------- 1. Парсим CHANGELOG.md ----------
$changelogPath = Join-Path $PSScriptRoot '..\CHANGELOG.md'
# UTF8 без BOM: без явной кодировки PS 5.1 читает как ANSI и ломает кириллицу.
$lines = Get-Content -Path $changelogPath -Encoding UTF8
$versionMap = @{}
$current = $null
$currentBody = New-Object System.Text.StringBuilder

function Flush-Current {
    if ($null -ne $current) {
        if (-not $versionMap.ContainsKey($current)) {
            $versionMap[$current] = $currentBody.ToString().Trim()
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

# ---------- 2. Список релизов ----------
$readHeaders = @{ 'User-Agent' = 'cm-tool'; 'Accept' = 'application/vnd.github+json' }
$all = @()
$page = 1
do {
    $url = "https://api.github.com/repos/$repo/releases?per_page=100&page=$page"
    $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 20 -Headers $readHeaders
    $items = $r.Content | ConvertFrom-Json
    if ($items.Count -eq 0) { break }
    $all += $items
    $page++
} while ($items.Count -eq 100)

# ---------- 3. Вспомогательные функции ----------
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
    # точное совпадение
    if ($versionMap.ContainsKey($ver)) {
        $b = $versionMap[$ver]
        if ($b) { return @{ src = $ver; body = $b } }
    }
    # отбрасываем последний числовой компонент до совпадения
    $parts = $ver -split '\.'
    while ($parts.Count -gt 1) {
        $parts = $parts[0..($parts.Count - 2)]
        $cand = $parts -join '.'
        if ($versionMap.ContainsKey($cand)) {
            $b = $versionMap[$cand]
            if ($b) { return @{ src = $cand; body = $b } }
        }
    }
    return $null
}

# ---------- 4. План обновления ----------
$toUpdate = @()
foreach ($rel in $all) {
    if (Is-SkipTag $rel.tag_name) { continue }
    $hasBody = $rel.body -and $rel.body.Length -gt 0
    if ($hasBody -and -not $Overwrite) { continue }   # без -Overwrite — только пустые

    $ver = Normalize-Version $rel.tag_name
    if ($null -eq $ver) { continue }

    $res = Resolve-Body $ver
    if ($null -eq $res) { continue }

    $toUpdate += [PSCustomObject]@{
        id       = $rel.id
        tag      = $rel.tag_name
        version  = $ver
        src      = $res.src
        body_len = $res.body.Length
    }
}

Write-Host "Релизов к обновлению: $($toUpdate.Count)"
$toUpdate | Sort-Object tag | Format-Table -AutoSize | Out-String -Width 200

if ($DryRun) {
    Write-Host 'DRY-RUN: изменения не применялись.'
    Write-Host '---DONE---'
    exit 0
}

# ---------- 5. Реальный PATCH ----------
# Получаем токен из git credential manager (не хардкодим).
$inputLines = @('protocol=https', 'host=github.com', '')
$cred = $inputLines | git credential fill 2>&1
$token = ($cred | Select-String '^password=') -replace '^password=', ''
if (-not $token) {
    Write-Host 'ERROR: не удалось получить токен из git credential manager.' -ForegroundColor Red
    Write-Host '---DONE---'
    exit 1
}

$authHeaders = @{
    'User-Agent'    = 'cm-tool'
    'Accept'        = 'application/vnd.github+json'
    'Authorization' = "Bearer $token"
}

$ok = 0
$fail = 0
foreach ($item in ($toUpdate | Sort-Object tag)) {
    $srcBody = $versionMap[$item.src]
    $json = @{ body = $srcBody } | ConvertTo-Json
    # Передаём тело как байты UTF-8, чтобы исключить неверную кодировку на стороне Invoke-RestMethod.
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    try {
        Invoke-RestMethod -Method Patch -Uri "https://api.github.com/repos/$repo/releases/$($item.id)" `
            -Headers $authHeaders -ContentType 'application/json; charset=utf-8' -Body $bodyBytes -TimeoutSec 30 | Out-Null
        Write-Host "[OK]   $($item.tag) <- $($item.src) ($($srcBody.Length) симв.)"
        $ok++
    }
    catch {
        Write-Host "[FAIL] $($item.tag): $($_.Exception.Message)" -ForegroundColor Red
        $fail++
    }
}

Write-Host "Обновлено: $ok, ошибок: $fail"
Write-Host '---DONE---'