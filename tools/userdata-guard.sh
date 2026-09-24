#!/bin/zsh
# WP-066J · tools/userdata-guard.sh snapshot|restore <dir> [--scope all|editor|player]
#
# The one user-data guard standard (C-00, 2026-09-24): take a snapshot right before the run,
# restore to THAT snapshot afterwards and remove whatever the run added. Never a fixed old
# baseline (the private wp-0xx/tools/userdata.sh copies restore a 2026-09-14 copy), never a
# deletion (files the run created are moved into <dir>/created/).
#
#   snapshot <dir>   copies the real user data into <dir>/snap and writes <dir>/before.txt
#   restore  <dir>   puts <dir>/snap back and verifies against before.txt → <dir>/after.txt
#
# Covered: both prototype-profile.json (Editor/player folder + player bundle folder), the
# .bak/.corrupt side files, ui-settings / load-timing / input-settings / input-overrides /
# ui-hints .json, and both PlayerPrefs plists. `defaults import` MERGES, so a plist is restored
# exactly: keys not in the snapshot are `defaults delete`d first, then the snapshot is imported;
# a plist that did not exist loses every key. Unity's launch counter (unity.player_session_count /
# unity.player_sessionid, bumped by every player launch incl. other sessions') is left alone and
# left out of the plist digest. The .lock lease is a live process file: never copied or compared.
# WP-066K --scope (restore only; snapshot always takes everything): who the run can write.
#   editor = Editor area only: the Editor/player folder (D1: Editor profile + UI json files) and the
#            Editor PlayerPrefs domain (unity.<company>.<product>) — use it for Editor test runs.
#   player = player area only: the bundle folder profile + side files (D2) and the player domain
#            (com.<bundle id>).  all (default) = both — use it for player runs (smoke, captures).
#   A change outside the scope was made by someone else (a concurrent smoke, the user playing): it is
#   NOT restored, only listed in <dir>/out-of-scope-changed.txt and logged as a warning (exit stays 0).
# Exit: 0 ok · 30 restore did not reproduce the snapshot or a PlayerPrefs key not in it remains
# (<dir>/extra-keys.txt) · 64 usage
set -u
source "${0:A:h}/dd-env.sh"
CMD="${1:-}"; DIR="${2:-}"; SCOPE=all
[ "${3:-}" = "--scope" ] && SCOPE="${4:-}"
[[ "$CMD" == (snapshot|restore) && -n "$DIR" && "$SCOPE" == (all|editor|player) ]] || { echo "usage: userdata-guard.sh snapshot|restore <dir> [--scope all|editor|player]" >&2; exit 64; }

: "${DD_TIMELINE:=$DIR/timeline.txt}"; mkdir -p "$DIR"
AS="$HOME/Library/Application Support"; PR="$HOME/Library/Preferences"
DOM1="$DD_BUNDLE_ID"; DOM2="unity.Deaths Demolition.$DD_PRODUCT"
# Self-test only: DD_GUARD_ROOT redirects both folders and both plists (absolute-path defaults domains).
if [ -n "${DD_GUARD_ROOT:-}" ]; then AS="$DD_GUARD_ROOT/AS"; PR="$DD_GUARD_ROOT/PR"; DOM1="$PR/$DOM1"; DOM2="$PR/$DOM2"; fi
D1="$AS/Deaths Demolition/$DD_PRODUCT"; D2="$AS/$DD_BUNDLE_ID"
P1="$PR/$DD_BUNDLE_ID.plist"; P2="$PR/unity.Deaths Demolition.$DD_PRODUCT.plist"
FILES1=(prototype-profile.json ui-settings.json load-timing.json input-settings.json input-overrides.json ui-hints.json)
UNITY_VOLATILE="unity.player_session_count unity.player_sessionid"
SNAP="$DIR/snap"

