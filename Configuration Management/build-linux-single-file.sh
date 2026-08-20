#!/usr/bin/env bash
# ==============================================================================
# Сборка ОДНОГО исполняемого файла для Linux (Avalonia, net10.0, linux-x64).
#
# Результат: self-contained single-file исполняемый файл
#   dist/linux-x64/ConfigurationManagement
# (без папок, без .dll рядом, только один исполняемый файл).
#
# Требования:
#   * запускать НА Linux (TFM net10.0 выбирается по ОС в csproj);
#   * установлен .NET SDK 10 (>= 10.0.400);
#   * установлены зависимости Avalonia для Linux:
#       sudo apt install -y libice6 libsm6 libfontconfig1 libfreetype6 libx11-6 \
#           libx11-dev libxext6 libxrender1 libglib2.0-0 libgtk-3-0
#
# Использование:
#   ./build-linux-single-file.sh            # сборка Release, RID linux-x64
#   ./build-linux-single-file.sh Debug      # другой конфиг
#   RID=linux-arm64 ./build-linux-single-file.sh   # другой RID
#
# ВНИМАНИЕ: скрипт НЕ запускает dotnet publish, если установлена переменная
# SKIP_PUBLISH=1 (полезно для проверки синтаксиса): SKIP_PUBLISH=1 ./script.sh
# ==============================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/Configuration Management.csproj"
CONFIG="${1:-Release}"
RID="${RID:-linux-x64}"
DIST="$ROOT/dist/$RID"

# Проверяем, что сборка идёт на Linux (в csproj Linux TFM выбирается по ОС).
if [[ "$(uname -s)" != "Linux" ]]; then
  echo "!! Скрипт предназначен для запуска НА Linux (TFM net10.0 задаётся по ОС сборки)."
  echo "   На текущей ОС ('$(uname -s)') кросс-компиляция даст net10.0-windows/WPF и не запустится на Linux."
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "!! .NET SDK не найден. Установите .NET SDK 10."
  exit 1
fi

echo "==> Конфигурация: $CONFIG"
echo "==> RID:          $RID"
echo "==> Цель:         $DIST"

echo "==> Restore"
dotnet restore "$PROJECT"

if [[ "${SKIP_PUBLISH:-0}" == "1" ]]; then
  echo "==> SKIP_PUBLISH=1 — публикация пропущена."
  exit 0
fi

echo "==> Publish (self-contained single-file)"
rm -rf "$DIST"
dotnet publish "$PROJECT" \
  -c "$CONFIG" \
  -r "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:PublishReadyToRun=false \
  -o "$DIST"

# Оставляем ТОЛЬКО исполняемый файл: удаляем .pdb и любые файлы, кроме бинарника.
find "$DIST" -type f \( -name "*.pdb" -o -name "*.json" -o -name "*.xml" \) -delete 2>/dev/null || true
for f in "$DIST"/*; do
  if [[ -f "$f" && "$(basename "$f")" != "ConfigurationManagement" ]]; then
    echo "    (удаляем лишний файл: $(basename "$f"))"
    rm -f "$f"
  fi
done

echo "==> Собран один исполняемый файл:"
ls -lh "$DIST/ConfigurationManagement"
chmod +x "$DIST/ConfigurationManagement"
echo "==> Готово. Запуск: $DIST/ConfigurationManagement"