#!/bin/zsh
# C-00 12.10 (queue fairness) · tools/ug-step.sh — ONE Unity / Player step in its OWN user-data guard window.
# A long chain (build → audit → bake → EditMode → scan → PlayMode) held one editor window for 1.5 h while 8 runs queued;
# run it as one ug-step per step instead: the window closes after each step and the FIFO queue lets the runs that waited
# in before the next step of the chain.
#
# usage: ug-step.sh --scope editor|player --ug <new window dir> [--warn-min 25] [--ledger L] -- <command> [args…]
#   --ug        a fresh folder per step (it must not exist); <ug>-snapshot.out / <ug>.restore.out / <ug>-oos_rule.out
#               sit next to it
#   --warn-min  while the step runs: once its window has been open this long (DD_GUARD_FAIR_WARN_MIN, 25) AND runs wait
#               in the guard queue (tools/guard-queue.py --waiting), a FAIRNESS line goes to the timeline (DD_TIMELINE)
#               and stderr, every 10 min — a warning only, nothing is stopped
#   --ledger    the guard ledger the queue is read from (default: the guard's own)
# The command runs as is: an editor step is e.g. `tools/test.sh editmode --filter …` or `tools/build.sh dev …`, a player
# step goes through tools/player-run.sh. R4 rule 1: DD_GUARD_KNOWN_GOOD must be exported and the snapshot must say
# kgSource=env. Restore: editor 32 passes only via qa/oos_rule.py; verified=1 required. Chains:
#   for s in build audit em; do zsh tools/ug-step.sh --scope editor --ug "$OUT/ug-$s" -- zsh steps/$s.sh || break; done
# exit: the command's exit code · 64 usage / no KG env / snapshot without kgSource=env · the guard's code when the
#       snapshot or the restore failed (30/31/32/65/75 …; it wins over the command's) · 128+n signal (the restore still runs)
set -u
SELF="${0:A}"; HERE="${SELF:h}"
source "$HERE/dd-env.sh"

SCOPE=""; UG=""; WARN="${DD_GUARD_FAIR_WARN_MIN:-25}"; LEDGER=""
usage() { sed -n '7,19p' "$SELF" >&2; exit 64; }
while [ $# -gt 0 ]; do
  case "$1" in
    --scope) SCOPE="${2:-}"; shift 2 ;;
    --ug) UG="${2:-}"; shift 2 ;;
    --warn-min) WARN="${2:-}"; shift 2 ;;
    --ledger) LEDGER="${2:-}"; shift 2 ;;
    --) shift; break ;;
    -h|--help) usage ;;
    *) echo "ug-step: unknown option $1" >&2; usage ;;
  esac
done
[ "$SCOPE" = editor ] || [ "$SCOPE" = player ] || { echo "ug-step: --scope editor|player" >&2; usage; }
[ -n "$UG" ] && [ $# -gt 0 ] || usage
[[ "$WARN" == <-> ]] && [ "$WARN" -ge 1 ] || { echo "ug-step: --warn-min must be a whole number of minutes ≥ 1" >&2; exit 64; }
[ -e "$UG" ] && { echo "ug-step: $UG exists — every step takes a fresh window folder" >&2; exit 64; }
[ -n "${DD_GUARD_KNOWN_GOOD:-}" ] || { echo "ug-step: export DD_GUARD_KNOWN_GOOD first (R4 rule 1, KARAR-GUARD-KG-10.10)" >&2; exit 64; }
dd_guard_canon_check || exit $?
UG="${UG:a}"; mkdir -p "${UG:h}"
: "${DD_TIMELINE:=$UG-timeline.txt}"
QARGS=(--waiting); [ -n "$LEDGER" ] && QARGS+=(--ledger "$LEDGER")

SNAP_OK=0; GX=0; RC=0; CHILD=""; SIG=0
finish() {
  local rc=$1; trap '' INT TERM HUP
  if [ -n "$CHILD" ] && dd_pid_live "$CHILD"; then
    dd_stop_child "$CHILD" 60 || { dd_log "ug-step: the step is still alive — window $UG NOT restored, tell C-00"; exit 1; }
  fi
  if [ "$SNAP_OK" -eq 1 ]; then
    "$DD_GUARD_CANON" restore "$UG" --scope "$SCOPE" > "$UG.restore.out" 2>&1; GX=$?
    if [ "$GX" -eq 32 ] && [ "$SCOPE" = editor ]; then
      python3 "$HERE/qa/oos_rule.py" "$UG" > "$UG-oos_rule.out" 2>&1 && GX=0 || GX=32
    fi
    [ "$GX" -eq 0 ] && ! grep -q "restore scope=$SCOPE .*verified=1" "$UG.restore.out" && GX=31
  fi
  dd_log "ug-step: done $UG rc=$rc guard=$GX ($((SECONDS - T0)) s)"
  [ "$GX" -ne 0 ] && exit "$GX"
  exit "$rc"
}
trap 'SIG=130; finish 130' INT; trap 'SIG=143; finish 143' TERM; trap 'SIG=129; finish 129' HUP

T0=$SECONDS
dd_log "ug-step: $SCOPE window $UG — $*"
"$DD_GUARD_CANON" snapshot "$UG" --scope "$SCOPE" > "$UG-snapshot.out" 2>&1 & CHILD=$!; wait $CHILD; GS=$?; CHILD=""
if [ "$GS" -ne 0 ]; then
  [ -f "$UG/before.txt" ] && SNAP_OK=1   # the snapshot failed with its window open: restore it, run nothing
  dd_log "ug-step: snapshot exit $GS — the step does not run"; finish "$GS"
fi
SNAP_OK=1
grep -q 'kgSource=env' "$UG-snapshot.out" || { dd_log "ug-step: snapshot without kgSource=env — the step does not run (R4 rule 1)"; finish 64; }

MINUTE=${DD_UG_STEP_MINUTE_SECONDS:-60}; POLL=${DD_UG_STEP_POLL:-5}   # test seams: a short minute and poll
OPENED=$SECONDS; WARNED=-1
"$@" & CHILD=$!
while dd_pid_live "$CHILD"; do
  sleep "$POLL"
  open_min=$(( (SECONDS - OPENED) / MINUTE ))
  if [ "$open_min" -ge "$WARN" ] && { [ "$WARNED" -lt 0 ] || [ $((SECONDS - WARNED)) -ge $((10 * MINUTE)) ]; }; then
    waiting=$(python3 "$HERE/guard-queue.py" "${QARGS[@]}" 2>/dev/null)
    if [[ "$waiting" == <-> ]] && [ "$waiting" -ge 1 ]; then
      WARNED=$SECONDS
      dd_log "ug-step: FAIRNESS — $UG has been open $open_min min with $waiting waiting in the guard queue; split the chain, one Unity step per window (C-00 12.10)" >&2
    fi
  fi
done
wait $CHILD; RC=$?; CHILD=""
finish "$RC"
