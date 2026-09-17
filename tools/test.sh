#!/bin/zsh
# WP-051 · tools/test.sh {editmode|playmode} [--filter <regex>] [--out DIR] [--timeout-min N] [--no-wait]
#
# One command for Unity test runs with a RUNNER-LEVEL time limit: NUnit's [Timeout] only fires between
# frames on the main thread, so a test stuck inside one blocking call (or an editor stuck after the run)
# hangs forever — seen as 25–47 min "runs". This wrapper takes a machine slot (tools/slot.sh, FIFO),
# starts the editor, and kills it when --timeout-min passes (default: editmode 20, playmode 45), then
# prints the per-test duration table (tools/test-durations.py) from the XML.
# Output: <out>/<mode>.xml, <out>/<mode>.log, <out>/durations.json. Default <out> = $DD_BUILD_ROOT/tests-<mode>-<utc>.
# Exit: 0 all passed · 1 failures (see table) · 2 no XML produced · 124 killed by the watchdog · 64 usage · 75 busy (--no-wait)
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
MODE="${1:-}"; shift 2>/dev/null || true
[ "$MODE" = "editmode" ] || [ "$MODE" = "playmode" ] || { echo "usage: test.sh {editmode|playmode} [--filter re] [--out DIR] [--timeout-min N] [--no-wait]" >&2; exit 64; }
FILTER=""; OUT=""; TMIN=0; NOWAIT=0
while [ $# -gt 0 ]; do case "$1" in --filter) FILTER="$2"; shift ;; --out) OUT="$2"; shift ;; --timeout-min) TMIN="$2"; shift ;; --no-wait) NOWAIT=1 ;; *) echo "unknown option: $1" >&2; exit 64 ;; esac; shift; done
[ "$TMIN" -gt 0 ] || TMIN=$( [ "$MODE" = "editmode" ] && echo 20 || echo 45 )
[ -n "$OUT" ] || OUT="$DD_BUILD_ROOT/tests-$MODE-$(dd_utc)"
mkdir -p "$OUT"; DD_TIMELINE="$OUT/timeline.txt"; XML="$OUT/$MODE.xml"; LOG="$OUT/$MODE.log"
dd_log "test.sh mode=$MODE filter='${FILTER:-all}' timeout=${TMIN}min out=$OUT head=$(git -C "$DD_REPO" rev-parse --short=12 HEAD 2>/dev/null)"
# WP-052 note (S-02 finding): the Editor test runner leaves ProfilerRecorder.LastValue at 0 while
# Application.targetFrameRate == 60; tests that read recorders must set targetFrameRate = -1 in SetUp.
[ "$MODE" = "playmode" ] && dd_log "note: ProfilerRecorder-based tests need Application.targetFrameRate = -1 in SetUp (LastValue stays 0 at 60 in the Editor runner)"
if [ "$NOWAIT" -eq 1 ] && [ "$(dd_unity_count)" -ge "$DD_MAX_UNITY" ]; then dd_log "busy"; exit 75; fi
trap 'dd_slot_release' EXIT
dd_slot_acquire
PLAT=$( [ "$MODE" = "editmode" ] && echo EditMode || echo PlayMode ); NOGFX=(); [ "$MODE" = "editmode" ] && NOGFX=(-nographics)
FARGS=(); [ -n "$FILTER" ] && FARGS=(-testFilter "$FILTER")
T0=$(date +%s)
"$DD_UNITY" -batchmode "${NOGFX[@]}" -forgetProjectPath -projectPath "$DD_PROJECT" -runTests -testPlatform "$PLAT" "${FARGS[@]}" -testResults "$XML" -logFile "$LOG" >/dev/null 2>&1 &
UPID=$!
# watchdog: kill the editor (and its children) when the limit passes; leaves a KILLED marker in the timeline
( sleep $((TMIN*60)); if kill -0 $UPID 2>/dev/null; then echo "$(date -u +%FT%TZ) WATCHDOG: ${TMIN} min passed — killing Unity pid $UPID" >> "$DD_TIMELINE"; pkill -P $UPID 2>/dev/null; kill $UPID 2>/dev/null; sleep 5; kill -9 $UPID 2>/dev/null; fi ) &
WPID=$!
wait $UPID; X=$?
kill $WPID 2>/dev/null; wait $WPID 2>/dev/null
T1=$(date +%s)
KILLED=0; grep -q "WATCHDOG" "$DD_TIMELINE" 2>/dev/null && KILLED=1
ERRS=$(grep -c 'error CS' "$LOG" 2>/dev/null); ERRS=${ERRS:-0}
if [ ! -f "$XML" ]; then dd_log "$MODE exit=$X seconds=$((T1-T0)) errorsCS=$ERRS NO XML killed=$KILLED"; [ "$KILLED" -eq 1 ] && exit 124; exit 2; fi
SUM=$(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$XML" | head -1)
dd_log "$MODE exit=$X seconds=$((T1-T0)) errorsCS=$ERRS $SUM killed=$KILLED"
python3 "$DD_TOOLS_DIR/test-durations.py" "$XML" --json "$OUT/durations.json" --top 15 | tee -a "$DD_TIMELINE"
[ "$KILLED" -eq 1 ] && exit 124
grep -q 'failed="0"' <<< "$SUM" && [ "$X" -eq 0 ] && exit 0
exit 1
