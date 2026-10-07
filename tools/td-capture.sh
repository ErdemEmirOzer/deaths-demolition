#!/bin/zsh
# WP-099 (S-01, C-00 07.10) · tools/td-capture.sh — the R3 visual-evidence standard: 8 fixed TopDown pose stills and a
# 30–60 s TopDown gameplay MP4 (H.264 + AAC, same seed every time) from a DEV build, inside the user-data guard.
#
# usage: td-capture.sh --app <dev DeathsDemolition.app> --wp <wp-0NN> --label <label>
#                      [--poses <file>] [--seed 26] [--video-seconds 45] [--size 1920x1080] [--tier sinematik]
#                      [--stills-only | --video-only] [--with-enemies] [--crowd-wait 45]
#   --app       a DEVELOPMENT build (tools/build.sh dev); release builds carry no capture agent (exit 3)
#   --wp/--label  output folder K/<wp>/capture/<label>/ — must not exist yet (an earlier delivery is never overwritten)
#   --poses     "key x z" lines, TopDown map coordinates (default tools/td-capture-poses.txt: the 8 standard poses —
#               use the default for before/after comparisons)
#   --seed      Random seed for both launches (stills: Random.InitState; video: the QA bot's seed too)
#   --with-enemies  stills keep enemy spawning on (default: held, so before/after stills compare world/light/characters)
#   --crowd-wait  seconds a "crowd40" pose waits for ≥ 38 zombies within 15 m (1..120; a short crowd exits 1)
# Output: stills/<nn>-<key>.png + stills/contact.png + stills/poses.json · video/td-<label>.mp4 (+ frames/, audio.wav,
#         sync.json, summary.json) · logs/ · ug/ (the guard window) · capture.json + SHA256SUMS.
# Order (D8): guard snapshot --scope player FIRST (it holds the player slot), then each Player through tools/player-run.sh
# (re-entrant on that slot), then restore — one shell. No Unity slot is taken (the Player is not Unity batch).
# exit: 0 done · 1 a launch failed or produced nothing · 3 not a dev build · 30/31/65/75 guard (snapshot or restore;
#       restore 32 is accepted only via qa/oos_rule.py) · 64 usage · 128+n signal (the restore still runs)
set -u
setopt nullglob
SELF="${0:A}"; HERE="${SELF:h}"
source "$HERE/dd-env.sh"

APP=""; WP=""; LABEL=""; POSES="$HERE/td-capture-poses.txt"; SEED=26; SECS=45; SIZE="1920x1080"; TIER="sinematik"; CROWD_WAIT=45
DO_STILLS=1; DO_VIDEO=1; HOLD=1
usage() { sed -n '5,15p' "$SELF" >&2; exit 64; }
while [ $# -gt 0 ]; do
  case "$1" in
    --app) APP="${2:-}"; shift ;; --wp) WP="${2:-}"; shift ;; --label) LABEL="${2:-}"; shift ;;
    --poses) POSES="${2:-}"; shift ;; --seed) SEED="${2:-}"; shift ;; --video-seconds) SECS="${2:-}"; shift ;;
    --size) SIZE="${2:-}"; shift ;; --tier) TIER="${2:-}"; shift ;; --crowd-wait) CROWD_WAIT="${2:-}"; shift ;;
    --stills-only) DO_VIDEO=0 ;; --video-only) DO_STILLS=0 ;; --with-enemies) HOLD=0 ;;
    -h|--help) usage ;;
    *) echo "td-capture: unknown option $1" >&2; usage ;;
  esac
  shift
