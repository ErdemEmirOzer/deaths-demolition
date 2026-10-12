#!/bin/zsh
# WP-043C · tools/compile-check.sh [ref] [--ci-dir DIR] [--out DIR] [--player dev|release|release-probe]
#           tools/compile-check.sh --lease-status [--ci-dir DIR]
#
# Fast "does it compile" gate for the end of a multi-session integration batch (C-00 25.09: two commits that
# compile on their own branches broke main together — 6d69dafd × 47bf74c4). Checks out <ref> (default: main)
# detached in the main-ci checkout (warm Library, so only scripts recompile), opens the project in batchmode
# with -quit under a Unity slot and the stall watchdog, and counts unique "error CS" lines. No tests run.
# Refuses (75) while the same canonical checkout is leased or has tracked changes; unrelated checkouts may run concurrently.
# WP-043E (S-07 measured a false green): "compiles" now needs Unity exit 0 AND a compile trace in the log (CompileScripts /
# Asset Pipeline Refresh); an abort such as "Multiple Unity instances cannot open the same project" (exit 134) is busy (75),
# any other non-zero exit without CS errors is "did not run" (3). It refuses up front while any Unity has the checkout's
# project open. Compile and main-ci share an owner lease per canonical checkout.
# Signals (Codex-audit Y1 / G4 Q10, C-00 30.09): INT/TERM/HUP exit 130/143/129 through the one EXIT cleanup; a running Unity
# (our own child) gets TERM and ≤ DD_CI_CHILD_GRACE s (default 120); if it is still alive the slot and lease stay held: tell C-00.
# Lease (Codex-audit M-05, C-00 30.09): dd_ci_lease_* in dd-env.sh. A live owner = busy 75; a stale (dead / reused pid),
# malformed or symlinked lease is never reclaimed = 3. --lease-status is the read-only diagnosis (lease path, owner pid,
# recorded vs current start, alive / same process, `verdict:` FREE · HELD-LIVE · STALE-DEAD-OWNER · STALE-PID-REUSED ·
# MALFORMED · SYMLINK): deletes nothing, takes no slot, runs no git; exit 0 FREE · 75 HELD-LIVE · 3 stale/malformed/symlink.
# Quarantine of a stale lease is C-00's (tools/README.md).
# --player <mode> (WP-099 R4, C-00 12.10): after the Editor compile a second Unity session runs DdBuild.CompilePlayerScripts
# (log compile-player-<mode>.log, Editor and player errors never mixed). Its verdict is DdBuild's line, and only the CS lines of
# the real pass count (cc_player_verdict below): the first player compile after the script set changed runs on the Library's
# out-of-date build graph and logs errors Unity then re-runs away. --player <mode> --player-log <log> re-reads a log, no Unity.
# Exit: 0 compiles · 2 compile errors (first ones printed) · 3 checkout failed / check did not run / lease stale or malformed / player verdict contradictory
#   · 75 busy · 124 stalled/timed out · 64 usage · 130/143/129 INT/TERM/HUP
set -u
source "${0:A:h}/dd-env.sh"
REF=""; CI_DIR="$DD_KPROJ/wp-079/ci-worktree/deaths-demolition"; OUT=""; LEASE_STATUS=0; PLAYER=""; PLAYER_LOG=""
CC_USAGE="usage: compile-check.sh [ref] [--ci-dir DIR] [--out DIR] [--player dev|release|release-probe] | compile-check.sh --lease-status [--ci-dir DIR] | compile-check.sh --player <mode> --player-log <log>"
while [ $# -gt 0 ]; do case "$1" in
  --ci-dir) [ $# -ge 2 ] || { echo "$CC_USAGE" >&2; exit 64; }; CI_DIR="$2"; shift ;;
  --out) [ $# -ge 2 ] || { echo "$CC_USAGE" >&2; exit 64; }; OUT="$2"; shift ;;
  # WP-099 R4 (C-00 12.10): also compile the PLAYER scripts of a mode (DdBuild.CompilePlayerScripts) — the Editor always
  # defines UNITY_EDITOR, so only a player compile sees code a release / release-probe player lacks
  --player) [ $# -ge 2 ] && [[ "$2" == (dev|release|release-probe) ]] || { echo "$CC_USAGE" >&2; exit 64; }; PLAYER="$2"; shift ;;
  --player-log) [ $# -ge 2 ] || { echo "$CC_USAGE" >&2; exit 64; }; PLAYER_LOG="$2"; shift ;;
  --lease-status) LEASE_STATUS=1 ;;
  -*) echo "$CC_USAGE" >&2; exit 64 ;;
  *) REF="$1" ;; esac; shift; done
