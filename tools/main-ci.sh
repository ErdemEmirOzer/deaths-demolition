#!/bin/zsh
# WP-079 · tools/main-ci.sh <sha|ref> [--out-root DIR] [--ci-dir DIR] [--seed-library DIR] [--skip-playmode]
#                                    [--editmode-timeout N] [--playmode-timeout N]
#
# Continuous verification of main: puts a dedicated, DETACHED CI worktree on the given commit
# (never touches main or any branch), compiles and runs the full EditMode suite plus the PlayMode
# core suites (tools/ci-suites.txt, ≈ 25 min), then writes <out-root>/<sha>/summary.json + summary.md
# with every red mapped to its owner session (tools/ci-owners.tsv) and the reds that are NEW since
# the previous CI run of another commit.
#
# Safety: the CI worktree must be clean (checkout only, no reset); Unity runs under the machine slot
# (tag main-ci, tools/slot.sh); tests use their process-local roots, and the player folder the Editor
# shares with the game (profile, settings, telemetry) is hashed before/after — anything a test changed
# is restored from the copy taken before and reported as a user-data warning.
# Processes (R1 §4.13): this script kills nothing; tools/test.sh's watchdog only ends the Unity pid it started.
# Signals (Codex-audit Y1 / G4 Q10, C-00 30.09): INT/TERM/HUP exit 130/143/129 through the one EXIT cleanup. A running
# test.sh (our own child) gets TERM and ≤ DD_CI_CHILD_GRACE s (default 120) to end with its Unity; if any of them is still
# alive the user-data window is LEFT OPEN — no restore, no player slot / lease release — and the line says: tell C-00.
# Exit: 0 green · 1 red tests · 2 compile error / missing results · 3 CI worktree not clean · 64 usage
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
REF="${1:-}"; shift 2>/dev/null || true
[ -n "$REF" ] || { echo "usage: main-ci.sh <sha|ref> [--out-root DIR] [--ci-dir DIR] [--seed-library DIR] [--skip-playmode]" >&2; exit 64; }
OUT_ROOT="${DD_CI_ROOT:-$HOME/Desktop/KADRO/context/projects/deaths-demolition/ci}"
CI_DIR="${DD_CI_DIR:-$HOME/Desktop/KADRO/context/projects/deaths-demolition/wp-079/ci-worktree/deaths-demolition}"
SEED=""; SKIP_PLAY=0; ETMIN=30; PTMIN=35
while [ $# -gt 0 ]; do case "$1" in
  --out-root) OUT_ROOT="$2"; shift ;; --ci-dir) CI_DIR="$2"; shift ;; --seed-library) SEED="$2"; shift ;;
  --skip-playmode) SKIP_PLAY=1 ;; --editmode-timeout) ETMIN="$2"; shift ;; --playmode-timeout) PTMIN="$2"; shift ;;
  *) echo "unknown option: $1" >&2; exit 64 ;; esac; shift; done

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
CI_PLAYER_HELD=0; CI_GUARD_READY=0; CI_GUARD_EXIT=0; CI_CHILD=""; CI_CLEANING=0; CI_SIGNAL=""
ci_guard_restore() {
  "$DD_TOOLS_DIR/userdata-guard.sh" restore "$OUT/userdata-guard" --scope editor > "$OUT/userdata-guard.out" 2>&1
  CI_GUARD_EXIT=$?; CI_GUARD_READY=0
  if [ "$CI_GUARD_EXIT" -eq 0 ] && ! grep -q 'restore scope=editor .*verified=1' "$OUT/userdata-guard.out"; then CI_GUARD_EXIT=31; fi
}
# Y1b (G4 D0 data-loss judge MUST-1/2, C-00 30.09) — used by BOTH the normal path and the cleanup: nothing is written
# while any Unity still has the CI checkout open (a writer outside our process tree), and the restore itself cannot die
# to a second Ctrl-C / group signal (ff06 guard: a kill between `defaults delete` and `import` loses keys) — ignored
# dispositions are inherited by the guard child. 1 = window left open (slot + lease must stay held; tell C-00).
ci_guard_restore_safe() {
  ci_project_open; local open=$?
  if [ "$open" -ne 1 ]; then
    dd_log "main-ci ${CI_SIGNAL:-exit}: user-data window left open: a Unity still has $CI_DIR open (or the check failed: $open), tell C-00 — guard $OUT/userdata-guard NOT restored, player slot and lease $CI_LEASE kept" >&2
    return 1
  fi
  trap '' INT TERM HUP
  ci_guard_restore
  [ "$CI_CLEANING" -eq 1 ] || { trap 'ci_signal INT 130' INT; trap 'ci_signal TERM 143' TERM; trap 'ci_signal HUP 129' HUP; }
}
ci_cleanup() {
  local original=$1 slot_exit=0 lease_exit=0
  trap - EXIT; CI_CLEANING=1
  # Codex-audit Y1 / G4 Q10: restoring while the child (or its orphaned Unity) can still write would be a false "verified".
  if [ -n "$CI_CHILD" ] && ! dd_stop_child "$CI_CHILD" "${DD_CI_CHILD_GRACE:-120}"; then
    dd_log "main-ci ${CI_SIGNAL:-exit}: user-data window left open: child $DD_CHILD_ALIVE still alive, tell C-00 — guard $OUT/userdata-guard NOT restored, player slot and lease $CI_LEASE kept" >&2
    exit "$original"
  fi
  if [ "$CI_GUARD_READY" -eq 1 ]; then ci_guard_restore_safe || exit "$original"; fi
  if [ "$CI_PLAYER_HELD" -eq 1 ]; then DD_SLOT_TAG="$CI_PLAYER_TAG" dd_slot_release player; slot_exit=$?; fi
  ci_lease_release; lease_exit=$?
  [ "$CI_GUARD_EXIT" -eq 0 ] && [ "$slot_exit" -eq 0 ] && [ "$lease_exit" -eq 0 ] || echo "CI cleanup failed: guard=$CI_GUARD_EXIT slot=$slot_exit lease=$lease_exit (original=$original)" >&2
  [ "$original" -eq 0 ] || exit "$original"
  [ "$CI_GUARD_EXIT" -eq 0 ] || exit "$CI_GUARD_EXIT"
  [ "$slot_exit" -eq 0 ] || exit "$slot_exit"
  exit "$lease_exit"
}
# zsh function-local EXIT traps run on function return; install this at script scope.
trap 'ci_cleanup $?' EXIT
# zsh skips EXIT on an untrapped INT/TERM/HUP: record it, exit 128+n (cleanup runs once; a signal during cleanup is ignored).
ci_signal() { [ "$CI_CLEANING" -eq 0 ] || return 0; CI_SIGNAL=SIG$1; echo "main-ci: SIG$1 — exit $2 through cleanup" >&2; exit $2; }
trap 'ci_signal INT 130' INT; trap 'ci_signal TERM 143' TERM; trap 'ci_signal HUP 129' HUP
ci_lease_acquire || exit $?
SHA=$(git -C "$DD_REPO" rev-parse --verify "$REF^{commit}" 2>/dev/null) || { echo "unknown commit: $REF" >&2; exit 64; }
# WP-043D: hold the single 'player' slot for the whole run — Player captures / bots share the audio device and
# focus with the Editor PlayMode tests (a full-screen capture broke the UiV3 audio test); wrapped launches wait.
OUT="$OUT_ROOT/$SHA"; mkdir -p "$OUT" || exit 3; DD_TIMELINE="$OUT/timeline.txt"
ci_project_open; OPEN=$?
[ "$OPEN" -eq 1 ] || { dd_log "CI project open or process check unavailable — busy"; exit 75; }
CI_PLAYER_TAG="$DD_SLOT_TAG"; CI_PLAYER_HELD=1
dd_slot_acquire player || exit $?
export DD_SLOT_TAG="main-ci"
dd_log "main-ci sha=$SHA ci=$CI_DIR out=$OUT"

