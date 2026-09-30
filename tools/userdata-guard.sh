#!/bin/zsh
# WP-066J · tools/userdata-guard.sh snapshot|restore <dir> [--scope all|editor|player]
# WP-066N (§4.14 hole, 25.09: a snapshot taken during another session's live release-probe Player captured its transient
#   Screenmanager values, and the restore wrote them back "verified"): with scope player/all (the default)
#   · snapshot takes the single 'player' slot for the CALLER's shell (tools/slot.sh, DD_SLOT_OWNER) and holds it until
#     restore — no other Player or player snapshot can start in the window (Players launched inside it by the same shell
#     pass: the lock is re-entrant). Run snapshot → Player → restore from ONE shell/script;
#   · snapshot refuses (30) while any Deaths Demolition Player is alive (pgrep on the executable path);
#   · restore refuses (31, nothing written) when the snapshot's player PlayerPrefs deviate from the known-good display
#     values (DD_GUARD_KNOWN_GOOD, default fullscreen mode 1, 1920×1080, window 0,0; ui-settings untouched) — tell C-00.
#   main-ci snapshots with --scope editor (Editor tests cannot write the player area) and is unaffected.
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
# WP-066L (S-05 race find): a live process that holds a profile lock (prototype-profile.json.lock:
#   pid + name) can save while the guard copies. snapshot waits (≤ DD_GUARD_WAIT s, default 300) until no
#   live foreign process holds either lock, copies, then hashes the COPIES (before*.txt describe what
#   restore will put back) and re-checks the live files — a difference means a save landed mid-copy:
#   retake (3 tries). restore waits the same way before touching an area whose lock is held.
# WP-066M: while a snapshot is active a sampler records every running Player process every 5 s
#   (<dir>/players.log: utc, pid, command line — the .app path and -logFile name the session/run); it stops
#   on restore (flag file removed; nothing is killed) or after 6 h. restore attributes each changed file
#   (in or out of scope) to the Players alive at its mtime ± 10 s → <dir>/attribution.txt, one line per file.
# Exit (WP-066N): 30 also = snapshot refused, a Player is running · 31 = restore refused, snapshot deviates from the
#   known-good player display values.
# Exit: 0 ok · 75 busy (a live foreign lock holder / live data kept changing) — nothing restored in that area · 30 restore did not reproduce the snapshot or a PlayerPrefs key not in it remains
# (<dir>/extra-keys.txt) · 64 usage
# WP-079G D6a (C-00 30.09): (1) lock order is always player → unity: `snapshot … --scope player|all` refuses with 64
#   (nothing written, no slot taken) while the calling shell or one of its ancestors holds a `unity` slot (tools/slot.sh
#   holds; the pid chain from this guard, plus DD_SLOT_OWNER when set) — editor scope takes no player slot and is unaffected.
#   (2) `restore <dir>` appends its final line (the summary, or the refusal/error line) and `exit=<n>` to <dir>/restore.txt on
#   every normal exit once <dir> is known (a guard killed by TERM/KILL writes nothing; HUP records exit=1), and every guard
#   line also goes to <dir>/timeline.txt whatever DD_TIMELINE says.
set -u
source "${0:A:h}/dd-env.sh"
# Codex-audit M-02(e) (C-00 30.09): argv is exact — `snapshot|restore <dir>` or `… <dir> --scope <s>`; anything else
# (--scope=player, a stray or extra argument) is a usage error before anything is touched, never a silent scope=all.
{ [ $# -eq 2 ] || { [ $# -eq 4 ] && [ "$3" = "--scope" ]; }; } || { echo "usage: userdata-guard.sh snapshot|restore <dir> [--scope all|editor|player]" >&2; exit 64; }
CMD="${1:-}"; DIR="${2:-}"; SCOPE=all
[ "${3:-}" = "--scope" ] && SCOPE="${4:-}"
PLAYER_RE="${DD_GUARD_PLAYER_RE:-Contents/MacOS/(Deaths Demolition|DeathsDemolition)}"
KNOWN_GOOD="${DD_GUARD_KNOWN_GOOD:-Screenmanager Fullscreen mode=1;Screenmanager Resolution Width=1920;Screenmanager Resolution Height=1080;Screenmanager Window Position X=0;Screenmanager Window Position Y=0}"
[[ "$CMD" == (snapshot|restore) && -n "$DIR" && "$SCOPE" == (all|editor|player) ]] || { echo "usage: userdata-guard.sh snapshot|restore <dir> [--scope all|editor|player]" >&2; exit 64; }

# WP-079G D6a (1): checked before anything is written or acquired.
if [ "$CMD" = snapshot ] && [ "$SCOPE" != editor ] && [ -x "$DD_SLOT" ]; then
  # only slot.sh's own "SLOT … held by …" answer counts (an older slot.sh without `holds` prints something else → not held)
  UH=$("$DD_SLOT" holds unity $$ 2>/dev/null); [[ "$UH" == "SLOT unity held by "* ]] || UH=""
  # D6a review SHOULD-1: the order is already right when an ancestor holds the player slot (re-entrant, e.g. a release
  # script's own window) — a unity slot below it cannot deadlock against this snapshot.
  if [ -n "$UH" ] && [[ "$("$DD_SLOT" holds player $$ 2>/dev/null)" == "SLOT player held by "* ]]; then UH=""; fi
  if [ -z "$UH" ] && [ -n "${DD_SLOT_OWNER:-}" ]; then
    UH=$("$DD_SLOT" holds unity "$DD_SLOT_OWNER" 2>/dev/null); [[ "$UH" == "SLOT unity held by "* ]] || UH=""; fi
  if [ -n "$UH" ]; then
    echo "userdata-guard: lock order — take the guard snapshot (player slot) BEFORE the Unity slot" >&2
    echo "userdata-guard: $UH — release it, snapshot, then take the Unity slot again (exit 64); an Editor PlayMode run needs only --scope editor" >&2
    exit 64
  fi
fi

: "${DD_TIMELINE:=$DIR/timeline.txt}"; mkdir -p "$DIR"
# WP-079G D6a (2): <dir>/timeline.txt is always the guard's own timeline (also when DD_TIMELINE points elsewhere);
# UG_LAST = the last line logged, for <dir>/restore.txt.
UG_TL="${DIR:A}/timeline.txt"; UG_LAST=""
# one timestamp per line, written identically to stdout, DD_TIMELINE and <dir>/timeline.txt (D6a review SHOULD-3)
dd_log() { UG_LAST="$(date -u +%FT%TZ) $*"; print -r -- "$UG_LAST"
  [ -n "$DD_TIMELINE" ] && print -r -- "$UG_LAST" >> "$DD_TIMELINE"
  [ "${DD_TIMELINE:A}" = "$UG_TL" ] || print -r -- "$UG_LAST" >> "$UG_TL"; return 0; }
AS="$HOME/Library/Application Support"; PR="$HOME/Library/Preferences"
DOM1="$DD_BUNDLE_ID"; DOM2="unity.Deaths Demolition.$DD_PRODUCT"
# Self-test only: DD_GUARD_ROOT redirects both folders and both plists (absolute-path defaults domains).
if [ -n "${DD_GUARD_ROOT:-}" ]; then AS="$DD_GUARD_ROOT/AS"; PR="$DD_GUARD_ROOT/PR"; DOM1="$PR/$DOM1"; DOM2="$PR/$DOM2"; fi
D1="$AS/Deaths Demolition/$DD_PRODUCT"; D2="$AS/$DD_BUNDLE_ID"
P1="$PR/$DD_BUNDLE_ID.plist"; P2="$PR/unity.Deaths Demolition.$DD_PRODUCT.plist"
FILES1=(prototype-profile.json ui-settings.json load-timing.json input-settings.json input-overrides.json ui-hints.json)
UNITY_VOLATILE="unity.player_session_count unity.player_sessionid"
SNAP="$DIR/snap"

# pid of a LIVE process (not this one) holding <dir>/prototype-profile.json.lock, else nothing.
lock_holder() { local f="$1/prototype-profile.json.lock" pid name
  [ -f "$f" ] || return 0
  pid=$(sed -n 's/^pid=//p' "$f" | head -1); name=$(sed -n 's/^name=//p' "$f" | head -1)
  [[ "$pid" == <-> ]] && [ "$pid" != "$$" ] && kill -0 "$pid" 2>/dev/null || return 0
  # pid reuse: only a process whose command still carries the recorded name counts
  ps -o command= -p "$pid" 2>/dev/null | grep -qF -- "${name:-Deaths Demolition}" && echo "$pid"
}
# Waits until no live foreign process holds the locks of the given folders; 1 on timeout.
wait_unlocked() { local waited=0 h d
  while :; do h=""; for d in "$@"; do h+=$(lock_holder "$d"); done
    [ -z "$h" ] && return 0
    [ "$waited" -ge "${DD_GUARD_WAIT:-300}" ] && { dd_log "userdata-guard: profile lock still held by live pid $h after ${waited}s"; return 1; }
    [ "$waited" -eq 0 ] && dd_log "userdata-guard: profile lock held by live pid $h — waiting"
    sleep 5; waited=$((waited+5))
  done
}
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

# WP-066L: the state of the snapshot COPIES, labelled with the live paths (what restore puts back).
snap_state_of() { local f
  if [ "$1" = editor ]; then guarded "$D1" "${FILES1[@]}" | while IFS= read -r f; do echo "$(dd_sha "$SNAP/D1/${f:t}")  $f"; done
    echo "$(plist_digest "$SNAP/PR/p2.plist")  $P2 (launch-counter keys excluded)"
  else guarded "$D2" prototype-profile.json | while IFS= read -r f; do echo "$(dd_sha "$SNAP/D2/${f:t}")  $f"; done
    echo "$(plist_digest "$SNAP/PR/p1.plist")  $P1 (launch-counter keys excluded)"; fi
}

# WP-066N: player-slot window + known-good display values.
# Keys of the player domain plist ($1) that deviate from KNOWN_GOOD, one "key=actual (want expected)" per line.
KG_CHECK='import plistlib, sys, os
p, spec = sys.argv[1], sys.argv[2]
if not os.path.isfile(p): sys.exit(0)
d = plistlib.load(open(p, "rb"))
for kv in spec.split(";"):
    if "=" not in kv: continue
    k, want = kv.split("=", 1)
    if k in d and str(d[k]) != want: print("%s=%s (want %s)" % (k, d[k], want))'
kg_deviation(){ python3 -c "$KG_CHECK" "$1" "$KNOWN_GOOD"; }
release_player_slot(){ [ -f "$DIR/player-slot" ] || return 0; local t o; read -r t o < "$DIR/player-slot"
  DD_SLOT_OWNER="$o" "$DD_SLOT" release player "$t" >/dev/null 2>&1; rm -f "$DIR/player-slot"; }

if [ "$CMD" = snapshot ]; then
  [ -e "$SNAP" ] && { echo "userdata-guard: $SNAP exists — one snapshot per directory" >&2; exit 64; }
  if in_scope player; then
    PT="ug-$(printf '%s' "$DIR" | shasum | cut -c1-10)"; PO=$PPID
    DD_SLOT_OWNER=$PO "$DD_SLOT" acquire player "$PT" > "$DIR/player-slot.out" 2>&1 || { dd_log "userdata-guard: player slot not acquired — $DIR/player-slot.out"; exit 75; }
    printf '%s %s\n' "$PT" "$PO" > "$DIR/player-slot"
    dd_log "userdata-guard: $(tail -1 "$DIR/player-slot.out") (held until restore, owner pid $PO)"
    LIVE=$(pgrep -fl "$PLAYER_RE" 2>/dev/null)
    if [ -n "$LIVE" ]; then
      dd_log "userdata-guard: REFUSED — a Deaths Demolition Player is running; a snapshot now would capture its transient state: $(echo "$LIVE" | cut -c1-140 | tr '\n' ';')"
      release_player_slot; exit 30
    fi
    DEV=$(kg_deviation "$P1"); [ -n "$DEV" ] && dd_log "WARNING userdata-guard: player display values already deviate from known-good (restore will refuse): $(echo "$DEV" | tr '\n' ';')"
  fi
  # Codex-audit M-07 (C-00 30.09): the lock wait follows the scope — editor waits on D1 only (D2 is read in editor scope
  # only for the out-of-scope report, so a live Player holding the bundle profile must not stall main-ci); player/all as before.
  if [ "$SCOPE" = editor ]; then wait_unlocked "$D1" || exit 75; else wait_unlocked "$D1" "$D2" || exit 75; fi
  try=1
  while :; do
    rm -rf "$SNAP"; mkdir -p "$SNAP/D1" "$SNAP/D2" "$SNAP/PR"   # our own fresh copy only
    settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
    guarded "$D1" "${FILES1[@]}" | while IFS= read -r f; do cp -p "$f" "$SNAP/D1/"; done
    guarded "$D2" prototype-profile.json | while IFS= read -r f; do cp -p "$f" "$SNAP/D2/"; done
    [ -f "$P1" ] && cp -p "$P1" "$SNAP/PR/p1.plist"; [ -f "$P2" ] && cp -p "$P2" "$SNAP/PR/p2.plist"
    snap_state_of editor > "$DIR/before-editor.txt"; snap_state_of player > "$DIR/before-player.txt"
    cat "$DIR/before-editor.txt" "$DIR/before-player.txt" > "$DIR/before.txt"
    state > "$DIR/live-at-snapshot.txt"
    # M-12 review SHOULD-4: editor scope checks only its own area for stability (a Player saving to D2 is out of scope).
    if [ "$SCOPE" = editor ]; then grep -F -e "  $D1/" -e "  $P2 " "$DIR/live-at-snapshot.txt" | cmp -s "$DIR/before-editor.txt" - && break
    else cmp -s "$DIR/before.txt" "$DIR/live-at-snapshot.txt" && break; fi
    [ "$try" -ge 3 ] && { dd_log "userdata-guard: live data changed during the snapshot 3 times — $DIR/live-at-snapshot.txt"; exit 75; }
    dd_log "userdata-guard: a save landed during the snapshot (try $try) — retaking"; try=$((try+1)); sleep 2
  done
  # WP-066M: Player-process sampler for attribution (own background loop; ends when restore removes the flag).
  : > "$DIR/players.log"; touch "$DIR/.sampling"
  ( n=0; while [ -f "$DIR/.sampling" ] && [ $n -lt 4320 ]; do
      ps -axo pid=,command= 2>/dev/null | grep -E "Contents/MacOS/(Deaths Demolition|DeathsDemolition)" | grep -v grep \
        | while IFS= read -r l; do printf '%s\t%s\n' "$(date -u +%s)" "$l"; done >> "$DIR/players.log"
      sleep 5; n=$((n+1)); done ) < /dev/null > /dev/null 2>&1 &
  dd_log "userdata-guard snapshot → $SNAP"
  exit 0
fi

# WP-079G D6a (2): every exit of restore from here on is recorded; the player slot is released only once the restore proper
# has started (WP-066N), as before.
UG_RELEASE=0
restore_exit() { local rc=$?
  [ "$UG_RELEASE" -eq 1 ] && release_player_slot
  { print -r -- "${UG_LAST:-$(date -u +%FT%TZ) userdata-guard restore: no result line}"; print -r -- "exit=$rc"; } >> "$DIR/restore.txt"
}
trap 'restore_exit' EXIT
[ -f "$DIR/before.txt" ] || { UG_LAST="$(date -u +%FT%TZ) userdata-guard: no snapshot in $DIR"; echo "userdata-guard: no snapshot in $DIR" >&2; exit 64; }
# A snapshot taken by the WP-066J guard has only before.txt: split it by area (D1 + P2 = editor).
if [ ! -f "$DIR/before-editor.txt" ]; then
  grep -F -e "  $D1/" -e "  $P2 " "$DIR/before.txt" > "$DIR/before-editor.txt"
  grep -F -v -e "  $D1/" -e "  $P2 " "$DIR/before.txt" > "$DIR/before-player.txt"
fi
settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
rm -f "$DIR/.sampling"   # WP-066M: stops the sampler
UG_RELEASE=1   # WP-066N: the snapshot's player slot ends with this restore, whatever the outcome (restore_exit)
if in_scope player; then
  DEV=$(kg_deviation "$SNAP/PR/p1.plist")
  if [ -n "$DEV" ]; then
    echo "$DEV" > "$DIR/known-good-deviation.txt"
    dd_log "userdata-guard: REFUSED — the snapshot's player display values deviate from known-good; nothing restored, tell C-00: $(echo "$DEV" | tr '\n' ';')"
    exit 31
  fi
fi
LOCKDIRS=(); in_scope editor && LOCKDIRS+=("$D1"); in_scope player && LOCKDIRS+=("$D2")
wait_unlocked "${LOCKDIRS[@]}" || { dd_log "userdata-guard: NOT restored — a live process holds the profile (retry when it exits)"; exit 75; }
state > "$DIR/after-run.txt"
: > "$DIR/out-of-scope-changed.txt"
for a in editor player; do
  state_of $a > "$DIR/after-run-$a.txt"
  in_scope $a || cmp -s "$DIR/before-$a.txt" "$DIR/after-run-$a.txt" || diff "$DIR/before-$a.txt" "$DIR/after-run-$a.txt" | grep '^>' | sed "s|^> |$a  |" >> "$DIR/out-of-scope-changed.txt"
done
# WP-066M: who wrote each changed file — Players alive at its mtime (± 10 s), from the sampler.
python3 "${0:A:h}/userdata_attrib.py" "$DIR" || true
[ -s "$DIR/attribution.txt" ] && dd_log "userdata-guard: writers → $DIR/attribution.txt"
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
