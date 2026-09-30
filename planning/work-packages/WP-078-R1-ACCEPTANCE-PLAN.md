# WP-078 — M4-R1 kabul planı · S-10 · v2 2026-09-30 (v1 2026-09-24)

Paket sahibi: S-10 · Koordinasyon: C-00 · Durum: **koşu planı; kabul koşusu P1–P13 teslim edilip tek FINAL commit belirlenince** (C-00 30.09, P14). v2 güncel main `422de54a`'ya göre yazıldı; v1'den farklar §11.
Çerçeve (karar önceliği: PROJE-SISTEM-PROMPTU §0.3): **DEC-091** (soak kararlılık MUST, gerçek bot rapor), **DEC-092** (Yüksek 60 fps), **DEC-093** (release ≤ 5 GB), **DEC-095/098** (iki katmanlı ağaç), **DEC-097/099** (arsenal + Special cephane/süre), **DEC-100** (varsayılan Sinematik 30 fps, kapı), `QUALITY-TIERS-V2.md`, WP-070 §11–§12, WP-074 B9, `tools/qa/wp078-report-contract.md` (A8 üretici). Bu belge karar/gereksinim metni değiştirmez; eşikler kaynaktan alıntıdır; karar gerektirenler §10'dadır.

## 0 · Amaç ve kapsam

M4-R1 sonunda **tek FINAL commit** üzerinde: (1) değişen süitler yeşil, (2) QaRunBot üç kamera modunda V6 dünyada koşar, (3) projectile / ragdoll / Special / ağaç regresyonları kanıtlı, (4) 3 kademe × 3 mod perf matrisi DEC-100/092 + Performans eşiğine göre ölçülmüş, (5) DEC-091 kararlılık soak'ı ±55 V6 dünyada, (6) release ≤ 5 GB ve kullanıcı verisi bayt-eşit korunmuş. Sonra release → kullanıcı oynar → R2.
Kapsam dışı: sahip birim testlerinin yeniden yazımı, görsel kalite yargısı (AD), denge sayıları (S-02/S-04), yeni ürün kuralı.

## 1 · Kabul zinciri (sıra = bağımlılık; hepsi aynı FINAL commit)

| Adım | Ne | Eşik | Araç / kanıt |
|---|---|---|---|
| A0 | Taban | FINAL tam SHA, `git status` boş, LFS 0 işaretçi, Library kökeni post-build | `tools/qa/library_provenance.py` |
| A1 | Değişen süitler | FINAL'a giren paketlerin süitleri tek koşu + `main-ci` FINAL özeti yeşil | `tools/test.sh` (guard'lı), `ci/<sha>/summary.md` |
| A2 | Bot üç modda (§2) | TD/TP/FP × 5 round, tohum 61, normal bot: 5/5, 0 ölüm, 0 istisna, zaman aşımı 0, kamera-duvar 0 kare | KADRO `wp-078/tools/player-run.sh` → `bot-summary.json`, `player.log` |
| A3 | Regresyon kapsamı (§3) | her alan: sahip test XML'i PASS + S-10 uçtan uca satırı | `A3_REQUIRED_TEST_IDS` (wp078_report) + bot log satırları |
| A4 | Perf matrisi (§4) | 9 hücre × ≥ 3 pencere, her tam pencere eşik altı | `tools/qa/wp078_perf.py` → `wp078_perf_report.py` |
| A5 | Soak (§5) | DEC-091: tohum 61 + 26 × 12/12, kararlılık kapısı | `player-run.sh … -ddQaBotStability` → `soak_gate.py --stability` |
| A6 | Build (§6) | dev + release (LZ4HC) + release-probe; release ≤ 5 GB | `tools/build.sh` |
| A7 | Kullanıcı verisi & bütünlük (§7) | her Player/PlayMode koşusu guard verified=1; `ProjectIntegrityTests` PASS | guard makbuzları, NUnit XML |
| A8 | Rapor (§9) | tüm A0–A7 aynı FINAL; eksik = BLOCKED | `tools/qa/wp078_report.py --manifest … --evidence-root … --out …` |