# Guarded files of one folder: the fixed names plus the profile's side files (.lock excluded).
guarded() { local d="$1"; shift; local n
  for n in "$@"; do [ -f "$d/$n" ] && echo "$d/$n"; done
  [ -d "$d" ] && find "$d" -maxdepth 1 -name 'prototype-profile.json.*' ! -name '*.lock' 2>/dev/null | LC_ALL=C sort
}
plist_digest() { [ -f "$1" ] || { echo MISSING; return; }
  python3 - "$1" "$UNITY_VOLATILE" <<'PY'
import hashlib, json, plistlib, sys
d = plistlib.load(open(sys.argv[1], "rb"))
for k in sys.argv[2].split(): d.pop(k, None)
print(hashlib.sha256(json.dumps(d, sort_keys=True, default=repr).encode()).hexdigest())
PY
}
PLIST_SAME='import plistlib, sys
raw = sys.stdin.buffer.read()
live = plistlib.loads(raw) if raw.strip() else None
try:
    disk = plistlib.load(open(sys.argv[1], "rb"))
except FileNotFoundError:
    disk = None
sys.exit(0 if (live or None) == (disk or None) else 1)'
# cfprefsd writes lazily: wait (≤ 10 s) until the file on disk mirrors `defaults export`.
settle() { local i=0
  while [ $i -lt 50 ]; do defaults export "$1" - 2>/dev/null | python3 -c "$PLIST_SAME" "$2" && return 0; sleep 0.2; i=$((i+1)); done
  dd_log "userdata-guard: $1 not settled on disk after 10 s"; return 1
}
EXTRA_KEYS='import plistlib, sys, os
raw = sys.stdin.buffer.read()
live = plistlib.loads(raw) if raw.strip() else {}
back = plistlib.load(open(sys.argv[1], "rb")) if os.path.isfile(sys.argv[1]) else {}
for k in sorted(set(live) - set(back) - set(sys.argv[2].split())): print(k)'
extra_keys() { defaults export "$1" - 2>/dev/null | python3 -c "$EXTRA_KEYS" "$2" "$UNITY_VOLATILE"; }
# state <area>: editor = D1 files + Editor domain plist (P2); player = D2 files + player domain plist (P1).
state_of() { local f
  if [ "$1" = editor ]; then guarded "$D1" "${FILES1[@]}" | while IFS= read -r f; do echo "$(dd_sha "$f")  $f"; done
    echo "$(plist_digest "$P2")  $P2 (launch-counter keys excluded)"
  else guarded "$D2" prototype-profile.json | while IFS= read -r f; do echo "$(dd_sha "$f")  $f"; done
    echo "$(plist_digest "$P1")  $P1 (launch-counter keys excluded)"; fi
}
state() { state_of editor; state_of player; }
AREAS=(editor player); [ "$SCOPE" != all ] && AREAS=("$SCOPE")
in_scope() { [[ " ${AREAS[*]} " == *" $1 "* ]]; }

if [ "$CMD" = snapshot ]; then
  [ -e "$SNAP" ] && { echo "userdata-guard: $SNAP exists — one snapshot per directory" >&2; exit 64; }
  mkdir -p "$SNAP/D1" "$SNAP/D2" "$SNAP/PR"
  settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
  guarded "$D1" "${FILES1[@]}" | while IFS= read -r f; do cp -p "$f" "$SNAP/D1/"; done
  guarded "$D2" prototype-profile.json | while IFS= read -r f; do cp -p "$f" "$SNAP/D2/"; done
  [ -f "$P1" ] && cp -p "$P1" "$SNAP/PR/p1.plist"; [ -f "$P2" ] && cp -p "$P2" "$SNAP/PR/p2.plist"
  state > "$DIR/before.txt"; state_of editor > "$DIR/before-editor.txt"; state_of player > "$DIR/before-player.txt"
  dd_log "userdata-guard snapshot → $SNAP"
  exit 0
fi