# M-05: read-only lease diagnosis — before any trap, slot, git or lease; nothing is written.
if [ "$LEASE_STATUS" -eq 1 ]; then
  [ -z "$REF" ] && [ -z "$OUT" ] && [ -z "$PLAYER" ] && [ -z "$PLAYER_LOG" ] || { echo "$CC_USAGE" >&2; exit 64; }
  dd_ci_lease_status "$CI_DIR"; exit $?
fi
# C-00 12.10 · the verdict of a player compile log (DdBuild.CompilePlayerScripts). The first player compile after the script set
# changed runs on the Library's out-of-date build graph (the player Bee DAG): a file the checkout added is missing from it
# (CS0103 …), one it deleted is still listed (CS2001 "could not be found"); Unity then logs "Tundra requires additional run",
# rebuilds the graph and compiles for real (main 250b40aa: 90 such lines, then "compiled"; a second Unity session does not
# avoid it — the graph lives in Library). So the verdict is DdBuild's (its assemblies), the errors that count are the CS lines
# after the LAST "Tundra requires additional run" (the real pass; a real error repeats there), and a "compiled" with errors in
# that pass, or with a non-zero Unity exit, is no green (3). Prints the errors / the DdBuild line; returns 0 · 2 · 3.
cc_player_verdict() {   # <mode> <log> <unity exit> <label>
  local mode=$1 log=$2 ux=$3 label=$4 pl last stale ferrs final
  pl=$(grep -ao "\[DdBuild\] player scripts mode=$mode .*" "$log" 2>/dev/null | tr -d '\r' | tail -1)
  [ -n "$pl" ] || { echo "RESULT: $label player $mode NOT checked — no \"[DdBuild] player scripts\" line (a ref before DdBuild.CompilePlayerScripts?) — $log"; return 3; }
  last=$(grep -an "Tundra requires additional run" "$log" 2>/dev/null | tail -1 | cut -d: -f1); last=${last:-0}
  stale=$(awk -v t="$last" 'NR <= t+0 && /error CS[0-9]+:/' "$log" | grep -c .)
  ferrs=$(awk -v t="$last" 'NR > t+0 && /error CS[0-9]+:/' "$log" | tr -d '\r' | sed 's/^[[:space:]]*//' | sort -u)
  final=$(printf '%s' "$ferrs" | grep -c .)
  if [[ "$pl" == *"— compiled"* ]]; then
    if [ "$final" -gt 0 ] || [ "$ux" != 0 ]; then
      [ "$final" -gt 0 ] && printf '%s\n' "$ferrs" | head -8
      echo "RESULT: $label player $mode NOT checked — DdBuild says compiled, but unity exit $ux and $final CS errors in the final pass: contradictory, tell C-00 — $log"; return 3
    fi
    echo "$pl"
    [ "$stale" -gt 0 ] && echo "player $mode: $stale CS lines of the first pass on the out-of-date build graph ignored (Unity re-ran it, DdBuild compiled)"
    return 0
  fi
  [ -n "$ferrs" ] || ferrs=$(grep -a 'error CS[0-9]*:' "$log" | tr -d '\r' | sed 's/^[[:space:]]*//' | sort -u)
  [ -n "$ferrs" ] && printf '%s\n' "$ferrs" | head -8
  echo "RESULT: $label does NOT compile (player $mode) ($(printf '%s' "$ferrs" | grep -c .) errors; $pl) — $log"; return 2
}
if [ -n "$PLAYER_LOG" ]; then   # read-only: re-read a player compile log (no Unity, no slot, nothing written)
  [ -n "$PLAYER" ] && [ -z "$REF" ] && [ -z "$OUT" ] && [ -f "$PLAYER_LOG" ] || { echo "$CC_USAGE" >&2; exit 64; }
  cc_player_verdict "$PLAYER" "$PLAYER_LOG" 0 log; V=$?
  [ "$V" -eq 0 ] && echo "RESULT: log player $PLAYER compiles — $PLAYER_LOG"
  exit $V
