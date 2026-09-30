#!/bin/zsh
# WP-025 · tools/build.sh {dev|release} [--smoke] [--out DIR] [--no-wait] [--target mac|win64] [--compression none|lz4|lz4hc]
#
# One command, repeatable, for every session and for S-10's regression automation:
#   build.sh release --smoke      release player + main-menu/run smoke + user-data guard
#   build.sh release-probe        WP-078: release settings (LZ4HC, no Development) + DD_PERF_PROBE define so
#                                 PilotPerfProbe is compiled in; not distributed; its own gate perf mode.
#   build.sh dev                  development player (probes enabled: -ddProbe …)
#   build.sh release --target win64   WP-066W (DEC-089): Windows x64 Mono player → <out>/DeathsDemolition-win64/
#                                 DeathsDemolition.exe (+ DeathsDemolition_Data); no smoke (cannot run on macOS).
#                                 The macOS path (default --target mac) is unchanged: same command line and report.
#   --compression none|lz4|lz4hc   WP-066C/066E: player data compression (BuildOptions.CompressWithLz4/Lz4HC).
#                                 DEC-090: release defaults to lz4hc (macOS + Windows); dev stays uncompressed;
#                                 `--compression none` gives the pre-DEC-090 uncompressed release.
#
# Output: <out>/DeathsDemolition.app, <out>/build.log, <out>/build-report.json (from the
# Editor), <out>/report.json (script summary incl. smoke), <out>/app-manifest.sha256 (WP-079H) and <out>/timeline.txt.
# Default <out> = $DD_BUILD_ROOT/<mode>-<utc>. The version stamp (git describe, commit,
# branch, date, mode, Unity) is written into the player by DD.Prototype.Core.EditorTools.DdBuild.
#
# Exit codes (contract for callers):
#   0 ok · 10 build failed · 20 smoke failed · 30 user-data guard failed · 64 usage · 75 machine busy (--no-wait)
# Machine rule: a Unity slot is taken through tools/slot.sh (max 2 Unity batch on this machine) and released after the build.
set -u
setopt nullglob
source "${0:A:h}/dd-env.sh"
MODE="${1:-}"; shift 2>/dev/null || true
[ "$MODE" = "dev" ] || [ "$MODE" = "release" ] || [ "$MODE" = "release-probe" ] || { echo "usage: build.sh {dev|release|release-probe} [--smoke] [--out DIR] [--no-wait] [--target mac|win64] [--compression none|lz4|lz4hc]" >&2; exit 64; }
SMOKE=0; OUT=""; NOWAIT=0; TARGET="mac"; COMP=""
while [ $# -gt 0 ]; do
  case "$1" in
    --smoke) SMOKE=1 ;;
    --out) OUT="$2"; shift ;;
    --no-wait) NOWAIT=1 ;;
    --target) TARGET="$2"; shift ;;
    --compression) COMP="$2"; shift ;;
    *) echo "unknown option: $1" >&2; exit 64 ;;
  esac
  shift
done
[ "$TARGET" = "mac" ] || [ "$TARGET" = "win64" ] || { echo "--target must be mac or win64" >&2; exit 64; }
# DEC-090 (user 2026-09-24): release default LZ4HC; dev unchanged (no flag → uncompressed).
[ -z "$COMP" ] && { [ "$MODE" = "release" ] || [ "$MODE" = "release-probe" ]; } && COMP="lz4hc"
[ -z "$COMP" ] || [ "$COMP" = "none" ] || [ "$COMP" = "lz4" ] || [ "$COMP" = "lz4hc" ] || { echo "--compression must be none, lz4 or lz4hc" >&2; exit 64; }
[ "$TARGET" = "win64" ] && [ "$SMOKE" -eq 1 ] && { echo "--smoke is macOS-only (a Windows player cannot run here)" >&2; exit 64; }
[ -n "$OUT" ] || { [ "$TARGET" = "mac" ] && OUT="$DD_BUILD_ROOT/$MODE-$(dd_utc)" || OUT="$DD_BUILD_ROOT/$MODE-$TARGET-$(dd_utc)"; }
mkdir -p "$OUT"; DD_TIMELINE="$OUT/timeline.txt"
APP="$OUT/$DD_APP_NAME"; LOG="$OUT/build.log"; BREPORT="$OUT/build-report.json"; REPORT="$OUT/report.json"
# WP-066W: Windows output = folder with the exe and its _Data folder; APP stays the thing whose size is reported.
TARGS=(); TJSON=""; TLOG=""
if [ "$TARGET" = "win64" ]; then
  APP="$OUT/$DD_WIN_DIR"; TARGS=(-buildTarget Win64 -ddTarget win64); TJSON=$'\n  "target": "win64",'; TLOG=" target=win64"
fi
DDOUT="$APP"; [ "$TARGET" = "win64" ] && DDOUT="$APP/$DD_WIN_EXE"
# WP-066C: only an explicit --compression reaches the editor/report (default argv unchanged).
CARGS=(); [ -n "$COMP" ] && { CARGS=(-ddCompression "$COMP"); TJSON="$TJSON"$'\n  "compression": "'"$COMP"'",'; TLOG="$TLOG compression=$COMP"; }
dd_log "build.sh mode=$MODE$TLOG smoke=$SMOKE out=$OUT repo=$DD_REPO head=$(git -C "$DD_REPO" rev-parse --short=12 HEAD 2>/dev/null)"

