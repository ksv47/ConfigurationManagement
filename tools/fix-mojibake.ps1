# Fixes mojibake (garbled) text in XML comments of .xaml files.
#
# Root cause: a file was originally UTF-8, then mistakenly re-read/saved as
# CP1251. Every Cyrillic char (2 UTF-8 bytes) became a pair of broken chars,
# e.g. "Цветовая" -> "Р¦РІРµС‚РѕРІР°СЏ".
#
# Fix: take the comment text, encode it back to CP1251 (restores original
# UTF-8 bytes), then decode as UTF-8.
#
# Safety: only lines that contain an XML comment WITH characteristic broken
# chars (which never occur in normal Russian prose) are processed. Already
# correct comments are left untouched.

param(
    [string]$Root = "Configuration Management"
)

$ErrorActionPreference = 'Stop'

$cp1251 = [System.Text.Encoding]::GetEncoding(1251)
$utf8   = [System.Text.Encoding]::UTF8

# Characteristic "broken" chars from CP1251 ranges 0x80-0xAF / 0xB0-0xBF
# that do not occur in normal Russian prose.
$telltaleCodes = @(
    0x00B5, 0x00B0, 0x00B1, 0x040E, 0x0402, 0x0403, 0x201A, 0x0405,
    0x0456, 0x0406, 0x0453, 0x201E, 0x2020, 0x2021, 0x20AC, 0x2030,
    0x0409, 0x040C, 0x040B, 0x040F, 0x2039, 0x0452, 0x0455, 0x0458,
    0x0457, 0x0459, 0x045A, 0x045C, 0x045B, 0x045F, 0x0491, 0x0454,
    0x0407, 0x0404, 0x2022, 0x2122, 0x203A, 0x00B6, 0x00B7, 0x00AF,
    0x00AC, 0x00AE, 0x00A4, 0x00A7, 0x00A6, 0x00A9, 0x0462, 0x0408
)
$telltaleChars = ($telltaleCodes | ForEach-Object { [char]$_ })

function Convert-CommentLine([string]$line) {
    $i = $line.IndexOf('<!--')
    if ($i -lt 0) { return $null }
    $j = $line.IndexOf('-->', $i + 4)
    if ($j -lt 0) { return $null }

    $inner = $line.Substring($i + 4, $j - ($i + 4))

    # Skip if there are no broken chars (not mojibake)
    if ($inner.IndexOfAny($telltaleChars) -lt 0) { return $null }

    # Re-decode: string -> CP1251 bytes -> UTF-8 string
    $bytes = $cp1251.GetBytes($inner)
    $fixed = $utf8.GetString($bytes)

    return $line.Substring(0, $i + 4) + $fixed + $line.Substring($j)
}

$totalFixedFiles = 0
$totalFixedLines = 0

Get-ChildItem -Path $Root -Recurse -Filter *.xaml | ForEach-Object {
    $path = $_.FullName
    $lines = Get-Content -LiteralPath $path
    $changed = $false
    $fixedCount = 0

    $newLines = foreach ($line in $lines) {
        if ($line.IndexOf('<!--') -ge 0) {
            $c = Convert-CommentLine $line
            if ($c -ne $null -and $c -ne $line) {
                $changed = $true
                $fixedCount++
                $c
            } else {
                $line
            }
        } else {
            $line
        }
    }

    if ($changed) {
        # Preserve the previous BOM/encoding of the file
        $raw = [System.IO.File]::ReadAllBytes($path)
        $hasBom = $raw.Length -ge 3 -and $raw[0] -eq 0xEF -and $raw[1] -eq 0xBB -and $raw[2] -eq 0xBF
        $enc = New-Object System.Text.UTF8Encoding($hasBom)
        [System.IO.File]::WriteAllLines($path, $newLines, $enc)
        $totalFixedFiles++
        $totalFixedLines += $fixedCount
        Write-Output ("FIXED ({0} comments): {1}" -f $fixedCount, $path)
    }
}

Write-Output ""
Write-Output ("TOTAL: files={0}, comments={1}" -f $totalFixedFiles, $totalFixedLines)