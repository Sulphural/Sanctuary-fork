#!/bin/sh
set -eu

if [ "$#" -ne 3 ]; then
	echo "usage: $0 <osx-x64 publish dir> <osx-arm64 publish dir> <output dir>" >&2
	exit 1
fi

here="$(cd "$(dirname "$0")" && pwd)"
x64="$1"
arm64="$2"
out="$3"
app="$out/Free Realms Plugin.app"
plist="$app/Contents/Info.plist"

mkdir -p "$out"
rm -rf "$app"

osacompile -o "$app" "$here/applet.applescript"

cp "$x64/FreeRealmsPlugin" "$app/Contents/Resources/FreeRealmsPlugin-x64"
cp "$arm64/FreeRealmsPlugin" "$app/Contents/Resources/FreeRealmsPlugin-arm64"
chmod +x "$app/Contents/Resources/FreeRealmsPlugin-x64" "$app/Contents/Resources/FreeRealmsPlugin-arm64"

/usr/libexec/PlistBuddy -c "Set :CFBundleName Free Realms Plugin" "$plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleIdentifier string org.sanctuary.freerealmsplugin" "$plist" 2>/dev/null || /usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier org.sanctuary.freerealmsplugin" "$plist"
/usr/libexec/PlistBuddy -c "Add :LSUIElement bool true" "$plist" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Delete :CFBundleURLTypes" "$plist" 2>/dev/null || true
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes array" "$plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0 dict" "$plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0:CFBundleURLName string Free Realms Plugin" "$plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes array" "$plist"
/usr/libexec/PlistBuddy -c "Add :CFBundleURLTypes:0:CFBundleURLSchemes:0 string freerealms" "$plist"

codesign --force --sign - "$app/Contents/Resources/FreeRealmsPlugin-x64"
codesign --force --sign - "$app/Contents/Resources/FreeRealmsPlugin-arm64"
codesign --force --deep --sign - "$app"

(cd "$out" && rm -f FreeRealmsPlugin-macos.zip && ditto -c -k --keepParent "Free Realms Plugin.app" FreeRealmsPlugin-macos.zip)

echo "Built $out/FreeRealmsPlugin-macos.zip"
