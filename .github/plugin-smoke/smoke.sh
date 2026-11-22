#!/usr/bin/env bash
set -uo pipefail

PLUGIN="$1"
STUB="$2"
PORT=18099
SITE="http://127.0.0.1:$PORT"
TICKET=0123456789abcdef0123456789abcdef
HERE="$(cd "$(dirname "$0")" && pwd)"
WRAP=()

if [ "$(uname)" = "Darwin" ]; then
	DATA="$HOME/Library/Application Support/FreeRealmsPlugin"
else
	export XDG_DATA_HOME="${XDG_DATA_HOME:-$RUNNER_TEMP/xdg}"
	DATA="$XDG_DATA_HOME/FreeRealmsPlugin"
	if command -v xvfb-run >/dev/null; then
		WRAP=(xvfb-run -a)
	fi
fi

failures=0
pass() { echo "PASS: $1"; }
fail() { echo "FAIL: $1"; failures=$((failures + 1)); }

status() { curl -s --max-time 2 "http://127.0.0.1:20055/status" || true; }

stop_plugins() {
	pkill -f "^$PLUGIN" 2>/dev/null
	pkill -f osascript 2>/dev/null
	pkill -f zenity 2>/dev/null
	for _ in $(seq 1 40); do
		curl -s --max-time 1 "http://127.0.0.1:20055/status" >/dev/null || return 0
		sleep 0.5
	done
}

state_of() { sed -n 's/.*"state": *"\([a-z]*\)".*/\1/p' | head -n 1; }

chmod +x "$PLUGIN"
mkdir -p "$DATA"
python3 "$HERE/mock_site.py" "$PORT" "$STUB" 2>"$RUNNER_TEMP/mock_site.log" &
MOCK=$!
trap 'kill $MOCK 2>/dev/null' EXIT

for _ in $(seq 1 50); do
	curl -s "$SITE/website/plugin/config" >/dev/null && break
	sleep 0.2
done

run_and_watch() {
	local link="$1" limit="$2" states="$3"
	: >"$states"
	${WRAP[@]+"${WRAP[@]}"} "$PLUGIN" "$link" >"$RUNNER_TEMP/plugin-stdout.log" 2>&1 &
	local pid=$! last="" waited=0
	while kill -0 "$pid" 2>/dev/null; do
		local now
		now="$(status | state_of)"
		if [ -n "$now" ] && [ "$now" != "$last" ]; then
			echo "$now" >>"$states"
			echo "  state: $now"
			last="$now"
		fi
		sleep 0.5
		waited=$((waited + 1))
		if [ "$waited" -gt $((limit * 2)) ]; then
			echo "  timed out after ${limit}s, stopping plugin"
			kill "$pid" 2>/dev/null
			stop_plugins
			pkill -f wineserver 2>/dev/null
			wait "$pid" 2>/dev/null
			return 124
		fi
	done
	wait "$pid"
	local code=$?
	stop_plugins
	return $code
}

echo "== 1. Invalid link is rejected"
${WRAP[@]+"${WRAP[@]}"} "$PLUGIN" "freerealms://play/?site=$SITE&guid=abc&ticket=nothex" >/dev/null 2>&1 &
pid=$!
for _ in $(seq 1 30); do kill -0 "$pid" 2>/dev/null || break; sleep 0.5; done
kill "$pid" 2>/dev/null; stop_plugins
grep -q "Rejected link" "$DATA/plugin.log" 2>/dev/null && pass "invalid link rejected" || fail "invalid link not rejected"

echo "== 2. Untrusted website asks before starting"
rm -f "$DATA/settings.json"
${WRAP[@]+"${WRAP[@]}"} "$PLUGIN" "freerealms://play/?site=$SITE&guid=123&ticket=$TICKET" >/dev/null 2>&1 &
pid=$!
seen=""
for _ in $(seq 1 40); do
	now="$(status | state_of)"
	[ -n "$now" ] && seen="$seen $now"
	case "$now" in confirm|denied) break ;; esac
	sleep 0.5
done
kill "$pid" 2>/dev/null; stop_plugins
wait "$pid" 2>/dev/null
echo "  states:$seen"
case "$seen" in *confirm*|*denied*) pass "untrusted site needs permission" ;; *) fail "untrusted site was not stopped" ;; esac
[ ! -d "$DATA/client" ] && pass "nothing downloaded for untrusted site" || fail "client downloaded for untrusted site"

