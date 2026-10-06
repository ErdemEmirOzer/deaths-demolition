#!/bin/zsh
# WP-079 · tools/main-ci.sh <sha|ref> [--out-root DIR] [--ci-dir DIR] [--seed-library DIR] [--skip-playmode]
#                                    [--editmode-timeout N] [--playmode-timeout N] [--alone-timeout N]
#
# Continuous verification of main: puts a dedicated, DETACHED CI worktree on the given commit
# (never touches main or any branch), compiles and runs the full EditMode suite plus the PlayMode
# core suites (tools/ci-suites.txt, ≈ 25 min), then writes <out-root>/<sha>/summary.json + summary.md
# with every red mapped to its owner session (tools/ci-owners.tsv) and the reds that are NEW since
# the previous CI run of another commit.
# Isolated tests (Codex-audit M-11, C-00 30.09): tools/ci-isolated.txt lists single tests the core must not run (capture,
# DEC-077 reading, allocation); the core -testFilter excludes all of them (tools/ci_isolated.py, negative lookahead). Each
# `alone` entry then runs in its OWN test.sh call (filter = that test only, out <sha>/playmode-alone-<n>-<name>,
# --alone-timeout min, default 10) after the core step; ci_summary.py counts it like the core. `a3` entries are not run here.
#
# Safety: the CI worktree must be clean (checkout only, no reset); Unity runs under the machine slot
# (tag main-ci, tools/slot.sh); tests use their process-local roots, and the player folder the Editor
# shares with the game (profile, settings, telemetry) is hashed before/after — anything a test changed
# is restored from the copy taken before and reported as a user-data warning.
# Processes (R1 §4.13): this script kills nothing; tools/test.sh's watchdog only ends the Unity pid it started.
# Signals (Codex-audit Y1 / G4 Q10, C-00 30.09): INT/TERM/HUP exit 130/143/129 through the one EXIT cleanup. A running
# test.sh (our own child) gets TERM and ≤ DD_CI_CHILD_GRACE s (default 120) to end with its Unity; if any of them is still
# alive the user-data window is LEFT OPEN — no restore, no player slot / lease release — and the line says: tell C-00.
# Driver (Codex-audit M-12, C-00 30.09): the git worktree holding THIS main-ci.sh (guard, summary, owners, suites come from
# it) must be clean, on a detached HEAD, and carry main's tools tree; its HEAD + tools tree hash go into summary.json/md.
# Lease (Codex-audit M-05, C-00 30.09): dd_ci_lease_* (dd-env.sh), shared with compile-check.sh; a live owner → 75 at once
# (no waiting); a stale / malformed / symlinked lease is never reclaimed → 3 (diagnose: compile-check.sh --lease-status).
# Env (Codex-audit M-10, C-00 30.09): right before the tests every variable the CI checkout's C# reads is unset except HOME
# (dd_scrub_code_env); the cleared names go into summary.json envScrubbed / summary.md; git grep failure → 3.
# Exit: 0 green · 1 red tests · 2 compile error / missing results · 3 CI worktree or driver worktree not clean, driver not
#   detached or its tools ≠ main's, lease stale/malformed/symlink, CI project missing · 65 a guard window NOT restored
#   (G4 D2 MUST-3a, C-00 01.10: a child still alive at cleanup, or a Unity has the CI checkout open / the check failed at the
#   restore) — $OUT/userdata-NOT-RESTORED.txt, slot + lease kept, tell C-00; or such a marker unresolved under
#   $OUT_ROOT/*/ at the start (no .resolved next to it — only C-00 writes it): nothing taken · 64 usage, unknown commit, an unrestored guard window of this SHA,
#   malformed ci-isolated.txt / ci-suites.txt (checked with the driver, before anything is taken), DD_GUARD_ROOT set or the
#   canonical guard ($DD_GUARD_CANON = KADRO wp-079/driver/tools/userdata-guard.sh, G4 D4) missing
#   · 75 busy (lease held, CI project open, guard snapshot 75 three times) · 130/143/129 INT/TERM/HUP.
#   Guard (editor scope): a snapshot never answers 30 (that is player scope's "a Player is running"); any snapshot exit other
#   than 75 is returned as is, nothing started. A restore that fails or does not print "verified=1" makes the run exit the
#   guard's code — 31 when it exited 0 unverified — even over green/red (0/1/2). Slot acquire/release failures return
#   slot.sh's code; a lease release failure returns 3.
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
REF="${1:-}"; shift 2>/dev/null || true
[ -n "$REF" ] || { echo "usage: main-ci.sh <sha|ref> [--out-root DIR] [--ci-dir DIR] [--seed-library DIR] [--skip-playmode]" >&2; exit 64; }
OUT_ROOT="${DD_CI_ROOT:-$DD_KPROJ/ci}"
CI_DIR="${DD_CI_DIR:-$DD_KPROJ/wp-079/ci-worktree/deaths-demolition}"
SEED=""; SKIP_PLAY=0; ETMIN=30; PTMIN=35; ATMIN=10
while [ $# -gt 0 ]; do case "$1" in
  --out-root) OUT_ROOT="$2"; shift ;; --ci-dir) CI_DIR="$2"; shift ;; --seed-library) SEED="$2"; shift ;;
  --skip-playmode) SKIP_PLAY=1 ;; --editmode-timeout) ETMIN="$2"; shift ;; --playmode-timeout) PTMIN="$2"; shift ;;
  --alone-timeout) ATMIN="$2"; shift ;;
  *) echo "unknown option: $1" >&2; exit 64 ;; esac; shift; done