Kontrol noktaları: **C1** (üç mod bot) ve **C2** (V6 halkası) geçildi (kanıt `wp-078/evidence/c1d-4cab1e1a`, `r1-820a885f`). **C3** = FINAL: A0–A8 tek seferde; sonrasında giren her paket yalnız etkilediği adımı geçersiz kılar.
Süre (sessiz makinede): build'ler ~30 dk → A1 ~25 dk (main-ci) → A2 ~20 dk → A3 bayrak dumanı ~10 dk → A5 2 × ~25 dk → A4 27 pencere ≈ 70 dk + parti araları → A8. Toplam ≈ 4–5 saat; A4 yalnız sessiz pencerede.

## 2 · QaRunBot üç kamera modunda (A2)

Seçim `-ddCameraMode topdown|tp|fp`, kademe `-ddQualityTier sinematik` (DEC-100 varsayılan; açıkça verilir), dünya varsayılan (V6, bayraksız yol). Sandbox: `-ddProfilePath`, `-ddTelemetryDir`, `<out>/ui-data` (QaRunBot) — kullanıcı dosyası yazılmaz; guard `snapshot/restore --scope player`, verified=1; kabul edilen tek değişiklik Unity `Screenmanager*`/`UnitySelectMonitor` plist anahtarları (C-00 25.09).
Bot sözleşmesi (main'de): kamera-göreli sol çubuk; TP/FP sağ çubukla yaw/pitch (`LookStickToward`), ateş yalnız merkez ışını hedef göğsündeyken (`LookRayOnTarget`); yok sayılan reload 45 kararda bir yeniden basılır; halka `WorldBounds.HalfExtent` (V6 55).
Metrikler: round/ölüm/zaman aşımı; round satırları `[Camera] inside-collider … frames=0/N`, `[OutOfWorld]`, `[Projectile]`, `[Ragdoll]`, `[AiV3]`, `[SpawnV6]`, koşu satırı `[Progress]`; `stuckRecoveries` (rapor); nişan hatası (TP/FP kamera kökenli — §8 D3).
Kanıt kareleri: mod başına avlu / koridor-çatı altı / ufuk (contract `frames` anahtarları `courtyard`, `corridor`, `horizon`). **Bugün bot bu kareleri üretmiyor** (`-ddQaBotFrames` tier kareleri içindir) → §8 D3.
**Kabul:** üç modda 5/5, 0 ölüm, 0 istisna, zaman aşımı 0, kamera-duvar 0 kare, guard OK (`wp078_report` ayrıca `stuckRecoveries == 0` istiyor → §10 K6). TP/FP kararlılık (12 round) **rapor** (§10 K3).

## 3 · Regresyon kapsamı (A3)

Kural: sahip testi birincil kanıt (NUnit XML, `wp078_report.A3_REQUIRED_TEST_IDS`); S-10 yalnız uçtan uca satır ekler, süitleri bir kez koşar.

| Alan | Sahip kanıtı | S-10 uçtan uca |
|---|---|---|
| projectile (ADR-069-1) | 1/120 s sabit adım, tavan 256, 0 B/kare | `[Projectile] overflow=0`; 10 silahın ölçülen/katalog hızı ±%1 (`WeaponQaHooks.TrySetClassicWeapon` döngüsü) |
| penetration | sr_1 dört gövde, 3.'sü kasklı; WP-006 zırh | — |
| launcher rl_1 | bölge dağılımı, kurma 4,0 m, kendine hasar %45 / tavan 72 | gerçek bot raporunda kendine hasar ölümü > %5 → S-02 |
| arsenal (10 silah) | `WeaponDefinition` v3 doğrulaması (DEC-097/099) | her silah ≥ 1 round — QaRunBot'ta silah döngüsü **henüz yok** (§8 D3) |
| gel (Namekuji) | DEC-097: cephane 8/16 + tavan 45 s; DEC-099: birikinti 12 s, eşzamanlı tavan 8, Guard kayma ölümü yalnız R ≤ 10 | `[AiV3] gelSlides/gelFalls` |
| chain (Raiju) | DEC-097: cephane 3/9 + tavan 40 s; `WD_sw_arc.maxTargets = 8` (ilk hedef dahil → ≤ 7 sekme), **kademesiz** | zincir başına hedef ≤ 8 — `SpecialImpacts.HopsApplied` birikimli, zincir başına sayaç gerekir (§8 D3 / S-02 kancası) |
| ragdoll | `RagdollProxyPool` kademe tavanı, 200 döngü sızıntısız | `[Ragdoll] peak ≤ tierCap`, `totalAcquired − totalReleased − active = 0` |
| zombieAttacks | 5 stil, tier izinleri (DEC-096) | ölüm nedeni dağılımı (rapor) |
| animation | kadans bandı, 4 ölüm ailesi | `[Ragdoll] families` |
| camera (WP-070 K1–K9) | TopDown regresyonu, D-20 nişan | A2 + A4 |
| world (WP-074 B2/B9) | 18 kapı yolu | A5 (oda içi takılma > 5 s = 0) |
| aiFlags | `-ddAiV3 0/1` (**main'de var olan tek bayrak**; v1'deki `-ddRagdoll/-ddCorpseLinger/-ddChainHops/-ddGelPuddles/-ddCrowdNeighbours/-ddSpawnLos` kodda yok) | `-ddAiV3 0` ve `1` ile birer 1 round Player dumanı (§10 K4) |
| progression (DEC-095/098) | ağaç sim kabulü (S-04), `ApplyRun` sırası | `[Progress]` 5 gerçek bot koşusundan (bot koşu kapanışı main'de: `e5888d47`) |
| audio | World kapasitesi, ses CPU | perf matrisinde ses payı (rapor) |
| ui | ADR-024-2 tarama 0 | — |
| readability (DEC-077) | `ArmorTests.Okunabilirlik` | TP/FP kareleri AD'ye rapor |

## 4 · Perf ölçüm protokolü (A4)

Eşikler **veriden**: `tools/qa/perf-targets.json` — Sinematik ≤ 33,3 / p99 ≤ 40 (DEC-100, **kapı**) · Yüksek ≤ 16,6 / 20 (DEC-092, kapı) · Performans ≤ 8,0 / 10 (kapı). **Uyarı:** takılma eşiği tüm kademelerde `stallMs` 33,33; süzülmüş p99 bu karelerin dışında hesaplandığı için Sinematik'in p99 ≤ 40 sınırı hiç bağlamaz, gerçek kısıt "> 33,33 ms kare ≤ %1" olur → §10 K9. 3 kademe × 3 mod = 9 hücre, **her hücre ≥ 3 tam pencere** (27). Her tam pencere sayılır, en iyisi seçilmez; iptal yalnız host nedeniyle (`attempts-ledger.jsonl`: `preflight-failed` exit 75, ya da `failed` + `hostClean:false`).
Senaryo: PilotPerfProbe `Run`, 1920×1080, tam 40 aktif, 10 s ısınma + 60 s pencere, `-ddQualityTier`, `-ddCameraMode`, TP/FP `-ddPilotCameraSweep 1`, `-ddUiDataRoot`, `-ddTelemetryDir`; guard `--scope player` (92eb636e: 30 BLOCKED / 31 STOP yukarı taşınır).
Sessiz pencere (C-00 26.09): koşucu player slotunu ve **üç** Unity slotunu tutar; her pencereden önce load1 < 6, yabancı Unity/Player 0; ≤ 25 dk parti + 180 s ara; `wp064_run --load-max 6`. Kirli ölçüm kapı değildir.
Build: kapı sayıları **release-probe**'dan (§10 K1). Kayıt: model, macOS, güç, ısıl durum, load1.

## 5 · Soak (A5, DEC-091)

Tek FINAL commit; kararlılık **MUST**: tohum 61 + 26 × 12/12, `-ddQaBotStability`, tepeden, V6 (`WorldBounds` 55), kademe **Sinematik** (DEC-100 varsayılan; §10 K5), sıralı 12/12 ve zaman aşımı 0 (DEC-091 "takılma 0" = takılan round), Player logunda hata 0, bellek **büyümesi** (r2 tabanlı: Mono ≤ 48 MB ve ≤ 4 MB/round, toplam ayırma ≤ 256 MB, RSS ≤ 512 MB; RSS okunamazsa −1 = "yok"), guard OK, temiz host. Kapı `soak_gate.py --stability`.
Rapor (kapı değil): gerçek bot tepeden ≥ 5 + TP ≥ 2 + FP ≥ 2; TP/FP ve Sinematik-dışı kararlılık gözlemleri; `[OutOfWorld] stuckRemove` koşu başına > 2 → S-03 bileti; `stuckRecoveries` (botun kendi takılma kurtarması) sayılır, kapı değildir (§10 K6).

## 6 · Build ve boyut (A6)

dev + release (`tools/build.sh release`, LZ4HC) + release-probe (dağıtılmaz), aynı FINAL; editör hatası 0, damga temiz; release `totalSize` ≤ 5 GB (DEC-093; §10 K7). Ayak izi Δ rapor.

## 7 · Kullanıcı verisi ve bütünlük (A7)

Her Player/PlayMode koşusu `tools/userdata-guard.sh` snapshot → koşu → restore (verified=1; exit 30 bekle, 31 DUR → C-00); koşunun yarattığı dosyalar `created/`'e. `-ddCameraMode`/`-ddQualityTier` kayıtlı tercihi değiştirmez (UI dosyaları sandbox'ta). `ProjectIntegrityTests` PASS + LFS eşitliği. **C-00 "GUARD OK" demeden hiçbir A-koşusu başlamaz.**

## 8 · İş kalemleri (S-10, FINAL'dan önce)

- **D2 araç:** KADRO `r1-accept2.sh` + `player-run.sh` (Codex değişiklikleri dahil) gözden geçirilip repo `tools/qa/wp078_accept.sh`'a taşınır (tek betik, aşama seçimi, guard çıkış sınırları); `wp078_report.py` manifest'ini koşu çıktılarından üreten `tools/qa/wp078_manifest.py`; python testleri.
- **D3 kod (QaRunBot, S-10):** TP/FP nişan hatası kamera kökenli; A2 kanıt kareleri (`courtyard/corridor/horizon`, V6 sabit noktalar); SpecialWeaponProbe yaşam döngüsü DEC-097 cephane+süre. Unity doğrulaması GUARD OK sonrası.
- **D3 ek:** bot özetine `qualityTier` ve `worldHalfExtent` alanları (A5 kapısının okuduğu, bugün yok); silah döngüsü (`WeaponQaHooks.TrySetClassicWeapon`); zincir başına Raiju hedef sayacı (S-02 ile).
- **D2 ek:** koşu makbuzlarına guard alanları (`snapshotExit/restoreExit/verified/outOfScopeChanged`) — `wp064_run` bugün yalnız `hostClean` yazar.
- Bot spawn sayacı + `[Progress]` düzeltmesi main'de (`e5888d47`; 28.09 notundaki `048f411b` içeriği).

## 9 · Rapor ve sahiplik (A8)

`wp078_report.py` (5e71a508; şema `dd-wp078-acceptance-v1`): manifest + kanıt kökü → `wp078-report.json`; eksik kanıt BLOCKED, kanıtlı ihlal FAIL, FAIL→sahip (bilinmiyorsa UNKNOWN). C-00'a ≤ 10 satır + FAIL→sahip tablosu. Kanıt `KADRO/…/wp-078/evidence/<FINAL>/`.

## 10 · Kararlar (öneri; C-00 yetkisi K1–K2, K4–K9 · kullanıcı K3)

- **K1 Kapı perf'i release-probe'dan** — öneri **EVET**. Kullanıcının oynadığı build release'tir; dev build (DEVELOPMENT_BUILD) ek iz/ayırma yükü ve p99 sıçramaları taşır (WP-070 dev ölçümünde p99 kapıyı karartmıştı); release-probe = release ayarları + yalnız sonda (`DD_PERF_PROBE`), dağıtılmaz. Dev ölçüm yalnız özellik A/B deltaları için. **Çakışma:** `wp078_report` A6 probe build'i `mode == "dev"` bekliyor, `DdBuild` ise `release-probe` yazıyor → K1 kabul edilirse üretici hizalanmalı.
- **K2 Hücre başına 3 pencere** — öneri **EVET** (sözleşme tabanı 2). En iyi pencere seçilmediği için 3 pencere tek pencerelik host gürültüsünü kapıda görünür kılar; 27 pencere ≈ 70 dk sessiz pencerede yapılabilir.
- **K3 TP/FP kararlılık MUST mu (kullanıcı) — etki notu:** MUST olursa DEC-091 değişir; kabul koşusuna 2 tohum × 2 mod × 12 round (≈ 100 dk sessiz makine) eklenir ve TP/FP'ye özgü takılma/kamera bulguları R1 kapanışını durdurur. Rapor kalırsa aynı koşular yine yapılır, bulgular sahiplere bilet olur ama release'i bekletmez. S-10 önerisi: R1'de rapor, R2'de MUST'a yükseltme kullanıcıya sorulsun (TP/FP ilk turunda; bot TP/FP kısa koşuları PASS).
- **K4 AI bayrak dumanı** — A3 `aiFlags` = main'de var olan bayraklar (bugün yalnız `-ddAiV3`) × 0/1. v1'deki 6 bayrak kodda yok; `wp078_report` 7 bayrak × 0/1 = 14 koşu istediği için FINAL'da daima BLOCKED olur → üreticinin bayrak listesi koddaki bayraklarla hizalanmalı.
- **K5 Soak kademesi** — öneri **Sinematik** (DEC-100 varsayılan, kullanıcının oynadığı). `wp078_report` A5 bugün `topdown/Yuksek` bekliyor → Sinematik'e hizalanmalı; ayrıca bot özetinde `qualityTier` alanı yok (yalnız PilotPerfProbe yazar) → §8 D3 eklemeden A5 her tier'de düşer.
- **K6 `stuckRecoveries = 0` şartı (wp078_report A2 ve A5)** — öneri **rapora çevir**: DEC-091 "zaman aşımı/takılma 0" round takılmasıdır (`fatalStuckSeconds`, timeout); `stuckRecoveries` botun başarılı kendini kurtarmasıdır — 24.09 PASS kararlılık koşularında 15 (tohum 61) ve 27 (tohum 26) idi → icat kapı (H1).
- **K7 5 GB birimi** — `wp078_report` 5 000 000 000 B, `build.sh` 5120 MB; release ≈ 1,1 GB → pratik etkisi yok; birim C-00 kaydıyla sabitlenmeli.
- **K8 Host-iptal defteri** — `wp078_report` defter girdisi almıyor ve tamamlanmamış her `perf-*` denemesini FAIL sayıyor (contract "C-00 defteri" diyor, kod yok). Öneri: `wp064_run`'ın `attempts-ledger.jsonl` kayıtları (`preflight-failed` exit 75; `failed` + `hostClean:false`) host-iptal defteri olarak okunsun; aksi hâlde yüklü makinede tek iptal A4'ü kalıcı olarak FAIL bırakır.
- **K9 Kademe başına takılma eşiği** — öneri: `perf-targets.json`'da `stallMs` kademeye bağlansın (hedef kare aralığının 2 katı: Sinematik 66,7 ms, Yüksek 33,3 ms, Performans 33,3 ms mevcut tanım) ki Sinematik p99 ≤ 40 anlam kazansın. Veri değişikliği; eşik değeri C-00 kaydıyla.

## 11 · v1 → v2 farkları

Sinematik rapor → **kapı** (DEC-100) · 24 → **27** pencere, her hücre ≥ 3 · load1 ≤ 4 → **< 6** + 3 Unity + player slotu (C-00 26.09) · soak Yüksek → **Sinematik** · zincir yıldırım hop 6/8/10 → **maxTargets 8 kademesiz** · jel birikinti Pick(10/16/24) → **DEC-099: 12 s, eşzamanlı tavan 8** · Special cephane+süre (DEC-097) eklendi · AI bayrakları koddakine indirildi (K4) · A8 = `wp078_report.py` (üreticiyle çakışmalar K1, K4–K6, K8) · Sinematik p99 anlamı (K9) · C1/C2 geçti · v1 §8 kancaları main'de (S-01/02/03/04/05/06).
