<#
.SYNOPSIS
  Локальная сборка и публикация «Управление конфигурациями 1С».

.PARAMETER Configuration
  Debug или Release (по умолчанию Release).

.PARAMETER Publish
  Если указан — выполнить dotnet publish (self-contained win-x64).

.PARAMETER Output
  Каталог публикации (по умолчанию .\publish\win-x64).

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Publish
  .\build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Publish,
    [string]$Output = '.\publish\win-x64'
)

$ErrorActionPreference = 'Stop'
$Project = Join-Path $PSScriptRoot 'Configuration Management.csproj'

Write-Host "==> Restore" -ForegroundColor Cyan
dotnet restore $Project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Build ($Configuration)" -ForegroundColor Cyan
dotnet build $Project -c $Configuration --no-restore `
    -p:RuntimeIdentifier= `
    -p:SelfContained=false `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Publish) {
    Write-Host "==> Publish win-x64 -> $Output" -ForegroundColor Cyan
    if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
    dotnet publish $Project -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $Output
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $exe = Get-ChildItem $Output -Filter *.exe | Select-Object -First 1
    if ($exe) {
        Write-Host "==> Готово: $($exe.FullName) ($([math]::Round($exe.Length/1MB, 1)) MB)" -ForegroundColor Green
    } else {
        Write-Host "==> Публикация завершена: $Output" -ForegroundColor Green
    }
} else {
    Write-Host "==> Сборка завершена. Для публикации: .\build.ps1 -Publish" -ForegroundColor Green
}
