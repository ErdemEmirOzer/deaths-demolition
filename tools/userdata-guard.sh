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
# Ledger root (§2.4): quarantine, INDEX and (G4 D8) the window ledger windows/ closed/ aborted/ — permanent, nothing deletes it
if [ -n "${DD_GUARD_ROOT:-}" ]; then LEDGER="$DD_GUARD_ROOT/ledger"; else LEDGER="$PWHOME/.dd-userdata-guard"; fi
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
print(hashlib.sha256(json.dumps(d, sort_keys=True, default=repr).encode()).hexdigest() if d else "MISSING")   # G4 D6: {} ≡ absent
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
settle() { local t0=$SECONDS   # ≤ 10 s in all; one hung export is cut at 10 s (D6 review SHOULD-3)
  while (( SECONDS - t0 < 10 )); do perl -e 'alarm 10; exec @ARGV' defaults export "$1" - 2>/dev/null | python3 -c "$PLIST_SAME" "$2" && return 0
    sleep 0.2; done
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
try:
    with open(p, "rb") as f: d = plistlib.load(f)
except Exception: sys.exit(3)   # unreadable: the restore helper refuses it as SNAPCORRUPT (nothing of it is imported)
for kv in spec.split(";"):
    if "=" not in kv: continue
    k, want = kv.split("=", 1)
    if k in d and str(d[k]) != want: print("%s=%s (want %s)" % (k, d[k], want))'
kg_deviation(){ python3 -c "$KG_CHECK" "$1" "$KNOWN_GOOD"; }
release_player_slot(){ [ -f "$DIR/player-slot" ] || return 0; local t o; read -r t o < "$DIR/player-slot"
  DD_SLOT_OWNER="$o" "$DD_SLOT" release player "$t" >/dev/null 2>&1; rm -f "$DIR/player-slot"; }

if [ "$CMD" = snapshot ]; then
  # ---- G4 D5 snapshot (S1…S11); every failure goes through snap_fail -------------------------------------------------
  GUARD_SIG=0; WAITPID=""; PT=""; PO=$PPID; FAILDIR=""; LEDGER_REC=0
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
    [ "$LEDGER_REC" -eq 1 ] && [ -f "$LEDGER/windows/$ID.json" ] && { python3 "$IO" ledger-abort "$LEDGER" "$ID" \
      || dd_log "WARNING userdata-guard: ledger record $ID could not be moved to aborted/ — it blocks its areas, tell C-00"; }   # M4
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
  # S6 (G4 D8, INV-7): the window ledger. Under L/.lock: an overlapping record that is dirty, or whose owner (or restorer)
  # is dead → 65 with the next step; a live owner that is our ancestor → nested, allowed; a live foreign one (or a live
  # restore) → the lock is let go and we poll (≤ DD_GUARD_WAIT, default 300 s, sleep 5 & wait) → 75 — short on purpose:
  # a caller may hold a unity slot while it waits (D8 review SHOULD-3). Then our record is written: state snapshotting.
  REC_JSON=$(R_ID="$ID" R_DIR="${DIR:A}" R_AREAS="${AREAS[*]}" R_SCOPE="$SCOPE" R_PO="$PO" R_OL="$OWNER_LSTART" R_PT="$PT"     R_HP="$HOLDER_PID" R_HL="$HOLDER_LSTART" R_GS="$GUARDSHA" R_LINE="$HOLDER_LINE" python3 -c 'import json, os, time
e = os.environ
print(json.dumps({"id": e["R_ID"], "dir": e["R_DIR"], "scope": e["R_SCOPE"], "windowAreas": e["R_AREAS"].split(),
  "owner": {"pid": int(e["R_PO"]), "lstart": e["R_OL"]}, "restorer": None,
  "slot": {"tag": e["R_PT"], "holder": {"pid": int(e["R_HP"]), "lstart": e["R_HL"]} if e["R_HP"] else None,
           "reentrant": " reentrant -> " in e["R_LINE"]} if e["R_PT"] else None,
  "state": "snapshotting", "areas": {a: "open" for a in e["R_AREAS"].split()}, "guardSha": e["R_GS"],
  "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}))') || snap_fail 75
  LW0=$SECONDS
  # D8 review SHOULD-2: armed BEFORE the first attempt — a signal that lands after the helper's rename still sends the
  # record to aborted/ (ledger-abort is a no-op when there is none)
  LEDGER_REC=1
  while :; do
    LMSG=$(print -r -- "$REC_JSON" | python3 "$IO" ledger-begin "$LEDGER" "$PO"); LRC=$?
    [ $LRC -eq 0 ] && break
    check_sig
    [ $LRC -eq 65 ] && { dd_log "userdata-guard: $LMSG (65)"; snap_fail 65; }
    [ $LRC -eq 75 ] || { dd_log "userdata-guard: ledger check failed ($LRC): $LMSG"; snap_fail 75; }
    (( SECONDS - LW0 >= ${DD_GUARD_WAIT:-300} )) && { dd_log "userdata-guard: $LMSG — still after ${DD_GUARD_WAIT:-300}s (75)"; snap_fail 75; }
    [ $((SECONDS - LW0)) -lt 5 ] && dd_log "userdata-guard: waiting — $LMSG"
    sleep 5 & WAITPID=$!; bg_wait; check_sig
  done
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
  print -r -- '{"state": "open"}' | python3 "$IO" ledger-update "$LEDGER" "$ID" \
    || dd_log "WARNING userdata-guard: ledger record $ID could not be set open (it stays snapshotting)"
  # S11
  dd_log "userdata-guard snapshot → $SNAP $M7 window=$ID"
  exit 0
fi

# ---- G4 D6 (WP-089, C-00 05.10; design v3.1 §3 D6) restore: R0 … R13 ------------------------------------------------
# WP-079G D6a (2): every exit of restore from here on is recorded in <dir>/restore.txt; the player slot is released only once
# the restore proper has started (WP-066N), whatever the outcome, never on 64 (R13).
UG_RELEASE=0
restore_exit() { local rc=$?
  [ "$UG_RELEASE" -eq 1 ] && release_player_slot
  { print -r -- "${UG_LAST:-$(date -u +%FT%TZ) userdata-guard restore: no result line}"; print -r -- "exit=$rc"; } >> "$DIR/restore.txt"
}
trap 'restore_exit' EXIT
# G4 D8: a window the ledger knows (by its DIR) that cannot be restored any more is marked dirty — SNAPSHOT_MISSING —
# so later snapshots of that area stop with 65 until C-00 resolves it
ledger_dirty() { local id   # never a snapshot still in progress (its owner alive, state snapshotting — D8 review NIT)
  id=$(python3 "$IO" ledger-mark-missing "$LEDGER" "$DIR" "$1" 2>/dev/null) \
    && echo "userdata-guard: SNAPSHOT_MISSING — ledger window $id marked dirty (tell C-00)" >&2; return 0; }
refuse64() { UG_LAST="$(date -u +%FT%TZ) userdata-guard: $* — refused (64)"; ledger_dirty "$*"; echo "userdata-guard: $* — refused (64)" >&2; exit 64; }
[ -f "$DIR/before.txt" ] || refuse64 "no snapshot in $DIR"
for f in snapshot-manifest.json sampler.json; do
  [ -e "$DIR/$f" ] && refuse64 "v1 (74f62dd6) snapshot ($f) — restore it with the guard version that took it, tell C-00"
done
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
[ -n "$FOREIGN" ] && refuse64 "$DIR was taken under another user root: $(print -r -- "$FOREIGN" | tr '\n' ';')"
# A snapshot taken by the WP-066J guard has only before.txt: split it by area (D1 + P2 = editor).
if [ ! -f "$DIR/before-editor.txt" ]; then
  grep -F -e "  $D1/" -e "  $P2 " "$DIR/before.txt" > "$DIR/before-editor.txt"
  grep -F -v -e "  $D1/" -e "  $P2 " "$DIR/before.txt" > "$DIR/before-player.txt"
fi
# R0 binding (§2.3): window kind, W, protected set; W ↔ receipt (SHOULD-8); live-at-snapshot = before on W's lines (C-00
# 05.10, D5 review SHOULD-3: only the window's own areas are compared; a difference elsewhere is an info line).
if [ -f "$DIR/window.json" ]; then
  KIND=v2
  WIN=$(python3 -c 'import json, sys
try: w = json.load(open(sys.argv[1]))
except Exception: print("ERR"); sys.exit(0)
print(" ".join(w.get("windowAreas") or []), "|", "receipt" if w.get("receipt") else "none", "|", w.get("id", ""))' "$DIR/window.json")
  [ "$WIN" = ERR ] && refuse64 "$DIR/window.json is unreadable"
  W=(${=${WIN%% |*}}); WRECEIPT=$(print -r -- "$WIN" | cut -d'|' -f2 | tr -d ' '); WINID=$(print -r -- "$WIN" | cut -d'|' -f3 | tr -d ' ')
  [ ${#W} -gt 0 ] || refuse64 "$DIR/window.json has no windowAreas"
  if (( ${W[(Ie)player]} )); then [ "$WRECEIPT" = receipt ] || refuse64 "window records the player area but no slot receipt (SHOULD-8)"
  else [ "$WRECEIPT" = none ] || refuse64 "window has a slot receipt but not the player area (SHOULD-8)"; fi
else
  KIND=legacy; W=(editor); [ -f "$DIR/player-slot.out" ] && W=(editor player)
  WINID="legacy-$(print -rn -- "${DIR:A}" | shasum | cut -c1-10)"
fi
[ -f "$DIR/live-at-snapshot.txt" ] || refuse64 "no live-at-snapshot.txt — the snapshot never bound"
area_lines() { if [ "$1" = editor ]; then grep -F -e "  $D1/" -e "  $P2 " "$2"; else grep -F -e "  $D2/" -e "  $P1 " "$2"; fi; }
for a in $W; do
  area_lines $a "$DIR/live-at-snapshot.txt" | cmp -s - "$DIR/before-$a.txt" || refuse64 "the $a area of $DIR was not bound (live-at-snapshot ≠ before-$a.txt)"
done
cmp -s "$DIR/live-at-snapshot.txt" "$DIR/before.txt" || dd_log "userdata-guard: info — an area outside the window changed while the snapshot was taken (not restored by this window)"
# R1 one restore per DIR at a time (the kernel releases the lock when this shell dies; no takeover race)
zmodload zsh/system
: >> "$DIR/.restore.lock"   # zsystem flock does not create the file
zsystem flock -t 0 -f UG_LOCKFD "$DIR/.restore.lock" 2>/dev/null || {
  UG_LAST="$(date -u +%FT%TZ) userdata-guard restore scope=$SCOPE LOCKED — another restore of $DIR is running (75) retry=same-dir class=LOCKED"; print -r -- "$UG_LAST"; exit 75; }
# D6 review (concurrency SHOULD-2): a guard shell killed with SIGKILL frees the lock above while its write helper (signals
# ignored) still runs — the helper holds .restore-write.lock for its whole life; probe it and let go at once
: >> "$DIR/.restore-write.lock"
zsystem flock -t 0 -f UG_WFD "$DIR/.restore-write.lock" 2>/dev/null || {
  UG_LAST="$(date -u +%FT%TZ) userdata-guard restore scope=$SCOPE LOCKED — a write helper of $DIR is still running (75) retry=same-dir class=LOCKED"; print -r -- "$UG_LAST"; exit 75; }
zsystem flock -u $UG_WFD
# R2 wait phase: a signal stops the current wait; the rest of the waits are skipped (areas still locked → BUSY)
GUARD_SIG=0; WAITPID=""
TRAPINT()  { GUARD_SIG=2;  [ -n "$WAITPID" ] && kill -TERM "$WAITPID" 2>/dev/null; return 0; }
TRAPTERM() { GUARD_SIG=15; [ -n "$WAITPID" ] && kill -TERM "$WAITPID" 2>/dev/null; return 0; }
TRAPHUP()  { GUARD_SIG=1;  [ -n "$WAITPID" ] && kill -TERM "$WAITPID" 2>/dev/null; return 0; }
trap '' PIPE   # D6 review SHOULD-4: a dead reader of stdout must not kill the guard before its summary and slot release
# G4 D8 R2: the ledger record (a window taken before D8 has none — skipped) is restoring, by this process
LREC=0
if [ "$KIND" = v2 ]; then
  RL=$(python3 "$IO" lstart $$)
  RP=$$ RL="$RL" python3 -c 'import json, os; print(json.dumps({"state": "restoring", "restorer": {"pid": int(os.environ["RP"]), "lstart": os.environ["RL"]}}))' \
    | ( trap '' INT TERM HUP; python3 "$IO" ledger-update "$LEDGER" "$WINID" >/dev/null )   # D8 review SHOULD-2
  [ -f "$LEDGER/windows/$WINID.json" ] && LREC=1   # R12 runs whenever the record exists
fi
case "${DIR:A}" in /private/tmp/*|/private/var/folders/*|/tmp/*) dd_log "WARNING userdata-guard: $DIR is under a temporary folder";; esac
[ -f "$DIR/quarantine.txt" ] || print -r -- "$LEDGER/quarantine/$WINID/" > "$DIR/quarantine.txt"
# R3 the sampler stops (the dead writers' temp sweep runs in the write phase, only for the areas written — D6 review N3)
rm -f "$DIR/.sampling"
CLASSES=(); add_class() { (( ${CLASSES[(Ie)$1]} )) || CLASSES+=("$1"); }
# R4 (G4 D7, Q2): a window with a slot receipt — its owner (re-entrant: the slot holder) must still be the same live
# process; otherwise the player slot may have passed to someone else meanwhile: WINDOW_LOST (30), restored all the same.
# The slot lock file is not consulted. No check for a legacy window.
if [ "$KIND" = v2 ] && [ "$WRECEIPT" = receipt ]; then
  # reported once (window-lost.txt): a later restore of the same window that converges exits 0 (T34)
  if ! WL=$(python3 "$IO" window-owner "$DIR/window.json"); then
    if [ -f "$DIR/window-lost.txt" ]; then dd_log "userdata-guard: info — window owner still gone ($WL); WINDOW_LOST was reported $(head -1 "$DIR/window-lost.txt")"
    else print -r -- "$(date -u +%FT%TZ) $WL" > "$DIR/window-lost.txt"; add_class WINDOW_LOST
      dd_log "userdata-guard: WINDOW_LOST — $WL; restoring anyway (30), tell C-00"; fi
  fi
fi
# R5
settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
UG_RELEASE=1   # WP-066N: the snapshot's player slot ends with this restore, whatever the outcome (restore_exit)
typeset -A AREA_CLASS
# R7 area gate: A∖W → OUT_OF_WINDOW; a live lock holder → BUSY; the player area while a Player runs → BUSY
WRITE_AREAS=()
for a in $AREAS; do
  if ! (( ${W[(Ie)$a]} )); then AREA_CLASS[$a]=OUT_OF_WINDOW; add_class OUT_OF_WINDOW; dd_log "userdata-guard: $a is outside the window ($W) — not written"; continue; fi
  if [ "$GUARD_SIG" -ne 0 ]; then
    [ -n "$(lock_holder "${${a:#player}:+$D1}${${a:#editor}:+$D2}")" ] && { AREA_CLASS[$a]=BUSY; add_class BUSY; continue; }
  else
    if [ $a = editor ]; then F=$D1; else F=$D2; fi
    wait_unlocked "$F" || { AREA_CLASS[$a]=BUSY; add_class BUSY; dd_log "userdata-guard: NOT restored ($a) — a live process holds the profile (retry when it exits)"; continue; }
  fi
  if [ $a = player ] && [ -n "$(pgrep -fl "$PLAYER_RE" 2>/dev/null)" ]; then AREA_CLASS[$a]=BUSY; add_class BUSY; dd_log "userdata-guard: a Player is running — player area BUSY"; continue; fi
  WRITE_AREAS+=($a)
done
[ "$GUARD_SIG" -ne 0 ] && add_class INTERRUPTED
# No waiting from here on: INT/TERM/HUP ignored to the end (§2.7; D6 reviews: a group signal must not cut the after-run
# commit or the KG check half-way). SIG_IGN reaches every helper and `defaults`.
trap '' INT TERM HUP
# a lock holder that ended during R7 may have left its prefs only in cfprefsd (D6 review SHOULD-5)
settle "$DOM1" "$P1"; settle "$DOM2" "$P2"
# R8 after-run (M2; D6 reviews): per file, the FIRST observation is kept for ever — published with a no-clobber link, so a
# crashed earlier attempt's file (the earlier observation) wins; every later state goes to <file>.N. after-run.txt is the
# mark, committed last. A failed or malformed state file → no write phase (WRITE_FAILED), never a guessed "first".
valid_state() { [ -s "$1" ] && ! grep -qvE '^([0-9a-f]{64}|MISSING)  /' "$1"; }
publish() { local f=$1 chk=$2 t="$1.tmp.$$" n=2; shift 2
  "$@" > "$t" 2>/dev/null && { [ "$chk" = raw ] || valid_state "$t"; } || { rm -f "$t"; return 1; }
  if ln "$t" "$f" 2>/dev/null; then rm -f "$t"; REPLY=$f; return 0; fi
  [ -e "$f" ] || { rm -f "$t"; return 1; }
  while ! ln "$t" "$f.$n" 2>/dev/null; do [ -e "$f.$n" ] || { rm -f "$t"; return 1; }; n=$((n+1)); done
  rm -f "$t"; REPLY="$f.$n"
}
# an earlier attempt that reached R8 (its attempt file, or an older guard's mark) leaves no room for a fresh "first"
R8OK=1; typeset -A CUR; MARKED=0; { [ -f "$DIR/.after-run-attempt" ] || [ -f "$DIR/after-run.txt" ]; } && MARKED=1
: >> "$DIR/.after-run-attempt"
for a in editor player; do
  if publish "$DIR/after-run-$a.txt" state state_of $a; then CUR[$a]=$REPLY; else R8OK=0; CUR[$a]=/dev/null; fi
  [ -f "$DIR/after-run-$a.txt" ] || R8OK=0
  # the mark is only committed after both firsts; a first created now after an earlier attempt could be the user's later
  # save, not the run's state — no "first" to trust
  [ "$MARKED" -eq 1 ] && [ "${CUR[$a]}" = "$DIR/after-run-$a.txt" ] && { R8OK=0; dd_log "userdata-guard: $DIR/after-run-$a.txt was missing under an existing after-run.txt — no first to trust (an earlier attempt reached this point) — tell C-00"; }
done
OTHER=$(for a in editor player; do
  (( ${AREAS[(Ie)$a]} )) && continue
  diff "$DIR/before-$a.txt" "${CUR[$a]}" | python3 -c 'import sys
a = sys.argv[1]; before, after = {}, {}
for l in sys.stdin:
    if l[:2] in ("< ", "> ") and "  " in l[2:]:
        d, p = l[2:].rstrip("\n").split("  ", 1); (before if l[0] == "<" else after)[p] = d
for p, d in after.items(): print("%s  %s  %s" % (a, d, p))
for p in before:
    if p not in after: print("%s  MISSING  %s" % (a, p))' $a
done)
OOS="$DIR/out-of-scope-changed.txt"
NL=$'\n'   # C-00 05.10 MUST: $'\n' is not expanded inside double quotes — the file ended in a literal $'\n' (since 52b7a00f)
publish "$OOS" raw print -rn -- "${OTHER:+$OTHER$NL}" && OOS=$REPLY || R8OK=0
[ "$R8OK" -eq 1 ] && { publish "$DIR/after-run.txt" state state || R8OK=0; }   # the mark, last, only over committed firsts
if [ "$R8OK" -ne 1 ]; then
  add_class WRITE_FAILED; WRITE_AREAS=()
  dd_log "userdata-guard: the after-run files of $DIR could not be committed — nothing written (30)"
fi
python3 "${0:A:h}/userdata_attrib.py" "$DIR" || true
[ -s "$DIR/attribution.txt" ] && dd_log "userdata-guard: writers → $DIR/attribution.txt"
[ -n "$OTHER" ] && { add_class OTHER; dd_log "WARNING userdata-guard: an area outside --scope $SCOPE changed during the run and was NOT restored — $OOS"; }
TOUCHED=0; for a in $AREAS; do cmp -s "$DIR/before-$a.txt" "$DIR/after-run-$a.txt" || TOUCHED=1; done
# R6 (after R8, SHOULD-1): the snapshot's player display values off known-good → 31, nothing written
if (( ${WRITE_AREAS[(Ie)player]} )); then
  DEV=$(kg_deviation "$SNAP/PR/p1.plist"); kr=$?
  [ $kr -eq 0 ] || [ $kr -eq 3 ] || DEV="the known-good check of the snapshot failed ($kr) — fail closed"
  if [ -n "$DEV" ]; then
    echo "$DEV" > "$DIR/known-good-deviation.txt"
    dd_log "userdata-guard: REFUSED — the snapshot's player display values deviate from known-good; nothing restored, tell C-00: $(echo "$DEV" | tr '\n' ';')"
    add_class KG_DEVIATION; WRITE_AREAS=()
  fi
fi
# G4 D7 FOREIGN_WRITER (M8): a Player outside the window's own process tree (players-lineage.tsv owned=0) was seen while
# the window was open, and the PLAYER area of the window changed (before ≠ the first after-run) — restored all the same
# (30); the ledger rows of this restore carry the class, the verdict names the Players and the kept bytes. A Player writes
# only the player area (WP-066K), so an editor-only window never has this class (D7 review MUST-1).
FW_JSON="[]"; INDEXCLASS=""
if [ -s "$DIR/players-lineage.tsv" ] && (( ${W[(Ie)player]} )) && (( ${AREAS[(Ie)player]} )); then
  FW_JSON=$(python3 "$IO" foreign-players "$DIR/players-lineage.tsv"); fw=$?
  if [ $fw -eq 1 ]; then
    FWCHG=0; cmp -s "$DIR/before-player.txt" "$DIR/after-run-player.txt" || FWCHG=1
    if [ $FWCHG -eq 1 ]; then
      add_class FOREIGN_WRITER; INDEXCLASS=FOREIGN_WRITER
      dd_log "userdata-guard: FOREIGN_WRITER — a Player outside this window ran while it was open and the window's data changed; restoring anyway (30), tell C-00: $FW_JSON"
    fi
  elif [ $fw -ne 0 ]; then FW_JSON="[]"; fi
fi
# R9 write phase. First the R3 sweep, only in the folders about to be written: a dead writer's temp file is moved to the
# quarantine (SHOULD-4: a live pid's is left alone)
for a in $WRITE_AREAS; do
  if [ $a = editor ]; then F=$D1; else F=$D2; fi
  for t in "$F"/.ddguard-*.tmp(N); do
    tp=${${t:t:r}##*-}
    if [[ "$tp" == <-> ]] && ! kill -0 "$tp" 2>/dev/null; then
      q="$LEDGER/quarantine/$WINID/tmp-sweep"; mkdir -p "$q"; ts="$(dd_sha "$t")"; mv -n "$t" "$q/${t:t}.$(date -u +%s)" \
        && python3 "$IO" index-append "$LEDGER" "$(date -u +%FT%TZ)" "$WINID" "$DIR" "$t" "$ts" tmp-sweep "$INDEXCLASS" \
        && dd_log "userdata-guard: a dead writer's temp file moved to $q: $t"
    fi
  done
done
area_cfg() { local a=$1 f
  local folder=$D1 plist=$P2 dom=$DOM2 px=p2 sf="$SNAP/D1" sp="$SNAP/PR/p2.plist" check=0 names=("${FILES1[@]}")
  [ $a = player ] && { folder=$D2; plist=$P1; dom=$DOM1; px=p1; sf="$SNAP/D2"; sp="$SNAP/PR/p1.plist"; check=1; names=("${PLAYER_FILES[@]}"); }
  local first="$DIR/after-run-$a.txt"
  A_DIR="$DIR" A_AREA=$a A_ID="$WINID" A_LEDGER="$LEDGER" A_FOLDER="$folder" A_NAMES="${(j:/:)names}" A_PLIST="$plist" A_DOMAIN="$dom" \
  A_PX=$px A_SF="$sf" A_SP="$sp" A_BEFORE="$DIR/before-$a.txt" A_FIRST="$first" A_VOL="$UNITY_VOLATILE" A_RE="$PLAYER_RE" \
  A_CHECK=$check A_AS="${AS:A}" A_PR="${PR:A}" A_PID=$$ A_RESULT="$DIR/restore-$a.json" A_IC="$INDEXCLASS" python3 -c 'import json, os
e = os.environ
print(json.dumps({"dir": e["A_DIR"], "area": e["A_AREA"], "id": e["A_ID"], "ledger": e["A_LEDGER"], "folder": e["A_FOLDER"],
  "names": e["A_NAMES"].split("/"), "plist": e["A_PLIST"], "domain": e["A_DOMAIN"], "pxName": e["A_PX"], "snapFolder": e["A_SF"],
  "snapPlist": e["A_SP"], "before": e["A_BEFORE"], "first": e["A_FIRST"], "volatile": e["A_VOL"].split(), "playerRe": e["A_RE"],
  "checkPlayer": e["A_CHECK"] == "1", "rootAS": e["A_AS"], "rootPR": e["A_PR"], "guardPid": int(e["A_PID"]),
  "result": e["A_RESULT"], "indexClass": e["A_IC"]}))'
}
HELPER_FAILED=(); VERIFIED_AREAS=()
for a in $WRITE_AREAS; do
  area_cfg $a | python3 "$IO" restore-area > "$DIR/restore-$a.json.tmp.$$" && mv "$DIR/restore-$a.json.tmp.$$" "$DIR/restore-$a.json" \
    || { HELPER_FAILED+=($a); add_class WRITE_FAILED; dd_log "userdata-guard: the $a write helper failed — $DIR/restore-$a.json"; continue; }
  for c in ${(f)"$(python3 -c 'import json, sys; [print(c) for c in json.load(open(sys.argv[1]))["classes"]]' "$DIR/restore-$a.json")"}; do
    add_class "$c"; done
done
# R10 verification: settle, then every written area against before (sha; plists on disk AND through defaults), extra keys
sleep 1; settle "$DOM1" "$P1" || add_class SETTLE_TIMEOUT; settle "$DOM2" "$P2" || add_class SETTLE_TIMEOUT
OK=1; : > "$DIR/extra-keys.txt"
for a in $AREAS; do
  if ! (( ${WRITE_AREAS[(Ie)$a]} )) || (( ${HELPER_FAILED[(Ie)$a]} )); then OK=0; continue; fi   # no current restore-<a>.json
  V=$(area_cfg $a | python3 "$IO" verify-area) || { OK=0; add_class MISMATCH; continue; }
  print -r -- "$V" > "$DIR/verify-$a.json"
  python3 -c 'import json, sys; v = json.loads(sys.argv[1]); sys.exit(0 if v["ok"] else 1)' "$V" && VERIFIED_AREAS+=($a) || { OK=0; add_class MISMATCH; }
  python3 -c 'import json, sys; v = json.loads(sys.argv[1]); [print(sys.argv[2] + "  " + k) for k in v["extraKeys"]]' "$V" \
    "$( [ $a = player ] && print -r -- "$DOM1" || print -r -- "$DOM2")" >> "$DIR/extra-keys.txt"
done
[ -s "$DIR/extra-keys.txt" ] && { OK=0; add_class EXTRA_KEYS; }
for c in $CLASSES; do case $c in OUT_OF_WINDOW|BUSY|KG_DEVIATION|INTERRUPTED) OK=0;; esac; done
for c in $CLASSES; do case $c in STALE|SYMLINK|SNAPCORRUPT|WRITE_FAILED|RACED-EARLY|RACED-LATE|MISMATCH|SETTLE_TIMEOUT) OK=0;; esac; done
state > "$DIR/after.txt"; for a in editor player; do state_of $a > "$DIR/after-$a.txt"; done
# exit code by priority 64 > 31 > 30 > 75 > 32 > 0 (§2.5, G4 D7); class= = the first class of the deciding code in the
# table order; retry=same-dir ⇔ 75 or INTERRUPTED. OTHER (another area changed) never lowers verified (INV-1).
T30=(MISMATCH EXTRA_KEYS SETTLE_TIMEOUT SYMLINK SNAPCORRUPT WRITE_FAILED OUT_OF_WINDOW RACED-LATE STALE INTERRUPTED WINDOW_LOST FOREIGN_WRITER)
T75=(BUSY RACED-EARLY LOCKED)
first_of() { local c; for c in "$@"; do (( ${CLASSES[(Ie)$c]} )) && { print -r -- $c; return 0; }; done; return 1; }
RC=0; CLS=OK
if C=$(first_of KG_DEVIATION); then RC=31; CLS=$C
elif C=$(first_of $T30); then RC=30; CLS=$C
elif C=$(first_of $T75); then RC=75; CLS=$C
elif [ "$OK" -ne 1 ]; then RC=30; CLS=MISMATCH   # unverified without a class of its own
elif C=$(first_of OTHER); then RC=32; CLS=$C; fi
# D7 review SHOULD-3: an area left unwritten (BUSY / RACED-EARLY) needs the retry even when a 30 class decides the code
RETRY=none; { [ $RC -eq 75 ] || (( ${CLASSES[(Ie)INTERRUPTED]} )) || [ -n "$(first_of $T75)" ]; } && RETRY=same-dir
SNAPID=$(python3 "$IO" sha "$DIR/before.txt" | cut -c1-12)
# R11 verdict (atomic; v1 alias keys kept)
VERDICT=$(V_ID="$WINID" V_KIND=$KIND V_SCOPE=$SCOPE V_W="$W" V_CLASSES="${(j: :)CLASSES}" V_RC=$RC V_OK=$OK V_TOUCHED=$TOUCHED \
  V_RETRY=$RETRY V_SNAPID=$SNAPID V_DIR="$DIR" V_AREAS="${(j: :)WRITE_AREAS}" V_CLS=$CLS V_FW="$FW_JSON" V_FWC=$INDEXCLASS \
  python3 -c 'import json, os, time
e = os.environ; d = e["V_DIR"]
per = {}
for a in e["V_AREAS"].split():
    try: per[a] = json.load(open(os.path.join(d, "restore-%s.json" % a)))
    except Exception: per[a] = None
print(json.dumps({"version": 2, "id": e["V_ID"], "kind": e["V_KIND"], "scope": e["V_SCOPE"], "windowAreas": e["V_W"].split(),
  "classes": e["V_CLASSES"].split(), "class": e["V_CLS"], "exit": int(e["V_RC"]), "verified": e["V_OK"] == "1", "touched": e["V_TOUCHED"] == "1",
  "retry": e["V_RETRY"], "snapshotId": e["V_SNAPID"], "areas": per, "status": "ok" if e["V_RC"] in ("0", "32") else "failed",
  "foreignWriter": None if not e["V_FWC"] else {"players": json.loads(e["V_FW"] or "[]"),
    "displaced": [q for a in per.values() if a for p in a.get("paths", []) for q in p.get("quarantine", [])]},
  "samplerClosed": not os.path.exists(os.path.join(d, ".sampling")), "snapshotManifestSha256": None,
  "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}, indent=1))')
print -r -- "$VERDICT" | python3 "$IO" atomic-write "$DIR/guard-verdict.json"
VN=1; VU=$(date -u +%Y%m%dT%H%M%SZ); while [ -e "$DIR/verdict-$VU.$VN.json" ]; do VN=$((VN+1)); done
print -r -- "$VERDICT" | python3 "$IO" atomic-write "$DIR/verdict-$VU.$VN.json"
# R12 (G4 D8): per area of the window — restored and verified here → finalized, outside this scope → released, else
# dirty; nothing dirty → the record moves to closed/, else it stays (state dirty) and blocks that area's next snapshots
if [ "$LREC" -eq 1 ]; then
  # finalized = written AND verified in THIS run (VERIFIED_AREAS; never a verify file of an earlier attempt — D8 review
  # SHOULD-1) with no class in this run's restore-<a>.json; released never overwrites dirty/finalized (MUST-1, io)
  LPATCH=$(L_W="$W" L_A="${AREAS[*]}" L_V="${VERIFIED_AREAS[*]}" L_DIR="$DIR" python3 -c 'import json, os
e = os.environ; d = e["L_DIR"]; areas = {}
def ok(a):
    if a not in e["L_V"].split():
        return False
    try:
        r = json.load(open(os.path.join(d, "restore-%s.json" % a)))
    except Exception:
        return False
    return not r.get("classes")
for a in e["L_W"].split():
    areas[a] = ("finalized" if ok(a) else "dirty") if a in e["L_A"].split() else "released"
print(json.dumps({"state": "auto", "areas": areas}))')
  LSTATE=$(print -r -- "$LPATCH" | python3 "$IO" ledger-update "$LEDGER" "$WINID") \
    || dd_log "WARNING userdata-guard: ledger record $WINID could not be updated at R12 — tell C-00"
  if [ "$LSTATE" = closed ]; then python3 "$IO" ledger-close "$LEDGER" "$WINID" \
      || dd_log "WARNING userdata-guard: ledger record $WINID could not be moved to closed/ — tell C-00"
  else dd_log "userdata-guard: ledger window $WINID stays dirty — its areas block new snapshots until this DIR is restored (or C-00 resolves it)"; fi
fi
# R13 summary line (stdout's LAST line); the player slot is released by restore_exit
dd_log "userdata-guard restore scope=$SCOPE touched=$TOUCHED verified=$OK extraKeys=$(grep -c . "$DIR/extra-keys.txt") outOfScopeChanged=$(grep -c . "$OOS") $M7 window=$WINID snapshotId=$SNAPID retry=$RETRY class=$CLS"
[ "$OK" -eq 1 ] || { [ ${#CLASSES} -gt 0 ] && print -r -- "userdata-guard: classes ${CLASSES[*]} — $DIR/guard-verdict.json" >&2; cat "$DIR/extra-keys.txt" >&2; }
exit $RC
