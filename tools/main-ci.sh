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
CI_PLAYER_HELD=0; CI_GUARD_READY=0; CI_GUARD_EXIT=0
# Diagnostic-only adapter: exact paths from bound guard TSV or legacy diff text.
# This never interprets writer text as a command or changes guard/CI verdicts.
ci_other_writers() {
  python3 - "$1" "$2" "$3" <<'PY_OTHER_WRITERS'
from pathlib import Path
import re, sys

report, attribution, home = sys.argv[1:]
caption = ' (launch-counter keys excluded)'
digest = re.compile(r'(?:[0-9a-f]{64}|MISSING)\Z')
try:
    changes = []
    seen = set()
    for number, line in enumerate(Path(report).read_text().split('\n'), 1):
        if not line.strip():
            continue
        if re.match(r'(?:editor|player)\t(?:added|removed|modified)\t', line):
            area, kind, tail = line.split('\t', 2)
            path, before, after = tail.rsplit('\t', 2)
            if area not in {'editor', 'player'} or kind not in {'added', 'removed', 'modified'}:
                raise ValueError('invalid TSV area/kind at row ' + str(number))
            if kind == 'added':
                valid = before == 'ABSENT' and bool(digest.fullmatch(after))
            elif kind == 'removed':
                valid = after == 'ABSENT' and bool(digest.fullmatch(before))
            else:
                valid = bool(digest.fullmatch(before) and digest.fullmatch(after)) and before != after
            if not valid:
                raise ValueError('invalid TSV delta at row ' + str(number))
        else:
            match = re.fullmatch(r'(editor|player)  ([0-9a-f]{64}|MISSING)  (.+)', line)
            if not match:
                raise ValueError('invalid legacy OTHER row ' + str(number))
            area, _, path = match.groups()
            kind = 'legacy'
        if path.endswith('.plist' + caption):
            path = path[:-len(caption)]
        if not Path(path).is_absolute() or any(c in path for c in '\x00\r\n'):
            raise ValueError('invalid OTHER path at row ' + str(number))
        if path in seen:
            raise ValueError('duplicate OTHER path at row ' + str(number))
        seen.add(path)
        changes.append((path, kind))
    try:
        attributed = Path(attribution).read_text().split('\n')
    except (OSError, UnicodeError):
        attributed = []
    output = []
    for path, kind in changes:
        writers = []
        for row in attributed:
            # A literal path boundary avoids prefix, substring and regex matches.
            if not row.startswith(path + '\t'):
                continue
            fields = row[len(path) + 1:].split('\t', 1)
            if len(fields) == 2 and fields[1] and (fields[0] == 'removed' or re.fullmatch(r'mtime (?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9]Z', fields[0])):
                value = 'removed (writer UNKNOWN)' if fields == ['removed', '-'] else fields[1]
                if value not in writers:
                    writers.append(value)
        if not writers:
            writers = ['removed (writer UNKNOWN; no attribution record)' if kind == 'removed'
                       else 'writer UNKNOWN (no attribution record)']
        home_prefix = home.rstrip('/') + '/'
        display = path[len(home_prefix):] if home and path.startswith(home_prefix) else path
        output.append(display + '\t' + '; '.join(writers))
    print(';'.join(output), end='')
except (OSError, UnicodeError, ValueError) as error:
    print('writer attribution UNKNOWN (' + str(error) + ')', end='')
PY_OTHER_WRITERS
}
ci_guard_restore() {
  "$DD_TOOLS_DIR/userdata-guard.sh" restore "$OUT/userdata-guard" --scope editor > "$OUT/userdata-guard.out" 2>&1
  CI_GUARD_EXIT=$?; CI_GUARD_READY=0
  if [ "$CI_GUARD_EXIT" -eq 0 ] && ! grep -q 'restore scope=editor .*verified=1' "$OUT/userdata-guard.out"; then CI_GUARD_EXIT=31; fi
}
ci_cleanup() {
  local original=$1 slot_exit=0 lease_exit=0
  trap - EXIT
  [ "$CI_GUARD_READY" -eq 0 ] || ci_guard_restore
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
ci_guard_restore; GX=$CI_GUARD_EXIT
if ! cmp -s "$UG/before-editor.txt" "$UG/after-run-editor.txt" || [ "$GX" -ne 0 ]; then
  GW="Editor PlayerPrefs/profile changed during the tests: restored to the pre-run snapshot ($([ "$GX" -eq 0 ] && echo verified || echo "RESTORE MISMATCH exit $GX")) — $UG"
  UDWARN="${UDWARN:+$UDWARN; }$GW"; dd_log "WARNING userdata: $GW"
fi
if [ -s "$UG/out-of-scope-changed.txt" ]; then
  GW="player area (bundle profile / player PlayerPrefs) changed during the tests — not by the Editor, NOT restored: $UG/out-of-scope-changed.txt; writers (WP-066M): $(ci_other_writers "$UG/out-of-scope-changed.txt" "$UG/attribution.txt" "$HOME")"
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