# 1) CI worktree on the commit (detached; created once, then checkout only)
if [ ! -e "$CI_DIR/.git" ]; then
  mkdir -p "${CI_DIR:h}"
  git -C "$DD_REPO" worktree add --detach "$CI_DIR" "$SHA" >/dev/null 2>&1 || { dd_log "worktree add failed"; exit 3; }
else
  [ "$(git -C "$CI_DIR" symbolic-ref -q HEAD)" != refs/heads/main ] || { dd_log "refusing main checkout"; exit 3; }
  DIRTY=$(git -C "$CI_DIR" status --porcelain --untracked-files=no) || exit 3
  [ -z "$DIRTY" ] || { dd_log "CI worktree not clean: $DIRTY"; exit 3; }
  git -C "$CI_DIR" checkout -q --detach "$SHA" || { dd_log "checkout failed"; exit 3; }
fi
git -C "$CI_DIR" lfs pull >/dev/null 2>&1 || true
dd_log "ci worktree at $(git -C "$CI_DIR" rev-parse --short=10 HEAD)"

[ -d "$CI_DIR/prototype/DeathsDemolition_PlayableConcept" ] || { dd_log "CI project missing"; exit 3; }

# 2) Library seed (APFS clone) on the first run
LIB="$CI_DIR/prototype/DeathsDemolition_PlayableConcept/Library"
if [ ! -d "$LIB" ]; then
  if [ -z "$SEED" ]; then
    for cand in "$HOME"/Desktop/KADRO/context/projects/deaths-demolition/*/worktree/*/prototype/DeathsDemolition_PlayableConcept/Library(N/om); do
      w="${cand%/prototype/*}"; pgrep -f "$w" >/dev/null && continue; SEED="$cand"; break
    done
  fi
  [ -n "$SEED" ] && cp -cR "$SEED" "$LIB" && dd_log "library seeded from $SEED"