done
[ -n "$APP" ] && [ -n "$WP" ] && [ -n "$LABEL" ] || usage
[[ "$WP" =~ '^(wp|ap)-[0-9a-z]+$' ]] || { echo "td-capture: --wp must look like wp-0NN" >&2; exit 64; }
[[ "$LABEL" =~ '^[A-Za-z0-9._-]+$' ]] || { echo "td-capture: --label may hold only A-Z a-z 0-9 . _ -" >&2; exit 64; }
[[ "$SEED" == <-> ]] && [[ "$SECS" == <-> ]] && (( SECS >= 10 && SECS <= 120 )) || { echo "td-capture: --seed integer, --video-seconds 10..120" >&2; exit 64; }
[[ "$CROWD_WAIT" == <-> ]] && (( CROWD_WAIT >= 1 && CROWD_WAIT <= 120 )) || { echo "td-capture: --crowd-wait 1..120" >&2; exit 64; }
[[ "$SIZE" =~ '^[0-9]+x[0-9]+$' ]] || { echo "td-capture: --size WxH" >&2; exit 64; }
W=${SIZE%x*}; H=${SIZE#*x}
[ -d "$APP" ] && [[ "$APP" == *.app ]] || { echo "td-capture: --app must be a .app folder: $APP" >&2; exit 64; }
APP="${APP:A}"; POSES="${POSES:A}"
[ "$DO_STILLS" -eq 0 ] || [ -f "$POSES" ] || { echo "td-capture: pose file missing: $POSES" >&2; exit 64; }
OUT="$DD_KPROJ/$WP/capture/$LABEL"
[ -e "$OUT" ] && { echo "td-capture: $OUT exists — an earlier capture is never overwritten; pick a new --label" >&2; exit 64; }
mkdir -p "$OUT/logs" "$OUT/sandbox/ui" "$OUT/sandbox/telemetry"
DD_TIMELINE="$OUT/logs/timeline.txt"
dd_log "td-capture: app=$APP wp=$WP label=$LABEL seed=$SEED size=$SIZE tier=$TIER stills=$DO_STILLS video=$DO_VIDEO hold=$HOLD"

# the Swift helpers (built once per source hash; nothing installed)
swift_bin() { local src="$HERE/td-capture/$1.swift" h bin
  h=$(shasum -a 256 "$src" | cut -c1-12); bin="${DD_TD_CAPTURE_BIN:-${TMPDIR:-/tmp}/dd-td-capture}/$1-$h"
  [ -x "$bin" ] || { mkdir -p "${bin:h}"; swiftc -O "$src" -o "$bin.tmp.$$" 2>"$OUT/logs/swiftc-$1.log" && mv "$bin.tmp.$$" "$bin" \
    || { echo "td-capture: swiftc $1 failed — $OUT/logs/swiftc-$1.log" >&2; return 1; }; }
  print -r -- "$bin"; }
TDMUX=$(swift_bin tdmux) && MP4PROBE=$(swift_bin mp4probe) || exit 1

# --- guard window (player scope: the Player writes the player area) ---------------------------------------------------
UG="$OUT/ug"
SIG=0; RC=0; PRUN=""
trap 'SIG=130; [ -n "$PRUN" ] && kill -TERM $PRUN 2>/dev/null' INT
trap 'SIG=143; [ -n "$PRUN" ] && kill -TERM $PRUN 2>/dev/null' TERM
trap 'SIG=129; [ -n "$PRUN" ] && kill -TERM $PRUN 2>/dev/null' HUP
# gt SHOULD-2 (08.10): the snapshot can wait up to an hour in the window queue — `& wait` with the traps already set, so a
# signal stops it at once (the guard leaves the queue / aborts its record); a window it opened anyway (before.txt, INV-8)
# is restored below like any other
"$DD_GUARD_CANON" snapshot "$UG" --scope player > "$OUT/logs/guard-snapshot.out" 2>&1 & PRUN=$!
wait $PRUN; GS=$?
[ $SIG -ne 0 ] && { wait $PRUN 2>/dev/null; GS=$?; }
PRUN=""
if [ $GS -ne 0 ]; then
  if [ -f "$UG/before.txt" ]; then
    echo "td-capture: guard snapshot exit $GS but its window is open (before.txt) — nothing launched, restoring it" >&2; RC=1
  else
    cat "$OUT/logs/guard-snapshot.out" >&2; echo "td-capture: guard snapshot exit $GS — nothing launched" >&2
    [ $SIG -ne 0 ] && exit $SIG; exit $GS
  fi
fi

COMMON=(-screen-fullscreen 0 -screen-width "$W" -screen-height "$H" -ddProfilePath "$OUT/sandbox/profile.json"
        -ddUiDataRoot "$OUT/sandbox/ui" -ddTelemetryDir "$OUT/sandbox/telemetry" -ddCameraMode topdown
        -ddQualityTier "$TIER" -ddTdCapture "$OUT" -ddTdSeed "$SEED")
# launch <name> <limit s> <args…>: tools/player-run.sh in the background (a signal reaches it at once), bounded
launch() { local name=$1 limit=$2 t0=$SECONDS; shift 2
  sysctl -n vm.loadavg > "$OUT/logs/load-$name.txt" 2>/dev/null   # the quality record: a loaded machine slows the Player
  dd_log "td-capture: $name launch (≤ ${limit}s)"
  "$DD_TOOLS_DIR/player-run.sh" "$APP" "${COMMON[@]}" -logFile "$OUT/logs/$name.log" "$@" > "$OUT/logs/$name.out" 2>&1 &
  PRUN=$!
  while kill -0 $PRUN 2>/dev/null; do
    [ $SIG -ne 0 ] && break
    (( SECONDS - t0 > limit )) && { dd_log "td-capture: $name over ${limit}s — TERM"; kill -TERM $PRUN 2>/dev/null; }
    sleep 1
  done
  wait $PRUN 2>/dev/null; local rc=$?; PRUN=""
  dd_log "td-capture: $name exit=$rc ($((SECONDS - t0))s)"; return $rc; }

if [ "$DO_STILLS" -eq 1 ] && [ $SIG -eq 0 ] && [ $RC -eq 0 ]; then
  launch stills 300 -ddDirectToRun -ddTdCaptureMode stills -ddTdPoses "$POSES" -ddTdHoldSpawns $HOLD -ddTdCrowdWaitSeconds "$CROWD_WAIT"
  n=$(ls "$OUT/stills"/*.png 2>/dev/null | wc -l | tr -d ' ')
  [ -f "$OUT/error.json" ] && { echo "td-capture: the capture agent refused — $(cat "$OUT/error.json")" >&2; RC=1; }
  if [ ! -f "$OUT/stills/poses.json" ]; then
    grep -q "TdCapture" "$OUT/logs/stills.log" 2>/dev/null || { echo "td-capture: no capture agent answered — is $APP a DEV build? (tools/build.sh dev)" >&2; RC=3; }
    [ $RC -eq 0 ] && { echo "td-capture: stills produced no poses.json ($n PNG) — $OUT/logs/stills.log" >&2; RC=1; }
  else
    grep -q 'navmesh-miss' "$OUT/stills/poses.json" && { echo "td-capture: a pose had no NavMesh within 2 m — see stills/poses.json" >&2; RC=1; }
    short=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("crowdShort", 0))' "$OUT/stills/poses.json" 2>/dev/null || echo "?")
    [ "$short" = 0 ] || { echo "td-capture: $short crowd pose(s) did not fill (crowdShort=$short) — raise --crowd-wait; see stills/poses.json" >&2; RC=1; }
    python3 "$HERE/td-capture/contact.py" "$OUT/stills" > "$OUT/logs/contact.out" 2>&1 || RC=1
  fi
fi
if [ "$DO_VIDEO" -eq 1 ] && [ $SIG -eq 0 ] && [ $RC -eq 0 ]; then
  mkdir -p "$OUT/video" "$OUT/bot"
  launch video $((SECS + 240)) -ddQaBot "$OUT/bot" -ddQaBotSeed "$SEED" -ddQaBotInvulnerable 1 \
    -ddTdCaptureMode video -ddTdVideoSeconds "$SECS" -ddAudioCapture "$OUT/video/audio.wav"
  if [ -f "$OUT/video/frames.tsv" ] && [ -f "$OUT/video/sync.json" ]; then
    OFF=$(python3 "$HERE/td-capture/sync_offset.py" "$OUT/video") || { echo "td-capture: no A/V offset — $OUT/video/sync.json" >&2; RC=1; }
    if [ $RC -eq 0 ]; then
      MUXARGS=(--frames "$OUT/video/frames" --times "$OUT/video/frames.tsv" --out "$OUT/video/td-$LABEL.mp4")
      [ -s "$OUT/video/audio.wav" ] && MUXARGS+=(--wav "$OUT/video/audio.wav" --wav-offset "$OFF")
      "$TDMUX" "${MUXARGS[@]}" > "$OUT/logs/tdmux.out" 2>&1 && "$MP4PROBE" "$OUT/video/td-$LABEL.mp4" > "$OUT/video/probe.json" \
        || { echo "td-capture: mux failed — $OUT/logs/tdmux.out" >&2; RC=1; }
    fi
  else
    echo "td-capture: the video launch wrote no frames.tsv/sync.json — $OUT/logs/video.log" >&2; RC=1
  fi
fi

# --- restore (signals ignored from here: the restore must finish) -----------------------------------------------------
trap '' INT TERM HUP
"$DD_GUARD_CANON" restore "$UG" --scope player > "$OUT/logs/guard-restore.out" 2>&1; GR=$?
if [ $GR -eq 32 ]; then
  if python3 "$HERE/qa/oos_rule.py" "$UG" > "$OUT/logs/oos-rule.out" 2>&1; then
    dd_log "td-capture: WARNING guard 32 accepted (C-00 30.09 rule) — $(cat "$OUT/logs/oos-rule.out")"; GR=0
  else
    echo "td-capture: guard restore 32 NOT accepted — $(cat "$OUT/logs/oos-rule.out"); STOP, tell C-00" >&2
  fi
fi
[ $GR -eq 0 ] && ! grep -q 'restore scope=player .*verified=1' "$OUT/logs/guard-restore.out" && GR=31
[ $GR -eq 0 ] || { echo "td-capture: guard restore $GR — STOP, tell C-00 ($OUT/logs/guard-restore.out)" >&2; }

# --- record (always: what was asked, what came out) -------------------------------------------------------------------
python3 "$HERE/td-capture/manifest.py" "$OUT" --app "$APP" --seed "$SEED" --poses "$POSES" --size "$SIZE" --tier "$TIER" \
  --hold "$HOLD" --guard "$GR" --rc "$RC" --signal "$SIG" > "$OUT/logs/manifest.out" 2>&1 || true
python3 -c 'import json, sys; q = json.load(open(sys.argv[1])).get("videoQuality") or {}
v = (q.get("verdict") or "").split("+"); d = q.get("avDrift") or {}
"low-fps" in v and print("WARNING td-capture: the MP4 stutters — effective %.1f fps < %d (load at launch %s); retake on a quieter machine" % (q["effectiveFps"], q["minFps"], q.get("loadAtLaunch")))
"av-drift" in v and print("WARNING td-capture: the audio clock drifted %+.3f s from real time by the stop (max |%.3f| s > %.2f) — sound and picture are out of step in the MP4; retake on a quieter machine" % (d["atStop"], d["maxAbs"], q["maxAvDrift"]))' "$OUT/capture.json" 2>&1 | tee -a "$DD_TIMELINE" >&2
[ $SIG -ne 0 ] && exit $SIG
[ $GR -ne 0 ] && exit $GR
[ $RC -eq 0 ] && dd_log "td-capture: done — $OUT"
exit $RC