# G4 D4 (C-00 01.10): the guard is the canonical driver copy ($DD_GUARD_CANON, dd-env.sh), never this worktree's; a set
# DD_GUARD_ROOT or a missing canonical guard is 64 before anything is taken.
dd_guard_canon_check || exit 64
# Codex-audit M-12 (C-00 30.09): driver check before anything is taken (no lease, no slot, no guard).
CI_DRIVER=""; CI_DRIVER_HEAD=""; CI_TOOLS_TREE=""
ci_driver_check() {
  local why="" dirty main_tools
  CI_DRIVER=$(git -C "$DD_TOOLS_DIR" rev-parse --show-toplevel 2>/dev/null) && [ -n "$CI_DRIVER" ] ||
    { echo "main-ci: driver refused — $DD_TOOLS_DIR is not inside a git worktree (exit 3)" >&2; return 3; }
  dirty=$(git -C "$CI_DRIVER" status --porcelain 2>/dev/null) || why="git status failed"
  [ -z "$why" ] && [ -n "$dirty" ] && why="worktree not clean: $(print -r -- "$dirty" | head -5 | tr '\n' ';')"
  [ -z "$why" ] && git -C "$CI_DRIVER" symbolic-ref -q HEAD >/dev/null 2>&1 &&
    why="HEAD is on branch $(git -C "$CI_DRIVER" symbolic-ref -q --short HEAD), not detached"
  CI_DRIVER_HEAD=$(git -C "$CI_DRIVER" rev-parse --verify -q HEAD 2>/dev/null)
  CI_TOOLS_TREE=$(git -C "$CI_DRIVER" rev-parse --verify -q HEAD:tools 2>/dev/null)
  main_tools=$(git -C "$CI_DRIVER" rev-parse --verify -q main:tools 2>/dev/null)
  if [ -z "$why" ] && { [ -z "$CI_TOOLS_TREE" ] || [ "$CI_TOOLS_TREE" != "$main_tools" ]; }; then
    why="tools tree ${CI_TOOLS_TREE:-none} (HEAD ${CI_DRIVER_HEAD:0:10}) differs from main's ${main_tools:-none}"; fi
  # C-00 30.09: the refusal names the one-line fix (C-00's procedure: detached driver at the SHA under test).
  [ -z "$why" ] || { echo "main-ci: driver $CI_DRIVER refused — $why (exit 3)" >&2
    if [ "$CI_DRIVER" = "$(git -C "$DD_REPO" rev-parse --show-toplevel 2>/dev/null)" ]; then
      echo "fix: run main-ci from the dedicated CI driver worktree (KADRO wp-079/driver), never from the primary checkout: git -C <driver> checkout -q --detach main && <driver>/tools/main-ci.sh <sha>" >&2
    else
      echo "fix: git -C '$CI_DRIVER' checkout -q --detach main && '$CI_DRIVER/tools/main-ci.sh' <sha>   (clean first if dirty; the driver's tools must be main's)" >&2
    fi; return 3; }
}
ci_driver_check || exit 3
# Codex-audit M-11 (C-00 30.09): core filter + `alone` steps from the driver's ci-suites.txt / ci-isolated.txt, built before
# anything is taken — a malformed file is 64 with nothing held.
CORE_FILTER=$(python3 "$DD_TOOLS_DIR/ci_isolated.py" core-filter "$DD_TOOLS_DIR/ci-isolated.txt" "$DD_TOOLS_DIR/ci-suites.txt") &&
  ALONE_STEPS=$(python3 "$DD_TOOLS_DIR/ci_isolated.py" alone "$DD_TOOLS_DIR/ci-isolated.txt") ||
  { echo "main-ci: ci-isolated.txt / ci-suites.txt refused (exit 64)" >&2; exit 64; }

