#!/usr/bin/env bash
# =============================================================================
# package/linux/deb/build-deb.sh — упаковка Linux-версии в .deb (dpkg-deb).
#
# Требования:
#   - Собранный single-file бинарь linux-x64:
#       (cd "Configuration Management" && ./build.sh Release publish)
#   - dpkg-deb (обычно предустановлен в Debian/Ubuntu).
#
# Результат: package/linux/deb/out/configuration-management_<версия>_amd64.deb
# =============================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
PROJECT_DIR="$ROOT/Configuration Management"
PUBLISH_DIR="$PROJECT_DIR/publish/linux-x64"
BINARY="$PUBLISH_DIR/ConfigurationManagement"

APP_ID="configuration-management"
BINARY_NAME="ConfigurationManagement"
ARCH="amd64"

# Версия берётся из DEBIAN/control (поле Version), чтобы не дублировать.
CONTROL_SRC="$ROOT/package/linux/deb/DEBIAN/control"
VERSION="$(sed -n 's/^Version:[[:space:]]*//p' "$CONTROL_SRC" | head -n1)"
PKG="$(sed -n 's/^Package:[[:space:]]*//p' "$CONTROL_SRC" | head -n1)"

STAGING="$ROOT/package/linux/deb/staging"
OUTDIR="$ROOT/package/linux/deb/out"
OUT_FILE="${PKG}_${VERSION}_${ARCH}.deb"

# --- 1. Проверка бинаря -------------------------------------------------------
if [[ ! -x "$BINARY" ]]; then
  echo "Ошибка: не найден собранный single-file бинарь: $BINARY"
  echo "Сначала выполните сборку:"
  echo "  cd \"$PROJECT_DIR\" && ./build.sh Release publish"
  exit 1
fi
echo "==> Бинарь: $BINARY"
echo "==> Пакет: $PKG, версия $VERSION, архитектура $ARCH"

# --- 2. Стадия упаковки (staging) ---------------------------------------------
rm -rf "$STAGING" "$OUTDIR"
mkdir -p "$STAGING/DEBIAN"
mkdir -p "$STAGING/usr/bin"
mkdir -p "$STAGING/usr/share/applications"
mkdir -p "$STAGING/usr/share/icons/hicolor/256x256/apps"
mkdir -p "$OUTDIR"

install -m 644 "$CONTROL_SRC" "$STAGING/DEBIAN/control"
install -m 755 "$BINARY" "$STAGING/usr/bin/$BINARY_NAME"
install -m 644 "$ROOT/package/linux/$APP_ID.desktop" "$STAGING/usr/share/applications/$APP_ID.desktop"
install -m 644 "$ROOT/package/linux/app.png" "$STAGING/usr/share/icons/hicolor/256x256/apps/$APP_ID.png"

# --- 3. Сборка .deb ------------------------------------------------------------
echo "==> Сборка .deb (dpkg-deb) ..."
dpkg-deb --build --root-owner-group "$STAGING" "$OUTDIR/$OUT_FILE"

echo "==> Готово: $OUTDIR/$OUT_FILE"
echo
echo "Установка:  sudo dpkg -i $OUTDIR/$OUT_FILE"
echo "Удаление:   sudo apt remove $PKG"