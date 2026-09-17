# tools/qa — M3 kapanış regresyonu (WP-026)

Tek komut: `tools/qa/regression.sh <out-dir> [--rounds 8] [--seed 26] [--perf-runs 3] [--window 60] [--quick] [--skip-build] [--skip-tests]`

Adımlar (her biri `<out-dir>` altına kanıt bırakır; `timeline.txt` sırayı tutar):

| # | Adım | Araç | Kanıt |
|---|---|---|---|
| 0 | Makine kuralı | C-00 `tools/slot.sh acquire/release unity` her Unity batch ve Player koşusunu sarar (≤ 2 Unity; `DD_SLOT_SH` ile yol, yoksa süreç sayımı); perf için `perf-clean` kalıbı (Blender/diğer Unity < %50, 12×5 s) | `timeline.txt` |
| 1 | Dev build | WP-025 `tools/build.sh dev <app>` varsa o; yoksa `LifecycleProbeBuild.BuildDevelopmentPlayer` | `build-dev/`, `build-dev.log`, `build-info.json` (varsa) |
| 2 | EditMode QA | `-testFilter DD.Prototype.Qa.Tests.EditMode` — rapor şeması, bot eşlemesi, sanal gamepad, spawn-mix dilimi testi | `editmode-qa.xml` |
| 2b | Spawner karışımı | `-executeMethod DD.Prototype.Qa.EditorTools.QaSpawnMixCli.Run` → WP-027 `SpawnMixSimulator.Run(Options)` (dd-spawn-mix-v1): 1000 spawn × round 1–10, DEC-022 tanıtım garantisi, README tavanları serbest çekilişte (cw2 ≤ %30 / T7 ≤ %10 / H0 ≤ %25), yasak çift/zırh dışlama 0; zırh kısıtıyla yükselen paylar `notes` | `spawn-mix/spawn-mix.json`, `spawn-mix.txt`, `qa-report.json` |
| 3 | Bot koşusu | dev Player `-ddQaBot <dir> -ddQaBotRounds N -ddQaBotSeed S` — sanal gamepad (Input System), N round, tohumlu | `bot/bot-summary.json`, `events.txt`, `qa-report.json` |
| 4 | Perf kapısı | `PilotPerfProbe` Run sahnesi, temiz pencere, N × 60 s | `perf-N/04-window.json`, `frame-times-ms.csv` |
| 5 | Okunabilirlik | dev Player `-ddQaReadability <dir> -ddQaReadabilityRounds 1,4,8 -ddQaCrowd 40 -ddQaSkins 0,1,2` — gerçek spawner karışımı, kapsül örtülme + ΔV (DEC-077) | `readability/readability-rNN.json`, PNG'ler, `qa-report.json` |
| 6 | Zırh biçim testi | DD.Art PlayMode `ArmorTests.Okunabilirlik` (≥ %30 + bant 0,08/0,12) — `--quick`'te atlanır | `playmode-armor.xml` |
| 7 | Toplama | `aggregate.py` → `qa-report.json` (şema `dd-qa-report-v1`) + `qa-report.txt` (≤ 20 satır) | çıkış kodu 0 PASS / 1 FAIL / 2 kural |

Kapı değerleri araç içinde değiştirilmez: perf ort. ≤ 8,0 ms ve temiz p99 ≤ 10 ms (ADR-011-3 v3 / DEC-088, kullanıcı 2026-09-17; 7,0 look pass öncesiydi), okunabilirlik `ReadabilityThresholds` (DEC-077), bot = round ≥ 4 tamamlandı + öncesinde ölüm/zaman aşımı yok (C-00 kararı 2026-09-17; istenen round sayısı raporlanır), spawner = WP-027 simülatörü `violations` = 0.

## Rapor şeması `dd-qa-report-v1`