# Compile and CI serialize only the same physical checkout; never reclaim an unknown lease. Codex-audit M-05 (C-00 30.09):
# one source — dd_ci_lease_acquire / dd_ci_lease_release / dd_ci_lease_status (dd-env.sh).
# Codex-audit M-06 (C-00 30.09): one source — dd_project_open (dd-env.sh): case-insensitive -projectPath (Unity Hub),
# an unresolvable/removed foreign path is skipped + logged instead of failing the check. 0 open · 1 not open · 2 no ps.
ci_project_open() { dd_project_open "$CI_DIR/prototype/DeathsDemolition_PlayableConcept"; }
CI_PLAYER_HELD=0; CI_GUARD_READY=0; CI_GUARD_EXIT=0; CI_GUARD_RAW=0; CI_GUARD_DIR=""; CI_CHILD=""; CI_CLEANING=0; CI_SIGNAL=""
CI_GSNAP=""; GDIR=""   # G4 D8 queue (06.10): the guard snapshot in flight (it can wait ≤ DD_GUARD_QUEUE_WAIT in the window queue)
# CI_GUARD_DIR = the directory whose snapshot succeeded (Codex-audit M-07: it can be a .tryN retry directory).
ci_guard_restore() {
  "$DD_GUARD_CANON" restore "$CI_GUARD_DIR" --scope editor > "$CI_GUARD_DIR.out" 2>&1
  CI_GUARD_EXIT=$?; CI_GUARD_READY=0; CI_GUARD_RAW=$CI_GUARD_EXIT
  # G4 D7 K1: 32 = the Editor area restored and verified, the player area changed (someone else's, listed, never undone)
  # — a warning, not a failure; it still needs the verified=1 line like 0
  [ "$CI_GUARD_EXIT" -eq 32 ] && CI_GUARD_EXIT=0
  if [ "$CI_GUARD_EXIT" -eq 0 ] && ! grep -q 'restore scope=editor .*verified=1' "$CI_GUARD_DIR.out"; then CI_GUARD_EXIT=31; fi
}
# Y1b (G4 D0 data-loss judge MUST-1/2, C-00 30.09) — used by BOTH the normal path and the cleanup: nothing is written
# while any Unity still has the CI checkout open (a writer outside our process tree), and the restore itself cannot die
# to a second Ctrl-C / group signal (ff06 guard: a kill between `defaults delete` and `import` loses keys) — ignored
# dispositions are inherited by the guard child. 1 = window left open (slot + lease must stay held; tell C-00).
# G4 D2 MUST-3a (C-00 01.10): every "not restored" branch leaves $OUT/userdata-NOT-RESTORED.txt (the wp078 / guard_receipt
# format); the next main-ci stops on it with 65 before anything is taken until C-00 writes userdata-NOT-RESTORED.resolved.
CI_MARKER=userdata-NOT-RESTORED.txt; CI_MARKER_RESOLVED=userdata-NOT-RESTORED.resolved
ci_not_restored() {   # <reason> [live pids]
  { print -r -- "DIR $CI_GUARD_DIR"; print -r -- "reason $1"; print -r -- "live ${2:-}"; print -r -- "utc $(date -u +%FT%TZ)"
    print -r -- "NOT restored: the snapshot is kept as is; no inline inventory was made. STOP — tell C-00; only C-00 writes $CI_MARKER_RESOLVED next to this file."
  } > "$OUT/$CI_MARKER"
}
ci_guard_restore_safe() {
  ci_project_open; local open=$?
  if [ "$open" -ne 1 ]; then
    dd_log "main-ci ${CI_SIGNAL:-exit}: user-data window left open: a Unity still has $CI_DIR open (or the check failed: $open), tell C-00 — guard $CI_GUARD_DIR NOT restored, player slot and lease $DD_CI_LEASE kept, $OUT/$CI_MARKER (65)" >&2
    ci_not_restored "a Unity has the CI checkout open, or the process check failed ($open)" "$(pgrep -f "Unity.app/Contents/MacOS/Unity" 2>/dev/null | tr '\n' ' ')"
    return 1
  fi
  trap '' INT TERM HUP
  ci_guard_restore
  [ "$CI_CLEANING" -eq 1 ] || { trap 'ci_signal INT 130' INT; trap 'ci_signal TERM 143' TERM; trap 'ci_signal HUP 129' HUP; }
}
ci_cleanup() {
  local original=$1 slot_exit=0 lease_exit=0
  trap - EXIT; CI_CLEANING=1
  # G4 D8 queue (06.10): a signal while the snapshot ran (it may wait an hour in the window queue) stops it at once — the
  # guard leaves the queue / aborts its record; if it opened the window anyway (exit 0, or before.txt: the guard writes it
  # only on success, INV-8), that window is restored below like any other
  if [ -n "$CI_GSNAP" ]; then
    kill -TERM "$CI_GSNAP" 2>/dev/null; wait "$CI_GSNAP"; local gs=$?; CI_GSNAP=""
    { [ "$gs" -eq 0 ] || [ -f "$GDIR/before.txt" ]; } && { CI_GUARD_DIR="$GDIR"; CI_GUARD_READY=1; }
  fi
  # Codex-audit Y1 / G4 Q10: restoring while the child (or its orphaned Unity) can still write would be a false "verified".
  if [ -n "$CI_CHILD" ] && ! dd_stop_child "$CI_CHILD" "${DD_CI_CHILD_GRACE:-120}"; then
    dd_log "main-ci ${CI_SIGNAL:-exit}: user-data window left open: child $DD_CHILD_ALIVE still alive, tell C-00 — guard ${CI_GUARD_DIR:-(no snapshot)} NOT restored, player slot and lease $DD_CI_LEASE kept" >&2
    [ "$CI_GUARD_READY" -eq 1 ] || exit "$original"   # no open window: nothing to mark
    ci_not_restored "child $DD_CHILD_ALIVE still alive ${DD_CI_CHILD_GRACE:-120} s after TERM" "$DD_CHILD_ALIVE"
    exit 65
  fi
  if [ "$CI_GUARD_READY" -eq 1 ]; then ci_guard_restore_safe || exit 65; fi
  if [ "$CI_PLAYER_HELD" -eq 1 ]; then DD_SLOT_TAG="$CI_PLAYER_TAG" dd_slot_release player; slot_exit=$?; fi
  dd_ci_lease_release; lease_exit=$?
  [ "$CI_GUARD_EXIT" -eq 0 ] && [ "$slot_exit" -eq 0 ] && [ "$lease_exit" -eq 0 ] || echo "CI cleanup failed: guard=$CI_GUARD_EXIT slot=$slot_exit lease=$lease_exit (original=$original)" >&2
  [ "$original" -eq 0 ] || exit "$original"
  [ "$CI_GUARD_EXIT" -eq 0 ] || exit "$CI_GUARD_EXIT"
  [ "$slot_exit" -eq 0 ] || exit "$slot_exit"
  exit "$lease_exit"
}
# G4 D2 MUST-3a: an unrestored window of ANY earlier run (marker without .resolved) stops this run before the lease.
CI_OPEN_MARKERS=()
for m in "$OUT_ROOT"/*/$CI_MARKER; do [ -e "${m:h}/$CI_MARKER_RESOLVED" ] || CI_OPEN_MARKERS+=("$m"); done
if [ ${#CI_OPEN_MARKERS[@]} -gt 0 ]; then
  echo "main-ci: unrestored guard window(s), resolve with C-00 first — nothing taken (65): ${CI_OPEN_MARKERS[*]}" >&2; exit 65
fi
# zsh function-local EXIT traps run on function return; install this at script scope.
trap 'ci_cleanup $?' EXIT
# zsh skips EXIT on an untrapped INT/TERM/HUP: record it, exit 128+n (cleanup runs once; a signal during cleanup is ignored).
ci_signal() { [ "$CI_CLEANING" -eq 0 ] || return 0; CI_SIGNAL=SIG$1; echo "main-ci: SIG$1 — exit $2 through cleanup" >&2; exit $2; }
trap 'ci_signal INT 130' INT; trap 'ci_signal TERM 143' TERM; trap 'ci_signal HUP 129' HUP
dd_ci_lease_acquire "$CI_DIR" || exit $?
CI_DIR=$DD_CI_CHECKOUT
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
dd_log "main-ci driver=$CI_DRIVER head=$CI_DRIVER_HEAD toolsTree=$CI_TOOLS_TREE (= main:tools)"

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
    for cand in "$DD_KPROJ"/*/worktree/*/prototype/DeathsDemolition_PlayableConcept/Library(N/om); do
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
# Codex-audit M-07 (C-00 30.09): editor scope never refuses with 30 (that is the player-scope "a Player is running"), so the
# old wait-on-30 loop was dead. 75 (a live foreign holder of the Editor-area lock after DD_GUARD_WAIT, or live data kept
# changing) is retried ≤ CI_GUARD_TRIES times — the guard's own wait is the back-off — each time into a NEW directory
# (userdata-guard-<utc>, <same>.try2, …): a directory that already holds a snapshot is refused with 64. The directory
# that succeeded is the one restore uses. No tests start before a snapshot succeeded. G4 D7 (d): the name carries this
# run's UTC, so a second run of the same SHA starts in a fresh directory instead of meeting the first run's (64).
CI_GUARD_TRIES=3; GTRY=1; UG_BASE="$OUT/userdata-guard-$(dd_utc)"
# M-12 review SHOULD-2: a rerun of the same SHA must not start while an earlier guard window of this OUT is unrestored
# (a snapshot left in a .tryN dir would otherwise be bypassed by a fresh one in a new userdata-guard-<utc>/).
for d in "$OUT"/userdata-guard*(N/); do
  # a SUCCESSFUL snapshot starts the sampler log (players.log); a try failing before the guard's S9 leaves no players.log,
  # one failing after it keeps players.log without after.txt — also stopped here (64, the safe side: C-00 looks)
  [ -f "$d/players.log" ] && [ ! -f "$d/after.txt" ] && { dd_log "userdata: $d holds an unrestored guard window from an earlier run of this SHA — tell C-00, do not restore it blindly (exit 64)"; exit 64; }
done
while :; do
  GDIR="$UG_BASE"; [ "$GTRY" -gt 1 ] && GDIR="$UG_BASE.try$GTRY"
  # `& wait` like the tests (G4 D8 queue, 06.10): the snapshot can wait ≤ DD_GUARD_QUEUE_WAIT in the window queue — a
  # foreground call held a TERM until it returned and then left the window it opened unrestored (cleanup above)
  "$DD_GUARD_CANON" snapshot "$GDIR" --scope editor > "$GDIR-snapshot.out" 2>&1 & CI_GSNAP=$!; wait $CI_GSNAP; GS=$?; CI_GSNAP=""
  [ "$GS" -eq 0 ] && break
  # G4 D8 queue reviews (06.10): a 75 after the window queue's own wait (DD_GUARD_QUEUE_WAIT, 3600 s — this run holds the
  # player slot and the lease meanwhile) is not retried: a retry is a new ticket at the queue's end, ≤ 3 h of no Players
  [ "$GS" -eq 75 ] && grep -q "class=QUEUE_TIMEOUT" "$GDIR-snapshot.out" 2>/dev/null && { dd_log "userdata snapshot75 (try $GTRY, $GDIR): the window queue timed out — not retried; no tests started"; exit 75; }
  [ "$GS" -eq 75 ] && [ "$GTRY" -lt "$CI_GUARD_TRIES" ] || { dd_log "userdata snapshot exit $GS (try $GTRY/$CI_GUARD_TRIES, $GDIR) — no tests started"; exit "$GS"; }
  dd_log "userdata snapshot75 (try $GTRY/$CI_GUARD_TRIES, $GDIR): busy — retrying into a new directory; no tests started"
  GTRY=$((GTRY + 1))
done
CI_GUARD_DIR="$GDIR"; CI_GUARD_READY=1
dd_log "userdata snapshot → $CI_GUARD_DIR (try $GTRY)"

# 4) tests (the CI worktree's own tools: they belong to the commit under test); `& wait` so a signal is handled at once
# Codex-audit M-10 (C-00 30.09): first unset every env variable the C# of the commit under test reads (dd_scrub_code_env,
# dd-env.sh; allow-list HOME) — a caller's DD_* diagnostic variable must not change a CI result. git grep fails → 3.
dd_scrub_code_env "$CI_DIR" || exit 3
# C-00 05.10: the core EditMode + PlayMode runs exclude [Category("Diagnostics")] — a class regex counts as an explicit
# selection in Unity, so an [Explicit] diagnostics test inside a selected class ran anyway (285b7fe8: a 30-min kit test).
# The `alone` steps name one test each and keep no category filter. A commit whose test.sh predates --category runs
# without it (warned; the old behaviour).
CI_CATEGORY='!^Diagnostics$'; CARGS=(--category "$CI_CATEGORY")
grep -qF -e '--category' "$CI_DIR/tools/test.sh" 2>/dev/null \
  || { CARGS=(); dd_log "WARNING main-ci: $CI_DIR/tools/test.sh has no --category — Diagnostics tests are NOT excluded at this SHA"; }
"$CI_DIR/tools/test.sh" editmode "${CARGS[@]}" --out "$OUT/editmode" --timeout-min "$ETMIN" > "$OUT/editmode.out" 2>&1 & CI_CHILD=$!; wait $CI_CHILD; EX=$?; CI_CHILD=""
PX=0; AXS=""
if [ "$SKIP_PLAY" -eq 0 ]; then
  "$CI_DIR/tools/test.sh" playmode --filter "$CORE_FILTER" "${CARGS[@]}" --out "$OUT/playmode" --timeout-min "$PTMIN" > "$OUT/playmode.out" 2>&1 & CI_CHILD=$!; wait $CI_CHILD; PX=$?; CI_CHILD=""
  # M-11: each `alone` test in its own Unity session (a fresh session is what an allocation test's frame counters need)
  for step in "${(@f)ALONE_STEPS}"; do
    [ -n "$step" ] || continue
    AID=${step%%$'\t'*}; AFILTER=${step#*$'\t'}
    "$CI_DIR/tools/test.sh" playmode --filter "$AFILTER" --out "$OUT/$AID" --timeout-min "$ATMIN" > "$OUT/$AID.out" 2>&1 < /dev/null & CI_CHILD=$!; wait $CI_CHILD; AX=$?; CI_CHILD=""
    AXS="$AXS $AID=$AX"
  done
fi
dd_log "editmode exit=$EX playmode exit=$PX${AXS:+ alone:$AXS}"

# 5) user data: restore anything a test touched
inventory > "$OUT/userdata-after.txt"
UDWARN=""
# G4 D7 (b, V-S2): the guard is the one writer of its own files (prototype-profile.json*, the UI json files) — they are
# NOT copied back here (that raced the guard's restore); only files OUTSIDE the guard set that the run created are moved
# to userdata-created/; one it changed is named in the warning, not undone.
CI_GUARD_FILES=(prototype-profile.json ui-settings.json load-timing.json input-settings.json input-overrides.json ui-hints.json)
ci_guard_file() { [[ "$1" != */* ]] && { (( ${CI_GUARD_FILES[(Ie)$1]} )) || [[ "$1" == prototype-profile.json.* ]]; }; }
if ! cmp -s "$OUT/userdata-before.txt" "$OUT/userdata-after.txt"; then
  UDCHG=(${(f)"$(diff "$OUT/userdata-before.txt" "$OUT/userdata-after.txt" | grep -E '^[<>]' | sed -E 's/^[<>] [^ ]+  //' | LC_ALL=C sort -u)"})
  UDMOVED=(); UDKEPT=(); UDGUARD=()
  for rel in $UDCHG; do
    if ci_guard_file "$rel"; then UDGUARD+=("$rel")
    elif [ -f "$PD/$rel" ] && ! grep -qF -x -e "  $rel" <(sed -E 's/^[^ ]+//' "$OUT/userdata-before.txt"); then   # not there before the run (review N1)
      mkdir -p "$OUT/userdata-created/${rel:h}" && mv "$PD/$rel" "$OUT/userdata-created/$rel" && UDMOVED+=("$rel")
    else UDKEPT+=("$rel"); fi
  done
  UDWARN="player folder changed during the tests: guard files ${UDGUARD:-none} (the guard restores them, below)"
  [ ${#UDMOVED} -gt 0 ] && UDWARN="$UDWARN; created outside the guard set, moved to $OUT/userdata-created: ${(j:;:)UDMOVED}"
  [ ${#UDKEPT} -gt 0 ] && UDWARN="$UDWARN; changed or removed outside the guard set, NOT restored: ${(j:;:)UDKEPT} (pre-run copy: $UD)"
  dd_log "WARNING userdata: $UDWARN"
fi

# WP-066K: Editor tests can only write the Editor area (player folder + Editor PlayerPrefs domain); a
# change in the player area (bundle folder profile, player domain) is someone else's — listed, not undone.
UG="$CI_GUARD_DIR"
ci_guard_restore_safe || exit 65; GX=$CI_GUARD_EXIT
if ! cmp -s "$UG/before-editor.txt" "$UG/after-run-editor.txt" || [ "$GX" -ne 0 ]; then
  GW="Editor PlayerPrefs/profile changed during the tests: restored to the pre-run snapshot ($([ "$GX" -eq 0 ] && { [ "$CI_GUARD_RAW" -eq 32 ] && echo "verified; out-of-scope change → see below" || echo verified; } || echo "RESTORE MISMATCH exit $GX")) — $UG"
  UDWARN="${UDWARN:+$UDWARN; }$GW"; dd_log "WARNING userdata: $GW"
fi
if [ -s "$UG/out-of-scope-changed.txt" ]; then
  GW="player area (bundle profile / player PlayerPrefs) changed during the tests — not by the Editor, NOT restored: $UG/out-of-scope-changed.txt; writers (WP-066M): $(grep -F -f <(sed -e 's/\$.\\n.$//' -e 's/^player  [^ ]*  //' -e 's/ (launch-counter keys excluded)$//' "$UG/out-of-scope-changed.txt") "$UG/attribution.txt" 2>/dev/null | cut -f1,3 | sed "s|$HOME/||" | tr '\n' ';')"
  UDWARN="${UDWARN:+$UDWARN; }$GW"; dd_log "WARNING userdata: $GW"
fi

# 6) summary
SARGS=(); [ -n "$UDWARN" ] && SARGS=(--userdata-warning "$UDWARN")
python3 "$DD_TOOLS_DIR/ci_summary.py" "$OUT" "$SHA" "$OUT_ROOT" "$DD_TOOLS_DIR/ci-owners.tsv" "${SARGS[@]}" \
  --env-scrubbed "$DD_ENV_SCRUBBED" --driver-head "$CI_DRIVER_HEAD" --tools-tree "$CI_TOOLS_TREE" | tee -a "$DD_TIMELINE"
SX=${pipestatus[1]}
[ "$CI_GUARD_EXIT" -eq 0 ] || exit "$CI_GUARD_EXIT"
[ "$SX" -eq 0 ] && exit 0
grep -q '"compileErrors": \[\]' "$OUT/summary.json" && [ -f "$OUT/editmode/editmode.xml" ] && exit 1
exit 2
