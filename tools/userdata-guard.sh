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
# WP-066K --scope (snapshot copies both areas; restore uses the same recorded scope): who the run can write.
#   editor = Editor area only: the Editor/player folder (D1: Editor profile + UI json files) and the
#            Editor PlayerPrefs domain (unity.<company>.<product>) — use it for Editor test runs.
#   player = player area only: the bundle folder profile + side files (D2) and the player domain
#            (com.<bundle id>).  all (default) = both — use it for player runs (smoke, captures).
#   A change outside the scope was made by someone else (a concurrent smoke, the user playing): it is
#   NOT restored. Added/removed/modified paths are listed in <dir>/out-of-scope-changed.txt;
#   any such change makes the full guard verdict FAIL (exit 30), even when the scoped restore succeeds.
# WP-066L (S-05 race find): a live process that holds a profile lock (prototype-profile.json.lock:
#   pid + name) can save while the guard copies. snapshot waits (≤ DD_GUARD_WAIT s, default 300) until no
#   live foreign process holds either lock, copies, then hashes the COPIES (before*.txt describe what
#   restore will put back) and re-checks the live files — a difference means a save landed mid-copy:
#   retake (3 tries). restore waits the same way before touching an area whose lock is held.
# WP-066M: while a snapshot is active a sampler records every running Player process every 5 s
#   (<dir>/players.log: utc, pid, command line — the .app path and -logFile name the session/run); it stops
#   on restore (own PID/birth/UID closure observed after flag removal; nothing is killed) or after 6 h. restore attributes each changed file
#   (in or out of scope) to the Players alive at its mtime ± 10 s → <dir>/attribution.txt, one line per file.
# Exit (WP-066N): 30 also = snapshot refused, a Player is running · 31 = restore refused, snapshot deviates from the
#   known-good player display values.
# Exit: 0 ok · 75 busy (a live foreign lock holder / live data kept changing) — nothing restored in that area · 30 restore/verification failed, any OTHER area changed, evidence missing/corrupt, or own sampler closure unknown; a PlayerPrefs key not in the snapshot also fails
# (<dir>/extra-keys.txt) · 64 usage
set -u
GUARD_TOOLS="${0:A:h}"
source "$GUARD_TOOLS/dd-env.sh"
CMD="${1:-}"; DIR="${2:-}"; SCOPE=all
case $# in
  2) ;;
  4) [ "$3" = --scope ] || { echo 'userdata-guard: expected --scope' >&2; exit 64; }; SCOPE="$4" ;;
  *) echo 'usage: userdata-guard.sh snapshot|restore <dir> [--scope all|editor|player]' >&2; exit 64 ;;
