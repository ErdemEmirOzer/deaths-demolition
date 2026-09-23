#!/bin/zsh
# WP-025 · tools/smoke.sh <DeathsDemolition.app> <outdir> [--delay S]
#
# Wraps the e1 (main menu) and e4 (direct-to-run) player smoke used since WP-000:
#   e1: launch, capture <out>/e1-mainmenu-1280x720.png after --delay seconds, exit 0, no exception
#   e4: PlayerPrefs DD.Prototype.DirectToRun=1, launch, capture <out>/e4-run-1280x720.png, exit 0, no exception
#
# User-data guard (WP-009 rule: a build never changes the real profile):
#   * development builds get -ddProfilePath <out>/profile-sandbox/prototype-profile.json, so the
#     real profile is never opened (release builds have no such flag: the player saves on quit);
#   * macOS preferences (PlayerPrefs plists) and the release profile cannot be redirected, so the
#     four real files (+ side files) are copied before the runs and restored + hash-verified
#     afterwards — the guard fails (exit 30) only if the restore does not reproduce the hashes;
#   * WP-066D: cfprefsd writes plists to disk lazily, so after the players exit and after the
#     `defaults import` restore the guard waits (≤ 10 s) until each plist on disk equals the
#     cfprefsd view (`defaults export`) before hashing; Unity's own launch counter keys
#     (unity.player_session_count / unity.player_sessionid — every player launch, including other
#     sessions' players with the same bundle id, bumps them in both plists) are left out of the two
#     plist digests; every other key is still compared, the profile and its side files stay byte-hashed;
#   * result in <out>/smoke-report.json ("userdata": before/after/restored/verified).
# Exit: 0 ok · 20 smoke failed (exit code / exception / missing capture) · 30 user-data guard failed · 64 usage
# Machine slot: takes the single 'smoke' slot (tools/slot.sh v4) for the whole e1+e4+restore sequence.
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
APP="${1:-}"; OUT="${2:-}"; shift 2 2>/dev/null || true
DELAY=8; MODE=""
while [ $# -gt 0 ]; do case "$1" in --delay) DELAY="$2"; shift ;; --mode) MODE="$2"; shift ;; *) echo "unknown option: $1" >&2; exit 64 ;; esac; shift; done
[ -d "$APP" ] && [ -n "$OUT" ] || { echo "usage: smoke.sh <app> <outdir> [--delay S] [--mode dev|release]" >&2; exit 64; }
[ -n "$MODE" ] || MODE=$( [ -f "$OUT/build-report.json" ] && grep -o '"mode": "[a-z]*"' "$OUT/build-report.json" | head -1 | cut -d'"' -f4 || echo release )
mkdir -p "$OUT"; DD_TIMELINE="$OUT/timeline.txt"
BIN="$APP/Contents/MacOS/$DD_PRODUCT"; [ -x "$BIN" ] || { dd_log "smoke: player binary missing: $BIN"; exit 20; }

AS="$HOME/Library/Application Support"; PR="$HOME/Library/Preferences"
D1="$AS/Deaths Demolition/$DD_PRODUCT"; D2="$AS/$DD_BUNDLE_ID"
F1="$D1/prototype-profile.json"; F2="$D2/prototype-profile.json"
P1="$PR/$DD_BUNDLE_ID.plist"; P2="$PR/unity.Deaths Demolition.$DD_PRODUCT.plist"
# Side files = .bak / .corrupt.N.json (saved progress). The .lock file is a live process lease
# (pid/name diagnostics, WP-009H); its bytes change whenever any player holds the profile, so it
# is neither backed up nor compared — parallel sessions would otherwise trip the guard.
# WP-066D: digest of a PlayerPrefs plist with only Unity's launch-counter keys removed (canonical JSON of the rest).
UNITY_VOLATILE="unity.player_session_count unity.player_sessionid"
plist_digest() { [ -f "$1" ] || { echo MISSING; return; }
  python3 - "$1" "$UNITY_VOLATILE" <<'PY'
import hashlib, json, plistlib, sys
d = plistlib.load(open(sys.argv[1], "rb"))
for k in sys.argv[2].split(): d.pop(k, None)
print(hashlib.sha256(json.dumps(d, sort_keys=True, default=repr).encode()).hexdigest())
PY
}
# Wait until the plist on disk mirrors cfprefsd's in-memory domain (lazy write-back), ≤ 10 s.
PLIST_SAME='import plistlib, sys
raw = sys.stdin.buffer.read()
live = plistlib.loads(raw) if raw.strip() else None
try:
    disk = plistlib.load(open(sys.argv[1], "rb"))
except FileNotFoundError:
    disk = None
sys.exit(0 if (live or None) == (disk or None) else 1)'
settle_plist() { local dom="$1" file="$2" i=0
  while [ $i -lt 50 ]; do
    defaults export "$dom" - 2>/dev/null | python3 -c "$PLIST_SAME" "$file" && return 0
    sleep 0.2; i=$((i+1))
  done
  dd_log "guard: $dom not settled on disk after 10 s"; return 1
}
settle_all() { settle_plist "$DD_BUNDLE_ID" "$P1"; settle_plist "unity.Deaths Demolition.$DD_PRODUCT" "$P2"; }
sidefiles() { for d in "$D1" "$D2"; do [ -d "$d" ] && find "$d" -maxdepth 1 -name 'prototype-profile.json.*' ! -name '*.lock' 2>/dev/null; done | LC_ALL=C sort; }
snapshot() { for f in "$F1" "$F2"; do echo "$(dd_sha "$f")  $f"; done; for f in "$P1" "$P2"; do echo "$(plist_digest "$f")  $f (launch-counter keys excluded)"; done; sidefiles | while IFS= read -r f; do echo "$(dd_sha "$f")  $f"; done; }

# 1) guard: copy the real user data (no deletion, our own directory only)
UD="$OUT/userdata-backup"; mkdir -p "$UD/D1" "$UD/D2" "$UD/PR"
[ -f "$F1" ] && cp -p "$F1" "$UD/D1/"; for f in "$D2"/prototype-profile.json*; do [ -f "$f" ] && [ "${f##*.}" != "lock" ] && cp -p "$f" "$UD/D2/"; done
[ -f "$P1" ] && cp -p "$P1" "$UD/PR/"; [ -f "$P2" ] && cp -p "$P2" "$UD/PR/"
snapshot > "$OUT/userdata-before.txt"
SBX="$OUT/profile-sandbox"; mkdir -p "$SBX"
DEVFLAGS=(); [ "$MODE" = "dev" ] && DEVFLAGS=(-ddProfilePath "$SBX/prototype-profile.json")
dd_log "smoke mode=$MODE profileSandbox=$([ "$MODE" = "dev" ] && echo yes || echo no)"

trap 'dd_slot_release smoke' EXIT
dd_slot_acquire smoke   # slot.sh v4 'smoke' kind (limit 1): only one player smoke at a time — release smokes write the real profile
# 2) e1 main menu
"$BIN" -screen-fullscreen 0 -ddCapture "$OUT/e1-mainmenu-1280x720.png" -ddSize 1280x720 -ddDelay "$DELAY" "${DEVFLAGS[@]}" -logFile "$OUT/e1-player.log"
E1=$?; E1X=$(grep -c 'Exception\|WITH FAILURES' "$OUT/e1-player.log" 2>/dev/null); E1X=${E1X:-0}
dd_log "e1 exit=$E1 exceptions=$E1X capture=$([ -s "$OUT/e1-mainmenu-1280x720.png" ] && echo yes || echo no)"
# 3) e4 direct to run — WP-052C: process-local `-ddDirectToRun` flag instead of the machine-wide
#    PlayerPrefs key (that key dropped OTHER sessions' players straight into Run). Guard: if an old
#    run left the key in the real plist, remove it now and after the run (idempotent).
defaults delete "$DD_BUNDLE_ID" "DD.Prototype.DirectToRun" >/dev/null 2>&1 && dd_log "guard: stale DD.Prototype.DirectToRun key removed from PlayerPrefs"
"$BIN" -screen-fullscreen 0 -ddDirectToRun -ddCapture "$OUT/e4-run-1280x720.png" -ddSize 1280x720 -ddDelay "$DELAY" "${DEVFLAGS[@]}" -logFile "$OUT/e4-run-player.log"
E4=$?; E4X=$(grep -c 'Exception\|WITH FAILURES' "$OUT/e4-run-player.log" 2>/dev/null); E4X=${E4X:-0}
defaults delete "$DD_BUNDLE_ID" "DD.Prototype.DirectToRun" >/dev/null 2>&1 && dd_log "guard: DD.Prototype.DirectToRun key removed after e4"
dd_log "e4 exit=$E4 exceptions=$E4X capture=$([ -s "$OUT/e4-run-1280x720.png" ] && echo yes || echo no)"

# 4) guard: restore + verify (WP-066D: let cfprefsd write the players' last changes first)
settle_all
snapshot > "$OUT/userdata-after-run.txt"
TOUCHED=0; cmp -s "$OUT/userdata-before.txt" "$OUT/userdata-after-run.txt" || TOUCHED=1
if [ "$TOUCHED" -eq 1 ]; then
  [ -f "$UD/D1/prototype-profile.json" ] && cp -p "$UD/D1/prototype-profile.json" "$F1"
  for f in "$D2"/prototype-profile.json*; do [ -f "$f" ] && [ "${f##*.}" != "lock" ] && [ ! -f "$UD/D2/$(basename "$f")" ] && mv "$f" "$OUT/userdata-extra-$(basename "$f")"; done
  for f in "$UD/D2"/prototype-profile.json*; do [ -f "$f" ] && cp -p "$f" "$D2/"; done
  [ -f "$UD/PR/$(basename "$P1")" ] && defaults import "$DD_BUNDLE_ID" "$UD/PR/$(basename "$P1")"
  [ -f "$UD/PR/$(basename "$P2")" ] && defaults import "unity.Deaths Demolition.$DD_PRODUCT" "$UD/PR/$(basename "$P2")"
  sleep 1
  settle_all
fi
snapshot > "$OUT/userdata-after.txt"
VERIFIED=0; cmp -s "$OUT/userdata-before.txt" "$OUT/userdata-after.txt" && VERIFIED=1
SBXPROFILE=$( [ -f "$SBX/prototype-profile.json" ] && echo "$SBX/prototype-profile.json" || echo "" )
dd_log "userdata touched=$TOUCHED restored=$TOUCHED verified=$VERIFIED sandboxProfile=$([ -n "$SBXPROFILE" ] && echo yes || echo no)"

SMOKE_OK=1
[ "$E1" -eq 0 ] && [ "$E1X" -eq 0 ] && [ -s "$OUT/e1-mainmenu-1280x720.png" ] || SMOKE_OK=0
[ "$E4" -eq 0 ] && [ "$E4X" -eq 0 ] && [ -s "$OUT/e4-run-1280x720.png" ] || SMOKE_OK=0
CODE=0; [ "$SMOKE_OK" -eq 1 ] || CODE=20; [ "$VERIFIED" -eq 1 ] || CODE=30
cat > "$OUT/smoke-report.json" <<JSON
{
  "ok": $([ "$CODE" -eq 0 ] && echo true || echo false),
  "exitCode": $CODE,
  "app": "$APP",
  "mode": "$MODE",
  "e1": { "exit": $E1, "exceptions": $E1X, "capture": "$OUT/e1-mainmenu-1280x720.png", "log": "$OUT/e1-player.log" },
  "e4": { "exit": $E4, "exceptions": $E4X, "capture": "$OUT/e4-run-1280x720.png", "log": "$OUT/e4-run-player.log" },
  "userdata": { "touched": $([ "$TOUCHED" -eq 1 ] && echo true || echo false), "restored": $([ "$TOUCHED" -eq 1 ] && echo true || echo false), "verified": $([ "$VERIFIED" -eq 1 ] && echo true || echo false), "sandboxProfile": $([ -n "$SBXPROFILE" ] && echo true || echo false), "before": "$OUT/userdata-before.txt", "after": "$OUT/userdata-after.txt" },
  "utc": "$(date -u +%FT%TZ)"
}
JSON
dd_log "smoke.sh done exit=$CODE"
exit $CODE
