#!/bin/zsh
# WP-043C · tools/compile-check.sh [ref] [--ci-dir DIR] [--out DIR]
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
# Exit: 0 compiles · 2 compile errors (first ones printed) · 3 checkout failed / check did not run · 75 busy · 124 stalled/timed out · 64 usage
set -u
source "${0:A:h}/dd-env.sh"
REF=""; CI_DIR="$HOME/Desktop/KADRO/context/projects/deaths-demolition/wp-079/ci-worktree/deaths-demolition"; OUT=""
while [ $# -gt 0 ]; do case "$1" in
  --ci-dir) CI_DIR="$2"; shift ;; --out) OUT="$2"; shift ;;
  -*) echo "usage: compile-check.sh [ref] [--ci-dir DIR] [--out DIR]" >&2; exit 64 ;;
  *) REF="$1" ;; esac; shift; done
# Compile and CI serialize only the same physical checkout; never reclaim an unknown lease.
CI_LEASE_HELD=0; CI_LEASE_READY=0
ci_lease_release() {
  [ "$CI_LEASE_HELD" -eq 1 ] || return 0
  [ -d "$CI_LEASE" ] && [ ! -L "$CI_LEASE" ] || return 3
  if [ "$CI_LEASE_READY" -eq 1 ]; then
    [ -f "$CI_LEASE/owner-$CI_LEASE_TOKEN" ] &&
      [ "$(cat "$CI_LEASE/owner-$CI_LEASE_TOKEN")" = "$CI_LEASE_OWNER" ] || return 3
  fi
  rm -f -- "$CI_LEASE/owner-$CI_LEASE_TOKEN" || return 3
  rmdir -- "$CI_LEASE" || return 3
  CI_LEASE_HELD=0
}
ci_lease_acquire() {
  CI_DIR=$(python3 - "$CI_DIR" <<'PY_CANON'
from pathlib import Path
import os, sys
try:
    requested = Path(sys.argv[1]).absolute()
    if os.path.lexists(requested):
        target = requested.resolve(strict=True)
        if not target.is_dir(): raise ValueError("checkout is not a directory")
    else:
        target = requested.parent.resolve(strict=True) / requested.name
        if not target.parent.is_dir(): raise ValueError("checkout parent is not a directory")
    if "\n" in str(target): raise ValueError("newline in checkout path")
    print(target)
except (OSError, RuntimeError, ValueError) as error:
    print("cannot resolve CI checkout: " + str(error), file=sys.stderr)
    sys.exit(3)
PY_CANON
  ) || return 3
  local key=$(print -rn -- "$CI_DIR" | shasum -a 256 | cut -d' ' -f1)
  [ "${#key}" -eq 64 ] || return 3
  CI_LEASE="${CI_DIR:h}/.dd-ci-$key.lease"
  CI_LEASE_TOKEN=$(python3 -c 'import uuid; print(uuid.uuid4())') || return 3
  local started=$(ps -o lstart= -p $$ | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
  [ -n "$started" ] || return 3
  CI_LEASE_OWNER="pid=$$"$'\n'"start=$started"$'\n'"token=$CI_LEASE_TOKEN"
  mkdir -m 700 -- "$CI_LEASE" 2>/dev/null || { echo "CI checkout lease busy: $CI_DIR" >&2; return 75; }
  CI_LEASE_HELD=1
  print -r -- "$CI_LEASE_OWNER" > "$CI_LEASE/owner-$CI_LEASE_TOKEN" || return 3
  CI_LEASE_READY=1
}
# Resolve Unity's projectPath too, so manual launches through an alias remain busy.
ci_project_open() {
  python3 - "$CI_DIR/prototype/DeathsDemolition_PlayableConcept" <<'PY_PROCESS'
from pathlib import Path
import re, subprocess, sys
try:
    target = Path(sys.argv[1]).resolve(strict=False)
    rows = subprocess.check_output(["ps", "-axo", "args="], text=True).splitlines()
    for command in rows:
        executable = command.split(" -", 1)[0].strip()
        if not executable.endswith("/Unity.app/Contents/MacOS/Unity"): continue
        match = re.search(r"(?:^|\s)-projectPath\s+(.*?)(?=\s+-[A-Za-z]|$)", command)
        if not match: continue
        raw = match.group(1).strip()
        if len(raw) >= 2 and raw[0] == raw[-1] and raw[0] in "\"'": raw = raw[1:-1]
        if Path(raw).resolve(strict=True) == target: sys.exit(0)
except (OSError, RuntimeError, ValueError):
    sys.exit(2)
sys.exit(1)
PY_PROCESS
}
ci_disk_ready() {
  local free=$(df -k "$CI_DIR" | awk 'NR==2{print $4}')
  if [[ "$free" != <-> ]] || [ "$free" -lt 15728640 ]; then
    dd_log "compile-check: disk below 15 GiB or unavailable (freeKiB=$free) — not started"; return 75
  fi
  dd_log "compile-check: disk freeKiB=$free minimumKiB=15728640"
}
CI_SLOT_HELD=0; CI_CLEANING=0; CI_SIGNAL=""
ci_cleanup() {
  local original=$1 slot_exit=0 lease_exit=0
  trap - EXIT; CI_CLEANING=1
  if [ -n "$DD_UNITY_PID" ] && ! dd_stop_child "$DD_UNITY_PID" "${DD_CI_CHILD_GRACE:-120}"; then
    echo "compile-check ${CI_SIGNAL:-exit}: Unity child $DD_CHILD_ALIVE still alive, tell C-00 — slot $DD_SLOT_TAG and lease $CI_LEASE kept" >&2
    exit "$original"
  fi
  if [ "$CI_SLOT_HELD" -eq 1 ]; then dd_slot_release; slot_exit=$?; fi
  ci_lease_release; lease_exit=$?
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
ci_lease_acquire || exit $?
SHA=$(git -C "$DD_REPO" rev-parse --verify "${REF:-main}^{commit}" 2>/dev/null) || { echo "unknown ref: ${REF:-main}" >&2; exit 64; }
: "${OUT:=$HOME/Desktop/KADRO/context/projects/deaths-demolition/ci/compile-${SHA:0:10}-$(dd_utc)}"
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
dd_unity_watched "$LOG" 30 "$DD_UNITY" -batchmode -nographics -quit -forgetProjectPath -projectPath "$PROJ" -logFile "$LOG"
X=$?
ERRS=$(grep -o 'Assets/[^(]*([0-9,]*): error CS[0-9]*: [^\r]*' "$LOG" 2>/dev/null | sort -u)
N=$(printf '%s' "$ERRS" | grep -c . ); N=${N:-0}
dd_log "compile-check ${SHA:0:12}: unity exit=$X outcome=$DD_UNITY_OUTCOME errorsCS=$N seconds=$(( $(date +%s) - T0 ))"
[ "$DD_UNITY_OUTCOME" = killed ] || [ "$DD_UNITY_OUTCOME" = stalled ] && exit 124
# WP-043E: no green without evidence that Unity really compiled.
if grep -q "Multiple Unity instances cannot open the same project" "$LOG" 2>/dev/null; then
  echo "RESULT: ${SHA:0:12} NOT checked — another Unity had the project open (unity exit $X) — $LOG"; exit 75; fi
TRACE=$(grep -c "CompileScripts:\|Asset Pipeline Refresh" "$LOG" 2>/dev/null); TRACE=${TRACE:-0}
if [ "$N" -eq 0 ] && { [ "$X" -ne 0 ] || [ "$TRACE" -eq 0 ]; }; then
  echo "RESULT: ${SHA:0:12} NOT checked — unity exit $X, compile trace lines $TRACE, no CS errors: the editor did not compile — $LOG"; exit 3; fi
if [ "$N" -gt 0 ]; then printf '%s\n' "$ERRS" | head -8 | tee -a "$DD_TIMELINE"; echo "RESULT: ${SHA:0:12} does NOT compile ($N errors) — $LOG"; exit 2; fi
echo "RESULT: ${SHA:0:12} compiles (errorsCS 0) — $LOG"; exit 0
