#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PACKAGE="$ROOT/macos/CursorPocketMac"
ARTIFACTS="$ROOT/artifacts"
APP="$ARTIFACTS/CursorPocket.app"
ARCHIVE="$ARTIFACTS/CursorPocket-macOS-universal.zip"
VERSION="$(perl -ne 'print $1 if /<CursorPocketVersion>([^<]+)/' "$ROOT/native/Version.props")"

if [[ -z "$VERSION" ]]; then
  echo "Could not read CursorPocketVersion." >&2
  exit 1
fi

rm -rf "$APP" "$ARCHIVE"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$ARTIFACTS"

build_arch() {
  local arch="$1"
  swift build --package-path "$PACKAGE" -c release --arch "$arch"
  swift build --package-path "$PACKAGE" -c release --arch "$arch" --show-bin-path
}

arm_bin="$(build_arch arm64 | tail -1)/CursorPocketMac"
x64_bin="$(build_arch x86_64 | tail -1)/CursorPocketMac"
lipo -create "$arm_bin" "$x64_bin" -output "$APP/Contents/MacOS/CursorPocket"

# App icon: generated at build time from a committed brand PNG with sips and
# iconutil (both ship with macOS). Preference order: the square 1254x1254
# brand marks, then the site pocket mark as a fallback.
ICON_SOURCE=""
for candidate in \
  "$ROOT/assets/brand/main-logo.png" \
  "$ROOT/assets/brand/brand-logo-02-pocket-v3-transparent.png" \
  "$ROOT/site/assets/pocket-mark.png"; do
  if [[ -f "$candidate" ]]; then
    ICON_SOURCE="$candidate"
    break
  fi
done
if [[ -z "$ICON_SOURCE" ]]; then
  echo "No committed brand PNG found for the app icon (looked in assets/brand and site/assets)." >&2
  exit 1
fi

ICON_W="$(sips -g pixelWidth "$ICON_SOURCE" | awk '/pixelWidth/ {print $2}')"
ICON_H="$(sips -g pixelHeight "$ICON_SOURCE" | awk '/pixelHeight/ {print $2}')"
if [[ -z "$ICON_W" || -z "$ICON_H" || "$ICON_H" -eq 0 ]]; then
  echo "Could not read dimensions of $ICON_SOURCE." >&2
  exit 1
fi
RATIO_PCT=$((ICON_W * 100 / ICON_H))
if (( RATIO_PCT < 80 || RATIO_PCT > 125 )); then
  echo "Icon source $ICON_SOURCE is ${ICON_W}x${ICON_H} — not square-ish enough for an app icon." >&2
  exit 1
fi

ICONSET="$ARTIFACTS/AppIcon.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
# iconutil accepts only the canonical names; 64 px ships as icon_32x32@2x
# and 1024 px as icon_512x512@2x.
for size in 16 32 128 256 512; do
  sips -s format png -z "$size" "$size" "$ICON_SOURCE" \
    --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  retina=$((size * 2))
  sips -s format png -z "$retina" "$retina" "$ICON_SOURCE" \
    --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"
if [[ ! -s "$APP/Contents/Resources/AppIcon.icns" ]]; then
  echo "AppIcon.icns was not generated — the bundle must not ship icon-less." >&2
  exit 1
fi

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDisplayName</key><string>CursorPocket</string>
  <key>CFBundleExecutable</key><string>CursorPocket</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleIdentifier</key><string>app.cursorpocket.preview</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>CursorPocket</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSMicrophoneUsageDescription</key><string>CursorPocket records your microphone only for narrated screen recordings and audio notes you start, saved locally.</string>
  <key>NSCameraUsageDescription</key><string>CursorPocket shows your camera in the on-screen self-view only during recordings where you enable it.</string>
  <key>NSAppleEventsUsageDescription</key><string>CursorPocket asks your front browser for the current page address when you save a link.</string>
</dict></plist>
PLIST

codesign --force --deep --sign - "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ARCHIVE"
echo "Created $ARCHIVE"
