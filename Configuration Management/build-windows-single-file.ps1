<#
.SYNOPSIS
  Сборка ОДНОГО исполняемого файла для Windows (WPF, net10.0-windows, win-x64).

.DESCRIPTION
  Результат: self-contained single-file исполняемый файл
    dist/win-x64/ConfigurationManagement.exe
  (без папок, без .dll и .pdb рядом — только один исполняемый файл).

  Требуется: Windows + .NET SDK 10 (>= 10.0.400).

.USAGE
  .\build-windows-single-file.ps1                # Release, RID win-x64
  .\build-windows-single-file.ps1 -Configuration Debug   # другой конфиг
  .\build-windows-single-file.ps1 -RID win-arm64         # другой RID

  SKIP_PUBLISH=1 (переменная окружения) пропускает dotnet publish
  (удобно для быстрой проверки синтаксиса).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RID = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$Root       = $PSScriptRoot
$Project    = Join-Path $Root 'Configuration Management.csproj'
$Dist       = Join-Path $Root "dist\$RID"

# На Windows выбирается TFM net10.0-windows (WPF), поэтому проверяем ОС.
if ($env:OS -ne 'Windows_NT') {
    Write-Host "!! Скрипт предназначен для запуска НА Windows (TFM net10.0-windows/WPF задаётся по ОС)." -ForegroundColor Yellow
    Write-Host "   На другой ОС кросс-компиляция даст Linux/Avalonia и не запустится на Windows." -ForegroundColor Yellow
    exit 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "!! .NET SDK не найден. Установите .NET SDK 10." -ForegroundColor Red
    exit 1
}

Write-Host "==> Конфигурация: $Configuration" -ForegroundColor Cyan
Write-Host "==> RID:          $RID" -ForegroundColor Cyan
Write-Host "==> Цель:         $Dist" -ForegroundColor Cyan

Write-Host "==> Restore" -ForegroundColor Cyan
dotnet restore $Project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($env:SKIP_PUBLISH -eq '1') {
    Write-Host "==> SKIP_PUBLISH=1 — публикация пропущена." -ForegroundColor Yellow
    exit 0
}

Write-Host "==> Publish (self-contained single-file)" -ForegroundColor Cyan
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
dotnet publish $Project `
    -c $Configuration `
    -r $RID `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=false `
    -o $Dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Оставляем ТОЛЬКО исполняемый файл ConfigurationManagement.exe:
# удаляем .pdb, .json, .xml и любые другие файлы и папки в выходной папке.
# Встроенные языки (ru, en) уже в ресурсах сборки, поэтому внешняя папка
# Localization/Languages здесь не нужна (см. LocalizationManager.cs).
Get-ChildItem $Dist -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    if ($_.FullName -eq (Join-Path $Dist 'ConfigurationManagement.exe')) { return }
    if ($_.PSIsContainer) {
        Write-Host "    (удаляем лишнюю папку: $($_.FullName.Substring($Dist.Length)))" -ForegroundColor DarkGray
        Remove-Item $_.FullName -Recurse -Force
    } else {
        Write-Host "    (удаляем лишний файл: $($_.FullName.Substring($Dist.Length)))" -ForegroundColor DarkGray
        Remove-Item $_.FullName -Force
    }
}

$exe = Join-Path $Dist 'ConfigurationManagement.exe'
if (-not (Test-Path $exe)) {
    Write-Host "!! Исполняемый файл не найден: $exe" -ForegroundColor Red
    exit 1
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "==> Собран один исполняемый файл:" -ForegroundColor Green
Write-Host "    $exe ($size MB)" -ForegroundColor Green
Write-Host "==> Готово. Запуск: $exe" -ForegroundColor Green