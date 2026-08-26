# Splits the monolithic MainViewModel.cs (WPF) into partial files by feature area.
# Method bodies are preserved BYTE-FOR-BYTE: the script only slices the source at
# method boundaries and wraps each slice in a partial-class file.
# Cut points are chosen BETWEEN methods so no method is split across files.
# NOTE: keep this file ASCII-only (no Cyrillic) to avoid PowerShell codepage issues.
$ErrorActionPreference = 'Stop'

$root = Get-Location
$base = Join-Path $root 'Configuration Management\ViewModels'
$src = Join-Path $base 'MainViewModel.cs'
$bak = Join-Path $base 'MainViewModel.cs.bak'

if (-not (Test-Path $bak)) {
    if (-not (Test-Path $src)) { throw "Source not found: $src" }
    Copy-Item $src $bak -Force
    $source = $src
} else {
    # Idempotent: always read the pristine original from the backup
    $source = $bak
}

$lines = [System.IO.File]::ReadAllLines($source)

# using block (source lines 2..16) is inserted into every partial file
$usings = for ($n = 2; $n -le 16; $n++) { $lines[$n - 1] }

function New-Partial {
    param(
        [string]$fileName,
        [int]$start,
        [int]$end,
        [switch]$mainFile
    )
    $body = for ($n = $start; $n -le $end; $n++) { $lines[$n - 1] }

    $out = New-Object System.Collections.Generic.List[string]
    $out.Add('#if WINDOWS')
    foreach ($u in $usings) { $out.Add($u) }
    $out.Add('')
    $out.Add('namespace Configuration_Management.ViewModels;')
    $out.Add('')
    $out.Add('/// <summary>Main ViewModel (partial class split by feature blocks, see MainViewModel.*.cs).</summary>')
    $out.Add('public partial class MainViewModel : ViewModelBase')
    $out.Add('{')
    foreach ($b in $body) { $out.Add($b) }
    $out.Add('}')

    if ($mainFile) {
        # TagFilterItem type stays in the main file
        $out.Add('')
        for ($n = 5916; $n -le 5927; $n++) { $out.Add($lines[$n - 1]) }
    }

    $out.Add('#endif')

    $dest = Join-Path $base $fileName
    [System.IO.File]::WriteAllLines($dest, $out, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("OK  {0}  (body lines: {1}..{2}, total {3})" -f $fileName, $start, $end, $body.Count)
}

Write-Host 'Splitting MainViewModel.cs into partial files...'
New-Partial 'MainViewModel.cs'            25   1151 -mainFile   # fields, ctor, collections, platform versions, ibases settings
New-Partial 'MainViewModel.Sync.cs'      1152   1391            # ibases.v8i sync (timer, import/export)
New-Partial 'MainViewModel.Display.cs'   1392   2233            # columns, tag filters, session, status bar, window layout, command decls
New-Partial 'MainViewModel.Commands.cs'  2234   3162            # command impls: select, add, edit, delete, favorites, pin, hotkeys
New-Partial 'MainViewModel.Launch.cs'    3163   3666            # 1C launch, list save, filter, language
New-Partial 'MainViewModel.Theme.cs'     3667   4253            # themes, color schemes, fonts, collapsed groups, RebuildGroupTree
New-Partial 'MainViewModel.Tools.cs'     4255   5913            # import/export, cache, config, COM, dump, tags, group move, behavior
Write-Host 'Done.'