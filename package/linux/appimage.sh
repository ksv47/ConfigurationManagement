#!/usr/bin/env bash
# =============================================================================
# package/linux/appimage.sh — упаковка Linux-версии приложения в AppImage.
#
# Требования:
#   - Собранный single-file бинарь linux-x64:
#       (cd "Configuration Management" && ./build.sh Release publish)
#   - appimagetool (или linuxdeploy) в PATH, либо переменная APPIMAGETOOL.
#       https://github.com/AppImage/appimagetool/releases
#
# Создаёт AppDir (usr/bin, usr/share/applications, usr/share/icons),
# AppRun и собирает .AppImage.
# =============================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PROJECT_DIR="$ROOT/Configuration Management"
PUBLISH_DIR="$PROJECT_DIR/publish/linux-x64"
BINARY="$PUBLISH_DIR/ConfigurationManagement"

APP_ID="configuration-management"
BINARY_NAME="ConfigurationManagement"

# appimagetool читает архитектуру из переменной окружения, иначе угадывает
# её по содержимому AppDir.
export ARCH="${ARCH:-x86_64}"

CSPROJ="$PROJECT_DIR/Configuration Management.csproj"
VERSION="$(sed -e 's/<!--.*-->//g' "$CSPROJ" \
  | sed -n 's/.*<InformationalVersion>\([^<]*\)<.*/\1/p' \
  | head -n1 | tr -d '[:space:]')"
if [[ -z "$VERSION" ]]; then
  echo "Ошибка: не удалось прочитать InformationalVersion из $CSPROJ"
  exit 1
fi

PACKAGE_DIR="$ROOT/package/linux"
APPDIR="$PACKAGE_DIR/AppDir"
OUTDIR="$PACKAGE_DIR/out"
OUT_FILE="ConfigurationManagement-$VERSION-$ARCH.AppImage"

# Инструменты упаковки. По умолчанию appimagetool из PATH; можно переопределить:
#   APPIMAGETOOL=/путь/к/appimagetool ./appimage.sh
APPIMAGETOOL="${APPIMAGETOOL:-appimagetool}"

# --- 1. Проверка собранного бинаря ------------------------------------------
if [[ ! -x "$BINARY" ]]; then
  echo "Ошибка: не найден собранный single-file бинарь: $BINARY"
  echo "Сначала выполните сборку:"
  echo "  cd \"$PROJECT_DIR\" && ./build.sh Release publish"
  exit 1
fi
echo "==> Бинарь: $BINARY"

# --- 2. Сборка AppDir --------------------------------------------------------
rm -rf "$APPDIR" "$OUTDIR"
mkdir -p "$APPDIR/usr/bin"
mkdir -p "$APPDIR/usr/share/applications"
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"
mkdir -p "$OUTDIR"

install -m 755 "$BINARY" "$APPDIR/usr/bin/$BINARY_NAME"
install -m 644 "$PACKAGE_DIR/$APP_ID.desktop" "$APPDIR/usr/share/applications/$APP_ID.desktop"
install -m 644 "$PACKAGE_DIR/app.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/$APP_ID.png"

# appimagetool ищет описание и значок в корне AppDir и без них отказывается
# собирать («Desktop file not found»). Значок дополнительно кладётся под
# именем .DirIcon: его показывает файловый менеджер.
install -m 644 "$PACKAGE_DIR/$APP_ID.desktop" "$APPDIR/$APP_ID.desktop"
install -m 644 "$PACKAGE_DIR/app.png" "$APPDIR/$APP_ID.png"
install -m 644 "$PACKAGE_DIR/app.png" "$APPDIR/.DirIcon"

# AppRun: точка входа AppImage (запуск через PATH внутри AppDir)
cat > "$APPDIR/AppRun" <<'EOF'
#!/usr/bin/env bash
SELF="$(dirname "$(readlink -f "$0")")"
export PATH="$SELF/usr/bin:$PATH"
exec "$SELF/usr/bin/ConfigurationManagement" "$@"
EOF
chmod +x "$APPDIR/AppRun"

echo "==> AppDir собран: $APPDIR"

# --- 3. Сборка AppImage ------------------------------------------------------
if ! command -v "$APPIMAGETOOL" >/dev/null 2>&1; then
  echo "Внимание: $APPIMAGETOOL не найден в PATH."
  echo "Скачайте appimagetool: https://github.com/AppImage/appimagetool/releases"
  echo "и либо добавьте в PATH, либо задайте APPIMAGETOOL=/путь/к/приложению."
  echo "AppDir готов, файл .AppImage не собран."
  exit 1
fi

echo "==> Сборка .AppImage ($APPIMAGETOOL) ..."
"$APPIMAGETOOL" --appimage-extract-and-run "$APPDIR" "$OUTDIR/$OUT_FILE"
echo "==> Готово: $OUTDIR/$OUT_FILE"

# --- (Опция) linuxdeploy ------------------------------------------------------
# Если предпочтителен linuxdeploy, можно собрать AppDir так:
#   linuxdeploy --appdir "$APPDIR" --desktop-file "$APPDIR/usr/share/applications/$APP_ID.desktop" \
#               --icon-file "$PACKAGE_DIR/app.png" --output appimage
# Подробнее: https://github.com/linuxdeploy/linuxdeploy