fi

# 3) user-data guard (the Editor shares the player folder)
PD="$HOME/Library/Application Support/Deaths Demolition/$DD_PRODUCT"
UD="$OUT/userdata-backup"; mkdir -p "$UD"
inventory() { [ -d "$PD" ] && find "$PD" -type f ! -name '*.lock' ! -name '.DS_Store' | LC_ALL=C sort | while IFS= read -r f; do echo "$(dd_sha "$f")  ${f#$PD/}"; done; }
[ -d "$PD" ] && rsync -a --exclude '*.lock' "$PD/" "$UD/"
inventory > "$OUT/userdata-before.txt"
# WP-066J: the shared guard also covers the player bundle folder and both PlayerPrefs plists (exact
# restore to this snapshot: run-added keys removed — `defaults import` alone merges).
while :; do
  "$DD_TOOLS_DIR/userdata-guard.sh" snapshot "$OUT/userdata-guard" --scope editor > "$OUT/userdata-guard-snapshot.out" 2>&1; GS=$?
  [ "$GS" -eq 0 ] && break
  [ "$GS" -eq 30 ] || exit "$GS"
  dd_log "userdata snapshot30: waiting; no tests started"; sleep 20
done
CI_GUARD_READY=1

# 4) tests (the CI worktree's own tools: they belong to the commit under test); `& wait` so a signal is handled at once
"$CI_DIR/tools/test.sh" editmode --out "$OUT/editmode" --timeout-min "$ETMIN" > "$OUT/editmode.out" 2>&1 & CI_CHILD=$!; wait $CI_CHILD; EX=$?; CI_CHILD=""
PX=0
if [ "$SKIP_PLAY" -eq 0 ]; then
  SUITES=$(grep -vE '^\s*(#|$)' "$DD_TOOLS_DIR/ci-suites.txt" | paste -sd'|' -)
  "$CI_DIR/tools/test.sh" playmode --filter "\\.($SUITES)\\." --out "$OUT/playmode" --timeout-min "$PTMIN" > "$OUT/playmode.out" 2>&1 & CI_CHILD=$!; wait $CI_CHILD; PX=$?; CI_CHILD=""
