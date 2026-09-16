#!/bin/zsh
# WP-026 · M3 kapanış regresyonu — tek komut (S-10). HIZLI HAT: tören yok; her adım bir klasöre kanıt bırakır, aggregate.py tek qa-report.json + ≤20 satır özet üretir.
#
# Kullanım: tools/qa/regression.sh <out-dir> [--rounds 8] [--seed 26] [--perf-runs 3] [--window 60] [--skip-build] [--skip-tests] [--quick]
#   --quick  : kısa doğrulama (2 round bot, 1 perf koşusu, okunabilirlik 1 karışım, EditMode simülatörü) — WP-026 §5E
#   Ortam    : DD_UNITY (Unity ikilisi), DD_PROJECT (proje kökü), DD_BUILD_SH (WP-025 tools/build.sh varsa onu kullanır), DD_PERF_CLEAN (perf-clean kalıbı: Blender < %50, 60 s),
#              DD_SLOT_SH (C-00 makine slot kilidi ~/Desktop/KADRO/context/projects/deaths-demolition/tools/slot.sh; her Unity batch/Player koşusu acquire/release ile sarılır)
# Adımlar  : 0 makine kuralı (slot.sh: ≤ 2 Unity batch) → 1 dev build (build.sh yoksa LifecycleProbeBuild.BuildDevelopmentPlayer) → 2 EditMode QA (şema, bot eşlemesi, spawn-mix dilimi)
#            → 2b spawn-mix (WP-027 SpawnMixSimulator, QaSpawnMixCli) → 3 bot koşusu (-ddQaBot) → 4 perf (PilotPerfProbe Run, temiz pencere, N×60 s)
#            → 5 okunabilirlik (-ddQaReadability, round 1/4/8 gerçek karışım) → 6 DD.Art DEC-077 zırh biçim testi (PlayMode ArmorTests.Okunabilirlik) → 7 aggregate.
#            Çıkış kodu: aggregate PASS 0 / FAIL 1 / kural ihlali 2.
set -u
OUT="${1:?out-dir}"; shift
ROUNDS=8; SEED=26; PERF_RUNS=3; WINDOW=60; SKIP_BUILD=0; SKIP_TESTS=0; QUICK=0
while [ $# -gt 0 ]; do case "$1" in
  --rounds) ROUNDS="$2"; shift 2;; --seed) SEED="$2"; shift 2;; --perf-runs) PERF_RUNS="$2"; shift 2;; --window) WINDOW="$2"; shift 2;;
  --skip-build) SKIP_BUILD=1; shift;; --skip-tests) SKIP_TESTS=1; shift;; --quick) QUICK=1; ROUNDS=2; PERF_RUNS=1; shift;;
  *) echo "bilinmeyen arg: $1"; exit 2;; esac; done
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
UNITY="${DD_UNITY:-/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity}"
PROJECT="${DD_PROJECT:-$REPO/prototype/DeathsDemolition_PlayableConcept}"
APP=""; BIN=""   # resolved after the build step (build.sh → DeathsDemolition.app; fallback → DeathsDemolition-dev.app)
find_app(){ APP=$(ls -d "$OUT"/build-dev/*.app 2>/dev/null | head -1); BIN="$APP/Contents/MacOS/Deaths Demolition - Playable Concept"; }
mkdir -p "$OUT"; T="$OUT/timeline.txt"; log(){ echo "$(date -u +%FT%TZ) $*" | tee -a "$T"; }
SLOT="${DD_SLOT_SH:-}"; [ -n "$SLOT" ] || { [ -x "$REPO/tools/slot.sh" ] && SLOT="$REPO/tools/slot.sh" || SLOT="$HOME/Desktop/KADRO/context/projects/deaths-demolition/tools/slot.sh"; }; SLOT_TAG="wp-026-qa-$$"
unity_count(){ ps -Ao comm | grep -c '/MacOS/Unity$'; }
load1(){ uptime | sed 's/.*load averages: //' | awk '{print $1}'; }
slot_acquire(){ # MAKİNE KURALI (C-00): slot.sh varsa kilit (≤ 2 Unity batch, bloklar); yoksa süreç sayımıyla bekle
  if [ -x "$SLOT" ]; then log "slot acquire: $("$SLOT" acquire unity "$SLOT_TAG")"; return 0; fi
  for w in $(seq 1 90); do n=$(unity_count); if [ "$n" -le 1 ]; then return 0; fi; [ $w -eq 1 ] && log "bekleme: $n Unity süreci (kural ≤ 2)"; sleep 20; done
  log "Unity kuyruğu açılmadı (30 dk)"; return 1; }
slot_release(){ [ -x "$SLOT" ] && "$SLOT" release unity "$SLOT_TAG" >/dev/null 2>&1; return 0; }
trap 'slot_release' EXIT
clean_window(){ # DD_PERF_CLEAN=0 ile atlanır; perf-clean kalıbı: Blender ve diğer Unity < %50 CPU, 12×5 s
  [ "${DD_PERF_CLEAN:-1}" = "0" ] && return 0
  for attempt in 1 2 3 4 5 6; do quiet=0
    for k in $(seq 1 12); do b=$(ps -Ao %cpu,comm | grep -E 'MacOS/Blender|MacOS/Unity' | grep -v "$OUT" | awk 'BEGIN{m=0}{if($1>m)m=$1}END{print m}'); if (( ${b:-0} < 50 )); then quiet=$((quiet+1)); else quiet=0; fi; sleep 5; done
    [ $quiet -ge 12 ] && return 0; log "perf penceresi kirli (deneme $attempt), 5 dk bekle"; sleep 300; done; return 1; }
PLAYER_TIMEOUT="${DD_PLAYER_TIMEOUT:-3600}"   # s; a Player that never quits (driver not armed) must not hang the chain
run_player(){ # run_player <label> <extra args…>
  local L="$1"; shift; local D="$OUT/$L"; mkdir -p "$D"; { uptime; ps -Ao pcpu,comm | awk '$1>5{print}'; } > "$D/load-before.txt"
  "$BIN" -screen-fullscreen 0 -ddProfilePath "$D/probe-profile.json" "$@" -logFile "$D/player.log" & local P=$!
  ( sleep "$PLAYER_TIMEOUT"; kill -0 $P 2>/dev/null && { echo "timeout ${PLAYER_TIMEOUT}s" > "$D/timeout.txt"; kill $P; sleep 5; kill -9 $P 2>/dev/null; } ) & local G=$!
  wait $P; local X=$?; kill $G 2>/dev/null; wait $G 2>/dev/null
  { uptime; ps -Ao pcpu,comm | awk '$1>5{print}'; } > "$D/load-after.txt"; echo "exit=$X" > "$D/exit.txt"; log "$L exit=$X $(head -1 "$D/summary.txt" 2>/dev/null)"; return $X; }

log "START out=$OUT rounds=$ROUNDS seed=$SEED perfRuns=$PERF_RUNS window=$WINDOW quick=$QUICK project=$PROJECT"
# 1 · dev build — WP-025 tools/build.sh (kendi slot kilidi + build-info damgası + build-report.json); yoksa LifecycleProbeBuild
BUILD_SH="${DD_BUILD_SH:-$REPO/tools/build.sh}"
if [ $SKIP_BUILD -eq 0 ]; then
  if [ -x "$BUILD_SH" ]; then
    "$BUILD_SH" dev --out "$OUT/build-dev" > "$OUT/build-dev.log" 2>&1; X=$?
    cp -f "$OUT/build-dev/build-report.json" "$OUT/build-info.json" 2>/dev/null || true
  else
    slot_acquire || exit 2
    "$UNITY" -batchmode -nographics -quit -forgetProjectPath -projectPath "$PROJECT" -executeMethod DD.Prototype.Core.EditorTools.LifecycleProbeBuild.BuildDevelopmentPlayer -ddOut "$OUT/build-dev/DeathsDemolition-dev.app" -logFile "$OUT/build-dev.log"; X=$?
    slot_release
  fi
  log "build-dev exit=$X errorsCS=$(grep -c 'error CS' "$OUT/build-dev.log" 2>/dev/null) via=$([ -x "$BUILD_SH" ] && echo build.sh || echo LifecycleProbeBuild)"; [ $X -eq 0 ] || exit 1
fi
find_app; [ -x "$BIN" ] || { log "dev build yok: $OUT/build-dev"; exit 1; }

# 2 · EditMode QA: rapor şeması + bot eşlemesi + spawn-mix dilimi testi
if [ $SKIP_TESTS -eq 0 ]; then
  slot_acquire || exit 2
  "$UNITY" -batchmode -nographics -forgetProjectPath -projectPath "$PROJECT" -runTests -testPlatform EditMode -testFilter 'DD.Prototype.Qa.Tests.EditMode' -testResults "$OUT/editmode-qa.xml" -logFile "$OUT/editmode-qa.log"
  X=$?; slot_release; log "editmode-qa exit=$X $(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$OUT/editmode-qa.xml" | head -1)"
fi

# 2b · spawner karışım istatistiği (WP-027 SpawnMixSimulator, dd-spawn-mix-v1) — 1000 spawn × round 1–10
slot_acquire || exit 2
"$UNITY" -batchmode -nographics -quit -forgetProjectPath -projectPath "$PROJECT" -executeMethod DD.Prototype.Qa.EditorTools.QaSpawnMixCli.Run -ddOut "$OUT/spawn-mix" -ddSeed "$SEED" -ddRounds 10 -ddSpawns 1000 -logFile "$OUT/spawn-mix.log"
X=$?; slot_release; log "spawn-mix exit=$X $(head -1 "$OUT/spawn-mix/spawn-mix.txt" 2>/dev/null)"

# 3 · bot koşusu (N round, tohumlu)
slot_acquire || exit 2; run_player bot -ddQaBot "$OUT/bot" -ddQaBotRounds "$ROUNDS" -ddQaBotSeed "$SEED"; slot_release
[ -f "$OUT/bot/bot-summary.json" ] || log "bot: bot-summary.json yok — Player dışarıdan sonlandırıldı (SIGTERM → Unity exit 0) ya da sürücü kurulmadı; player.log'a bak"

# 4 · perf: PilotPerfProbe Run sahnesi, temiz pencere, N × WINDOW s
for i in $(seq 1 $PERF_RUNS); do clean_window || log "perf-$i: temiz pencere yok, koşu yine de alınıyor (raporda işaretlenir)"   # slot alınmadan beklenir: kuyruktaki oturumları tutmaz
  slot_acquire || exit 2
  run_player "perf-$i" -ddPilotPerfProbe "$OUT/perf-$i" -ddPilotScene Run -ddPilotWindowSeconds "$WINDOW"; slot_release; done

# 5 · okunabilirlik: gerçek spawner karışımı, round 1/4/8, 3 skin
slot_acquire || exit 2; run_player readability -ddQaReadability "$OUT/readability" -ddQaReadabilityRounds "1,4,8" -ddQaCrowd 40 -ddQaSkins "0,1,2"; slot_release

# 6 · DEC-077 zırh biçim testi (DD.Art PlayMode) — quick'te atlanır
if [ $SKIP_TESTS -eq 0 ] && [ $QUICK -eq 0 ]; then
  slot_acquire || exit 2
  "$UNITY" -batchmode -forgetProjectPath -projectPath "$PROJECT" -runTests -testPlatform PlayMode -testFilter 'DD.Art.Tests.PlayMode.ArmorTests.Okunabilirlik' -testResults "$OUT/playmode-armor.xml" -logFile "$OUT/playmode-armor.log"
  X=$?; slot_release; log "playmode-armor exit=$X $(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$OUT/playmode-armor.xml" | head -1)"
fi

# 7 · aggregate
python3 "$HERE/aggregate.py" "$OUT" --window "$WINDOW" --perf-runs "$PERF_RUNS"; X=$?
log "DONE aggregate exit=$X"; cat "$OUT/qa-report.txt" 2>/dev/null; exit $X