# Machine slot: tools/slot.sh (blocks until one of the 2 Unity slots is free; --no-wait refuses instead).
if [ "$NOWAIT" -eq 1 ] && [ "$(dd_unity_count)" -ge "$DD_MAX_UNITY" ]; then dd_log "busy: $(dd_unity_count) Unity batch processes (limit $DD_MAX_UNITY)"; exit 75; fi
trap 'dd_slot_release' EXIT
dd_slot_acquire || exit $?   # D1 review MUST-2 / M-07c: slot.sh's code (75 timeout, 64 usage) now reaches the caller

T0=$(date +%s)
# WP-051D: log-stall watchdog (20 min silent → stop that pid, retry once; second stall → exit 124)
dd_unity_watched "$LOG" 0 "$DD_UNITY" -batchmode -nographics -quit -forgetProjectPath -projectPath "$DD_PROJECT" "${TARGS[@]}" \
  -executeMethod DD.Prototype.Core.EditorTools.DdBuild.Build -ddMode "$MODE" -ddOut "$DDOUT" -ddReport "$BREPORT" "${CARGS[@]}" \
  -logFile "$LOG"
X=$?; T1=$(date +%s)
[ "$DD_UNITY_OUTCOME" != ok ] && dd_log "unity outcome=$DD_UNITY_OUTCOME"
[ "$DD_UNITY_OUTCOME" = stalled ] && { dd_log "build stalled twice — exit 124"; exit 124; }
APPKB=$( [ -d "$APP" ] && du -sk "$APP" | cut -f1 || echo 0 )
ERRS=$(grep -c 'error CS' "$LOG" 2>/dev/null); ERRS=${ERRS:-0}
dd_log "build exit=$X seconds=$((T1-T0)) appKB=$APPKB errorsCS=$ERRS"
dd_slot_release   # the editor is gone; the smoke step takes its own slot
BUILD_OK=0; [ "$X" -eq 0 ] && [ -d "$APP" ] && grep -q '"result": "Succeeded"' "$BREPORT" 2>/dev/null && BUILD_OK=1
[ "$TARGET" = "win64" ] && { [ -f "$APP/$DD_WIN_EXE" ] && [ -d "$APP/${DD_WIN_EXE:r}_Data" ] || BUILD_OK=0; }
# WP-046: build footprint (categories, top assets, folder roll-up, cap check) from the Unity build log
if [ "$BUILD_OK" -eq 1 ] && [ -x "$DD_TOOLS_DIR/build-report.sh" ]; then
  "$DD_TOOLS_DIR/build-report.sh" "$OUT" --top 25 --cap-bytes "${DD_BUILD_CAP_BYTES:-5000000000}" | head -1 | while IFS= read -r l; do dd_log "$l"; done
fi

# WP-079H: content manifest of the built player (before the smoke run touches anything) — verify a copy later with
# `cd <out> && shasum -a 256 -c app-manifest.sha256` or `tools/app_manifest.py verify <out>`
APPMAN=""
if [ "$BUILD_OK" -eq 1 ]; then
  APPMAN=$(python3 "$DD_TOOLS_DIR/app_manifest.py" write "$OUT" "${APP:t}" 2>>"$OUT/app-manifest.err") \
    && dd_log "app-manifest sha256=$APPMAN files=$(wc -l < "$OUT/app-manifest.sha256" | tr -d ' ')" \
    || { APPMAN=""; dd_log "app-manifest FAILED (see $OUT/app-manifest.err)"; }
  [ -s "$OUT/app-manifest.err" ] || rm -f "$OUT/app-manifest.err"
fi

SMOKE_STATUS="skipped"; SMOKE_EXIT=0
if [ "$BUILD_OK" -eq 1 ] && [ "$SMOKE" -eq 1 ]; then
  "$DD_TOOLS_DIR/smoke.sh" "$APP" "$OUT" --mode "$MODE"; SMOKE_EXIT=$?
  case "$SMOKE_EXIT" in 0) SMOKE_STATUS="ok" ;; 20) SMOKE_STATUS="failed" ;; 30) SMOKE_STATUS="userdata-guard-failed" ;; *) SMOKE_STATUS="error($SMOKE_EXIT)" ;; esac
fi

CODE=0
[ "$BUILD_OK" -eq 1 ] || CODE=10
[ "$CODE" -eq 0 ] && [ "$SMOKE_EXIT" -ne 0 ] && CODE=$SMOKE_EXIT
STAMP=$( [ -f "$BREPORT" ] && grep -o '"describe": "[^"]*"' "$BREPORT" | head -1 | cut -d'"' -f4 || echo "" )
cat > "$REPORT" <<JSON
{
  "mode": "$MODE",$TJSON
  "ok": $([ "$CODE" -eq 0 ] && echo true || echo false),
  "exitCode": $CODE,
  "build": { "ok": $([ "$BUILD_OK" -eq 1 ] && echo true || echo false), "unityExit": $X, "seconds": $((T1-T0)), "appKB": $APPKB, "errorsCS": $ERRS, "app": "$APP", "log": "$LOG", "report": "$BREPORT", "stamp": "$STAMP" },
  "smoke": { "status": "$SMOKE_STATUS", "exitCode": $SMOKE_EXIT, "report": "$OUT/smoke-report.json" },
  "footprint": "$OUT/footprint.json",
  "appManifest": "$OUT/app-manifest.sha256",
  "appManifestSha256": $([ -n "$APPMAN" ] && echo "\"$APPMAN\"" || echo null),
  "head": "$(git -C "$DD_REPO" rev-parse HEAD 2>/dev/null)",
  "utc": "$(date -u +%FT%TZ)"
}
JSON
dd_log "build.sh done exit=$CODE report=$REPORT"
echo "exit=$CODE mode=$MODE$TLOG seconds=$((T1-T0)) appKB=$APPKB stamp=$STAMP smoke=$SMOKE_STATUS report=$REPORT"
exit $CODE
