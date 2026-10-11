#!/bin/zsh
# WP-110 (S-01 mk-capture, C-00 11.10; S-11 §9) · tools/mk-capture.sh — marketing capture: QA bots play the game, the capture
# agent (MkCaptureAgent, DEV builds only) keeps the highlight clips from an off-screen 1080×1920 master, and every clip
# becomes Instagram-format MP4s (9:16 1080×1920 · 3:4 1080×1440 · 4:5 1080×1350 · 1:1 1080×1080, centre crops, sound
# aligned per second) + cropped stills, inside ONE user-data guard window.
#
# usage: mk-capture.sh --app <dev DeathsDemolition.app> --wp <wp-1NN> --label <label> --segments <file>
#                      [--formats 1080x1920,1080x1440,1080x1350,1080x1080] [--tier sinematik] [--window 960x540] [--keep-frames]
#                      [--raw-gb 5] [--ring-mb 640] [--hud-safe] [--final-sha <R4 FINAL commit>]
#   --segments  one segment per line, whitespace-separated columns (# comments; "-" = default):
#                 name weapon start rounds life specials camera hud seconds clips music seed [threshold [mode [tree]]]
#               name [a-z0-9-]+ · weapon a ClassicWeaponId name/number (QaRunBot -ddQaBotWeapon) · start: first round
#               (-ddQaBotStartRound) · rounds: rounds the bot plays (≤ 50) · life immortal|mortal · specials 0|1 (the bot
#               grants itself Special Weapons near a crowd) · camera Y|O|G · hud off|on|both · seconds: session cap (60..7200)
#               · clips: kept-clip cap · music on|off (off = the mixer's Music group muted) · seed · threshold (heat, default 10)
#               · mode normal|hard|demolition (WP-102 -ddDifficulty, default normal) · tree a WP-104 -ddTreePreset (e.g. barrage_r15)
#   --window    the Player window (windowed; the capture is off-screen, the window only previews it)
#   --keep-frames  keep every clip's raw JPEG frames (default: removed once all its MP4s probed)
#   --raw-gb    raw bytes one segment may write (clip frames + stills; gt M1 11.10), capped at free disk − 20 GB; the agent
#               stops the segment at it (stopReason raw-budget). --ring-mb: the preroll ring's memory (640 ≈ 18 s hud both)
#   --hud-safe  the HUD streams lay the HUD out inside the Reels safe rectangle (x 0–960, y 250–1520 of 1080×1920; S-11)
#   R4 rule 1: DD_GUARD_KNOWN_GOOD must be exported (KARAR-GUARD-KG-10.10) and the snapshot must say kgSource=env.
# Output: K/<wp>/capture/<label>/<segment>/ — clips/NN/{clip.json, stills/, stills-hud/}, mp4/*.mp4, stills/*.png|jpg,
#         clips.json, summary.json, events.tsv, audio.wav, logs; capture.json + SHA256SUMS at the label root.
# Order (D8): guard snapshot --scope player FIRST (it holds the player slot), then one Player per segment through
# tools/player-run.sh (re-entrant on that slot), each segment muxed right after its Player (disk), then restore — one shell.
# exit: 0 done · 1 a segment produced no session / a clip failed to mux / the snapshot failed with its window open · 3 not a
#       dev build · 30/31/65/75 guard (restore 32 accepted only via qa/oos_rule.py) · 64 usage / no KG env / snapshot without
#       kgSource=env · 74 disk: < 20 GB free (or < 1 GB raw budget) before a segment · 128+n signal (the restore still runs)
set -u
setopt nullglob
SELF="${0:A}"; HERE="${SELF:h}"
source "$HERE/dd-env.sh"

