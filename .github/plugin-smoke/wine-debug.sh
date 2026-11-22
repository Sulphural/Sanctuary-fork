#!/usr/bin/env bash
set -uo pipefail

DATA="$RUNNER_TEMP/xdg/FreeRealmsPlugin"
STUB32="$1"
STUB64="$2"
WINE="$(find "$DATA/runtime" -path '*/bin/wine' | head -n 1)"
WORK="$RUNNER_TEMP/winedebug"
mkdir -p "$WORK"
cp "$STUB32" "$WORK/stub32.exe"
cp "$STUB64" "$WORK/stub64.exe"
cd "$WORK"

try() {
	local label="$1"; shift
	echo "---- $label"
	rm -rf "$WORK/prefix"
	( export WINEPREFIX="$WORK/prefix"; timeout 180 "$@" ) >"$WORK/out.txt" 2>&1
	local code=$?
	tail -n 25 "$WORK/out.txt" | sed 's/^/    /'
	echo "    exit code: $code"
	[ -f launch-args.txt ] && { echo "    launch-args.txt written"; rm -f launch-args.txt; }
}

echo "kernel: $(uname -r)"
sysctl vm.mmap_min_addr kernel.randomize_va_space 2>/dev/null
ldd "$WINE" | sed 's/^/    /'

try "Kron4ek 11.20 staging, preloader skipped, 32-bit" env WINELOADERNOEXEC=1 "$WINE" stub32.exe a b
try "Kron4ek 11.20 staging, preloader skipped, 32-bit, X" env WINELOADERNOEXEC=1 xvfb-run -a "$WINE" stub32.exe a b

for build in 11.20/wine-11.20-amd64-wow64 10.20/wine-10.20-staging-amd64-wow64 10.0/wine-10.0-amd64-wow64; do
	name="$(basename "$build")"
	if curl -sfL -o "$WORK/$name.tar.xz" "https://github.com/Kron4ek/Wine-Builds/releases/download/$build.tar.xz"; then
		mkdir -p "$WORK/alt/$name" && tar -xJf "$WORK/$name.tar.xz" -C "$WORK/alt/$name"
		alt="$(find "$WORK/alt/$name" -path '*/bin/wine' | head -n 1)"
		echo "sha256 $name: $(sha256sum "$WORK/$name.tar.xz" | cut -d' ' -f1)"
		try "$name, 32-bit" "$alt" stub32.exe a b
		try "$name, preloader skipped, 32-bit" env WINELOADERNOEXEC=1 "$alt" stub32.exe a b
	else
		echo "---- $name: not downloadable"
	fi
done

if command -v wine >/dev/null; then
	echo "system wine: $(wine --version)"
	try "Ubuntu wine, 32-bit stub" wine stub32.exe a b
	try "Ubuntu wine, 64-bit stub" wine stub64.exe a b
fi