fi
# Compile and CI serialize only the same physical checkout; never reclaim an unknown lease. Codex-audit M-05 (C-00 30.09):
# one source — dd_ci_lease_acquire / dd_ci_lease_release / dd_ci_lease_status (dd-env.sh).
# Codex-audit M-06 (C-00 30.09): one source — dd_project_open (dd-env.sh): case-insensitive -projectPath (Unity Hub),
# an unresolvable/removed foreign path is skipped + logged instead of failing the check. 0 open · 1 not open · 2 no ps.
ci_project_open() { dd_project_open "$CI_DIR/prototype/DeathsDemolition_PlayableConcept"; }
ci_disk_ready() { dd_disk_gate "$CI_DIR"; }   # C-00 30.09: the shared gate (disk ≥ 15 GiB + the swap / free-memory rule), dd-env.sh
CI_SLOT_HELD=0; CI_CLEANING=0; CI_SIGNAL=""
ci_cleanup() {
  local original=$1 slot_exit=0 lease_exit=0
  trap - EXIT; CI_CLEANING=1
  if [ -n "$DD_UNITY_PID" ] && ! dd_stop_child "$DD_UNITY_PID" "${DD_CI_CHILD_GRACE:-120}"; then
    echo "compile-check ${CI_SIGNAL:-exit}: Unity child $DD_CHILD_ALIVE still alive, tell C-00 — slot $DD_SLOT_TAG and lease $DD_CI_LEASE kept" >&2
    exit "$original"
  fi
  if [ "$CI_SLOT_HELD" -eq 1 ]; then dd_slot_release; slot_exit=$?; fi
  dd_ci_lease_release; lease_exit=$?
  [ "$slot_exit" -eq 0 ] && [ "$lease_exit" -eq 0 ] || echo "compile cleanup failed: slot=$slot_exit lease=$lease_exit (original=$original)" >&2
  [ "$original" -eq 0 ] || exit "$original"
  [ "$slot_exit" -eq 0 ] || exit "$slot_exit"
  exit "$lease_exit"
}
# zsh function-local EXIT traps run on function return; install this at script scope.
trap 'ci_cleanup $?' EXIT
# zsh skips EXIT on an untrapped INT/TERM/HUP: record it, exit 128+n (cleanup runs once; a signal during cleanup is ignored).
ci_signal() { [ "$CI_CLEANING" -eq 0 ] || return 0; CI_SIGNAL=SIG$1; echo "compile-check: SIG$1 — exit $2 through cleanup" >&2; exit $2; }
trap 'ci_signal INT 130' INT; trap 'ci_signal TERM 143' TERM; trap 'ci_signal HUP 129' HUP
dd_ci_lease_acquire "$CI_DIR" || exit $?
CI_DIR=$DD_CI_CHECKOUT
SHA=$(git -C "$DD_REPO" rev-parse --verify "${REF:-main}^{commit}" 2>/dev/null) || { echo "unknown ref: ${REF:-main}" >&2; exit 64; }
: "${OUT:=$DD_KPROJ/ci/compile-${SHA:0:10}-$(dd_utc)}"
mkdir -p "$OUT" || exit 3; DD_TIMELINE="$OUT/timeline.txt"
PROJ="$CI_DIR/prototype/DeathsDemolition_PlayableConcept"
[ -d "$CI_DIR/.git" ] || [ -f "$CI_DIR/.git" ] || { dd_log "compile-check: no checkout at $CI_DIR (run main-ci once)"; exit 3; }
[ "$(git -C "$CI_DIR" symbolic-ref -q HEAD)" != refs/heads/main ] || { dd_log "compile-check: refusing main checkout"; exit 3; }
ci_project_open; OPEN=$?
[ "$OPEN" -eq 1 ] || { dd_log "compile-check: project open or process check unavailable — busy"; exit 75; }
DIRTY=$(git -C "$CI_DIR" status --porcelain --untracked-files=no) || exit 3
[ -z "$DIRTY" ] || { dd_log "compile-check: $CI_DIR has tracked changes — refusing"; exit 75; }
ci_disk_ready || exit $?
git -C "$CI_DIR" checkout -q --detach "$SHA" || { dd_log "compile-check: checkout $SHA failed"; exit 3; }
dd_log "compile-check ${SHA:0:12} in $CI_DIR"
[ -d "$PROJ" ] || { dd_log "compile-check: project missing at $PROJ"; exit 3; }
DD_SLOT_TAG="compile-check-$$"; CI_SLOT_HELD=1
dd_slot_acquire || exit $?
ci_disk_ready || exit $?
LOG="$OUT/compile.log"; T0=$(date +%s)
# 1) the Editor compile (also the whole check without --player): import what the checkout added / removed, compile, quit
dd_unity_watched "$LOG" 30 "$DD_UNITY" -batchmode -nographics -quit -forgetProjectPath -projectPath "$PROJ" -logFile "$LOG"
X=$?
ERRS=$(grep -ao 'Assets/[^(]*([0-9,]*): error CS[0-9]*: .*' "$LOG" 2>/dev/null | tr -d '\r' | sort -u)   # a bracket "not CR" class reads as "not \ or r" in grep: it cut each message at its first r
N=$(printf '%s' "$ERRS" | grep -c . ); N=${N:-0}
dd_log "compile-check ${SHA:0:12}: unity exit=$X outcome=$DD_UNITY_OUTCOME errorsCS=$N seconds=$(( $(date +%s) - T0 ))"
[ "$DD_UNITY_OUTCOME" = killed ] || [ "$DD_UNITY_OUTCOME" = stalled ] && exit 124
# WP-043E: no green without evidence that Unity really compiled.
if grep -q "Multiple Unity instances cannot open the same project" "$LOG" 2>/dev/null; then
  echo "RESULT: ${SHA:0:12} NOT checked — another Unity had the project open (unity exit $X) — $LOG"; exit 75; fi
