# Временный скрипт для проверки сохранённых учётных данных GitHub
$input = @('protocol=https','host=github.com','')
$input | git credential fill 2>&1 | Select-String -Pattern 'username|password'
Write-Host '---DONE---'