<#
.SYNOPSIS
  Сборка ОДНОГО исполняемого файла для Linux (Avalonia, net10.0, linux-x64)
  ПРЯМО ИЗ WINDOWS (кросс-компиляция, без WSL/Linux).

.DESCRIPTION
  Результат: self-contained single-file исполняемый файл
    dist/linux-x64/ConfigurationManagement
  (без папок, без .dll и .pdb рядом — только один исполняемый файл).

  Кросс-компиляция работает благодаря свойству -p:ForceLinux=true,
  которое в csproj принудительно включает Linux-ветку
  (net10.0 + Avalonia) даже при сборке на Windows (см. Configuration Management.csproj).

  ВНИМАНИЕ: бинарник собирается на Windows для запуска НА Linux.
  Проверить его работу на этой машине нельзя (нет Linux-рантайма/библиотек).

  Требуется: Windows + .NET SDK 10 (>= 10.0.400) с RID linux-x64.

.USAGE
  .\build-linux-single-file.ps1                     # Release, RID linux-x64
  .\build-linux-single-file.ps1 -Configuration Debug   # другой конфиг
  .\build-linux-single-file.ps1 -RID linux-arm64        # другой RID

  SKIP_PUBLISH=1 (переменная окружения) пропускает dotnet publish
  (удобно для быстрой проверки корректности конфигурации).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RID = 'linux-x64'
)

$ErrorActionPreference = 'Stop'

$Root       = $PSScriptRoot
$Project    = Join-Path $Root 'Configuration Management.csproj'
$Dist       = Join-Path $Root "dist\$RID"
$BinaryName = 'ConfigurationManagement'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "!! .NET SDK не найден. Установите .NET SDK 10." -ForegroundColor Red
    exit 1
}

Write-Host "==> Конфигурация: $Configuration" -ForegroundColor Cyan
Write-Host "==> RID:          $RID (кросс-сборка Linux из Windows)" -ForegroundColor Cyan
Write-Host "==> Цель:         $Dist" -ForegroundColor Cyan

# ForceLinux=true включает Linux-ветку csproj (net10.0 + Avalonia) на Windows.
Write-Host "==> Restore (ForceLinux=true)" -ForegroundColor Cyan
dotnet restore $Project -p:ForceLinux=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($env:SKIP_PUBLISH -eq '1') {
    Write-Host "==> SKIP_PUBLISH=1 — публикация пропущена." -ForegroundColor Yellow
    exit 0
}

Write-Host "==> Publish (self-contained single-file, linux)" -ForegroundColor Cyan
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
dotnet publish $Project `
    -c $Configuration `
    -r $RID `
    --self-contained true `
    -p:ForceLinux=true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=false `
    -o $Dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Оставляем ТОЛЬКО исполняемый файл ConfigurationManagement:
# удаляем .pdb, .json, .xml и любые другие файлы и папки в выходной папке.
# Встроенные языки (ru, en) уже в ресурсах сборки, поэтому внешняя папка
# Localization/Languages здесь не нужна (см. LocalizationManager.cs).
$keep = Join-Path $Dist $BinaryName
Get-ChildItem $Dist -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    if ($_.FullName -eq $keep) { return }
    if ($_.PSIsContainer) {
        Write-Host "    (удаляем лишнюю папку: $($_.FullName.Substring($Dist.Length)))" -ForegroundColor DarkGray
        Remove-Item $_.FullName -Recurse -Force
    } else {
        Write-Host "    (удаляем лишний файл: $($_.FullName.Substring($Dist.Length)))" -ForegroundColor DarkGray
        Remove-Item $_.FullName -Force
    }
}

if (-not (Test-Path $keep)) {
    Write-Host "!! Исполняемый файл не найден: $keep" -ForegroundColor Red
    exit 1
}

$size = [math]::Round((Get-Item $keep).Length / 1MB, 1)
Write-Host "==> Собран один исполняемый файл:" -ForegroundColor Green
Write-Host "    $keep ($size MB)" -ForegroundColor Green
Write-Host "==> Готово. Скопируйте на Linux и запустите: ./ConfigurationManagement" -ForegroundColor Green
Write-Host "    (предварительно chmod +x, если файловая система не сохранила бит исполнения)" -ForegroundColor DarkGray