TRACE=$(grep -c "CompileScripts:\|Asset Pipeline Refresh" "$LOG" 2>/dev/null); TRACE=${TRACE:-0}
if [ "$N" -eq 0 ] && { [ "$X" -ne 0 ] || [ "$TRACE" -eq 0 ]; }; then
  echo "RESULT: ${SHA:0:12} NOT checked — unity exit $X, compile trace lines $TRACE, no CS errors: the editor did not compile — $LOG"; exit 3; fi
if [ "$N" -gt 0 ]; then printf '%s\n' "$ERRS" | head -8 | tee -a "$DD_TIMELINE"; echo "RESULT: ${SHA:0:12} does NOT compile (editor) ($N errors) — $LOG"; exit 2; fi
# 2) --player: the player scripts in a second Unity session, its own log — Editor and player errors never mixed; the verdict
#    and the errors that count come from cc_player_verdict (the out-of-date first pass is not a red, C-00 12.10).
if [ -n "$PLAYER" ]; then
  PLOG="$OUT/compile-player-$PLAYER.log"; T1=$(date +%s)
  dd_unity_watched "$PLOG" 30 "$DD_UNITY" -batchmode -nographics -quit -forgetProjectPath -projectPath "$PROJ" -logFile "$PLOG" \
    -executeMethod DD.Prototype.Core.EditorTools.DdBuild.CompilePlayerScripts -ddMode "$PLAYER" -ddOut "$OUT/player-scripts-$PLAYER"
  PX=$?
  dd_log "compile-check ${SHA:0:12}: player $PLAYER unity exit=$PX outcome=$DD_UNITY_OUTCOME seconds=$(( $(date +%s) - T1 ))"
  [ "$DD_UNITY_OUTCOME" = killed ] || [ "$DD_UNITY_OUTCOME" = stalled ] && exit 124
  if grep -q "Multiple Unity instances cannot open the same project" "$PLOG" 2>/dev/null; then
    echo "RESULT: ${SHA:0:12} player $PLAYER NOT checked — another Unity had the project open (unity exit $PX) — $PLOG"; exit 75; fi
  PV=$(cc_player_verdict "$PLAYER" "$PLOG" "$PX" "${SHA:0:12}"); V=$?
  printf '%s\n' "$PV" | while IFS= read -r l; do case "$l" in
    RESULT:*) echo "$l"; [ -n "$DD_TIMELINE" ] && echo "$(date -u +%FT%TZ) $l" >> "$DD_TIMELINE" ;;
    *) dd_log "compile-check ${SHA:0:12}: $l" ;; esac; done
  [ "$V" -eq 0 ] || exit $V
fi
echo "RESULT: ${SHA:0:12} compiles${PLAYER:+ (editor + player $PLAYER)} (errorsCS 0) — $LOG${PLAYER:+ + $PLOG}"; exit 0
