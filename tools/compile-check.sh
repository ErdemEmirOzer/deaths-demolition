#!/bin/zsh
# WP-043C · tools/compile-check.sh [ref] [--ci-dir DIR] [--out DIR]
#
# Fast "does it compile" gate for the end of a multi-session integration batch (C-00 25.09: two commits that
# compile on their own branches broke main together — 6d69dafd × 47bf74c4). Checks out <ref> (default: main)
# detached in the main-ci checkout (warm Library, so only scripts recompile), opens the project in batchmode
# with -quit under a Unity slot and the stall watchdog, and counts unique "error CS" lines. No tests run.
# Refuses (75) while main-ci is running or when the checkout has tracked changes — it shares that checkout.
# Exit: 0 compiles · 2 compile errors (first ones printed) · 3 checkout failed · 75 busy · 124 stalled/timed out · 64 usage
set -u
source "${0:A:h}/dd-env.sh"
REF=""; CI_DIR="$HOME/Desktop/KADRO/context/projects/deaths-demolition/wp-079/ci-worktree/deaths-demolition"; OUT=""
while [ $# -gt 0 ]; do case "$1" in
  --ci-dir) CI_DIR="$2"; shift ;; --out) OUT="$2"; shift ;;
  -*) echo "usage: compile-check.sh [ref] [--ci-dir DIR] [--out DIR]" >&2; exit 64 ;;
  *) REF="$1" ;; esac; shift; done
SHA=$(git -C "$DD_REPO" rev-parse --verify "${REF:-main}^{commit}" 2>/dev/null) || { echo "unknown ref: ${REF:-main}" >&2; exit 64; }
: "${OUT:=$HOME/Desktop/KADRO/context/projects/deaths-demolition/ci/compile-${SHA:0:10}-$(dd_utc)}"
mkdir -p "$OUT"; DD_TIMELINE="$OUT/timeline.txt"
pgrep -f "tools/main-ci.sh" >/dev/null && { dd_log "compile-check: main-ci is running in $CI_DIR — busy"; exit 75; }
[ -d "$CI_DIR/.git" ] || [ -f "$CI_DIR/.git" ] || { dd_log "compile-check: no checkout at $CI_DIR (run main-ci once)"; exit 3; }
[ -z "$(git -C "$CI_DIR" status --porcelain --untracked-files=no)" ] || { dd_log "compile-check: $CI_DIR has tracked changes — refusing"; exit 75; }
git -C "$CI_DIR" checkout -q --detach "$SHA" || { dd_log "compile-check: checkout $SHA failed"; exit 3; }
dd_log "compile-check ${SHA:0:12} in $CI_DIR"
DD_SLOT_TAG="compile-check-$$"; trap 'dd_slot_release' EXIT; dd_slot_acquire
LOG="$OUT/compile.log"; T0=$(date +%s)
dd_unity_watched "$LOG" 30 "$DD_UNITY" -batchmode -nographics -quit -forgetProjectPath -projectPath "$CI_DIR/prototype/DeathsDemolition_PlayableConcept" -logFile "$LOG"
X=$?
ERRS=$(grep -o 'Assets/[^(]*([0-9,]*): error CS[0-9]*: [^\r]*' "$LOG" 2>/dev/null | sort -u)
N=$(printf '%s' "$ERRS" | grep -c . ); N=${N:-0}
dd_log "compile-check ${SHA:0:12}: unity exit=$X outcome=$DD_UNITY_OUTCOME errorsCS=$N seconds=$(( $(date +%s) - T0 ))"
[ "$DD_UNITY_OUTCOME" = killed ] || [ "$DD_UNITY_OUTCOME" = stalled ] && exit 124
if [ "$N" -gt 0 ]; then printf '%s\n' "$ERRS" | head -8 | tee -a "$DD_TIMELINE"; echo "RESULT: ${SHA:0:12} does NOT compile ($N errors) — $LOG"; exit 2; fi
echo "RESULT: ${SHA:0:12} compiles (errorsCS 0) — $LOG"; exit 0
