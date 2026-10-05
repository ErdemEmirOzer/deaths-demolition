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
# G4 D5 (WP-089, C-00 05.10; design v3.1 §3 D5) — the snapshot half:
#   S0 (read-only, before any mkdir/trap/defaults): argv; the user root (Q7: DD_GUARD_ROOT absolute, existing, a directory,
#      not a symlink, not under the REAL passwd home's Library; DD_GUARD_ROOT empty ∧ realpath($HOME) ≠ passwd home → 64,
#      MUST-4 — restore checks the same); DIR a symlink, or DIR already holds snap/, before.txt or window.json, or a v1
#      trace (snapshot-manifest.json / sampler.json) → 64.
#   S1 mkdir + INT/TERM/HUP traps (every wait is a background job the trap stops; snap_fail on the next step).
#   S2 player ∈ scope: player slot (tag ug-<sha10 realpath DIR>-<nonce6>, DD_SLOT_TIMEOUT=DD_GUARD_SLOT_WAIT, default 4800 s)
#      → 75 on timeout; receipt = <tag> <owner> in player-slot + the holder line and start times in window.json.
#   S3 player ∈ scope: a live Player → 30.  S4 player ∈ scope: live player prefs off the known-good display values →
#      known-good-deviation.txt, 31 (the snapshot now refuses; restore keeps its own check).
#   S5 protected set: a symlinked folder or protected path, a case-folded name variant (Prototype-Profile.json) in D1/D2
#      or an unreadable plist → 65.  S7 profile lock held by a live Player/Unity → wait ≤ DD_GUARD_WAIT, then 75.
#   S8 copy (≤ 3 tries) into snap/ + before-*.txt.tmp + live-at-snapshot.txt; S9 window.json (temp + rename) and the
#      sampler (players.log as before + players-lineage.tsv); S10 commit: window.json, before-editor.txt,
#      before-player.txt and LAST before.txt (signals ignored for these renames); S11 the summary line, 0.
#   Every failure (signals too): snap_fail — sampler stopped, snap/ and the .tmp files moved to failed-<utc>/ (never
#      deleted; the same DIR can be retried), the player slot released, no before.txt (INV-8).
#   M1: the player area now also covers the bundle folder's UI json files (D2/FILES). A window without window.json
#      (taken by an older guard) keeps the legacy player set (profile family only) at restore, so files it never
#      recorded are not taken for run-created ones (INV-5).
#   M7: both summary lines end with root=<real|redirected> kgSource=<default|env> kg=<sha8> guardSha=<sha12> window=<id>.
#   DD_GUARD_PLAYER_RE is gone (fixed pattern); tests shim pgrep/ps on PATH.
set -u
source "${0:A:h}/dd-env.sh"
# Codex-audit M-02(e) (C-00 30.09): argv is exact — `snapshot|restore <dir>` or `… <dir> --scope <s>`; anything else
# (--scope=player, a stray or extra argument) is a usage error before anything is touched, never a silent scope=all.
{ [ $# -eq 2 ] || { [ $# -eq 4 ] && [ "$3" = "--scope" ]; }; } || { echo "usage: userdata-guard.sh snapshot|restore <dir> [--scope all|editor|player]" >&2; exit 64; }
CMD="${1:-}"; DIR="${2:-}"; SCOPE=all
[ "${3:-}" = "--scope" ] && SCOPE="${4:-}"
PLAYER_RE="Contents/MacOS/(Deaths Demolition|DeathsDemolition)"
KG_DEFAULT="Screenmanager Fullscreen mode=1;Screenmanager Resolution Width=1920;Screenmanager Resolution Height=1080;Screenmanager Window Position X=0;Screenmanager Window Position Y=0"
KNOWN_GOOD="${DD_GUARD_KNOWN_GOOD:-$KG_DEFAULT}"; KGSRC=default; [ -n "${DD_GUARD_KNOWN_GOOD:-}" ] && KGSRC=env   # Q3: env only by a written C-00 decision
[[ "$CMD" == (snapshot|restore) && -n "$DIR" && "$SCOPE" == (all|editor|player) ]] || { echo "usage: userdata-guard.sh snapshot|restore <dir> [--scope all|editor|player]" >&2; exit 64; }

# G4 D5 S0/R0 (read-only; nothing is written, no trap, no `defaults` before this passes)
IO="${0:A:h}/userdata_guard_io.py"
[ -f "$IO" ] || { echo "userdata-guard: $IO missing — refused (64)" >&2; exit 64; }
PWHOME=$(python3 "$IO" pwhome) || { echo "userdata-guard: passwd home unknown — refused (64)" >&2; exit 64; }
if [ -n "${DD_GUARD_ROOT:-}" ]; then
  WHY=$(python3 "$IO" root-check "$DD_GUARD_ROOT") || { echo "userdata-guard: $WHY — refused, nothing written (64)" >&2; exit 64; }
elif [ -z "${HOME:-}" ] || [ "${HOME:A}" != "$PWHOME" ] && ! python3 "$IO" same "${HOME:-/nonexistent}" "$PWHOME"; then
  # MUST-4 (C-00 05.10): files would come from the fake home while the named defaults domains read the REAL plists
  echo "userdata-guard: HOME=${HOME:-(unset)} is not the real home $PWHOME and DD_GUARD_ROOT is empty — refused, nothing written (64)" >&2; exit 64
fi
if [ "$CMD" = snapshot ]; then
  [ -L "$DIR" ] && { echo "userdata-guard: $DIR is a symlink — refused (64)" >&2; exit 64; }
  for f in snap before.txt window.json; do
    [ -e "$DIR/$f" ] && { echo "userdata-guard: $DIR/$f exists — one snapshot per directory (64)" >&2; exit 64; }
  done
  for f in snapshot-manifest.json sampler.json; do
    [ -e "$DIR/$f" ] && { echo "userdata-guard: $DIR holds a v1 (74f62dd6) snapshot ($f) — restore it with the guard that took it, tell C-00 (64)" >&2; exit 64; }
  done
fi

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
# M1: the player area covers the bundle folder's FILES too (v2); a window without window.json is legacy (profile only).
PLAYER_FILES=("${FILES1[@]}")
if [ "$CMD" = restore ] && ! grep -q '"protectedSet": *"v2"' "$DIR/window.json" 2>/dev/null; then PLAYER_FILES=(prototype-profile.json); fi
ROOTKIND=redirected
python3 "$IO" under "$AS" "$PWHOME/Library" && python3 "$IO" under "$PR" "$PWHOME/Library" && ROOTKIND=real
KG8=$(print -rn -- "$KNOWN_GOOD" | shasum -a 256 | cut -c1-8)
GUARDSHA=$(python3 "$IO" sha "${0:A}"); HELPERSHA=$(python3 "$IO" sha "$IO")
M7="root=$ROOTKIND kgSource=$KGSRC kg=$KG8 guardSha=${GUARDSHA[1,12]}"
UNITY_VOLATILE="unity.player_session_count unity.player_sessionid"
SNAP="$DIR/snap"

# pid of a LIVE process (not this one) holding <dir>/prototype-profile.json.lock, else nothing.
lock_holder() { local f="$1/prototype-profile.json.lock" pid name
  [ -f "$f" ] || return 0
  pid=$(sed -n 's/^pid=//p' "$f" | head -1); name=$(sed -n 's/^name=//p' "$f" | head -1)
  [[ "$pid" == <-> ]] && [ "$pid" != "$$" ] && kill -0 "$pid" 2>/dev/null || return 0
  # pid reuse: only a live Player or Unity counts (G4 D5 S7/S3: by the command, not by the recorded name)
  ps -o command= -p "$pid" 2>/dev/null | grep -qE -- "$PLAYER_RE|/Unity\.app/Contents/MacOS/Unity" && echo "$pid"
}
# G4 D5 §2.7: every wait is a background job (`cmd & WAITPID=$!; bg_wait`) the INT/TERM/HUP trap stops with TERM; after a
# signal the job is waited for once more, so it finishes its own cleanup (slot.sh drops its queue ticket) before we go on.
bg_wait() { [ "${GUARD_SIG:-0}" -eq 0 ] || kill -TERM $WAITPID 2>/dev/null   # D5 review NIT-2: a signal before WAITPID was set
  wait $WAITPID; local rc=$?
  [ "${GUARD_SIG:-0}" -eq 0 ] || wait $WAITPID 2>/dev/null
  WAITPID=""; return $rc; }
# Waits until no live foreign process holds the locks of the given folders; 1 on timeout.
wait_unlocked() { local waited=0 h d
  while :; do h=""; for d in "$@"; do h+=$(lock_holder "$d"); done
    [ -z "$h" ] && return 0
    [ "$waited" -ge "${DD_GUARD_WAIT:-300}" ] && { dd_log "userdata-guard: profile lock still held by live pid $h after ${waited}s"; return 1; }
    [ "$waited" -eq 0 ] && dd_log "userdata-guard: profile lock held by live pid $h — waiting"
    sleep 5 & WAITPID=$!; bg_wait; waited=$((waited+5))
    [ "${GUARD_SIG:-0}" -eq 0 ] || return 1
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
  else guarded "$D2" "${PLAYER_FILES[@]}" | while IFS= read -r f; do echo "$(dd_sha "$f")  $f"; done
    echo "$(plist_digest "$P1")  $P1 (launch-counter keys excluded)"; fi
}
state() { state_of editor; state_of player; }
AREAS=(editor player); [ "$SCOPE" != all ] && AREAS=("$SCOPE")
in_scope() { [[ " ${AREAS[*]} " == *" $1 "* ]]; }

# WP-066L: the state of the snapshot COPIES, labelled with the live paths (what restore puts back).
snap_state_of() { local f
  if [ "$1" = editor ]; then guarded "$D1" "${FILES1[@]}" | while IFS= read -r f; do echo "$(dd_sha "$SNAP/D1/${f:t}")  $f"; done
    echo "$(plist_digest "$SNAP/PR/p2.plist")  $P2 (launch-counter keys excluded)"
  else guarded "$D2" "${PLAYER_FILES[@]}" | while IFS= read -r f; do echo "$(dd_sha "$SNAP/D2/${f:t}")  $f"; done
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
  # ---- G4 D5 snapshot (S1…S11); every failure goes through snap_fail -------------------------------------------------
  GUARD_SIG=0; WAITPID=""; PT=""; PO=$PPID; FAILDIR=""
  TRAPINT()  { GUARD_SIG=2;  [ -n "$WAITPID" ] && kill -TERM "$WAITPID" 2>/dev/null; return 0; }
  TRAPTERM() { GUARD_SIG=15; [ -n "$WAITPID" ] && kill -TERM "$WAITPID" 2>/dev/null; return 0; }
  TRAPHUP()  { GUARD_SIG=1;  [ -n "$WAITPID" ] && kill -TERM "$WAITPID" 2>/dev/null; return 0; }
  COMMITTING=0
  snap_fail() { local rc=$1 f; local -a names
    rm -f "$DIR/.sampling"
    names=(snap before-editor.txt.tmp before-player.txt.tmp before.txt.tmp live-at-snapshot.txt window.json players-lineage.tsv
           known-good-deviation.txt player-slot.out)
    # D5 review MUST-1: a commit that failed halfway leaves no committed name behind (INV-8: no before.txt on ≠ 0)
    [ "$COMMITTING" -eq 1 ] && names+=(before-editor.txt before-player.txt before.txt)
    for f in $names; do
      [ -e "$DIR/$f" ] || continue
      [ -n "$FAILDIR" ] || { FAILDIR="$DIR/failed-$(date -u +%Y%m%dT%H%M%SZ)"; [ -e "$FAILDIR" ] && FAILDIR="$FAILDIR-$$"; mkdir -p "$FAILDIR"; }
      mv "$DIR/$f" "$FAILDIR/"
    done
    [ -n "$PT" ] && DD_SLOT_OWNER="$PO" "$DD_SLOT" release player "$PT" >/dev/null 2>&1
    rm -f "$DIR/player-slot"
    dd_log "userdata-guard snapshot FAILED exit=$rc${FAILDIR:+ — partial files kept in $FAILDIR} $M7"
    exit "$rc"
  }
  check_sig() { [ "$GUARD_SIG" -eq 0 ] || snap_fail $((128+GUARD_SIG)); }
  case "${DIR:A}" in /private/tmp/*|/private/var/folders/*|/tmp/*) dd_log "WARNING userdata-guard: $DIR is under a temporary folder — the snapshot can be cleaned away by the system";; esac
  SHA10=$(print -rn -- "${DIR:A}" | shasum | cut -c1-10); NONCE=$(od -An -N3 -tx1 /dev/urandom | tr -d ' \n')
  ID="$(date -u +%Y%m%dT%H%M%SZ)-$SHA10-$NONCE"
  OWNER_LSTART=$(python3 "$IO" lstart "$PO") || snap_fail 75
  dd_log "userdata-guard: known-good (kgSource=$KGSRC kg=$KG8): $KNOWN_GOOD"
  HOLDER_LINE=""; HOLDER_PID=""; HOLDER_LSTART=""
  if in_scope player; then
    # S2 player slot for the CALLER's shell, bounded wait; the job is stopped by a signal (slot.sh drops its ticket)
    PT="ug-$SHA10-$NONCE"
    DD_SLOT_OWNER=$PO DD_SLOT_TIMEOUT="${DD_GUARD_SLOT_WAIT:-4800}" "$DD_SLOT" acquire player "$PT" > "$DIR/player-slot.out" 2>&1 &
    WAITPID=$!; bg_wait; SRC=$?
    check_sig
    if [ "$SRC" -ne 0 ]; then dd_log "userdata-guard: player slot not acquired (slot.sh exit $SRC) — $DIR/player-slot.out"; snap_fail 75; fi
    printf '%s %s\n' "$PT" "$PO" > "$DIR/player-slot"
    HOLDER_LINE=$(tail -1 "$DIR/player-slot.out")
    HOLDER_PID=$(print -r -- "$HOLDER_LINE" | sed -nE 's/.*ancestor pid ([0-9]+).*/\1/p; s/.*\(pid ([0-9]+);.*/\1/p' | head -1)
    [ -n "$HOLDER_PID" ] && { HOLDER_LSTART=$(python3 "$IO" lstart "$HOLDER_PID") || snap_fail 75; }
    dd_log "userdata-guard: $HOLDER_LINE (held until restore, owner pid $PO)"
    # S3 a live Player would put its transient state into the snapshot
    LIVE=$(pgrep -fl "$PLAYER_RE" 2>/dev/null)
    if [ -n "$LIVE" ]; then
      dd_log "userdata-guard: REFUSED — a Deaths Demolition Player is running; a snapshot now would capture its transient state: $(echo "$LIVE" | cut -c1-140 | tr '\n' ';')"
      snap_fail 30
    fi
    # S4 Q3: the live player prefs already off the known-good display values → refuse before anything is copied
    settle "$DOM1" "$P1"   # D5 review SHOULD-1: the file on disk first mirrors cfprefsd
    DEV=$(kg_deviation "$P1")
    if [ -n "$DEV" ]; then
      echo "$DEV" > "$DIR/known-good-deviation.txt"
      dd_log "userdata-guard: REFUSED — the player display values deviate from known-good; no snapshot, tell C-00: $(echo "$DEV" | tr '\n' ';')"
      snap_fail 31
    fi
  fi
  check_sig
  # S5 the protected set must be plain files under plain folders, spelled exactly, plists readable
  BAD=""
  for f in "$AS" "$PR" "$D1" "$D2" "$P1" "$P2" "${FILES1[@]/#/$D1/}" "${FILES1[@]/#/$D2/}" "$D1"/prototype-profile.json.*(N) "$D2"/prototype-profile.json.*(N); do
    [ -L "$f" ] && BAD+="symlink $f; "
  done
  CF=$( { python3 "$IO" casefold "$D1" "${FILES1[@]}"; python3 "$IO" casefold "$D2" "${FILES1[@]}"; } )
  [ -n "$CF" ] && BAD+="case-folded name variant $(print -r -- "$CF" | tr '\n' ' '); "
  UNREAD=$(python3 "$IO" plist-ok "$P1" "$P2") || BAD+="unreadable plist $UNREAD; "
  check_sig   # D5 review NIT-3: a group signal that killed a helper is a signal, not an unreadable plist
  [ -n "$BAD" ] && { dd_log "userdata-guard: REFUSED — protected set: $BAD(65)"; snap_fail 65; }
  # S6 (D8: the ledger check goes here)
  # S7 Codex-audit M-07: the lock wait follows the scope (editor: D1 only)
  if [ "$SCOPE" = editor ]; then wait_unlocked "$D1" || { check_sig; snap_fail 75; }
  else wait_unlocked "$D1" "$D2" || { check_sig; snap_fail 75; }; fi
  check_sig
  # S8 copy (≤ 3 tries); before-*.txt describe the COPIES (what restore puts back), live-at-snapshot the live files
  try=1
  while :; do
    rm -rf "$SNAP"; mkdir -p "$SNAP/D1" "$SNAP/D2" "$SNAP/PR"   # our own fresh copy only
    settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
    guarded "$D1" "${FILES1[@]}" | while IFS= read -r f; do cp -p "$f" "$SNAP/D1/"; done
    guarded "$D2" "${PLAYER_FILES[@]}" | while IFS= read -r f; do cp -p "$f" "$SNAP/D2/"; done
    [ -f "$P1" ] && cp -p "$P1" "$SNAP/PR/p1.plist"; [ -f "$P2" ] && cp -p "$P2" "$SNAP/PR/p2.plist"
    snap_state_of editor > "$DIR/before-editor.txt.tmp"; snap_state_of player > "$DIR/before-player.txt.tmp"
    state > "$DIR/live-at-snapshot.txt"
    check_sig
    # M-12 review SHOULD-4: editor scope checks only its own area for stability (a Player saving to D2 is out of scope).
    if [ "$SCOPE" = editor ]; then grep -F -e "  $D1/" -e "  $P2 " "$DIR/live-at-snapshot.txt" | cmp -s "$DIR/before-editor.txt.tmp" - && break
    else cat "$DIR/before-editor.txt.tmp" "$DIR/before-player.txt.tmp" | cmp -s - "$DIR/live-at-snapshot.txt" && break; fi
    [ "$try" -ge 3 ] && { dd_log "userdata-guard: live data changed during the snapshot 3 times — $DIR/live-at-snapshot.txt"; snap_fail 75; }
    dd_log "userdata-guard: a save landed during the snapshot (try $try) — retaking"; try=$((try+1))
    sleep 2 & WAITPID=$!; bg_wait; check_sig
  done
  # D5 review SHOULD-1: restore checks the COPY's display values — re-check them, and a Player that started meanwhile
  if in_scope player; then
    LIVE=$(pgrep -fl "$PLAYER_RE" 2>/dev/null)
    [ -n "$LIVE" ] && { dd_log "userdata-guard: REFUSED — a Player started during the snapshot: $(echo "$LIVE" | cut -c1-140 | tr '\n' ';')"; snap_fail 30; }
    DEV=$(kg_deviation "$SNAP/PR/p1.plist")
    if [ -n "$DEV" ]; then
      echo "$DEV" > "$DIR/known-good-deviation.txt"
      dd_log "userdata-guard: REFUSED — the copied player display values deviate from known-good; no snapshot, tell C-00: $(echo "$DEV" | tr '\n' ';')"
      snap_fail 31
    fi
  fi
  check_sig
  # S9 window.json (evidence; windowAreas + protectedSet are restore inputs) and the sampler
  WIN_JSON=$(ID="$ID" SCOPE="$SCOPE" AREAS="${AREAS[*]}" PT="$PT" PO="$PO" OWNER_LSTART="$OWNER_LSTART" HOLDER_LINE="$HOLDER_LINE" \
    HOLDER_PID="$HOLDER_PID" HOLDER_LSTART="$HOLDER_LSTART" GUARDSHA="$GUARDSHA" HELPERSHA="$HELPERSHA" ROOTKIND="$ROOTKIND" \
    KGSRC="$KGSRC" KG="$KNOWN_GOOD" AS_R="${AS:A}" PR_R="${PR:A}" python3 -c 'import json, os, time
e = os.environ
receipt = None
if e["PT"]:
    receipt = {"tag": e["PT"], "owner": {"pid": int(e["PO"]), "lstart": e["OWNER_LSTART"]}, "holderLine": e["HOLDER_LINE"],
               "holder": {"pid": int(e["HOLDER_PID"]), "lstart": e["HOLDER_LSTART"]} if e["HOLDER_PID"] else None}
print(json.dumps({"version": 2, "id": e["ID"], "scope": e["SCOPE"], "windowAreas": e["AREAS"].split(), "protectedSet": "v2",
    "receipt": receipt, "owner": {"pid": int(e["PO"]), "lstart": e["OWNER_LSTART"]}, "guardSha": e["GUARDSHA"],
    "helperSha": e["HELPERSHA"], "root": e["ROOTKIND"], "as": e["AS_R"], "pr": e["PR_R"], "kgSource": e["KGSRC"], "kg": e["KG"],
    "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}, indent=1))') || snap_fail 75
  print -r -- "$WIN_JSON" | python3 "$IO" atomic-write "$DIR/window.json" || snap_fail 75
  : > "$DIR/players.log"; : > "$DIR/players-lineage.tsv"; touch "$DIR/.sampling"
  ( unfunction TRAPINT TRAPTERM TRAPHUP 2>/dev/null; trap - INT TERM HUP   # D5 review NIT-1: TERM ends the sampler
    n=0; while [ -f "$DIR/.sampling" ] && [ $n -lt 4320 ]; do
      python3 "$IO" sample "$DIR" "$PLAYER_RE" "$PO" $HOLDER_PID
      sleep 5; n=$((n+1)); done ) < /dev/null > /dev/null 2>&1 &
  check_sig
  # S10 commit — signals ignored for these renames; before.txt is the commit mark and comes LAST
  trap '' INT TERM HUP
  COMMITTING=1   # D5 review MUST-1: every rename is checked; any failure undoes the whole commit (snap_fail, 75)
  mv "$DIR/before-editor.txt.tmp" "$DIR/before-editor.txt" && mv "$DIR/before-player.txt.tmp" "$DIR/before-player.txt" \
    && cat "$DIR/before-editor.txt" "$DIR/before-player.txt" > "$DIR/before.txt.tmp" && mv "$DIR/before.txt.tmp" "$DIR/before.txt" \
    || { dd_log "userdata-guard: commit failed (a rename or write in $DIR) — snapshot undone"; snap_fail 75; }
  cat "$DIR/before-editor.txt" "$DIR/before-player.txt" | cmp -s - "$DIR/before.txt" && [ -s "$DIR/before.txt" ] \
    || { dd_log "userdata-guard: committed before.txt is not before-editor + before-player — snapshot undone"; snap_fail 75; }
  # S11
  dd_log "userdata-guard snapshot → $SNAP $M7 window=$ID"
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
# D5 review SHOULD-2: the window must belong to THIS user root — every recorded path under the current D1/D2 or P1/P2, and
# a v2 window's recorded AS/PR equal to the current ones (a fixture window restored with the real HOME, or vice versa → 64)
FOREIGN=$(sed -E 's/^[^ ]+  //; s/ \(launch-counter keys excluded\)$//' "$DIR"/before*.txt \
  | while IFS= read -r f; do [[ "$f" == "$D1/"* || "$f" == "$D2/"* || "$f" == "$P1" || "$f" == "$P2" ]] || print -r -- "$f"; done | head -3)
if [ -z "$FOREIGN" ] && [ -f "$DIR/window.json" ]; then
  FOREIGN=$(AS_R="${AS:A}" PR_R="${PR:A}" python3 -c 'import json, os, sys
try: w = json.load(open(sys.argv[1]))
except Exception: sys.exit(0)
for k, cur in (("as", os.environ["AS_R"]), ("pr", os.environ["PR_R"])):
    if k in w and w[k] != cur: print("window %s=%s, now %s" % (k, w[k], cur))' "$DIR/window.json")
fi
[ -n "$FOREIGN" ] && { UG_LAST="$(date -u +%FT%TZ) userdata-guard: $DIR was taken under another user root — refused (64): $(print -r -- "$FOREIGN" | tr '\n' ';')"
  echo "${UG_LAST#* }" >&2; exit 64; }
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
    guarded "$D2" "${PLAYER_FILES[@]}" | while IFS= read -r f; do [ -f "$SNAP/D2/${f:t}" ] || mv "$f" "$DIR/created/D2/"; done
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
WINID=$(sed -nE 's/.*"id": *"([^"]+)".*/\1/p' "$DIR/window.json" 2>/dev/null | head -1)
dd_log "userdata-guard restore scope=$SCOPE touched=$TOUCHED verified=$OK extraKeys=$(grep -c . "$DIR/extra-keys.txt") outOfScopeChanged=$(grep -c . "$DIR/out-of-scope-changed.txt") $M7 window=${WINID:-legacy}"
[ "$OK" -eq 1 ] || { for a in "${AREAS[@]}"; do diff "$DIR/before-$a.txt" "$DIR/after-$a.txt" | grep '^[<>]' >&2; done; cat "$DIR/extra-keys.txt" >&2; exit 30; }
exit 0