APP=""; WP=""; LABEL=""; SEGS=""; FORMATS="1080x1920,1080x1440,1080x1350,1080x1080"; TIER="sinematik"; WIN="960x540"; KEEP=0
RAWGB=5; RINGMB=640; HUDSAFE=0; FINALSHA=""
usage() { sed -n '7,23p' "$SELF" >&2; exit 64; }
while [ $# -gt 0 ]; do
  case "$1" in
    --app) APP="${2:-}"; shift ;; --wp) WP="${2:-}"; shift ;; --label) LABEL="${2:-}"; shift ;;
    --segments) SEGS="${2:-}"; shift ;; --formats) FORMATS="${2:-}"; shift ;; --tier) TIER="${2:-}"; shift ;;
    --window) WIN="${2:-}"; shift ;; --keep-frames) KEEP=1 ;;
    --raw-gb) RAWGB="${2:-}"; shift ;; --ring-mb) RINGMB="${2:-}"; shift ;; --hud-safe) HUDSAFE=1 ;;
    --final-sha) FINALSHA="${2:-}"; [ -n "$FINALSHA" ] || { echo "mk-capture: --final-sha needs a value" >&2; exit 64; }; shift ;;
    -h|--help) usage ;;
    *) echo "mk-capture: unknown option $1" >&2; usage ;;
  esac
  shift
done
[ -n "$APP" ] && [ -n "$WP" ] && [ -n "$LABEL" ] && [ -n "$SEGS" ] || usage
[[ "$WP" =~ '^(wp|ap)-[0-9a-z]+$' ]] || { echo "mk-capture: --wp must look like wp-1NN" >&2; exit 64; }
[[ "$LABEL" =~ '^[A-Za-z0-9._-]+$' ]] || { echo "mk-capture: --label may hold only A-Z a-z 0-9 . _ -" >&2; exit 64; }
[[ "$WIN" =~ '^[0-9]+x[0-9]+$' ]] || { echo "mk-capture: --window WxH" >&2; exit 64; }
[[ "$FORMATS" =~ '^[0-9]+x[0-9]+(,[0-9]+x[0-9]+)*$' ]] || { echo "mk-capture: --formats WxH[,WxH…]" >&2; exit 64; }
[ -d "$APP" ] && [[ "$APP" == *.app ]] || { echo "mk-capture: --app must be a .app folder: $APP" >&2; exit 64; }
[ -f "$SEGS" ] || { echo "mk-capture: segment file missing: $SEGS" >&2; exit 64; }
[[ "$RAWGB" == <-> ]] && (( RAWGB >= 1 && RAWGB <= 50 )) && [[ "$RINGMB" == <-> ]] && (( RINGMB >= 32 && RINGMB <= 2048 )) \
  || { echo "mk-capture: --raw-gb 1..50, --ring-mb 32..2048" >&2; exit 64; }
[ -n "${DD_GUARD_KNOWN_GOOD:-}" ] || { echo "mk-capture: R4 rule 1 — export DD_GUARD_KNOWN_GOOD first (K/c-00/R3/KARAR-GUARD-KG-10.10.md)" >&2; exit 64; }
APP="${APP:A}"; SEGS="${SEGS:A}"
# C-00 12.10 (the user: the Instagram pictures are R4 as finished): a shoot only on a build of the R4 FINAL PASS commit — a
# label starting with dry is a dry run (exempt, recorded dry). Checked before anything opens (mk_manifest.py gate).
GATE=$(PYTHONDONTWRITEBYTECODE=1 python3 "$HERE/mk-capture/mk_manifest.py" gate --app "$APP" --label "$LABEL" ${FINALSHA:+--final-sha "$FINALSHA"}) \
  || { echo "mk-capture: R4 FINAL gate — $(python3 -c 'import json,sys; print(json.loads(sys.argv[1]).get("verdict"))' "$GATE" 2>/dev/null || print -r -- "$GATE")" >&2; exit 64; }

