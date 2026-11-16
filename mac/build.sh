#!/bin/bash
# Compila a SnapStudio para macOS e monta SnapStudio.app em mac/build/.
# Precisa só das Command Line Tools (xcode-select --install); não precisa do Xcode.
# Uso:  ./build.sh [--run] [--install]
#   --run       abre a app no fim
#   --install   copia a app para /Applications
set -euo pipefail

cd "$(dirname "$0")"
ROOT="$(pwd)"
OUT="$ROOT/build"
APP="$OUT/SnapStudio.app"
VERSION="1.4.1"

RUN=0; INSTALL=0
for a in "$@"; do
  case "$a" in
    --run) RUN=1 ;;
    --install) INSTALL=1 ;;
  esac
done

# Compila diretamente com o swiftc (não depende do SwiftPM). Binário universal: Apple Silicon + Intel.
SDK="$(xcrun --sdk macosx --show-sdk-path)"
SRC=(Sources/SnapStudio/*.swift)
mkdir -p "$OUT/obj"
ARCHS=("arm64" "x86_64")
[ "${FAST:-0}" = 1 ] && ARCHS=("$(uname -m)")
BINS=()
for arch in "${ARCHS[@]}"; do
  echo "› A compilar ($arch)…"
  if ! xcrun swiftc -O -swift-version 5 -sdk "$SDK" -target "$arch-apple-macos14.0" \
      -module-name SnapStudio -o "$OUT/obj/SnapStudio-$arch" "${SRC[@]}" > "$OUT/obj/build-$arch.log" 2>&1; then
    grep -E "error:" -A3 "$OUT/obj/build-$arch.log" || cat "$OUT/obj/build-$arch.log"
    echo "Falha na compilação ($arch). Registo completo: $OUT/obj/build-$arch.log"
    exit 1
  fi
  BINS+=("$OUT/obj/SnapStudio-$arch")
done
BIN="$OUT/obj/SnapStudio"
lipo -create "${BINS[@]}" -output "$BIN"

# fechar a app em execução
if pgrep -x SnapStudio >/dev/null; then
  echo "› A fechar a SnapStudio em execução…"
  osascript -e 'quit app "SnapStudio"' >/dev/null 2>&1 || true
  sleep 1
  pkill -x SnapStudio 2>/dev/null || true
fi

echo "› A montar SnapStudio.app…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/SnapStudio"

# ícone
ICONSET="$OUT/AppIcon.iconset"
rm -rf "$ICONSET"
"$BIN" --make-icon "$ICONSET"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>SnapStudio</string>
  <key>CFBundleDisplayName</key><string>SnapStudio</string>
  <key>CFBundleIdentifier</key><string>com.snapstudio.mac</string>
  <key>CFBundleExecutable</key><string>SnapStudio</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleDevelopmentRegion</key><string>pt</string>
  <key>CFBundleLocalizations</key><array><string>pt</string><string>en</string></array>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSUIElement</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSSupportsAutomaticTermination</key><false/>
  <key>NSHumanReadableCopyright</key><string>SnapStudio</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>Image</string>
      <key>CFBundleTypeRole</key><string>Editor</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>LSItemContentTypes</key>
      <array><string>public.png</string><string>public.jpeg</string><string>com.microsoft.bmp</string><string>com.compuserve.gif</string><string>public.tiff</string></array>
    </dict>
  </array>
</dict>
</plist>
PLIST

# Assinatura. Por omissão é ad hoc (sem certificado), mas com um requisito de identidade fixo
# (o identificador da app): sem isto o macOS identifica a app pelo hash do binário, que muda a cada
# compilação, e a autorização de Gravação do ecrã deixa de valer depois de atualizar.
# Com um certificado "Developer ID":  SIGN_ID="Developer ID Application: ..." ./build.sh
SIGN_ID="${SIGN_ID:--}"
if [ "$SIGN_ID" = "-" ]; then
  codesign --force --deep --options runtime --sign - \
    --requirements '=designated => identifier "com.snapstudio.mac"' "$APP"
else
  codesign --force --deep --options runtime --timestamp --sign "$SIGN_ID" "$APP"
fi
codesign --verify --deep --strict "$APP"
echo "OK -> $APP"

# ficheiro para a release
ZIP="$OUT/SnapStudio-macOS.zip"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"
echo "OK -> $ZIP"

if [ "$INSTALL" = 1 ]; then
  rm -rf "/Applications/SnapStudio.app"
  cp -R "$APP" /Applications/
  APP="/Applications/SnapStudio.app"
  echo "Instalada em $APP"
fi

if [ "$RUN" = 1 ]; then open "$APP"; fi
