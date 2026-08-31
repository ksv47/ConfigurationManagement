# Проверка api.github.com с принудительным TLS 1.2
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Write-Host ("TLS set to: " + [Net.ServicePointManager]::SecurityProtocol)
try {
    $r = Invoke-WebRequest -Uri 'https://api.github.com/repos/sivatorov/ConfigurationManagement/releases?per_page=3' -UseBasicParsing -TimeoutSec 20 -Headers @{ 'User-Agent' = 'cm-tool'; 'Accept' = 'application/vnd.github+json' }
    Write-Host ("STATUS: " + $r.StatusCode)
    $json = $r.Content | ConvertFrom-Json
    foreach ($rel in $json) { Write-Host ("TAG: " + $rel.tag_name) }
} catch { Write-Host ("ERROR: " + $_.Exception.Message) }
Write-Host '---DONE---'