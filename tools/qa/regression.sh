#!/bin/zsh
# WP-026 · M3 kapanış regresyonu — tek komut (S-10). HIZLI HAT: tören yok; her adım bir klasöre kanıt bırakır, aggregate.py tek qa-report.json + ≤20 satır özet üretir.
#
# Kullanım: tools/qa/regression.sh <out-dir> [--rounds 8] [--seed 26] [--perf-runs 3] [--window 60] [--skip-build] [--skip-tests] [--quick] [--steps build,bot,…]
#   --quick  : kısa doğrulama (2 round bot, 1 perf koşusu, okunabilirlik 1 karışım, EditMode simülatörü) — WP-026 §5E
#   Ortam    : DD_UNITY (Unity ikilisi), DD_PROJECT (proje kökü), DD_BUILD_SH (WP-025 tools/build.sh varsa onu kullanır), DD_PERF_CLEAN (perf-clean kalıbı: Blender < %50, 60 s),
#              DD_SLOT_SH (C-00 makine slot kilidi ~/Dev/KADRO/context/projects/deaths-demolition/tools/slot.sh; her Unity batch/Player koşusu acquire/release ile sarılır)
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
  --steps) STEPS="$2"; shift 2;;   # WP-044: yalnız etkilenen kapılar (build,editmode,spawnmix,bot,perf,readability,playmode; aggregate hep koşar)
  *) echo "bilinmeyen arg: $1"; exit 2;; esac; done
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="${DD_REPO:-$(cd "$HERE/../.." && pwd)}"   # DD_REPO: betik anlık kopyadan koşarken (queue-start.sh)
AGG="${DD_AGGREGATE:-$HERE/aggregate.py}"
WP064_RUN="$HERE/wp064_run.py"
EXECUTION_COMMIT=$(git -C "$REPO" rev-parse HEAD)
BOT_EXTRA=(); if [ -n "${DD_BOT_EXTRA:-}" ]; then if [ -n "${ZSH_VERSION:-}" ]; then BOT_EXTRA=(${=DD_BOT_EXTRA}); else read -ra BOT_EXTRA <<< "$DD_BOT_EXTRA"; fi; fi   # zsh kelime bölmez
RUN_EXTRA=(); for arg in "${BOT_EXTRA[@]}"; do RUN_EXTRA+=("--extra-arg=$arg"); done
STEPS="${STEPS:-build,editmode,spawnmix,bot,perf,readability,playmode}"; has_step(){ case ",$STEPS," in *",$1,"*) return 0;; esac; return 1; }
UNITY="${DD_UNITY:-/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity}"
PROJECT="${DD_PROJECT:-$REPO/prototype/DeathsDemolition_PlayableConcept}"
APP=""; BIN=""   # resolved after the build step (build.sh → DeathsDemolition.app; fallback → DeathsDemolition-dev.app)
find_app(){ APP=$(ls -d "$OUT"/build-dev/*.app 2>/dev/null | head -1); BIN="$APP/Contents/MacOS/$(source "$REPO/tools/dd-env.sh" >/dev/null 2>&1; print -r -- "$DD_PRODUCT")"; }   # product name from dd-env.sh
mkdir -p "$OUT"; T="$OUT/timeline.txt"; log(){ echo "$(date -u +%FT%TZ) $*" | tee -a "$T"; }
SLOT="${DD_SLOT_SH:-}"; [ -n "$SLOT" ] || { [ -x "$REPO/tools/slot.sh" ] && SLOT="$REPO/tools/slot.sh" || SLOT="$(source "$REPO/tools/dd-env.sh" >/dev/null 2>&1; print -r -- "$DD_KPROJ")/tools/slot.sh"; }; SLOT_TAG="qa-$$"
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
  # Y2 (C-00 30.09): through tools/player-run.sh (player slot; TERM reaches the Player), UI files + telemetry in <D>; the
  # user-data guard is the caller's (rc.sh m3-regression: snapshot/restore --scope player around this script)
  "$REPO/tools/player-run.sh" "$BIN" -screen-fullscreen 0 -ddProfilePath "$D/probe-profile.json" -ddUiDataRoot "$D/ui-data" -ddTelemetryDir "$D/telemetry" "$@" -logFile "$D/player.log" & local P=$!
  ( sleep "$PLAYER_TIMEOUT"; kill -0 $P 2>/dev/null && { echo "timeout ${PLAYER_TIMEOUT}s" > "$D/timeout.txt"; kill $P; sleep 5; { pkill -9 -P $P; kill -9 $P; } 2>/dev/null; } ) & local G=$!
  wait $P; local X=$?; kill $G 2>/dev/null; wait $G 2>/dev/null
  { uptime; ps -Ao pcpu,comm | awk '$1>5{print}'; } > "$D/load-after.txt"; echo "exit=$X" > "$D/exit.txt"; log "$L exit=$X $(head -1 "$D/summary.txt" 2>/dev/null)"; return $X; }

log "START out=$OUT rounds=$ROUNDS seed=$SEED perfRuns=$PERF_RUNS window=$WINDOW quick=$QUICK project=$PROJECT"
# 1 · dev build — WP-025 tools/build.sh (kendi slot kilidi + build-info damgası + build-report.json); yoksa LifecycleProbeBuild
BUILD_SH="${DD_BUILD_SH:-$REPO/tools/build.sh}"
if [ $SKIP_BUILD -eq 0 ] && has_step build; then
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
# WP-064 v2 (S-10 finding 2): the Library state this dev build ran on (the gate compares it with the commit)
if [ -d "$OUT/build-dev" ] && [ ! -f "$OUT/build-dev/library-provenance.json" ]; then
  python3 "$HERE/library_provenance.py" "$REPO" "$OUT/build-dev/library-provenance.json" >> "$OUT/build-dev.log" 2>&1
  log "library-provenance dev exit=$?"
fi

# 2 · EditMode QA: rapor şeması + bot eşlemesi + spawn-mix dilimi testi
if [ $SKIP_TESTS -eq 0 ] && has_step editmode; then
  slot_acquire || exit 2
  "$UNITY" -batchmode -nographics -forgetProjectPath -projectPath "$PROJECT" -runTests -testPlatform EditMode -testFilter 'DD.Prototype.Qa.Tests.EditMode' -testResults "$OUT/editmode-qa.xml" -logFile "$OUT/editmode-qa.log"
  X=$?; slot_release; log "editmode-qa exit=$X $(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$OUT/editmode-qa.xml" | head -1)"
fi

# 2b · spawner karışım istatistiği (WP-027 SpawnMixSimulator, dd-spawn-mix-v1) — 1000 spawn × round 1–10
if has_step spawnmix; then
slot_acquire || exit 2
"$UNITY" -batchmode -nographics -quit -forgetProjectPath -projectPath "$PROJECT" -executeMethod DD.Prototype.Qa.EditorTools.QaSpawnMixCli.Run -ddOut "$OUT/spawn-mix" -ddSeed "$SEED" -ddRounds 10 -ddSpawns 1000 -logFile "$OUT/spawn-mix.log"
X=$?; slot_release; log "spawn-mix exit=$X $(head -1 "$OUT/spawn-mix/spawn-mix.txt" 2>/dev/null)"
fi

# 3 · bot koşusu (N round, tohumlu)
if has_step bot; then
slot_acquire || exit 2
python3 "$WP064_RUN" soak --bin "$BIN" --out "$OUT/bot" --rounds "$ROUNDS" --seed "$SEED" --commit "$EXECUTION_COMMIT" --timeout "$PLAYER_TIMEOUT" --preflight-attempts "${DD_PREFLIGHT_WINDOWS:-30}" --preflight-wait 60 "${RUN_EXTRA[@]}"; X=$?
slot_release
log "bot exit=$X $(head -1 "$OUT/bot/summary.txt" 2>/dev/null)"
# DEC-091: a real-bot run that ends without passing (Player exit 3: death / round timeout) is a report, not a stop;
# a crash, an artifact write failure (4), a runner timeout or a host discard (75 / 76) still stops the run
[ $X -eq 0 ] || [ "$(cat "$OUT/bot/exit.txt" 2>/dev/null)" = "exit=3" ] || exit 1
[ -f "$OUT/bot/bot-summary.json" ] || log "bot: bot-summary.json yok — Player dışarıdan sonlandırıldı (SIGTERM → Unity exit 0) ya da sürücü kurulmadı; player.log'a bak"
fi

# 4 · perf: PilotPerfProbe Run sahnesi, temiz pencere, N × WINDOW s
if has_step perf; then
# WP-064 v2 (S-10 finding 6): attempts are numbered on; a host-discarded attempt (75 preflight / 76 contaminated)
# stays on disk and in attempts-ledger.jsonl and the next number runs; any other failure stops the chain.
i=0; complete=0
while [ $complete -lt $PERF_RUNS ]; do
  i=$((i+1)); [ $i -le $((PERF_RUNS * 3)) ] || { log "perf: $PERF_RUNS temiz deneme $((PERF_RUNS * 3)) denemede toplanamadı"; exit 1; }
  slot_acquire || exit 2
  python3 "$WP064_RUN" perf --bin "$BIN" --out "$OUT/perf-$i" --window "$WINDOW" --commit "$EXECUTION_COMMIT" --timeout "$PLAYER_TIMEOUT" --preflight-attempts "${DD_PREFLIGHT_WINDOWS:-30}" --preflight-wait 60 --extra-arg=-ddPilotHudAB; X=$?
  slot_release
  log "perf-$i exit=$X $(head -1 "$OUT/perf-$i/summary.txt" 2>/dev/null)"
  if [ $X -eq 0 ]; then complete=$((complete+1)); elif [ $X -eq 75 ] || [ $X -eq 76 ]; then continue; else exit 1; fi
done
fi

# 5 · okunabilirlik: gerçek spawner karışımı, round 1/4/8, 3 skin
if has_step readability; then
slot_acquire || exit 2; run_player readability -ddQaReadability "$OUT/readability" -ddQaReadabilityRounds "${DD_READABILITY_ROUNDS:-1,4,8,10,12}" -ddQaCrowd 40 -ddQaSkins "0,1,2"; slot_release
fi

# 6 · DEC-077 zırh biçim testi (DD.Art PlayMode) — quick'te atlanır
if [ $SKIP_TESTS -eq 0 ] && [ $QUICK -eq 0 ] && has_step playmode; then
  slot_acquire || exit 2
  "$UNITY" -batchmode -forgetProjectPath -projectPath "$PROJECT" -runTests -testPlatform PlayMode -testFilter 'DD.Art.Tests.PlayMode.ArmorTests.Okunabilirlik' -testResults "$OUT/playmode-armor.xml" -logFile "$OUT/playmode-armor.log" & P=$!
  ( sleep "${DD_EDITOR_TIMEOUT:-1800}"; kill -0 $P 2>/dev/null && { echo "timeout" > "$OUT/playmode-armor.timeout"; log "playmode-armor: ${DD_EDITOR_TIMEOUT:-1800} s içinde bitmedi, Unity sonlandırılıyor"; kill $P; sleep 10; kill -9 $P 2>/dev/null; } ) & G=$!
  wait $P; X=$?; kill $G 2>/dev/null; wait $G 2>/dev/null; slot_release; log "playmode-armor exit=$X $(grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$OUT/playmode-armor.xml" | head -1)"
fi

# 7 · aggregate
python3 "$AGG" "$OUT" --window "$WINDOW" --perf-runs "$PERF_RUNS"; X=$?
log "DONE aggregate exit=$X"; cat "$OUT/qa-report.txt" 2>/dev/null; exit $X