[ -f "$DIR/before.txt" ] || { echo "userdata-guard: no snapshot in $DIR" >&2; exit 64; }
# A snapshot taken by the WP-066J guard has only before.txt: split it by area (D1 + P2 = editor).
if [ ! -f "$DIR/before-editor.txt" ]; then
  grep -F -e "  $D1/" -e "  $P2 " "$DIR/before.txt" > "$DIR/before-editor.txt"
  grep -F -v -e "  $D1/" -e "  $P2 " "$DIR/before.txt" > "$DIR/before-player.txt"
fi
settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
state > "$DIR/after-run.txt"
: > "$DIR/out-of-scope-changed.txt"
for a in editor player; do
  state_of $a > "$DIR/after-run-$a.txt"
  in_scope $a || cmp -s "$DIR/before-$a.txt" "$DIR/after-run-$a.txt" || diff "$DIR/before-$a.txt" "$DIR/after-run-$a.txt" | grep '^>' | sed "s|^> |$a  |" >> "$DIR/out-of-scope-changed.txt"
done
[ -s "$DIR/out-of-scope-changed.txt" ] && dd_log "WARNING userdata-guard: the $( in_scope editor && echo player || echo editor ) area changed during the run and was NOT restored (--scope $SCOPE: someone else's run or the user) — $DIR/out-of-scope-changed.txt"
TOUCHED=0; for a in "${AREAS[@]}"; do cmp -s "$DIR/before-$a.txt" "$DIR/after-run-$a.txt" || TOUCHED=1; done
if [ "$TOUCHED" -eq 1 ]; then
  mkdir -p "$DIR/created/D1" "$DIR/created/D2"
  # files the run created (not in the snapshot) are moved out, never deleted
  if in_scope editor; then
    guarded "$D1" "${FILES1[@]}" | while IFS= read -r f; do [ -f "$SNAP/D1/${f:t}" ] || mv "$f" "$DIR/created/D1/"; done
    for f in "$SNAP/D1"/*(N); do cp -p "$f" "$D1/"; done
  fi
  if in_scope player; then
    guarded "$D2" prototype-profile.json | while IFS= read -r f; do [ -f "$SNAP/D2/${f:t}" ] || mv "$f" "$DIR/created/D2/"; done
    for f in "$SNAP/D2"/*(N); do cp -p "$f" "$D2/"; done
  fi
  PAIRS=(); in_scope player && PAIRS+=("$DOM1|$SNAP/PR/p1.plist"); in_scope editor && PAIRS+=("$DOM2|$SNAP/PR/p2.plist")
  for pair in "${PAIRS[@]}"; do
    dom="${pair%%|*}"; back="${pair#*|}"
    extra_keys "$dom" "$back" | while IFS= read -r k; do
      [ -n "$k" ] && defaults delete "$dom" "$k" >/dev/null 2>&1 && dd_log "userdata-guard: removed run-added key '$k' from $dom"
    done
    [ -f "$back" ] && defaults import "$dom" "$back"
  done
  sleep 1; settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
fi
state > "$DIR/after.txt"
{ in_scope player && extra_keys "$DOM1" "$SNAP/PR/p1.plist" | sed "s|^|$DOM1  |"
  in_scope editor && extra_keys "$DOM2" "$SNAP/PR/p2.plist" | sed "s|^|$DOM2  |"; } > "$DIR/extra-keys.txt"
OK=1; for a in "${AREAS[@]}"; do state_of $a > "$DIR/after-$a.txt"; cmp -s "$DIR/before-$a.txt" "$DIR/after-$a.txt" || OK=0; done
[ -s "$DIR/extra-keys.txt" ] && OK=0
dd_log "userdata-guard restore scope=$SCOPE touched=$TOUCHED verified=$OK extraKeys=$(grep -c . "$DIR/extra-keys.txt") outOfScopeChanged=$(grep -c . "$DIR/out-of-scope-changed.txt")"
[ "$OK" -eq 1 ] || { for a in "${AREAS[@]}"; do diff "$DIR/before-$a.txt" "$DIR/after-$a.txt" | grep '^[<>]' >&2; done; cat "$DIR/extra-keys.txt" >&2; exit 30; }
exit 0
