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

SHA=$(git -C "$DD_REPO" rev-parse --verify "$REF^{commit}" 2>/dev/null) || { echo "unknown commit: $REF" >&2; exit 64; }
OUT="$OUT_ROOT/$SHA"; mkdir -p "$OUT"; DD_TIMELINE="$OUT/timeline.txt"
export DD_SLOT_TAG="main-ci"
dd_log "main-ci sha=$SHA ci=$CI_DIR out=$OUT"

# 1) CI worktree on the commit (detached; created once, then checkout only)
if [ ! -e "$CI_DIR/.git" ]; then
  mkdir -p "${CI_DIR:h}"
  git -C "$DD_REPO" worktree add --detach "$CI_DIR" "$SHA" >/dev/null 2>&1 || { dd_log "worktree add failed"; exit 3; }
else
  DIRTY=$(git -C "$CI_DIR" status --porcelain --untracked-files=no | head -5)
  [ -z "$DIRTY" ] || { dd_log "CI worktree not clean: $DIRTY"; exit 3; }
  git -C "$CI_DIR" checkout -q --detach "$SHA" || { dd_log "checkout failed"; exit 3; }
fi
git -C "$CI_DIR" lfs pull >/dev/null 2>&1 || true
dd_log "ci worktree at $(git -C "$CI_DIR" rev-parse --short=10 HEAD)"

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
"$DD_TOOLS_DIR/userdata-guard.sh" snapshot "$OUT/userdata-guard" > "$OUT/userdata-guard-snapshot.out" 2>&1 || dd_log "WARNING userdata: guard snapshot failed (exit $?, a live Player holds the profile?) — $OUT/userdata-guard-snapshot.out"

# 4) tests (the CI worktree's own tools: they belong to the commit under test)
"$CI_DIR/tools/test.sh" editmode --out "$OUT/editmode" --timeout-min "$ETMIN" > "$OUT/editmode.out" 2>&1; EX=$?
PX=0
if [ "$SKIP_PLAY" -eq 0 ]; then
  SUITES=$(grep -vE '^\s*(#|$)' "$DD_TOOLS_DIR/ci-suites.txt" | paste -sd'|' -)
  "$CI_DIR/tools/test.sh" playmode --filter "\\.($SUITES)\\." --out "$OUT/playmode" --timeout-min "$PTMIN" > "$OUT/playmode.out" 2>&1; PX=$?
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
"$DD_TOOLS_DIR/userdata-guard.sh" restore "$UG" --scope editor > "$OUT/userdata-guard.out" 2>&1; GX=$?
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
[ "$SX" -eq 0 ] && exit 0
grep -q '"compileErrors": \[\]' "$OUT/summary.json" && [ -f "$OUT/editmode/editmode.xml" ] && exit 1
exit 2
