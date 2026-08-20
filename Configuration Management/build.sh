#!/usr/bin/env bash
# Локальная/CI-сборка. Поддерживает Windows (WPF, net10.0-windows) и Linux (Avalonia, net10.0).
# Целевой TFM выбирается автоматически по ОС (см. Configuration Management.csproj).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/Configuration Management.csproj"
CONFIG="${1:-Release}"
PUBLISH="${2:-}"

# Определяем ОС
case "$(uname -s)" in
  Linux*)             OS="linux" ;;
  Darwin*)            OS="macos" ;;
  MINGW*|MSYS*|CYGWIN*) OS="windows" ;;
  *)                  OS="linux" ;;
esac

RID=""
if [[ "$OS" == "linux" ]]; then   RID="linux-x64"; fi
if [[ "$OS" == "windows" ]]; then RID="win-x64"; fi

echo "==> OS: $OS, RID: ${RID:-<none>}"

echo "==> Restore"
dotnet restore "$PROJECT"

echo "==> Build ($CONFIG)"
# RuntimeIdentifier задаём пустым для обычного build (self-contained только при publish)
dotnet build "$PROJECT" -c "$CONFIG" --no-restore \
  -p:RuntimeIdentifier= \
  -p:SelfContained=false \
  -p:PublishSingleFile=false

if [[ "${PUBLISH}" == "publish" ]]; then
  if [[ -z "$RID" ]]; then
    echo "==> Публикация single-file для $OS не поддерживается; пропуск."
    exit 0
  fi
  OUT="$ROOT/publish/$RID"
  echo "==> Publish $RID -> $OUT"
  rm -rf "$OUT"
  dotnet publish "$PROJECT" -c "$CONFIG" -r "$RID" --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o "$OUT"
  echo "==> Done: $OUT"
fi
