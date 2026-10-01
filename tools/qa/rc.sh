#!/bin/zsh
# WP-061 · M4 release-candidate acceptance/regression — one repeatable entry point.
#
# Usage: tools/qa/rc.sh [out-dir] [--rounds 12] [--seed 61] [--perf-runs 3] [--window 60]
#                       [--diagnostic-continue]
#
# The chain is deliberately fail-fast by default: the first failed stage stops later expensive
# work. --diagnostic-continue keeps running independent evidence stages after a failure while the
# final verdict remains failed. Every attempted stage keeps its exit code, output and artefacts;
# rc_aggregate.py always runs.
#
# $DD_GUARD = dd-env.sh DD_GUARD_CANON (G4 D4, C-00 01.10): the CI driver's guard, never this checkout's; DD_GUARD_ROOT set
# or that guard missing → 64 before the output directory is created.
# User-data guard (Y2, C-00 30.09 · §4.14) — PER STAGE, never one snapshot around the whole RC (its final restore would
# roll back what the user did while playing during the RC). Every guarded stage runs
#   $DD_GUARD snapshot stages/<stage>/ug --scope S → stage → $DD_GUARD restore stages/<stage>/ug --scope S
#   · editor scope: full-editmode, full-playmode (tools/test.sh);
#   · player scope: m3-regression, soak-stability-61/26, each real-bot report run (stages/real-N/ug), economy-acceptance,
#     window-modes — their Players start through tools/player-run.sh (wp064_run.py, regression.sh readability,
#     economy-acceptance.sh, window-modes.sh); the snapshot holds the 'player' slot for this shell, the wrapped Players
#     re-enter it;
#   · release-smoke is NOT wrapped: build.sh --smoke → smoke.sh carries its own inline snapshot/restore (no double guard);
#     release-library, wp064-closure and log-scan start no Unity and no Player.
# A snapshot that fails (30 = a Deaths Demolition Player is running, 75 busy, …) BLOCKS the stage — never run unguarded.
# A restore that fails (30/31/75, or 32 = out-of-scope change once C-00 decides it) — or exits 0 without verified=1 —
# FAILS the stage and stops the RC even with --diagnostic-continue: "guard restore <code> in stage <name> — tell C-00".
# INT/TERM/HUP: the running guarded stage is stopped (its whole process tree, Player included), the open snapshot is
# restored FIRST, then the RC fails (128+signal, or the restore's code). A stage without an RC guard is let finish (its
# own guard must run), then the RC fails.
# Env (Codex-audit M-10, C-00 30.09): before any stage every variable the C# of $REPO reads is unset except HOME
# (dd_scrub_code_env in dd-env.sh); cleared names → timeline, environment.txt, rc-report.json envScrubbed; git grep fails → 3.
set -u

HERE="${0:A:h}"
REPO="${DD_REPO:-${HERE:h:h}}"
source "$REPO/tools/dd-env.sh"

OUT=""
ROUNDS=12
SEED=61
PERF_RUNS=3
WINDOW=60
DIAGNOSTIC_CONTINUE=0
while [ $# -gt 0 ]; do
  case "$1" in
    --rounds) ROUNDS="$2"; shift 2 ;;
    --seed) SEED="$2"; shift 2 ;;
    --perf-runs) PERF_RUNS="$2"; shift 2 ;;
    --window) WINDOW="$2"; shift 2 ;;
    --diagnostic-continue) DIAGNOSTIC_CONTINUE=1; shift ;;
    --*) echo "unknown option: $1" >&2; exit 64 ;;
    *) [ -z "$OUT" ] || { echo "unexpected argument: $1" >&2; exit 64; }; OUT="$1"; shift ;;
  esac
done
[ "$ROUNDS" -ge 12 ] || { echo "WP-061 RC requires --rounds >= 12" >&2; exit 64; }
# M-09 (C-00 30.09): the closure gate needs >= 3 complete perf windows; fewer is a usage error, not a late FAIL.
[[ "$PERF_RUNS" == <-> ]] && [ "$PERF_RUNS" -ge 3 ] || { echo "WP-061 RC requires --perf-runs >= 3" >&2; exit 64; }
[ "$SEED" = "61" ] || [ "$SEED" = "26" ] || { echo "DEC-091 RC requires --seed 61 or 26" >&2; exit 64; }
dd_guard_canon_check || exit 64   # G4 D4: DD_GUARD_ROOT set / canonical driver guard missing — nothing created
[ -n "$OUT" ] || OUT="$DD_BUILD_ROOT/rc-$(dd_utc)"
if [ -d "$OUT" ] && [ -n "$(ls -A "$OUT" 2>/dev/null)" ]; then
  echo "refusing non-empty RC output directory: $OUT" >&2
  exit 73