fi
dd_log "editmode exit=$EX playmode exit=$PX"

# 5) user data: restore anything a test touched
inventory > "$OUT/userdata-after.txt"
UDWARN=""
if ! cmp -s "$OUT/userdata-before.txt" "$OUT/userdata-after.txt"; then
  UDWARN="player folder changed during the tests: $(diff "$OUT/userdata-before.txt" "$OUT/userdata-after.txt" | grep -E '^[<>]' | awk '{print $1" "$3}' | sort -u -k2 | tr '\n' ';') restored from the pre-run copy"
  mkdir -p "$OUT/userdata-created"
  while IFS= read -r rel; do [ -f "$UD/$rel" ] || { mkdir -p "$OUT/userdata-created/${rel:h}"; mv "$PD/$rel" "$OUT/userdata-created/$rel"; }; done < <(inventory | awk '{ $1=""; sub(/^  */, ""); print }')
  rsync -a --exclude '*.lock' "$UD/" "$PD/"
  inventory > "$OUT/userdata-restored.txt"
  cmp -s "$OUT/userdata-before.txt" "$OUT/userdata-restored.txt" && UDWARN="$UDWARN (verified)" || UDWARN="$UDWARN (RESTORE MISMATCH)"
  dd_log "WARNING userdata: $UDWARN"
fi

# WP-066K: Editor tests can only write the Editor area (player folder + Editor PlayerPrefs domain); a
# change in the player area (bundle folder profile, player domain) is someone else's — listed, not undone.
UG="$OUT/userdata-guard"
ci_guard_restore_safe || exit 3; GX=$CI_GUARD_EXIT
if ! cmp -s "$UG/before-editor.txt" "$UG/after-run-editor.txt" || [ "$GX" -ne 0 ]; then
  GW="Editor PlayerPrefs/profile changed during the tests: restored to the pre-run snapshot ($([ "$GX" -eq 0 ] && echo verified || echo "RESTORE MISMATCH exit $GX")) — $UG"
  UDWARN="${UDWARN:+$UDWARN; }$GW"; dd_log "WARNING userdata: $GW"
fi
if [ -s "$UG/out-of-scope-changed.txt" ]; then
  GW="player area (bundle profile / player PlayerPrefs) changed during the tests — not by the Editor, NOT restored: $UG/out-of-scope-changed.txt; writers (WP-066M): $(grep -F -f <(sed 's/^player  [^ ]*  //' "$UG/out-of-scope-changed.txt") "$UG/attribution.txt" 2>/dev/null | cut -f1,3 | sed "s|$HOME/||" | tr '\n' ';')"
  UDWARN="${UDWARN:+$UDWARN; }$GW"; dd_log "WARNING userdata: $GW"
fi

# 6) summary
SARGS=(); [ -n "$UDWARN" ] && SARGS=(--userdata-warning "$UDWARN")
python3 "$DD_TOOLS_DIR/ci_summary.py" "$OUT" "$SHA" "$OUT_ROOT" "$DD_TOOLS_DIR/ci-owners.tsv" "${SARGS[@]}" | tee -a "$DD_TIMELINE"
SX=${pipestatus[1]}
[ "$CI_GUARD_EXIT" -eq 0 ] || exit "$CI_GUARD_EXIT"
[ "$SX" -eq 0 ] && exit 0
grep -q '"compileErrors": \[\]' "$OUT/summary.json" && [ -f "$OUT/editmode/editmode.xml" ] && exit 1
exit 2
