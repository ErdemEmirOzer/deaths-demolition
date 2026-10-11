#!/bin/zsh
# C-00 12.10 (queue fairness) · tools/ug-step.sh — ONE Unity / Player step in its OWN user-data guard window.
# A long chain (build → audit → bake → EditMode → scan → PlayMode) held one editor window for 1.5 h while 8 runs queued;
# run it as one ug-step per step instead: the window closes after each step and the FIFO queue lets the runs that waited
# in before the next step of the chain.
#
# usage: ug-step.sh --scope editor|player --ug <new window dir> [--warn-min 25] [--ledger L] -- <command> [args…]
#   --ug        a fresh folder per step (it must not exist); <ug>-snapshot.out / <ug>.restore.out / <ug>-oos_rule.out /
#               <ug>.result.json (rc, guard, restore done|skipped|not-restored) sit next to it
#   --warn-min  while the step runs: once its window has been open longer than this (DD_GUARD_FAIR_WARN_MIN, 25) AND live
#               runs over the same area wait in the guard queue, a FAIRNESS line goes to the timeline (DD_TIMELINE) and
#               stderr, every 10 min — a warning only, nothing is stopped
#   --ledger    the guard ledger the queue is read from (default: the guard's own)
# The command runs as is: an editor step is e.g. `tools/test.sh editmode --filter …` or `tools/build.sh dev …`, a player
# step goes through tools/player-run.sh. R4 rule 1: DD_GUARD_KNOWN_GOOD must be exported and the snapshot must say
# kgSource=env. Restore: editor 32 passes only via qa/oos_rule.py; verified=1 required; a 75 retry=same-dir is retried
# (4 × 30 s). Chains:
#   for s in build audit em; do zsh tools/ug-step.sh --scope editor --ug "$OUT/ug-$s" -- zsh steps/$s.sh || break; done
# exit: the command's exit code · 64 usage / no KG env / snapshot without kgSource=env · the guard's code when the
#       snapshot or the restore failed (30/31/32/65/75 …; it wins over the command's) · 70 the step (or a process it left
#       behind) did not stop — the window is NOT restored, tell C-00 · 128+n signal (the restore still runs)
set -u
setopt nullglob
SELF="${0:A}"; HERE="${SELF:h}"
source "$HERE/dd-env.sh"
trap '' PIPE   # gt S-1 (12.10): a closed reader (`| head`) must never kill the step's guard window half way
# …and a write to it must not abort the script either: zsh treats a builtin's EPIPE as an error and leaves the running code
# (a retry or drain line in finish would cut the restore short). Every line goes to the timeline first, then to the console
# through the external echo, whose failure is ignored.
ulog() {
  local line="$(date -u +%FT%TZ) $*"
  [ -n "${DD_TIMELINE:-}" ] && print -r -- "$line" >> "$DD_TIMELINE" 2>/dev/null
  /bin/echo "$line" 2>/dev/null; [ -n "${ULOG_ERR:-}" ] && /bin/echo "$line" >&2 2>/dev/null; return 0
}

SCOPE=""; UG=""; WARN="${DD_GUARD_FAIR_WARN_MIN:-25}"; LEDGER=""
usage() { sed -n '7,22p' "$SELF" >&2; exit 64; }
while [ $# -gt 0 ]; do
  case "$1" in
    --scope) SCOPE="${2:-}"; shift 2 ;;
    --ug) UG="${2:-}"; shift 2 ;;
    --warn-min) WARN="${2:-}"; WARN_ASKED=1; shift 2 ;;
    --ledger) LEDGER="${2:-}"; shift 2 ;;
    --) shift; break ;;
    -h|--help) usage ;;
    *) echo "ug-step: unknown option $1" >&2; usage ;;
  esac