echo "== 3. Trusted site: download client + runtime, launch through Wine"
printf '{\n  "trustedSites": [\n    "%s"\n  ]\n}\n' "$SITE" >"$DATA/settings.json"
start=$(date +%s)
run_and_watch "freerealms://play/?site=$SITE&guid=123456789&ticket=$TICKET" 1500 "$RUNNER_TEMP/states1.txt"
code=$?
echo "  exit code $code after $(( $(date +%s) - start ))s"
[ "$code" -eq 0 ] && pass "plugin exited cleanly" || fail "plugin exit code $code"
for expected in downloading running exited; do
	grep -qx "$expected" "$RUNNER_TEMP/states1.txt" && pass "status reached '$expected'" || fail "status never reached '$expected'"
done
[ -f "$DATA/client/FreeRealms.exe" ] && pass "client downloaded from manifest" || fail "client missing"
ls -d "$DATA"/runtime/*/.complete >/dev/null 2>&1 && pass "Wine runtime installed and verified" || fail "Wine runtime not installed"
args="$DATA/client/launch-args.txt"
if [ -f "$args" ]; then
	pass "game started through Wine"
	tr -d '' <"$args" >"$args.unix" && mv "$args.unix" "$args"
	echo "  arguments the game received:"; sed 's/^/    /' "$args"
	for expected in "Server=127.0.0.1:20042" "GUID=123456789" "Ticket=$TICKET" "Internationalization:Locale=8" "Portrait:UploadUrl=http://127.0.0.1/image"; do
		grep -qx "$expected" "$args" && pass "argument $expected" || fail "argument $expected missing"
	done
else
	fail "game never started (no launch-args.txt)"
fi
grep -q "Game exited with code 0" "$DATA/plugin.log" && pass "game exit detected" || fail "game exit not detected"

if [ ! -f "$args" ]; then
	echo "== Wine diagnostics"
	wine="$(find "$DATA/runtime" -path '*/bin/wine' | head -n 1)"
	echo "  wine: $wine"
	file "$wine" | sed 's/^/    /'
	"$wine" --version 2>&1 | sed 's/^/    /'
	echo "  -- running the game directly with error logging"
	(cd "$DATA/client" && WINEPREFIX="$DATA/prefix" WINEDEBUG=err+all,warn+module ${WRAP[@]+"${WRAP[@]}"} "$wine" "$DATA/client/FreeRealms.exe" one two 2>&1 | tail -n 60 | sed 's/^/    /'; echo "    exit code: ${PIPESTATUS[0]}")
	echo "  -- same, with a fresh prefix"
	rm -rf "$RUNNER_TEMP/freshprefix"
	(cd "$DATA/client" && WINEPREFIX="$RUNNER_TEMP/freshprefix" ${WRAP[@]+"${WRAP[@]}"} "$wine" "$DATA/client/FreeRealms.exe" one two 2>&1 | tail -n 40 | sed 's/^/    /'; echo "    exit code: ${PIPESTATUS[0]}")
	ls -la "$DATA/client" | sed 's/^/    /'
fi

echo "== 4. Second launch reuses the client and runtime"
rm -f "$args"
start=$(date +%s)
run_and_watch "freerealms://login/?site=$SITE&session=$TICKET" 300 "$RUNNER_TEMP/states2.txt"
code=$?
echo "  exit code $code after $(( $(date +%s) - start ))s"
[ "$code" -eq 0 ] && pass "second launch exited cleanly" || fail "second launch exit code $code"
grep -qx downloading "$RUNNER_TEMP/states2.txt" && fail "downloaded again on second launch" || pass "no re-download"
[ -f "$args" ] && tr -d '' <"$args" >"$args.unix" && mv "$args.unix" "$args"
grep -qx "Server=127.0.0.1:20040" "$args" 2>/dev/null && grep -qx "SessionId=$TICKET" "$args" && pass "login link passes Login server and session" || fail "login link arguments wrong"

echo "== plugin.log"
sed 's/^/    /' "$DATA/plugin.log" 2>/dev/null | tail -n 40

echo
if [ "$failures" -eq 0 ]; then
	echo "ALL CHECKS PASSED"
else
	echo "$failures CHECK(S) FAILED"
	exit 1
fi
