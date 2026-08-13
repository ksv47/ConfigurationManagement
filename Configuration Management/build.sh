#!/usr/bin/env bash
# Локальная/CI-сборка. WPF-проект собирается только на Windows-раннере.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$ROOT/Configuration Management.csproj"
CONFIG="${1:-Release}"
PUBLISH="${2:-}"

echo "==> Restore"
dotnet restore "$PROJECT"

echo "==> Build ($CONFIG)"
dotnet build "$PROJECT" -c "$CONFIG" --no-restore \
  -p:RuntimeIdentifier= \
  -p:SelfContained=false \
  -p:PublishSingleFile=false

if [[ "${PUBLISH}" == "publish" ]]; then
  OUT="$ROOT/publish/win-x64"
  echo "==> Publish win-x64 -> $OUT"
  rm -rf "$OUT"
  dotnet publish "$PROJECT" -c "$CONFIG" -r win-x64 --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o "$OUT"
  echo "==> Done: $OUT"
fi
