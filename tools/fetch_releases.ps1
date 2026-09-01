# Сбор всех релизов репозитория с GitHub API (с пагинацией) в JSON-файл.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$headers = @{ 'User-Agent' = 'cm-tool'; 'Accept' = 'application/vnd.github+json' }
$all = @()
$page = 1
do {
    $url = "https://api.github.com/repos/sivatorov/ConfigurationManagement/releases?per_page=100&page=$page"
    $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 20 -Headers $headers
    $items = $r.Content | ConvertFrom-Json
    if ($items.Count -eq 0) { break }
    $all += $items
    $page++
} while ($items.Count -eq 100)

$all | Select-Object @{n='tag_name';e={$_.tag_name}}, @{n='name';e={$_.name}}, @{n='id';e={$_.id}}, @{n='draft';e={$_.draft}}, @{n='prerelease';e={$_.prerelease}}, @{n='body_len';e={if($_.body){$_.body.Length}else{0}}} |
    Sort-Object tag_name | Format-Table -AutoSize | Out-String -Width 200

Write-Host "TOTAL: $($all.Count)"
$all | ConvertTo-Json -Depth 4 | Set-Content -Path "tools\releases_dump.json" -Encoding UTF8
Write-Host '---DONE---'