```json
{ "schema": "dd-qa-report-v1", "package": "WP-026", "driver": "aggregate|QaRunBot|QaReadabilityGate", "utc": "…",
  "buildInfo": "WP-025 build-info.json özeti ya da n/a", "machine": "…", "window": "60 s x 3 runs, …", "load1Before": "…", "load1After": "…",
  "seed": 26, "pass": true, "summary": "…",
  "gates": [ { "id": "perf.meanMs", "pass": true, "value": "6.32", "threshold": "<= 7.0 ms (ADR-011-3 v2)", "note": "", "source": "perf-1/04-window.json" } ] }
```

## Bot (`QaRunBot`)

`Assets/_Prototype/Diagnostics/Qa/QaRunBot.cs` (yalnız `DEVELOPMENT_BUILD || UNITY_EDITOR`, asm `DD.Prototype.Qa`). Girdi yalnız Input System'in sanal `Gamepad` cihazı (`QaVirtualGamepad`: leftStick = Move, rightStick = AimStick, rightTrigger = Fire, buttonWest = Reload) — oyun kodu ve `IInputSource` değişmez. Karar katmanı `QaBotBrain` (saf, tohumlu): en yakın zombiye nişan, menzilde + namlu hattı açıkken sürekli ateş (namlunun 2 m önündeki çevre çarpışıcısı her atışı yer → ateş yok, engelden geri çekil), kalabalık merkezine göre 8–14 m kite, hasar sonrası 1 s geri çekilme, uzaktaki hedefe NavMesh ile avlanma, spawn noktaları arasında devriye, takılma kurtarması. Sürücü her hareket komutunu NavMesh üzerinde yönlendirir (`NavMesh.Raycast` 1,6 m sonda, ilk açık dönüş kazanır) — duvara/saksıya sürtünme yerine kayar; 2,5 s kapalı hat sürerse arena merkezine yürür. `events.txt` 1 Hz iz satırı taşır (`t pos= mode= tgt= los= mz= eye= blk= fire= hp= near= kills=`); Player dışarıdan sonlandırılırsa (`Application.quitting`) kısmi özet yine yazılır. Upgrade ekranı `RunDirector.CloseUpgradeScreen()` (Skip düğmesinin çağrısı) ile atlanır. Çıktı: round kayıtları, kill/hasar sayıları, `spawnSequenceHash` (spawn tier dizisi FNV) ve `outcomeHash`; aynı seed ile spawn dizisi tekrarlanır, kare zamanı tohumlanmadığı için kill zaman çizgisi tekrarlanmayabilir (raporlanır, durma koşulu değil).

## Yalnız etkilenen kapılar (WP-044)

`--steps build,bot` gibi bir liste yalnız o adımları koşar (`build,editmode,spawnmix,bot,perf,readability,playmode`; aggregate her zaman koşar, koşulmayan adımlar raporda `missing`). Kapanış düzeltme turunda perf değiştiyse `--steps build,perf`, görsel değiştiyse `--steps build,readability,playmode`.

## Makine kuralı

≤ 2 Unity batch süreci (C-00 `slot.sh`); perf ölçümü yalnız temiz pencerede (Blender ve diğer Unity < %50 CPU); gece koşusu tercih edilir. Kapanış koşusu C-00 tetikler (tüm M3 paketleri merge olunca).

## Kapanış listesi ekleri (C-00, 2026-09-17)

Tam koşuda (`--quick` değil) aggregate'e elle eklenecek/ayrıca bakılacak maddeler: (1) `RegionalArmorSliceTests.VurusYolu_SifirAyirma` — WP-034 r2 / WP-036 üzerinde 12/16 KB aralıklı ayırma; kapanış koşusunda 3 tekrar, sonucu `qa-report.txt` altına not; (2) DEC-077 motor içi ölçüm (WP-036 devri) — `QaReadabilityGate` gerçek karışım değerleri bu ölçümün yerini tutar, WP-036'nın kendi ölçümüyle yan yana raporlanır; (3) release build 453 MB — tavan (450) kullanıcı kararı bekliyor, kapı değil, `build.sh release` boyutu rapora yazılır.
