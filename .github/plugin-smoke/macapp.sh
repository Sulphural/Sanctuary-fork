#!/usr/bin/env bash
set -uo pipefail

APP_ZIP="$1"
STUB="$2"
PORT=18099
SITE="http://127.0.0.1:$PORT"
TICKET=fedcba9876543210fedcba9876543210
HERE="$(cd "$(dirname "$0")" && pwd)"
DATA="$HOME/Library/Application Support/FreeRealmsPlugin"
failures=0
pass() { echo "PASS: $1"; }
fail() { echo "FAIL: $1"; failures=$((failures + 1)); }

rm -rf "$RUNNER_TEMP/app" && mkdir -p "$RUNNER_TEMP/app"
ditto -x -k "$APP_ZIP" "$RUNNER_TEMP/app"
APP="$(find "$RUNNER_TEMP/app" -maxdepth 1 -name '*.app' | head -n 1)"
[ -n "$APP" ] && pass "zip contains $(basename "$APP")" || { fail "no app in zip"; exit 1; }

codesign --verify --deep --strict "$APP" && pass "code signature valid" || fail "code signature invalid"
scheme="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleURLTypes:0:CFBundleURLSchemes:0' "$APP/Contents/Info.plist" 2>/dev/null)"
[ "$scheme" = "freerealms" ] && pass "app declares freerealms:// links" || fail "URL scheme is '$scheme'"
for arch in x64:x86_64 arm64:arm64; do
	file "$APP/Contents/Resources/FreeRealmsPlugin-${arch%%:*}" | grep -q "${arch##*:}" && pass "${arch%%:*} plugin binary present" || fail "${arch%%:*} plugin binary missing"
done

python3 "$HERE/mock_site.py" "$PORT" "$STUB" 2>"$RUNNER_TEMP/mock_site_app.log" &
MOCK=$!
trap 'kill $MOCK 2>/dev/null' EXIT
sleep 1

cp -R "$APP" /Applications/
INSTALLED="/Applications/$(basename "$APP")"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$INSTALLED"

mkdir -p "$DATA"
printf '{\n  "trustedSites": [\n    "%s"\n  ]\n}\n' "$SITE" >"$DATA/settings.json"
rm -f "$DATA/client/launch-args.txt"
before="$(grep -c 'Game exited' "$DATA/plugin.log" 2>/dev/null || echo 0)"

open "freerealms://play/?site=$SITE&guid=42&ticket=$TICKET" && pass "macOS accepted the freerealms:// link" || fail "open could not handle the link"

for _ in $(seq 1 240); do
	now="$(grep -c 'Game exited' "$DATA/plugin.log" 2>/dev/null || echo 0)"
	[ "$now" -gt "$before" ] && break
	sleep 1
done

if tr -d '' <"$DATA/client/launch-args.txt" 2>/dev/null | grep -qx "GUID=42"; then
	pass "link opened through the app launched the game"
	sed 's/^/    /' "$DATA/client/launch-args.txt"
else
	fail "game was not launched from the app"
fi

echo "== plugin.log (tail)"
tail -n 15 "$DATA/plugin.log" 2>/dev/null | sed 's/^/    /'

if [ "$failures" -eq 0 ]; then
	echo "ALL APP CHECKS PASSED"
else
	echo "$failures APP CHECK(S) FAILED"
	exit 1
fi
