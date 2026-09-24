#!/bin/zsh
# WP-066J · tools/userdata-guard.sh snapshot|restore <dir>
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
# Exit: 0 ok · 30 restore did not reproduce the snapshot or a PlayerPrefs key not in it remains
# (<dir>/extra-keys.txt) · 64 usage
set -u
source "${0:A:h}/dd-env.sh"
CMD="${1:-}"; DIR="${2:-}"
[[ "$CMD" == (snapshot|restore) && -n "$DIR" ]] || { echo "usage: userdata-guard.sh snapshot|restore <dir>" >&2; exit 64; }

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
state() {
  { guarded "$D1" "${FILES1[@]}"; guarded "$D2" prototype-profile.json; } | while IFS= read -r f; do echo "$(dd_sha "$f")  $f"; done
  for f in "$P1" "$P2"; do echo "$(plist_digest "$f")  $f (launch-counter keys excluded)"; done
}

if [ "$CMD" = snapshot ]; then
  [ -e "$SNAP" ] && { echo "userdata-guard: $SNAP exists — one snapshot per directory" >&2; exit 64; }
  mkdir -p "$SNAP/D1" "$SNAP/D2" "$SNAP/PR"
  settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
  guarded "$D1" "${FILES1[@]}" | while IFS= read -r f; do cp -p "$f" "$SNAP/D1/"; done
  guarded "$D2" prototype-profile.json | while IFS= read -r f; do cp -p "$f" "$SNAP/D2/"; done
  [ -f "$P1" ] && cp -p "$P1" "$SNAP/PR/p1.plist"; [ -f "$P2" ] && cp -p "$P2" "$SNAP/PR/p2.plist"
  state > "$DIR/before.txt"
  dd_log "userdata-guard snapshot → $SNAP"
  exit 0
fi

[ -f "$DIR/before.txt" ] || { echo "userdata-guard: no snapshot in $DIR" >&2; exit 64; }
settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
state > "$DIR/after-run.txt"
if ! cmp -s "$DIR/before.txt" "$DIR/after-run.txt"; then
  mkdir -p "$DIR/created/D1" "$DIR/created/D2"
  # files the run created (not in the snapshot) are moved out, never deleted
  guarded "$D1" "${FILES1[@]}" | while IFS= read -r f; do [ -f "$SNAP/D1/${f:t}" ] || mv "$f" "$DIR/created/D1/"; done
  guarded "$D2" prototype-profile.json | while IFS= read -r f; do [ -f "$SNAP/D2/${f:t}" ] || mv "$f" "$DIR/created/D2/"; done
  for f in "$SNAP/D1"/*(N); do cp -p "$f" "$D1/"; done
  for f in "$SNAP/D2"/*(N); do cp -p "$f" "$D2/"; done
  for pair in "$DOM1|$SNAP/PR/p1.plist" "$DOM2|$SNAP/PR/p2.plist"; do
    dom="${pair%%|*}"; back="${pair#*|}"
    extra_keys "$dom" "$back" | while IFS= read -r k; do
      [ -n "$k" ] && defaults delete "$dom" "$k" >/dev/null 2>&1 && dd_log "userdata-guard: removed run-added key '$k' from $dom"
    done
    [ -f "$back" ] && defaults import "$dom" "$back"
  done
  sleep 1; settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
fi
state > "$DIR/after.txt"
{ extra_keys "$DOM1" "$SNAP/PR/p1.plist" | sed "s|^|$DOM1  |"; extra_keys "$DOM2" "$SNAP/PR/p2.plist" | sed "s|^|$DOM2  |"; } > "$DIR/extra-keys.txt"
OK=1; cmp -s "$DIR/before.txt" "$DIR/after.txt" || OK=0; [ -s "$DIR/extra-keys.txt" ] && OK=0
dd_log "userdata-guard restore touched=$(cmp -s "$DIR/before.txt" "$DIR/after-run.txt" && echo 0 || echo 1) verified=$OK extraKeys=$(grep -c . "$DIR/extra-keys.txt")"
[ "$OK" -eq 1 ] || { diff "$DIR/before.txt" "$DIR/after.txt" | grep '^[<>]' >&2; cat "$DIR/extra-keys.txt" >&2; exit 30; }
exit 0
