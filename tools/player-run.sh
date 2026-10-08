#!/bin/zsh
# WP-043D · tools/player-run.sh <DeathsDemolition.app | player binary> [player args…]
#
# Every Player launch — captures, bots, perf probes — goes through the single 'player' machine slot
# (tools/slot.sh). Players share the audio device and window focus with Editor PlayMode tests: a
# full-screen capture running during main-ci broke the UiV3 audio test (C-00 25.09). Two wrapped launches
# never overlap. Since WP-099 (C-00 08.10, OTURUM-ACILIS-R3 §0.8) main-ci takes NO player slot — an editor-scope
# run never does (holding it while queued for a guard window starved every Player): a Player started during
# main-ci's PlayMode run can disturb an audio/focus test (WP-043D risk, accepted). Warns (does not refuse) when another Player is already running — the user's own game
# or an unwrapped launch — and when -ddTelemetryDir is missing (the run would write the real telemetry
# folder). User data is NOT guarded here: keep tools/userdata-guard.sh snapshot/restore around it.
# Y2 (C-00 30.09): INT/TERM/HUP to this wrapper are passed on to the Player (as TERM) and the wrapper waits for it — a
# caller that stops its stage (rc.sh) or times a run out (wp064_run.py) leaves no orphan Player writing user data after
# its guard restore.
# Exit: the Player's exit code · 64 usage
set -u
source "${0:A:h}/dd-env.sh"
: "${DD_TIMELINE:=}"   # dd_log appends to it only when the caller set one
[ $# -ge 1 ] || { echo "usage: player-run.sh <DeathsDemolition.app|binary> [player args…]" >&2; exit 64; }
BIN="$1"; shift
[ -d "$BIN" ] && [[ "$BIN" == *.app ]] && BIN="$BIN/Contents/MacOS/$DD_PRODUCT"
[ -x "$BIN" ] || { echo "player-run: not an executable Player: $BIN" >&2; exit 64; }
DD_SLOT_TAG="player-run-$$"
trap 'dd_slot_release player' EXIT
dd_slot_acquire player || exit $?   # D1 review MUST-2 / M-07c: slot.sh's code (75 timeout, 64 usage) now reaches the caller
dd_disk_gate || exit $?   # C-00 30.09: no Player below 15 GiB free disk / above 12 GiB swap
OTHERS=$(pgrep -fl "Contents/MacOS/(Deaths Demolition|DeathsDemolition)" | grep -v "$$" )
[ -n "$OTHERS" ] && dd_log "WARNING player-run: another Player is running (user's game or an unwrapped launch): $(echo "$OTHERS" | cut -c1-120 | tr '\n' ';')"
[[ " $* " == *" -ddTelemetryDir "* ]] || dd_log "WARNING player-run: no -ddTelemetryDir — this run writes the real telemetry folder"
dd_log "player-run: $BIN $*"
"$BIN" "$@" &
PID=$!
trap 'kill -TERM $PID 2>/dev/null' INT TERM HUP
wait $PID; X=$?
# a forwarded signal interrupts `wait`; wait again until the Player itself has exited (its own exit code)
while kill -0 $PID 2>/dev/null; do wait $PID; X=$?; done
dd_log "player-run: exit $X"
exit $X
