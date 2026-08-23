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

# Права каталогов в пакете не должны зависеть от umask сборщика: иначе
# на системе без каталога значков он создастся доступным на запись группе,
# а сама сборка перестанет быть воспроизводимой.
umask 022

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
PROJECT_DIR="$ROOT/Configuration Management"
PUBLISH_DIR="$PROJECT_DIR/publish/linux-x64"
BINARY="$PUBLISH_DIR/ConfigurationManagement"

APP_ID="configuration-management"
BINARY_NAME="ConfigurationManagement"
ARCH="amd64"

# Версия берётся только из csproj, запасного источника нет намеренно:
# в control стоит подстановка, и молчаливый откат к устаревшему числу
# (там была 0.3.1.1 при 0.3.3.39 в сборке) хуже честной остановки.
CONTROL_SRC="$ROOT/package/linux/deb/DEBIAN/control"
CSPROJ="$PROJECT_DIR/Configuration Management.csproj"

# Комментарии отбрасываются: в csproj над тегом лежит пояснение,
# где та же версия упомянута текстом, и оно попадало в подстановку.
VERSION="$(sed -e 's/<!--.*-->//g' "$CSPROJ" \
  | sed -n 's/.*<InformationalVersion>\([^<]*\)<.*/\1/p' \
  | head -n1 | tr -d '[:space:]')"

if [[ -z "$VERSION" ]]; then
  echo "Ошибка: не удалось прочитать InformationalVersion из $CSPROJ"
  exit 1
fi
if [[ ! "$VERSION" =~ ^[0-9][A-Za-z0-9.+~-]*$ ]]; then
  echo "Ошибка: версия «$VERSION» не годится для имени пакета Debian"
  exit 1
fi

PKG="$(sed -n 's/^Package:[[:space:]]*//p' "$CONTROL_SRC" | head -n1)"

STAGING="$ROOT/package/linux/deb/staging"
OUTDIR="$ROOT/package/linux/deb/out"
OUT_FILE="${PKG}_${VERSION}_${ARCH}.deb"

# --- 1. Проверка бинаря -------------------------------------------------------
if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo "Ошибка: dpkg-deb не найден. Установите пакет dpkg-dev."
  exit 1
fi

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

# control собирается из шаблона: подстановки заполняются вычисленными
# значениями. Замена идёт не через sed с переменной в правой части, чтобы
# символы вроде & и / в версии не меняли смысл выражения.
python3 - "$CONTROL_SRC" "$STAGING/DEBIAN/control" "$VERSION" <<'PY'
import sys
src, dst, version = sys.argv[1], sys.argv[2], sys.argv[3]
text = open(src, encoding='utf-8').read().replace('@VERSION@', version)
open(dst, 'w', encoding='utf-8').write(text)
PY
install -m 755 "$BINARY" "$STAGING/usr/bin/$BINARY_NAME"
install -m 644 "$ROOT/package/linux/$APP_ID.desktop" "$STAGING/usr/share/applications/$APP_ID.desktop"
install -m 644 "$ROOT/package/linux/app.png" "$STAGING/usr/share/icons/hicolor/256x256/apps/$APP_ID.png"

# Размер установки в килобайтах: только устанавливаемое дерево, без
# служебного каталога DEBIAN, как это считает dpkg-gencontrol.
INSTALLED_SIZE="$(du -sk --exclude=DEBIAN "$STAGING" | cut -f1)"
python3 - "$STAGING/DEBIAN/control" "$INSTALLED_SIZE" <<'PY'
import sys
path, size = sys.argv[1], sys.argv[2]
text = open(path, encoding='utf-8').read().replace('@INSTALLED_SIZE@', size)
open(path, 'w', encoding='utf-8').write(text)
PY

# Контрольные суммы: без них не работают dpkg --verify и debsums.
(cd "$STAGING" && find . -type f -not -path './DEBIAN/*' -printf '%P\0' \
  | xargs -0 md5sum > DEBIAN/md5sums)

# Файл о лицензии обязателен по политике Debian для любого пакета.
mkdir -p "$STAGING/usr/share/doc/$PKG"
install -m 644 "$ROOT/package/linux/deb/copyright" "$STAGING/usr/share/doc/$PKG/copyright"

# --- 3. Сборка .deb ------------------------------------------------------------
echo "==> Сборка .deb (dpkg-deb) ..."
dpkg-deb --build --root-owner-group "$STAGING" "$OUTDIR/$OUT_FILE"

echo "==> Готово: $OUTDIR/$OUT_FILE"
echo
echo "Установка:  sudo dpkg -i $OUTDIR/$OUT_FILE"
echo "Удаление:   sudo apt remove $PKG"