done
[ "$SCOPE" = editor ] || [ "$SCOPE" = player ] || { echo "ug-step: --scope editor|player" >&2; usage; }
[ -n "$UG" ] && [ $# -gt 0 ] || usage
# gt NOTE-2: one reading of the limit with userdata_guard_io.fair_warn_minutes — a number > 0 (25.0 too); a bad env value
# falls back to 25 like the guard does, a bad --warn-min is a usage error
if [[ "$WARN" == (<->|<->.<->) ]] && (( WARN > 0 )); then :; elif [ -n "${WARN_ASKED:-}" ]; then
  echo "ug-step: --warn-min must be a number of minutes > 0" >&2; exit 64; else WARN=25; fi
MINUTE=${DD_UG_STEP_MINUTE_SECONDS:-60}; POLL=${DD_UG_STEP_POLL:-5}; CHECK=${DD_UG_STEP_CHECK_SECONDS:-60}
RETRY_S=${DD_UG_STEP_RETRY_SECONDS:-30}; DRAIN=${DD_UG_STEP_DRAIN_SECONDS:-60}   # test seams, checked (gt S-1b)
for v in MINUTE POLL CHECK RETRY_S DRAIN; do
  [[ "${(P)v}" == <-> ]] && (( ${(P)v} >= 1 )) || { echo "ug-step: $v must be a whole number ≥ 1 (${(P)v})" >&2; exit 64; }
done
[ -e "$UG" ] && { echo "ug-step: $UG exists — every step takes a fresh window folder" >&2; exit 64; }
[ -n "${DD_GUARD_KNOWN_GOOD:-}" ] || { echo "ug-step: export DD_GUARD_KNOWN_GOOD first (R4 rule 1, KARAR-GUARD-KG-10.10)" >&2; exit 64; }
dd_guard_canon_check || exit $?
UG="${UG:a}"; mkdir -p "${UG:h}"
: "${DD_TIMELINE:=$UG-timeline.txt}"
QARGS=(--waiting --areas "$SCOPE" --not-under $$); [ -n "$LEDGER" ] && QARGS+=(--ledger "$LEDGER")

SNAP_OK=0; GX=0; RC=0; CHILD=""; FINISHED=0; RESTORE=skipped
typeset -A SEEN   # gt S-2: every process of the step seen while it ran (pid → start), so none outlives the restore unseen
remember() { local p; for p in ${(f)"$(dd_tree "$1")"}; do [ -n "${SEEN[$p]:-}" ] || SEEN[$p]="$(dd_pstart "$p")"; done; }
left_behind() {   # the remembered processes still alive with the same start time (not a reused pid)
  local p; LEFT=(); for p in ${(k)SEEN}; do dd_pid_live "$p" && [ "$(dd_pstart "$p")" = "${SEEN[$p]}" ] && LEFT+=($p); done; }
result() { print -r -- "{\"rc\": $1, \"guard\": $GX, \"restore\": \"$RESTORE\", \"scope\": \"$SCOPE\"}" > "$UG.result.json" 2>/dev/null; }
finish() {
  local rc=$1 t p; FINISHED=1; trap '' INT TERM HUP
  if [ -n "$CHILD" ] && dd_pid_live "$CHILD"; then
    remember "$CHILD"
    dd_stop_child "$CHILD" "$DRAIN" || { RESTORE=not-restored; result 70; ulog "ug-step: the step is still alive — window $UG NOT restored, tell C-00"; exit 70; }
  fi
  # gt M-1: a signal during the snapshot's last phase (it ignores TERM after its commit point, writes before.txt, exits 0)
  # leaves an open window although the snapshot never reported back — before.txt exists only in a committed window (INV-8)
  [ "$SNAP_OK" -eq 0 ] && [ -f "$UG/before.txt" ] && SNAP_OK=1
  if [ "$SNAP_OK" -eq 1 ]; then
    # gt S-2: a process the step left behind (a grandchild) may still write — let it end (≤ DRAIN s), then stop it, else no restore
    left_behind
    t=0; while [ ${#LEFT} -gt 0 ] && [ $t -lt "$DRAIN" ]; do sleep 1; t=$((t + 1)); left_behind; done
    if [ ${#LEFT} -gt 0 ]; then
      ulog "ug-step: the step left $LEFT running — stopping them before the restore"
      for p in $LEFT; do dd_stop_child "$p" "$DRAIN" || { RESTORE=not-restored; result 70; ulog "ug-step: pid $p did not stop — window $UG NOT restored, tell C-00"; exit 70; }; done
    fi
    local n=1
    while :; do   # gt S-3: 75 retry=same-dir (BUSY / LOCKED) is the guard asking for a later restore of the same DIR — retried
      "$DD_GUARD_CANON" restore "$UG" --scope "$SCOPE" > "$UG.restore.out" 2>&1; GX=$?
      [ "$GX" -eq 75 ] && grep -q 'retry=same-dir' "$UG.restore.out" && [ $n -lt 4 ] || break
      cp "$UG.restore.out" "$UG.restore.$n.out"; ulog "ug-step: restore 75 retry=same-dir — try $((n + 1)) in ${RETRY_S} s"; n=$((n + 1))
      sleep "$RETRY_S"
    done
    if [ "$GX" -eq 32 ] && [ "$SCOPE" = editor ]; then
      python3 "$HERE/qa/oos_rule.py" "$UG" > "$UG-oos_rule.out" 2>&1 && GX=0 || GX=32
    fi
    [ "$GX" -eq 0 ] && ! grep -q "restore scope=$SCOPE .*verified=1" "$UG.restore.out" && GX=31
    RESTORE=done
  fi
  result "$rc"
  ulog "ug-step: done $UG rc=$rc guard=$GX restore=$RESTORE ($((SECONDS - T0)) s)"
  [ "$GX" -ne 0 ] && exit "$GX"
  exit "$rc"
}
trap 'finish 130' INT; trap 'finish 143' TERM; trap 'finish 129' HUP
trap '[ "$FINISHED" -eq 1 ] || finish 1' EXIT   # gt S-1: any other way out (a shell error) still restores

T0=$SECONDS
ulog "ug-step: $SCOPE window $UG — $*"
"$DD_GUARD_CANON" snapshot "$UG" --scope "$SCOPE" > "$UG-snapshot.out" 2>&1 & CHILD=$!; wait $CHILD; GS=$?; CHILD=""
if [ "$GS" -ne 0 ]; then
  [ -f "$UG/before.txt" ] && SNAP_OK=1   # the snapshot failed with its window open: restore it, run nothing
  ulog "ug-step: snapshot exit $GS — the step does not run"; finish "$GS"
fi
SNAP_OK=1
grep -q 'kgSource=env' "$UG-snapshot.out" || { ulog "ug-step: snapshot without kgSource=env — the step does not run (R4 rule 1)"; finish 64; }

OPENED=$SECONDS; WARNED=-1; CHECKED=-1
"$@" & CHILD=$!
while dd_pid_live "$CHILD"; do
  remember "$CHILD"
  sleep "$POLL" & wait $!   # gt NOTE-3: a signal is handled at once, not after the sleep
  open_s=$((SECONDS - OPENED))
  if (( open_s > WARN * MINUTE && SECONDS - CHECKED >= CHECK && (WARNED < 0 || SECONDS - WARNED >= 10 * MINUTE) )); then
    CHECKED=$SECONDS
    waiting=$(python3 "$HERE/guard-queue.py" "${QARGS[@]}" 2>/dev/null)
    if [[ "$waiting" == <-> ]] && [ "$waiting" -ge 1 ]; then
      WARNED=$SECONDS
      ULOG_ERR=1 ulog "ug-step: FAIRNESS — $UG has been open $((open_s / MINUTE)) min with $waiting waiting for the $SCOPE area; split the chain, one Unity step per window (C-00 12.10)"
    fi
  fi
done
wait $CHILD; RC=$?; CHILD=""
finish "$RC"