fi
mkdir -p "$OUT/stages"
TIMELINE="$OUT/timeline.txt"
START_EPOCH=$(date +%s)
FAILED=0
FAILED_STAGE=""

log() { echo "$(date -u +%FT%TZ) $*" | tee -a "$TIMELINE"; }

# M11 / G4 D4 (C-00 01.10): the one canonical guard — the driver copy (dd-env.sh DD_GUARD_CANON), never this checkout's.
DD_GUARD="$DD_GUARD_CANON"
GUARD_DIR=""; GUARD_SCOPE=""; GUARD_STAGE=""; GUARD_CODE=0
CURRENT_STAGE=""; STAGE_PID=""; SIG_PENDING=""

# Signals: armed = stop + restore + fail (on_signal); deferred = remembered while a guard call runs, handled right after.
arm_signals() {
  trap 'on_signal INT' INT; trap 'on_signal TERM' TERM; trap 'on_signal HUP' HUP
  [ -z "$SIG_PENDING" ] || on_signal "$SIG_PENDING"
}
defer_signals() {
  trap 'SIG_PENDING=${SIG_PENDING:-INT}' INT; trap 'SIG_PENDING=${SIG_PENDING:-TERM}' TERM; trap 'SIG_PENDING=${SIG_PENDING:-HUP}' HUP
}
tree_pids() { local c; for c in $(pgrep -P "$1" 2>/dev/null); do tree_pids "$c"; done; echo "$1"; }
# stop_tree <pid>: TERM to the pid and every descendant (a Player under player-run.sh too), ≤ 60 s, then KILL.
stop_tree() {
  local pids=($(tree_pids "$1")) p alive i=0
  kill -TERM $pids 2>/dev/null
  while [ $i -lt 60 ]; do
    alive=0; for p in $pids; do kill -0 "$p" 2>/dev/null && alive=1; done
    [ $alive -eq 0 ] && break
    sleep 1; i=$((i+1))
  done
  kill -KILL $pids 2>/dev/null
  wait "$1" 2>/dev/null
}
# run_bg <log> <command…>: the stage's work runs in the background so a signal interrupts the wait at once.
run_bg() {
  local out="$1"; shift
  "$@" > "$out" 2>&1 &
  STAGE_PID=$!
  wait "$STAGE_PID"
  local code=$?
  STAGE_PID=""
  return $code
}

