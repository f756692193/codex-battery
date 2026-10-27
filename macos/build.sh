#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
if [ "$(uname -s)" != Darwin ]; then
  echo 'Build this AppKit app on macOS with Xcode Command Line Tools.' >&2
  exit 1
fi
cd "$ROOT"
ARGS=(-c release)
case "${ARCHS:-native}" in
  native) ;;
  universal) ARGS+=(--arch arm64 --arch x86_64) ;;
  *) echo 'ARCHS must be native or universal' >&2; exit 1 ;;
esac
swift build "${ARGS[@]}"
BIN="$(swift build "${ARGS[@]}" --show-bin-path)"
OUT="$ROOT/dist"
APP="$OUT/Codex Battery.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN/CodexBattery" "$APP/Contents/MacOS/CodexBattery"
cp "$ROOT/Info.plist" "$APP/Contents/Info.plist"
chmod +x "$APP/Contents/MacOS/CodexBattery"
/usr/bin/plutil -lint "$APP/Contents/Info.plist"
# Local ad-hoc signature; this is not Developer ID signing or notarization.
/usr/bin/codesign --force --sign - "$APP"
/usr/bin/codesign --verify --strict "$APP"
/usr/bin/ditto -c -k --keepParent "$APP" "$OUT/CodexBattery-macos-${ARCHS:-native}.zip"
/usr/bin/shasum -a 256 "$OUT/CodexBattery-macos-${ARCHS:-native}.zip" > "$OUT/SHA256.txt"
echo "$APP"
