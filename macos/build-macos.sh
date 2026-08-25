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

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDisplayName</key><string>CursorPocket</string>
  <key>CFBundleExecutable</key><string>CursorPocket</string>
  <key>CFBundleIdentifier</key><string>app.cursorpocket.preview</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>CursorPocket</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

codesign --force --deep --sign - "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ARCHIVE"
echo "Created $ARCHIVE"