# guard_open <stage> <scope>: snapshot into stages/<stage>/ug. 0, else GUARD_CODE = the snapshot's exit (stage not run).
guard_open() {
  local name="$1" scope="$2" dir="$OUT/stages/$1"
  mkdir -p "$dir"
  defer_signals
  # called from this shell (not a subshell): the player-scope snapshot binds the 'player' slot to this RC's pid
  "$DD_GUARD" snapshot "$dir/ug" --scope "$scope" > "$dir/guard-snapshot.out" 2>&1
  GUARD_CODE=$?
  if [ "$GUARD_CODE" -eq 0 ]; then
    GUARD_DIR="$dir/ug"; GUARD_SCOPE="$scope"; GUARD_STAGE="$name"
    log "GUARD snapshot $name scope=$scope"
  fi
  arm_signals
  [ "$GUARD_CODE" -eq 0 ]
}
# guard_close: restore the open snapshot (caller defers signals). 0, else the restore's exit — a 0 without the guard's
# "restore scope=<s> … verified=1" line counts as 31 (as main-ci).
guard_close() {
  [ -n "$GUARD_DIR" ] || return 0
  local ug="$GUARD_DIR" code
  "$DD_GUARD" restore "$ug" --scope "$GUARD_SCOPE" > "${ug:h}/guard-restore.out" 2>&1
  code=$?
  [ "$code" -eq 0 ] && ! grep -q "restore scope=$GUARD_SCOPE .*verified=1" "${ug:h}/guard-restore.out" && code=31
  GUARD_DIR=""
  if [ "$code" -ne 0 ]; then
    echo "$code" > "${ug:h}/guard-restore-exit.txt"
    log "FAIL guard restore $code in stage $GUARD_STAGE — tell C-00 (${ug:h}/guard-restore.out)"
    echo "guard restore $code in stage $GUARD_STAGE — tell C-00" >&2
    return "$code"
  fi
  [ -s "$ug/out-of-scope-changed.txt" ] && log "WARNING guard: the other area changed during $GUARD_STAGE and was NOT restored (someone else / the user) — $ug/out-of-scope-changed.txt"
  log "GUARD restore $GUARD_STAGE scope=$GUARD_SCOPE verified"
  return 0
}
# A failed restore fails the stage (its exit.txt keeps a non-zero code) and always stops the RC.
guard_failed() {
  local name="$1" code="$2" dir="$OUT/stages/$1"
  { [ ! -f "$dir/exit.txt" ] || [ "$(cat "$dir/exit.txt")" = 0 ]; } && echo "$code" > "$dir/exit.txt"
  if [ "$FAILED" -eq 0 ]; then FAILED="$code"; FAILED_STAGE="$name"; fi
  exit "$FAILED"
}
on_signal() {
  local sig="$1" num=15 code
  case "$sig" in INT) num=2 ;; HUP) num=1 ;; esac
  defer_signals
  code=$((128 + num))
  log "SIGNAL $sig in stage ${CURRENT_STAGE:--}"
  if [ -n "$STAGE_PID" ]; then
    if [ -n "$GUARD_DIR" ]; then
      log "stopping $CURRENT_STAGE (pid $STAGE_PID and its descendants) before the guard restore"
      stop_tree "$STAGE_PID"
    else
      log "letting $CURRENT_STAGE finish (no RC guard: it owns its user-data handling)"
      while kill -0 "$STAGE_PID" 2>/dev/null; do wait "$STAGE_PID" 2>/dev/null; done
    fi
    STAGE_PID=""
  fi
  guard_close || code=$?
  if [ -n "$CURRENT_STAGE" ]; then
    mkdir -p "$OUT/stages/$CURRENT_STAGE"; echo "$code" > "$OUT/stages/$CURRENT_STAGE/exit.txt"
    log "END $CURRENT_STAGE exit=$code (signal $sig)"
  fi
  if [ "$FAILED" -eq 0 ]; then FAILED="$code"; FAILED_STAGE="${CURRENT_STAGE:-signal-$sig}"; fi
  exit "$code"
}

stage() {
  local name="$1"; shift
  local dir="$OUT/stages/$name"
  mkdir -p "$dir"
  printf '%q ' "$@" > "$dir/command.txt"; echo >> "$dir/command.txt"
  log "START $name"
  CURRENT_STAGE="$name"
  run_bg "$dir/stdout.log" "$@"
  local code=$?
  echo "$code" > "$dir/exit.txt"
  log "END $name exit=$code"
  CURRENT_STAGE=""
  if [ "$code" -ne 0 ]; then
    if [ "$FAILED" -eq 0 ]; then FAILED="$code"; FAILED_STAGE="$name"; fi
    [ "$DIAGNOSTIC_CONTINUE" -eq 1 ] && return 0
    return "$code"
  fi
  return 0
}

blocked_stage() {
  local name="$1" reason="$2" code="${3:-1}"
  local dir="$OUT/stages/$name"
  mkdir -p "$dir"
  echo "$reason" > "$dir/stdout.log"
  echo "not run: $reason" > "$dir/command.txt"
  echo "$code" > "$dir/exit.txt"
  log "BLOCKED $name: $reason"
  if [ "$FAILED" -eq 0 ]; then FAILED="$code"; FAILED_STAGE="$name"; fi
  [ "$DIAGNOSTIC_CONTINUE" -eq 1 ] && return 0
  return "$code"
}

# guarded_stage <editor|player> <name> <command…>: snapshot → stage → restore (see the header).
guarded_stage() {
  local scope="$1" name="$2"; shift 2
  if ! guard_open "$name" "$scope"; then
    blocked_stage "$name" "guard snapshot $GUARD_CODE (scope $scope) — stage not run, never unguarded$([ "$GUARD_CODE" -eq 30 ] && echo ': a Deaths Demolition Player is running')" "$GUARD_CODE"
    return $?
  fi
  CURRENT_STAGE="$name"
  stage "$name" "$@"
  local code=$? g
  CURRENT_STAGE="$name"
  defer_signals; guard_close; g=$?; CURRENT_STAGE=""
  [ "$g" -eq 0 ] || guard_failed "$name" "$g"
  arm_signals
  return "$code"
}

