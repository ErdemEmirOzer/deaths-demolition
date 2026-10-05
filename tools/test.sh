#!/bin/zsh
# WP-051 · tools/test.sh {editmode|playmode} [--filter <regex>] [--category <expr>] [--out DIR] [--timeout-min N] [--no-wait]
#
# One command for Unity test runs with a RUNNER-LEVEL time limit: NUnit's [Timeout] only fires between
# frames on the main thread, so a test stuck inside one blocking call (or an editor stuck after the run)
# hangs forever — seen as 25–47 min "runs". This wrapper takes a machine slot (tools/slot.sh, FIFO),
# starts the editor, and kills it when --timeout-min passes (default: editmode 20, playmode 45), then
# prints the per-test duration table (tools/test-durations.py) from the XML.
# Output: <out>/<mode>.xml, <out>/<mode>.log, <out>/durations.json. Default <out> = $DD_BUILD_ROOT/tests-<mode>-<utc>.
# --category <expr> → Unity -testCategory (`;`-separated regexes, `!` excludes; main-ci core: '!^Diagnostics$'). Without it
# no category filter: a test named in full by --filter still runs, [Explicit]/Diagnostics or not (S-10 r2 steps).
# Exit: 0 all passed · 1 failures (see table) · 2 no XML produced · 124 killed by the watchdog · 64 usage · 75 busy (--no-wait)
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
MODE="${1:-}"; shift 2>/dev/null || true
[ "$MODE" = "editmode" ] || [ "$MODE" = "playmode" ] || { echo "usage: test.sh {editmode|playmode} [--filter re] [--category expr] [--out DIR] [--timeout-min N] [--no-wait]" >&2; exit 64; }
FILTER=""; CATEGORY=""; OUT=""; TMIN=0; NOWAIT=0
while [ $# -gt 0 ]; do
  case "$1" in
    --filter|--category|--out|--timeout-min)
      [ $# -ge 2 ] || { echo "missing value for $1" >&2; exit 64; }
      case "$1" in
        --filter) FILTER="$2" ;;
        --category) CATEGORY="$2" ;;
        --out) OUT="$2" ;;
        --timeout-min) TMIN="$2" ;;
      esac
      shift 2 ;;
    # H12 (C-00 01.10): the S02_R2 PlayMode observers (-ddGameFeelGateDiag / -ddDec082Diag and their -ddGateDiag* /
    # -ddDecDiag* identity values) were retired with WP-069 M-18 (114eb9c6); no extra Unity argument passes any more
    --extra-arg|--extra-arg=*) echo "$1: retired (H12, C-00 01.10 — no diagnostic flags reach Unity)" >&2; exit 64 ;;
    --no-wait) NOWAIT=1; shift ;;
    *) echo "unknown option: $1" >&2; exit 64 ;;
  esac
done

[ "$TMIN" -gt 0 ] || TMIN=$( [ "$MODE" = "editmode" ] && echo 20 || echo 45 )
[ -n "$OUT" ] || OUT="$DD_BUILD_ROOT/tests-$MODE-$(dd_utc)"
mkdir -p "$OUT"; DD_TIMELINE="$OUT/timeline.txt"; XML="$OUT/$MODE.xml"; LOG="$OUT/$MODE.log"
dd_log "test.sh mode=$MODE filter='${FILTER:-all}'${CATEGORY:+ category='$CATEGORY'} timeout=${TMIN}min out=$OUT head=$(git -C "$DD_REPO" rev-parse --short=12 HEAD 2>/dev/null)"
# WP-052 note (S-02 finding): the Editor test runner leaves ProfilerRecorder.LastValue at 0 while
# Application.targetFrameRate == 60; tests that read recorders must set targetFrameRate = -1 in SetUp.
[ "$MODE" = "playmode" ] && dd_log "note: ProfilerRecorder-based tests need Application.targetFrameRate = -1 in SetUp (LastValue stays 0 at 60 in the Editor runner)"
if [ "$NOWAIT" -eq 1 ] && [ "$(dd_unity_count)" -ge "$DD_MAX_UNITY" ]; then dd_log "busy"; exit 75; fi
# Codex-audit Y1 / G4 Q10 (C-00 30.09): INT/TERM/HUP (main-ci forwards TERM to this pid) exit 130/143/129 through EXIT,
# which first stops our own Unity (TERM to DD_UNITY_PID only, ≤ DD_CI_CHILD_GRACE s); a Unity still alive keeps the slot.
T_CLEANING=0
t_cleanup() {
  trap - EXIT; T_CLEANING=1
  if [ -n "$DD_UNITY_PID" ] && ! dd_stop_child "$DD_UNITY_PID" "${DD_CI_CHILD_GRACE:-120}"; then
    dd_log "test.sh: Unity child $DD_CHILD_ALIVE still alive, tell C-00 — slot $DD_SLOT_TAG kept" >&2; return 0; fi
  dd_slot_release
}
trap 't_cleanup' EXIT
trap '[ "$T_CLEANING" -eq 1 ] || exit 130' INT; trap '[ "$T_CLEANING" -eq 1 ] || exit 143' TERM; trap '[ "$T_CLEANING" -eq 1 ] || exit 129' HUP
dd_slot_acquire || exit $?   # D1 review MUST-2 / M-07c: slot.sh's code (75 timeout, 64 usage) now reaches the caller
dd_disk_gate || exit $?   # C-00 30.09: no Unity below 15 GiB free disk / above 12 GiB swap (checked after the slot wait)
PLAT=$( [ "$MODE" = "editmode" ] && echo EditMode || echo PlayMode ); NOGFX=(); [ "$MODE" = "editmode" ] && NOGFX=(-nographics)
FARGS=(); [ -n "$FILTER" ] && FARGS=(-testFilter "$FILTER"); [ -n "$CATEGORY" ] && FARGS+=(-testCategory "$CATEGORY")
T0=$(date +%s)
# watchdogs (WP-051D dd_unity_watched): total limit TMIN per attempt + log-stall (20 min silent → stop that pid, retry once)
dd_unity_watched "$LOG" "$TMIN" "$DD_UNITY" -batchmode "${NOGFX[@]}" -forgetProjectPath -projectPath "$DD_PROJECT" -runTests -testPlatform "$PLAT" "${FARGS[@]}" -testResults "$XML" -logFile "$LOG"
X=$?
T1=$(date +%s)
KILLED=0; case "$DD_UNITY_OUTCOME" in killed|stalled) KILLED=1 ;; esac
[ "$DD_UNITY_OUTCOME" != ok ] && dd_log "unity outcome=$DD_UNITY_OUTCOME"
ERRS=$(grep -c 'error CS' "$LOG" 2>/dev/null); ERRS=${ERRS:-0}
if [ ! -f "$XML" ]; then dd_log "$MODE exit=$X seconds=$((T1-T0)) errorsCS=$ERRS NO XML killed=$KILLED"; [ "$KILLED" -eq 1 ] && exit 124; exit 2; fi
SUM=$(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$XML" | head -1)
dd_log "$MODE exit=$X seconds=$((T1-T0)) errorsCS=$ERRS $SUM killed=$KILLED"
python3 "$DD_TOOLS_DIR/test-durations.py" "$XML" --json "$OUT/durations.json" --top 15 | tee -a "$DD_TIMELINE"
[ "$KILLED" -eq 1 ] && exit 124
grep -q 'failed="0"' <<< "$SUM" && [ "$X" -eq 0 ] && exit 0
exit 1