esac
PLAYER_RE="${DD_GUARD_PLAYER_RE:-Contents/MacOS/(Deaths Demolition|DeathsDemolition)}"
KNOWN_GOOD="${DD_GUARD_KNOWN_GOOD:-Screenmanager Fullscreen mode=1;Screenmanager Resolution Width=1920;Screenmanager Resolution Height=1080;Screenmanager Window Position X=0;Screenmanager Window Position Y=0}"
[[ "$CMD" == (snapshot|restore) && -n "$DIR" && $# -le 4 && "$SCOPE" == (all|editor|player) ]] || { echo "usage: userdata-guard.sh snapshot|restore <dir> [--scope all|editor|player]" >&2; exit 64; }

: "${DD_TIMELINE:=$DIR/timeline.txt}"; mkdir -p "$DIR" || exit 30
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
# Checked state/defaults I/O and artifact provenance live in the stdlib audit helper.
audit(){ python3 "$GUARD_TOOLS/userdata_guard_audit.py" "$1" "$DIR" "$SCOPE" "$D1" "$D2" "$P1" "$P2" "${@:2}"; }
settle(){ audit plist-settle "$1" "$2"; }
AREAS=(editor player); [ "$SCOPE" != all ] && AREAS=("$SCOPE")
in_scope(){ [[ " ${AREAS[*]} " == *" $1 "* ]]; }
fail(){ local reason="$1" code="${2:-30}"
  rm -f "$DIR/.sampling"
  audit failure "$reason" || true
  dd_log "userdata-guard: FAIL — $reason"
  exit "$code"
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
  trap 'rm -f "$DIR/.sampling"; release_player_slot' EXIT
  if in_scope player; then
    PT="ug-$(printf '%s' "$DIR" | shasum | cut -c1-10)"; PO=$PPID
    DD_SLOT_OWNER=$PO "$DD_SLOT" acquire player "$PT" > "$DIR/player-slot.out" 2>&1 || fail 'player slot not acquired' 75
    printf '%s %s\n' "$PT" "$PO" > "$DIR/player-slot" || fail 'player slot receipt write failed'
    LIVE=$(pgrep -fl "$PLAYER_RE" 2>/dev/null)
    [ -z "$LIVE" ] || fail 'a Deaths Demolition Player is already running'
    DEV=$(kg_deviation "$P1") || fail 'invalid player preferences'
    [ -z "$DEV" ] || dd_log "WARNING userdata-guard: display values deviate from known-good: $DEV"
  fi
  wait_unlocked "$D1" "$D2" || fail 'profile lock busy' 75
  try=1
  while :; do
    settle "$DOM1" "$P1" && settle "$DOM2" "$P2" || fail 'snapshot preferences not settled'
    audit snapshot-copy || fail 'snapshot copy/state failed'
    cmp -s "$DIR/before.txt" "$DIR/live-at-snapshot.txt" && break
    [ "$try" -lt 3 ] || fail 'live data changed during snapshot three times' 75
    try=$((try+1)); sleep 2
  done
  : > "$DIR/players.log" && touch "$DIR/.sampling" || fail 'sampler flag/log creation failed'
  ( n=0; while [ -f "$DIR/.sampling" ] && [ $n -lt 4320 ]; do
      ps -axo pid=,command= 2>/dev/null | grep -E "Contents/MacOS/(Deaths Demolition|DeathsDemolition)" | grep -v grep \
        | while IFS= read -r l; do printf '%s\t%s\n' "$(date -u +%s)" "$l"; done >> "$DIR/players.log"
      sleep 5; n=$((n+1)); done ) < /dev/null > /dev/null 2>&1 &
  SAMPLER_PID=$!
  audit sampler-record "$SAMPLER_PID" && audit bind || fail 'snapshot/sampler binding failed'
  trap - EXIT
  dd_log "userdata-guard snapshot → $SNAP samplerPid=$SAMPLER_PID"
  exit 0
fi

trap 'rm -f "$DIR/.sampling"; release_player_slot' EXIT
# A legacy before.txt alone cannot establish the current guard protocol; never promote it to PASS.
audit validate || fail 'snapshot bindings missing or invalid'
rm -f "$DIR/.sampling" || fail 'sampler flag removal failed'
audit sampler-close || fail 'sampler closure missing or not observed'
settle "$DOM1" "$P1" && settle "$DOM2" "$P2" || fail 'restore preferences not settled'
if in_scope player; then
  DEV=$(kg_deviation "$SNAP/PR/p1.plist") || fail 'invalid snapshot player preferences'
  if [ -n "$DEV" ]; then
    print -r -- "$DEV" > "$DIR/known-good-deviation.txt"
    fail 'snapshot display values deviate from known-good; nothing restored' 31
  fi
fi
LOCKDIRS=(); in_scope editor && LOCKDIRS+=("$D1"); in_scope player && LOCKDIRS+=("$D2")
wait_unlocked "${LOCKDIRS[@]}" || fail 'profile lock busy; nothing restored' 75
audit state after-run && audit other || fail 'after-run/OTHER reports invalid'
python3 "${0:A:h}/userdata_attrib.py" "$DIR" || true  # attribution is explanatory, not a PASS binding
TOUCHED=0; for a in "${AREAS[@]}"; do cmp -s "$DIR/before-$a.txt" "$DIR/after-run-$a.txt" || TOUCHED=1; done
if [ "$TOUCHED" -eq 1 ]; then
  audit restore-files || fail 'scoped file restore failed'
  in_scope player && audit plist-restore "$DOM1" "$SNAP/PR/p1.plist" "$P1" || { in_scope player && fail 'player preference restore failed'; }
  in_scope editor && audit plist-restore "$DOM2" "$SNAP/PR/p2.plist" "$P2" || { in_scope editor && fail 'editor preference restore failed'; }
  settle "$DOM1" "$P1" && settle "$DOM2" "$P2" || fail 'restored preferences not settled'
fi
audit state after || fail 'final state reports invalid'
: > "$DIR/extra-keys.txt" || fail 'extra-keys report write failed'
if in_scope player; then
  audit plist-extras "$DOM1" "$SNAP/PR/p1.plist" "$P1" > "$DIR/extra-player.txt" || fail 'player extras unavailable'
  sed "s|^|$DOM1  |" "$DIR/extra-player.txt" >> "$DIR/extra-keys.txt" || fail 'extra-keys report failed'
fi
if in_scope editor; then
  audit plist-extras "$DOM2" "$SNAP/PR/p2.plist" "$P2" > "$DIR/extra-editor.txt" || fail 'editor extras unavailable'
  sed "s|^|$DOM2  |" "$DIR/extra-editor.txt" >> "$DIR/extra-keys.txt" || fail 'extra-keys report failed'
fi
if ! audit final; then
  dd_log "userdata-guard restore scope=$SCOPE touched=$TOUCHED verified=0 (full guard failed; see guard-verdict.json)"
  fail 'final guard audit failed'
fi
dd_log "userdata-guard restore scope=$SCOPE touched=$TOUCHED verified=1 extraKeys=0 outOfScopeChanged=0 samplerClosed=1"
exit 0
