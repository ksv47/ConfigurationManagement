# Localization audit: compares ru.json<->en.json key sets and checks that
# every key used in code/XAML exists in ru.json.
# Writes report to tools/l10n-audit-report.txt.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$langDir = Join-Path $root 'Configuration Management\Localization\Languages'
$srcDir  = Join-Path $root 'Configuration Management'
$reportPath = Join-Path $PSScriptRoot 'l10n-audit-report.txt'

function Get-Keys([string]$jsonPath) {
    $obj = Get-Content -Raw -Encoding UTF8 $jsonPath | ConvertFrom-Json
    $keys = @{}
    foreach ($p in $obj.strings.PSObject.Properties) { $keys[$p.Name] = $true }
    return $keys
}

$ru = Get-Keys (Join-Path $langDir 'ru.json')
$en = Get-Keys (Join-Path $langDir 'en.json')

$inRuNotEn = @($ru.Keys | Where-Object { -not $en.ContainsKey($_) } | Sort-Object)
$inEnNotRu = @($en.Keys | Where-Object { -not $ru.ContainsKey($_) } | Sort-Object)

# Collect keys used in code / XAML:
#   LocalizationManager.T("KEY")        (incl. string.Format(LocalizationManager.T("KEY"), ...))
#   {loc:Loc KEY}
#   Loc["KEY"] / {Binding Loc[KEY]}
$used = @{}
Get-ChildItem -Path $srcDir -Recurse -File -Include *.cs,*.xaml,*.axaml |
    Where-Object { $_.FullName -notmatch '\\Languages\\' } |
    ForEach-Object {
        $text = Get-Content -Raw -Encoding UTF8 $_.FullName
        $noLine = [regex]::Replace($text, '//[^\r\n]*', '')
        $noBlock = [regex]::Replace($noLine, '(?s)/\*.*?\*/', '')

        $patterns = @(
            'LocalizationManager\.T\(\s*"([^"]+)"\s*\)',
            '\{loc:Loc\s+([^}]+)\}',
            'Loc\[\s*"([^"]+)"\s*\]',
            'Binding\s+Loc\[\s*([^\]]+)\s*\]'
        )
        foreach ($p in $patterns) {
            foreach ($m in [regex]::Matches($noBlock, $p)) {
                $key = $m.Groups[1].Value.Trim()
                if ($key -match '^[A-Za-z][A-Za-z0-9_.-]*$') { $used[$key] = $true }
            }
        }
    }

$usedInCode = @($used.Keys | Sort-Object)
$missingFromRu = @($usedInCode | Where-Object { -not $ru.ContainsKey($_) } | Sort-Object)

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('=== Localization audit report ===')
[void]$sb.AppendLine("Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$sb.AppendLine("Keys in ru.json: $($ru.Count); in en.json: $($en.Count)")
[void]$sb.AppendLine()
[void]$sb.AppendLine('--- (A) Keys present in ru.json but missing in en.json ---')
if ($inRuNotEn.Count -eq 0) { [void]$sb.AppendLine('  <none>') }
else { foreach ($k in $inRuNotEn) { [void]$sb.AppendLine("  $k") } }
[void]$sb.AppendLine()
[void]$sb.AppendLine('--- (B) Keys present in en.json but missing in ru.json (dead) ---')
if ($inEnNotRu.Count -eq 0) { [void]$sb.AppendLine('  <none>') }
else { foreach ($k in $inEnNotRu) { [void]$sb.AppendLine("  $k") } }
[void]$sb.AppendLine()
[void]$sb.AppendLine('--- (C) Keys used in code/XAML but missing in ru.json ---')
if ($missingFromRu.Count -eq 0) { [void]$sb.AppendLine('  <none>') }
else { foreach ($k in $missingFromRu) { [void]$sb.AppendLine("  $k") } }
[void]$sb.AppendLine()
[void]$sb.AppendLine("Total unique keys used in code: $($usedInCode.Count)")

Set-Content -Path $reportPath -Value $sb.ToString() -Encoding UTF8
Write-Host "Report written: $reportPath"
Write-Host "A) inRuNotEn=$($inRuNotEn.Count)  B) inEnNotRu=$($inEnNotRu.Count)  C) missingFromRu=$($missingFromRu.Count)"