PROJECT_LOCK="/tmp/dd-rc-project-$(printf '%s' "$DD_PROJECT" | cksum | awk '{print $1}').lock"
if ! mkdir "$PROJECT_LOCK" 2>/dev/null; then
  echo "another RC owns this project path: $PROJECT_LOCK" >&2
  exit 75
fi
echo "$$ $DD_PROJECT" > "$PROJECT_LOCK/owner.txt"

finalize() {
  DD_RC_M3_REPORT_ONLY=0 python3 "$HERE/rc_aggregate.py" "$OUT" --repo "$REPO" --rounds "$ROUNDS" --start-epoch "$START_EPOCH" --perf-runs "$PERF_RUNS" --window "$WINDOW" --failed-stage "$FAILED_STAGE" --env-scrubbed "$DD_ENV_SCRUBBED"
  local agg=$?
  [ "$FAILED" -ne 0 ] && return "$FAILED"
  return "$agg"
}
cleanup() { rm -f "$PROJECT_LOCK/owner.txt" 2>/dev/null || true; rmdir "$PROJECT_LOCK" 2>/dev/null || true; }
trap 'finalize >/dev/null 2>&1 || true; cleanup' EXIT
arm_signals

# Codex-audit M-10 (C-00 30.09): before any stage, unset every env variable the C# of $REPO reads (dd_scrub_code_env,
# dd-env.sh; allow-list HOME) — the RC runs what a user gets, never a caller's DD_* diagnostic switch. The cleared names go
# into the timeline, environment.txt and rc-report.json envScrubbed. git grep failure → 3 (preflight), nothing run.
DD_TIMELINE="$TIMELINE" dd_scrub_code_env "$REPO" || { FAILED=3; FAILED_STAGE="preflight"; exit "$FAILED"; }

{
  echo "repo=$REPO"
  echo "head=$(git -C "$REPO" rev-parse HEAD 2>/dev/null)"
  echo "branch=$(git -C "$REPO" branch --show-current 2>/dev/null)"
  echo "unity=$DD_UNITY"
  echo "project=$DD_PROJECT"
  echo "rounds=$ROUNDS seed=$SEED perfRuns=$PERF_RUNS window=$WINDOW"
  echo "diagnosticContinue=$DIAGNOSTIC_CONTINUE"
  echo "envScrubbed=$DD_ENV_SCRUBBED"
  echo "startEpoch=$START_EPOCH"
  uname -a
  "$REPO/tools/slot.sh" status
} > "$OUT/environment.txt" 2>&1

[ -x "$DD_UNITY" ] || { log "Unity missing: $DD_UNITY"; FAILED=66; FAILED_STAGE="preflight"; exit "$FAILED"; }
[ -x "$REPO/tools/test.sh" ] && [ -x "$REPO/tools/build.sh" ] && [ -x "$REPO/tools/qa/regression.sh" ] \
  && [ -x "$DD_GUARD" ] && [ -x "$REPO/tools/player-run.sh" ] || {
  log "required QA tools missing or not executable"; FAILED=66; FAILED_STAGE="preflight"; exit "$FAILED";
}

guarded_stage editor full-editmode "$REPO/tools/test.sh" editmode --out "$OUT/full-editmode" --timeout-min 30 || exit "$FAILED"
guarded_stage editor full-playmode "$REPO/tools/test.sh" playmode --out "$OUT/full-playmode" --timeout-min 60 || exit "$FAILED"
# Not RC-guarded on purpose: smoke.sh (build.sh --smoke) snapshots/restores the user data itself around its two Players.
stage release-smoke "$REPO/tools/build.sh" release --smoke --out "$OUT/release" || exit "$FAILED"
# MUST post-build receipt: capture immediately after release; wp064-closure verifies commit/Assets/clean/LFS/ArtifactDB.
stage release-library python3 "$HERE/library_provenance.py" "$REPO" "$OUT/release/library-provenance.json" || exit "$FAILED"