# --- the segment table (validated before anything opens) -------------------------------------------------------------
typeset -a SEG_LINES
LIFE_OK=(immortal mortal); BIT_OK=(0 1); CAM_OK=(Y O G); HUD_OK=(off on both); MUSIC_OK=(on off); MODE_OK=(normal hard demolition)
dflt() { [ "$1" = "-" ] && print -r -- "$2" || print -r -- "$1"; }
n=0
while IFS= read -r line || [ -n "$line" ]; do
  line="${line%%\#*}"; [[ -z "${line// /}" ]] && continue
  f=(${=line}); n=$((n + 1))
  (( ${#f} >= 12 && ${#f} <= 15 )) || { echo "mk-capture: segment line $n: 12 to 15 columns, got ${#f}: $line" >&2; exit 64; }
  [[ "${f[1]}" =~ '^[a-z0-9-]+$' ]] || { echo "mk-capture: segment $n: name [a-z0-9-]+ (${f[1]})" >&2; exit 64; }
  [[ "${f[2]}" =~ '^([A-Za-z][A-Za-z0-9]*|[0-9]+|-)$' ]] || { echo "mk-capture: ${f[1]}: weapon (${f[2]})" >&2; exit 64; }
  for k in 3 4 9 10 12; do [[ "${f[$k]}" == (<->|-) ]] || { echo "mk-capture: ${f[1]}: column $k must be an integer or - (${f[$k]})" >&2; exit 64; }; done
  [[ "$(dflt ${f[3]} 1)" -ge 1 && "$(dflt ${f[3]} 1)" -le 50 ]] || { echo "mk-capture: ${f[1]}: start 1..50" >&2; exit 64; }
  [[ "$(dflt ${f[4]} 30)" -ge 1 && "$(dflt ${f[4]} 30)" -le 50 ]] || { echo "mk-capture: ${f[1]}: rounds 1..50" >&2; exit 64; }
  (( ${LIFE_OK[(Ie)$(dflt ${f[5]} immortal)]} )) || { echo "mk-capture: ${f[1]}: life immortal|mortal (${f[5]})" >&2; exit 64; }
  (( ${BIT_OK[(Ie)$(dflt ${f[6]} 0)]} )) || { echo "mk-capture: ${f[1]}: specials 0|1 (${f[6]})" >&2; exit 64; }
  (( ${CAM_OK[(Ie)$(dflt ${f[7]} O)]} )) || { echo "mk-capture: ${f[1]}: camera Y|O|G (${f[7]})" >&2; exit 64; }
  (( ${HUD_OK[(Ie)$(dflt ${f[8]} off)]} )) || { echo "mk-capture: ${f[1]}: hud off|on|both (${f[8]})" >&2; exit 64; }
  [[ "$(dflt ${f[9]} 600)" -ge 60 && "$(dflt ${f[9]} 600)" -le 7200 ]] || { echo "mk-capture: ${f[1]}: seconds 60..7200" >&2; exit 64; }
  [[ "$(dflt ${f[10]} 12)" -ge 1 && "$(dflt ${f[10]} 12)" -le 60 ]] || { echo "mk-capture: ${f[1]}: clips 1..60" >&2; exit 64; }
  [[ ${#$(dflt ${f[12]} 26)} -le 10 && "$(dflt ${f[12]} 26)" -le 2147483647 ]] || { echo "mk-capture: ${f[1]}: seed 0..2147483647" >&2; exit 64; }
  [ ${#f} -lt 13 ] || [ "${f[13]}" = "-" ] || { (( ${f[13]%%.*} >= 1 && ${f[13]%%.*} <= 200 )) } || { echo "mk-capture: ${f[1]}: threshold 1..200" >&2; exit 64; }
  (( ${MUSIC_OK[(Ie)$(dflt ${f[11]} on)]} )) || { echo "mk-capture: ${f[1]}: music on|off (${f[11]})" >&2; exit 64; }
  [ ${#f} -lt 13 ] || [[ "${f[13]}" =~ '^([0-9]+(\.[0-9]+)?|-)$' ]] || { echo "mk-capture: ${f[1]}: threshold (${f[13]})" >&2; exit 64; }
  [ ${#f} -lt 14 ] || (( ${MODE_OK[(Ie)$(dflt ${f[14]} normal)]} )) || { echo "mk-capture: ${f[1]}: mode normal|hard|demolition (${f[14]})" >&2; exit 64; }
  [ ${#f} -lt 15 ] || [[ "${f[15]}" =~ '^([a-z][a-z0-9_]*|-)$' ]] || { echo "mk-capture: ${f[1]}: tree preset [a-z][a-z0-9_]* (${f[15]})" >&2; exit 64; }
  for prev in "${SEG_LINES[@]}"; do [ "${${=prev}[1]}" = "${f[1]}" ] && { echo "mk-capture: segment name ${f[1]} twice" >&2; exit 64; }; done
  SEG_LINES+=("$line")
done < "$SEGS"
(( ${#SEG_LINES} > 0 )) || { echo "mk-capture: no segment in $SEGS" >&2; exit 64; }

OUT="$DD_KPROJ/$WP/capture/$LABEL"
[ -e "$OUT" ] && { echo "mk-capture: $OUT exists — an earlier capture is never overwritten; pick a new --label" >&2; exit 64; }
mkdir -p "$OUT/logs"
cp "$SEGS" "$OUT/segments.txt"
print -r -- "$GATE" > "$OUT/logs/final-gate.json"
DD_TIMELINE="$OUT/logs/timeline.txt"
dd_log "mk-capture: app=$APP wp=$WP label=$LABEL segments=${#SEG_LINES} formats=$FORMATS tier=$TIER window=$WIN keepFrames=$KEEP"

swift_bin() { local src="$HERE/td-capture/$1.swift" h bin
  h=$(shasum -a 256 "$src" | cut -c1-12); bin="${DD_TD_CAPTURE_BIN:-${TMPDIR:-/tmp}/dd-td-capture}/$1-$h"
  [ -x "$bin" ] || { mkdir -p "${bin:h}"; swiftc -O "$src" -o "$bin.tmp.$$" 2>"$OUT/logs/swiftc-$1.log" && mv "$bin.tmp.$$" "$bin" \
    || { echo "mk-capture: swiftc $1 failed — $OUT/logs/swiftc-$1.log" >&2; return 1; }; }
  print -r -- "$bin"; }
TDMUX=$(swift_bin tdmux) && MP4PROBE=$(swift_bin mp4probe) || exit 1

# --- guard window (player scope) ----------------------------------------------------------------------------------------
UG="$OUT/ug"
SIG=0; RC=0; PRUN=""
trap 'SIG=130; [ -n "$PRUN" ] && kill -TERM $PRUN 2>/dev/null' INT
trap 'SIG=143; [ -n "$PRUN" ] && kill -TERM $PRUN 2>/dev/null' TERM
trap 'SIG=129; [ -n "$PRUN" ] && kill -TERM $PRUN 2>/dev/null' HUP
"$DD_GUARD_CANON" snapshot "$UG" --scope player > "$OUT/logs/guard-snapshot.out" 2>&1 & PRUN=$!
wait $PRUN; GS=$?
[ $SIG -ne 0 ] && { wait $PRUN 2>/dev/null; GS=$?; }
PRUN=""
if [ $GS -ne 0 ]; then
  if [ -f "$UG/before.txt" ]; then
    echo "mk-capture: guard snapshot exit $GS but its window is open (before.txt) — nothing launched, restoring it" >&2; RC=1
  else
    cat "$OUT/logs/guard-snapshot.out" >&2; echo "mk-capture: guard snapshot exit $GS — nothing launched" >&2
    [ $SIG -ne 0 ] && exit $SIG; exit $GS
  fi
fi

# R4 rule 1: the window checks the user's own display values (kgSource=env), else nothing runs in it
[ $RC -eq 0 ] && ! grep -q 'kgSource=env' "$OUT/logs/guard-snapshot.out" && {
  echo "mk-capture: the guard snapshot does not say kgSource=env — nothing launched (KARAR-GUARD-KG-10.10)" >&2; RC=64; }
PRE_RC=$RC
W=${WIN%x*}; H=${WIN#*x}
launch() { local name=$1 limit=$2 dir=$3 t0=$SECONDS; shift 3
  sysctl -n vm.loadavg > "$dir/logs/load.txt" 2>/dev/null
  dd_log "mk-capture: $name launch (≤ ${limit}s)"
  "$DD_TOOLS_DIR/player-run.sh" "$APP" -screen-fullscreen 0 -screen-width "$W" -screen-height "$H" \
    -ddProfilePath "$dir/sandbox/profile.json" -ddUiDataRoot "$dir/sandbox/ui" -ddTelemetryDir "$dir/sandbox/telemetry" \
    -ddCameraMode topdown -ddQualityTier "$TIER" -logFile "$dir/logs/player.log" "$@" > "$dir/logs/player.out" 2>&1 &
  PRUN=$!
  while kill -0 $PRUN 2>/dev/null; do
    [ $SIG -ne 0 ] && break
    (( SECONDS - t0 > limit )) && { dd_log "mk-capture: $name over ${limit}s — TERM"; kill -TERM $PRUN 2>/dev/null; }
    sleep 1
  done
  wait $PRUN 2>/dev/null; local rc=$?; PRUN=""
  dd_log "mk-capture: $name exit=$rc ($((SECONDS - t0))s)"; return $rc; }

for line in "${SEG_LINES[@]}"; do
  [ $PRE_RC -ne 0 ] || [ $SIG -ne 0 ] || [ $RC -eq 3 ] && break   # gt S5: after a failed snapshot / no kgSource=env nothing launches
  f=(${=line})
  name=${f[1]}; weapon=$(dflt ${f[2]} -); start=$(dflt ${f[3]} 1); rounds=$(dflt ${f[4]} 30); life=$(dflt ${f[5]} immortal)
  specials=$(dflt ${f[6]} 0); cam=$(dflt ${f[7]} O); hud=$(dflt ${f[8]} off); secs=$(dflt ${f[9]} 600); clips=$(dflt ${f[10]} 12)
  music=$(dflt ${f[11]} on); seed=$(dflt ${f[12]} 26); thr=$(dflt "${f[13]:--}" 10); mode=$(dflt "${f[14]:--}" normal); tree=$(dflt "${f[15]:--}" -)
  free_gb=$(( $(df -k "$OUT" | awk 'NR==2 {print $4}') / 1048576 )); min_free=${DD_MK_MIN_FREE_GB:-20}
  (( free_gb >= min_free )) || { echo "mk-capture: $free_gb GB free before segment $name (< $min_free) — stopping; the segments so far stay" >&2; RC=74; break; }
  seg_raw=$(( RAWGB < free_gb - min_free ? RAWGB : free_gb - min_free ))   # gt M1: never past the free-disk floor
  (( seg_raw >= 1 )) || { echo "mk-capture: raw budget $seg_raw GB before segment $name (free $free_gb − floor $min_free) — stopping" >&2; RC=74; break; }
  D="$OUT/$name"; mkdir -p "$D/logs" "$D/bot" "$D/sandbox/ui" "$D/sandbox/telemetry"
  BOT=(-ddQaBot "$D/bot" -ddQaBotSeed "$seed" -ddQaBotRounds "$rounds" -ddQaBotRoundTimeout 900 -ddQaBotStartRound "$start")
  [ "$weapon" != "-" ] && BOT+=(-ddQaBotWeapon "$weapon")
  [ "$tree" != "-" ] && BOT+=(-ddTreePreset "$tree")   # WP-104: the game grants the preset's nodes at run start (dev builds)
  BOT+=(-ddDifficulty "$mode")   # WP-102: always explicit (normal too), so the run plays the segment's mode
  [ "$life" = immortal ] && BOT+=(-ddQaBotInvulnerable 1)
  [ "$specials" = 1 ] && BOT+=(-ddQaBotSpecial 1)
  MK=(-ddMkCapture "$D" -ddMkCamera "$cam" -ddMkHud "$hud" -ddMkSeconds "$secs" -ddMkClips "$clips" -ddMkThreshold "$thr" -ddMkRawGB "$seg_raw" -ddMkRingMB "$RINGMB" -ddMkHudSafe "$HUDSAFE"
      -ddMkMusicOff "$([ "$music" = off ] && echo 1 || echo 0)" -ddAudioCapture "$D/audio.wav")
  launch "$name" $((secs + 300)) "$D" "${BOT[@]}" "${MK[@]}"; prc=$?
  if [ -f "$D/error.json" ]; then echo "mk-capture: $name: the capture agent refused — $(cat "$D/error.json")" >&2; RC=1
  elif [ ! -f "$D/summary.json" ]; then
    REFUSED=$(grep -E 'not started|TIMEOUT|refus' "$D/logs/player.out" 2>/dev/null | tail -1)
    if [ -n "$REFUSED" ]; then echo "mk-capture: $name: the launch was refused by player-run: $REFUSED" >&2; RC=1
    elif ! grep -q "MkCapture" "$D/logs/player.log" 2>/dev/null; then echo "mk-capture: no capture agent answered — is $APP a DEV build? (tools/build.sh dev)" >&2; RC=3
    else echo "mk-capture: $name: no summary.json (player exit $prc) — $D/logs/player.log" >&2; RC=1; fi
  else
    KF=(); [ $KEEP -eq 1 ] && KF=(--keep-frames)
    PYTHONDONTWRITEBYTECODE=1 python3 "$HERE/mk-capture/mk_clips.py" "$D" --tdmux "$TDMUX" --probe "$MP4PROBE" --formats "$FORMATS" --segment "$name" \
      --meta "weapon=$weapon" "start=$start" "life=$life" "specials=$specials" "music=$music" "seed=$seed" "mode=$mode" "tree=$tree" "${KF[@]}" > "$D/logs/mk_clips.out" 2>&1 \
      || { echo "mk-capture: $name: a clip did not mux — $D/logs/mk_clips.out" >&2; RC=1; }
    if [ "$tree" != "-" ]; then   # WP-104: an unknown preset is ignored by the game — the segment would play without it
      TREE_OK=$(python3 -c 'import json,sys; t=(json.load(open(sys.argv[1])).get("tree") or {}); print("ok" if (t.get("preset") or "").lower()==sys.argv[2].lower() and t.get("presetNodesOwned",0)>0 else "asked %s, applied %s, nodes %s/%s" % (sys.argv[2], t.get("preset"), t.get("presetNodesOwned"), t.get("presetNodes")))' "$D/summary.json" "$tree" 2>&1)
      [ "$TREE_OK" = ok ] || { echo "mk-capture: $name: the tree preset did not apply — $TREE_OK" >&2; RC=1; }
    fi
    dd_log "mk-capture: $name: $(python3 -c 'import json,sys; s=json.load(open(sys.argv[1])); print("stop", s.get("stopReason"), "clips kept", s.get("clipsKept"), "events", (s.get("events") or {}).get("total"), "fps", (s.get("frames") or {}).get("sessionFps"))' "$D/summary.json" 2>&1)"
  fi
done

# --- restore (signals ignored from here: the restore must finish) -----------------------------------------------------
trap '' INT TERM HUP
"$DD_GUARD_CANON" restore "$UG" --scope player > "$OUT/logs/guard-restore.out" 2>&1; GR=$?
if [ $GR -eq 32 ]; then
  if python3 "$HERE/qa/oos_rule.py" "$UG" > "$OUT/logs/oos-rule.out" 2>&1; then
    dd_log "mk-capture: WARNING guard 32 accepted (C-00 30.09 rule) — $(cat "$OUT/logs/oos-rule.out")"; GR=0
  else
    echo "mk-capture: guard restore 32 NOT accepted — $(cat "$OUT/logs/oos-rule.out"); STOP, tell C-00" >&2
  fi
fi
[ $GR -eq 0 ] && ! grep -q 'restore scope=player .*verified=1' "$OUT/logs/guard-restore.out" && GR=31
[ $GR -eq 0 ] || { echo "mk-capture: guard restore $GR — STOP, tell C-00 ($OUT/logs/guard-restore.out)" >&2; }

# --- record ------------------------------------------------------------------------------------------------------------
PYTHONDONTWRITEBYTECODE=1 python3 "$HERE/mk-capture/mk_manifest.py" "$OUT" --app "$APP" --tier "$TIER" --formats "$FORMATS" \
  --guard "$GR" --rc "$RC" --signal "$SIG" > "$OUT/logs/manifest.out" 2>&1 || true
dd_log "mk-capture: done rc=$RC guard=$GR signal=$SIG"
[ $GR -ne 0 ] && exit $GR
[ $SIG -ne 0 ] && exit $SIG
exit $RC