# Full suites above already include the QA EditMode and Armor PlayMode tests. Hand those XML files to
# the M3 aggregate so regression.sh can avoid repeating the same tests while still enforcing them.
export DD_EXTERNAL_EDITMODE_XML="$OUT/full-editmode/editmode.xml"
export DD_EXTERNAL_PLAYMODE_XML="$OUT/full-playmode/playmode.xml"
export DD_QA_PACKAGE="WP-061"
export DD_REQUIRE_ULTRA_EVIDENCE="1"
export DD_READABILITY_ROUNDS="1,4,8,10,12"
export DD_BOT_EXTRA="-ddQaBotFrames $OUT/m3/bot/tier-frames -ddUiCapture hud -ddUiCaptureOut $OUT/m3/bot/ui -ddUiCaptureRounds 1,2,4,8,12"
export DD_PLAYER_TIMEOUT="${DD_PLAYER_TIMEOUT:-7200}"
# Scope the DEC-091 adapter to this invocation. The raw M3 pass/original exit remain in qa-report.json;
# only bot.rounds is report-only, all other gates (including optional probe policy) are unchanged.
guarded_stage player m3-regression env DD_RC_M3_REPORT_ONLY=1 DD_AGGREGATE="$HERE/rc_aggregate.py" "$REPO/tools/qa/regression.sh" "$OUT/m3" --rounds "$ROUNDS" --seed "$SEED" --perf-runs "$PERF_RUNS" --window "$WINDOW" --skip-tests --steps build,spawnmix,bot,perf,readability || exit "$FAILED"
# DEC-091 (user 2026-09-24): the soak MUST is stability — seeds 61 and 26, ROUNDS each, the invulnerable-by-design bot
# (-ddQaBotStability; rounds, log and memory checked by soak_gate.py --stability inside the closure gate). The real-bot run
# of m3-regression and four more are a report (>= 5 runs, reached-round distribution not gated): a real-bot death does not stop the RC.
DEV_APP="$OUT/m3/build-dev/$DD_APP_NAME"            # names from dd-env.sh (single source; fixtures give their own)
DEV_BIN="$DEV_APP/Contents/MacOS/$DD_PRODUCT"
EXECUTION_COMMIT="$(git -C "$REPO" rev-parse HEAD)"
soak_run() { python3 "$HERE/wp064_run.py" soak --bin "$DEV_BIN" --out "$OUT/m3/$1" --rounds "$ROUNDS" --seed "$2" --commit "$EXECUTION_COMMIT" \
  --timeout "$DD_PLAYER_TIMEOUT" --preflight-attempts "${DD_PREFLIGHT_WINDOWS:-30}" --preflight-wait 60 "${@:3}"; }
guarded_stage player soak-stability-61 soak_run soak-61 61 --stability || exit "$FAILED"
guarded_stage player soak-stability-26 soak_run soak-26 26 --stability || exit "$FAILED"
for i in 2 3 4 5; do
  seed=61; [ $((i % 2)) -eq 1 ] && seed=26
  # a report run's Player result is not gated (DEC-091), its user-data guard is: blocked → RC fails, restore → RC stops
  if ! guard_open "real-$i" player; then
    blocked_stage "real-$i" "guard snapshot $GUARD_CODE (scope player) — report run not started, never unguarded" "$GUARD_CODE" || exit "$FAILED"
    continue
  fi
  CURRENT_STAGE="real-$i"
  run_bg "$OUT/m3/real-$i.out" soak_run "real-$i" "$seed"; X=$?
  log "real-bot report run real-$i seed=$seed exit=$X (report only, DEC-091)"
  defer_signals; guard_close; G=$?; CURRENT_STAGE=""
  [ "$G" -eq 0 ] || guard_failed "real-$i" "$G"
  arm_signals
done
stage wp064-closure python3 "$HERE/wp064_gate.py" "$OUT/m3" --release "$OUT/release" --commit "$EXECUTION_COMMIT" \
  --soak-dirs soak-61,soak-26 --seeds 61,26 --report-dirs bot,real-2,real-3,real-4,real-5 --rounds "$ROUNDS" --window "$WINDOW" --repo "$REPO" || exit "$FAILED"

if [ -d "$DEV_APP" ]; then
  guarded_stage player economy-acceptance "$REPO/tools/qa/economy-acceptance.sh" "$DEV_APP" "$OUT/economy-acceptance" || exit "$FAILED"
  guarded_stage player window-modes "$REPO/tools/qa/window-modes.sh" "$DEV_APP" "$OUT/window-modes" || exit "$FAILED"
else
  blocked_stage economy-acceptance "development app missing: $DEV_APP" || exit "$FAILED"
  blocked_stage window-modes "development app missing: $DEV_APP" || exit "$FAILED"
fi
stage log-scan python3 "$HERE/log_scan.py" "$OUT" --since "$START_EPOCH" || exit "$FAILED"

log "RC chain complete"
trap - EXIT INT TERM HUP
finalize
CODE=$?
cleanup
exit "$CODE"
