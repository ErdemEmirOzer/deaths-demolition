# WP-078 — M4-R1 kabul planı · S-10 · v2.1 2026-09-30 (v1 2026-09-24; v2.1 = C-00 K1–K9 kararları)

Paket sahibi: S-10 · Koordinasyon: C-00 · Durum: **koşu planı; kabul koşusu P1–P13 teslim edilip tek FINAL commit belirlenince** (C-00 30.09, P14). v2 main `422de54a`'ya göre yazıldı, v2.2 koşu adımları main `68317433` + dal `wp-078-accept-plan-r3` araçlarına göre; v1'den farklar §11.
Çerçeve (karar önceliği: PROJE-SISTEM-PROMPTU §0.3): **DEC-091** (soak kararlılık MUST, gerçek bot rapor), **DEC-092** (Yüksek 60 fps), **DEC-093** (release ≤ 5 GB), **DEC-095/098** (iki katmanlı ağaç), **DEC-097/099** (arsenal + Special cephane/süre), **DEC-100** (varsayılan Sinematik 30 fps, kapı), `QUALITY-TIERS-V2.md`, WP-070 §11–§12, WP-074 B9, `tools/qa/wp078-report-contract.md` (A8 üretici). Bu belge karar/gereksinim metni değiştirmez; eşikler kaynaktan alıntıdır; karar gerektirenler §10'dadır.

## 0 · Amaç ve kapsam

M4-R1 sonunda **tek FINAL commit** üzerinde: (1) değişen süitler yeşil, (2) QaRunBot üç kamera modunda V6 dünyada koşar, (3) projectile / ragdoll / Special / ağaç regresyonları kanıtlı, (4) 3 kademe × 3 mod perf matrisi DEC-100/092 + Performans eşiğine göre ölçülmüş, (5) DEC-091 kararlılık soak'ı ±55 V6 dünyada, (6) release ≤ 5 GB ve kullanıcı verisi bayt-eşit korunmuş. Sonra release → kullanıcı oynar → R2.
Kapsam dışı: sahip birim testlerinin yeniden yazımı, görsel kalite yargısı (AD), denge sayıları (S-02/S-04), yeni ürün kuralı.

## 1 · Kabul zinciri (sıra = bağımlılık; hepsi aynı FINAL commit)

| Adım | Ne | Eşik | Araç / kanıt |
|---|---|---|---|
| A0 | Taban | FINAL tam SHA, `git status` boş, LFS 0 işaretçi, Library kökeni post-build | `tools/qa/library_provenance.py` |
| A1 | Değişen süitler | FINAL'a giren paketlerin süitleri tek koşu + `main-ci` FINAL özeti yeşil | `tools/test.sh` (guard'lı), `ci/<sha>/summary.md` |
| A2 | Bot üç modda (§2) | TD/TP/FP × 5 round, tohum 61, normal bot: 5/5, 0 ölüm, 0 istisna, zaman aşımı 0, kamera-duvar 0 kare; + rota koşusu × 3 mod (kareler) | `tools/qa/wp078_bot.py --kind gameplay` / `--kind route` → `attempt.json` (guard makbuzu + artifactSha256), `bot-summary.json`, `player.log` |
| A3 | Regresyon kapsamı (§3) | her alan: sahip test XML'i PASS + S-10 uçtan uca satırı | `A3_REQUIRED_TEST_IDS` (wp078_report) + bot log satırları |
| A4 | Perf matrisi (§4) | 9 hücre × ≥ 3 pencere, her tam pencere eşik altı | `tools/qa/wp078_perf.py` → `wp078_perf_report.py` |
| A5 | Soak (§5) | DEC-091: tohum 61 + 26 × 12/12, kararlılık kapısı | `tools/qa/wp078_bot.py --kind stability --quiet` (temiz-host preflight; yalnız player slotu) → `soak_gate.py --stability` |
| A6 | Build (§6) | dev + release (LZ4HC) + release-probe; release ≤ 5 GB | `tools/build.sh` |
| A7 | Kullanıcı verisi & bütünlük (§7) | her Player/PlayMode koşusu guard verified=1; `ProjectIntegrityTests` PASS | guard makbuzları, NUnit XML |
| A8 | Rapor (§9) | tüm A0–A7 aynı FINAL; eksik = BLOCKED | `tools/qa/wp078_accept.py --final <SHA>` (A0–A6 aşamaları + manifest) → `tools/qa/wp078_report.py` |

Kontrol noktaları: **C1** (üç mod bot) ve **C2** (V6 halkası) geçildi (kanıt `wp-078/evidence/c1d-4cab1e1a`, `r1-820a885f`). **C3** = FINAL: A0–A8 tek seferde; sonrasında giren her paket yalnız etkilediği adımı geçersiz kılar.
Süre (sessiz makinede): build'ler ~30 dk → A1 ~25 dk (main-ci) → A2 ~20 dk → A3 bayrak dumanı ~10 dk → A5 2 × ~25 dk → A4 27 pencere ≈ 70 dk + parti araları → A8. Toplam ≈ 4–5 saat; A4 yalnız sessiz pencerede.

## 2 · QaRunBot üç kamera modunda (A2)

Seçim `-ddCameraMode topdown|tp|fp`, kademe `-ddQualityTier sinematik` (DEC-100 varsayılan; açıkça verilir), dünya varsayılan (V6, bayraksız yol). Sandbox: `-ddProfilePath`, `-ddTelemetryDir`, `<out>/ui-data` (QaRunBot) — kullanıcı dosyası yazılmaz; guard `snapshot/restore --scope player`, verified=1; kabul edilen tek değişiklik Unity `Screenmanager*`/`UnitySelectMonitor` plist anahtarları (C-00 25.09).
Bot sözleşmesi (main'de): kamera-göreli sol çubuk; TP/FP sağ çubukla yaw/pitch (`LookStickToward`), ateş yalnız merkez ışını hedef göğsündeyken (`LookRayOnTarget`); yok sayılan reload 45 kararda bir yeniden basılır; halka `WorldBounds.HalfExtent` (V6 55).
Metrikler: round/ölüm/zaman aşımı; round satırları `[Camera] inside-collider … frames=0/N`, `[OutOfWorld]`, `[Projectile]`, `[Ragdoll]`, `[AiV3]`, `[SpawnV6]`, koşu satırı `[Progress]`; `stuckRecoveries` (rapor); nişan hatası (TP/FP kamera kökenli — §8 D3).
Kanıt kareleri (C-00 30.09 R1–R3): A2 oyun koşusundan değil, mod başına ayrı **rota koşusundan** (`wp078_bot.py --kind route` → QaRunBot `-ddQaBotRouteTour`, yalnız dev; tur boyunca spawn tutulur): `courtyard` (0,−3) 0°, `horizon` (0,−3) 180° (tepeden N/A "topdown-fixed-camera"; E-3 FP), `corridor` R6 chūmon (0,19), `kairo` K1 merkez çizgisi (−33,15) 180°; çatı kanıtı yukarı RaycastAll `Kind==Roof`; her nokta NavMesh'e ≤ 2 m oturtulur; draw call/batches (E-14) kayıtta.
**Kabul:** üç modda 5/5, 0 ölüm, 0 istisna, zaman aşımı 0, kamera-duvar 0 kare, guard OK (`wp078_report` ayrıca `stuckRecoveries == 0` istiyor → §10 K6). TP/FP kararlılık (2 tohum × 12 round) **rapor** — kullanıcı kararı 30.09 (§10 K3).

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
| world (WP-074 B2/B9, WP-070 K2) | 18 kapı yolu | **paket kapısı (C-00 30.09 K6):** A2 + A5 player.log'larında aynı noktada ≥ 5 s takılma kümesi 0 (`tools/qa/stuck_clusters.py`; `time=` alanı olmayan log → "ölçülmedi", PASS değil) |
| aiFlags | `-ddAiV3 0/1` + `-ddRagdoll 0/1` (K4: kodda okunan iki bayrak, 4 koşu; kod↔liste testi) · diğer 5 bayrak: `-ddChainHops`/`-ddGelPuddles` DEC-097/099 ile emekli; `-ddCorpseLinger/-ddCrowdNeighbours/-ddSpawnLos` **yazılı geri çekildi** (S-03, C-00 30.09; komşu tavanı + spawn LOS kademe satırları KNOWN OPEN, R2, perf'e bağlı — WP-076 DIRECTION §7.1 c3c71eb4); `-ddRagdoll 0|1` S-06 R2 ile main'de (RagdollProxyPool; 1 = varsayılan ragdoll, 0 = her ölüm kendi klibi) → A3 flagRuns'ta | `-ddAiV3` ve `-ddRagdoll` × `0`/`1` ile birer 1 round Player dumanı |
| progression (DEC-095/098) | ağaç sim kabulü (S-04), `ApplyRun` sırası | `[Progress]` 5 gerçek bot koşusundan (bot koşu kapanışı main'de: `e5888d47`) |
| audio | World kapasitesi, ses CPU | perf matrisinde ses payı (rapor) |
| ui | ADR-024-2 tarama 0 | — |
| readability (DEC-077) | `ArmorTests.Okunabilirlik` | TP/FP kareleri AD'ye rapor |

## 4 · Perf ölçüm protokolü (A4)

Eşikler **veriden**: `tools/qa/perf-targets.json` — Sinematik ≤ 33,3 / p99 ≤ 40 (DEC-100, **kapı**) · Yüksek ≤ 16,6 / 20 (DEC-092, kapı) · Performans ≤ 8,0 / 10 (kapı). Takılma eşiği kademe başına (K9): `tiers.<kademe>.stallMs` Sinematik 40, Yüksek/Performans 33,33; takılma sayısı ve süzülmüş p99 hash bağlı `frame-times-ms.csv`'den bu eşikle yeniden hesaplanır (probe'un yerel 33,33 alanları çapraz kontrol). Her pencere ham p99, p99,9, maks ve > 33,33 ms oranını raporlar; ham p99 DEC sınırını aşıp süzülmüş p99 geçiyorsa (`rawP99OverLimit`) C-00 🔴 olarak kullanıcıya taşır. 3 kademe × 3 mod = 9 hücre, **her hücre ≥ 3 tam pencere** (27). Her tam pencere sayılır, en iyisi seçilmez; iptal yalnız host nedeniyle (K8): (a) `preflight-failed` ∧ exit 75, (b) `failed` ∧ hostClean false ∧ timedOut false ∧ exit 0 ∧ missingArtifacts []; diğer her tamamlanmamış deneme FAIL. FINAL başına tek kök + tek defter; iptal edilen pencere aynı kökte sıradaki boş indeksle yeniden koşulur, hücre ≥ 3 sayılan pencereye ulaşana dek (`wp078_perf.py --max-discards`). İptaller hücre başına raporlanır.
Senaryo: PilotPerfProbe `Run`, 1920×1080, tam 40 aktif, 10 s ısınma + 60 s pencere, `-ddQualityTier`, `-ddCameraMode`, TP/FP `-ddPilotCameraSweep 1`, `-ddUiDataRoot`, `-ddTelemetryDir`; guard `--scope player` (92eb636e: 30 BLOCKED / 31 STOP yukarı taşınır).
Sessiz pencere (C-00 26.09): koşucu player slotunu ve **üç** Unity slotunu tutar; her pencereden önce load1 < 6, yabancı Unity/Player 0; ≤ 25 dk parti + 180 s ara; `wp064_run --load-max 6`. Kirli ölçüm kapı değildir.
Build: **bot koşuları dev'de, perf kapısı release-probe'da; dev ölçümü yalnız A/B deltasıdır ve probe FAIL'ini örtmez** (K1). A4 `buildRole == probe`, her pencerenin binarySha256'sı probe build'ine eşit; A6 release-probe paritesi (development=false, LZ4HC, sahne listesi ve scripting backend release ile eşit). DEC-088/WP-044 tabanıyla karşılaştırmalar build türüyle yazılır. Kayıt: model, macOS, güç, ısıl durum, load1.
B9 pozları (C-00 01.10): S-05'in B9 perf matrisi ayrı pencere açmaz; **A4'ten hemen sonra aynı sessiz pencere koşusunda**, aynı release-probe build'iyle koşar (önce A4). Poz listesi S-05'ten JSON: `{"poses": [{name, start: "x,z,yaw", tiers, modes}]}` → `wp078_accept.py --b9-poses <dosya>`; her poz `wp078_perf --extra-arg=-ddPilotStart --extra-arg=<x,z,yaw>` ile kendi kökünde (`<E>/b9/<name>`), kayıt `stages/b9.json` (RECORDED). B9 kabul kapısı değildir; A4 hükmünü ve zincir çıkışını etkilemez. Poz dosyası (S-05 01.10): `KADRO/…/wp-074/b9-poses.json` (sha256 2d289851…; kopyası `wp-078/evidence/b9-poses/`), 6 poz: Yüksek SW (−48,−48,45) / NE (48,48,225) / plaza-N (0,−4,0) × topdown,tp,fp + Sinematik aynı üç poz × tp → 12 hücre × 3 pencere = 36 pencere ≈ 85–90 dk; A4 ile birlikte sessiz pencere ≈ 2,5–3 sa. S-07 (C-00 01.10): launcher kareleri + tier-ms 10 koşu (`wp073-quiet.sh`) aynı FINAL sessiz penceresinde, **B9'dan hemen sonra** (sıra A4 → B9 → S-07); S-07 kendi guard/player-run betiklerini kullanır, bu yüzden S-10 koşucusu slotları bıraktıktan sonra başlar (Unity/Player tutarken beklenmez); S-07 kendisi koşar: S-10 B9 bitince S-07'ye **"S-07 başla"** yazar (`NOWAIT=1 zsh …/wp-019r2/tools/wp073-quiet.sh`, ~25 dk: launcher 1 koşu + tier-ms 10 koşu; build S-07'nin `K/wp-073/build-dev-r4` @ 220eface = main'deki r4 içeriği), S-07 bitince haber verir; S-07 ölçümü WP-073 kapısıdır, WP-078 kabul kanıtı değildir. Pencere ≈ 3–3,5 sa. Diğer oturumlar pencere boyunca Unity/Player açmaz.

### 4.1 · FINAL sessiz pencere — tek sıra (C-00 01.10)

Pencere, FINAL zincirinde A5 bittikten sonra açılır; C-00 açılışı ve kapanışı tüm oturumlara duyurur. Pencere boyunca başka oturum Unity veya Player açmaz. Tek sahip S-10, S-07 yalnız kendi adımında.

| # | Adım | Sahip | Komut / kaynak | Süre | Başlama koşulu | Bitti sayılır |
|---|---|---|---|---|---|---|
| 0 | Açılış | C-00 → herkes | "sessiz pencere açık" duyurusu | — | A5 bitti; disk ≥ 15 GiB; FINAL probe build'i hazır (A6) | duyuru gitti |
| 1 | A4 perf matrisi | S-10 | `wp078_accept.py --final <SHA> --stages a4 --b9-poses KADRO/…/wp-074/b9-poses.json --resume` → `wp078_perf` (player slotu + 3 Unity tutucu, load1 < 6, ≤ 25 dk parti + 180 s ara) | 27 pencere ≈ 70 dk | 0 | `stages/a4.json` + `a4/perf/wp078-perf-report.json` |
| 2 | B9 pozları | S-10 (S-05 pozları) | aynı çağrı, A4'ün hemen ardından: 6 poz × `wp078_perf --extra-arg=-ddPilotStart …`, kök `<E>/b9/<name>` | 36 pencere ≈ 85–90 dk | 1 bitti (aynı süreç) | `stages/b9.json` (RECORDED) |
| 3 | Slot bırakma | S-10 | koşucu çıkınca player + Unity slotları serbest; S-10 `slot.sh status` ile doğrular | < 1 dk | 2 bitti | player 0/1, Unity tutucu yok |
| 4 | S-07 adımı | S-07 | S-10 → S-07: **"S-07 başla"**; S-07 `NOWAIT=1 zsh …/wp-019r2/tools/wp073-quiet.sh` (launcher 1 + tier-ms 10, kendi guard/player-run, r4 dev build) | ≈ 25 dk | 3 | S-07 → S-10 "bitti" |
| 5 | Kapanış | S-10 → C-00 | "sessiz pencere kapandı" + A4/B9 özet satırı; sonra A8 raporu (sessizlik gerekmez) | — | 4 | C-00 duyurusu |

Kurallar: kilit sırası player → unity (koşucu önce player slotunu alır). Bir adımda guard STOP (7) olursa pencere durur ve C-00'a bildirilir. Bekleme kodları (6/30/75) aynı kökte kaldığı yerden sürer. S-07 adımı S-10'un slotları bırakmasını bekler; S-10 slot tutarken S-07 beklemez. Toplam ≈ 3–3,5 sa. B9 ve S-07 ölçümleri kabul kapısı değildir, yalnız A4 kapıdır.

## 5 · Soak (A5, DEC-091)

Tek FINAL commit; kararlılık **MUST**: tohum 61 + 26 × 12/12, `-ddQaBotStability`, tepeden, V6 (`WorldBounds` 55), kademe **Sinematik** (DEC-100 varsayılan; §10 K5), sıralı 12/12 ve zaman aşımı 0 (DEC-091 "takılma 0" = takılan round), Player logunda hata 0, bellek **büyümesi** (r2 tabanlı: Mono ≤ 48 MB ve ≤ 4 MB/round, toplam ayırma ≤ 256 MB, RSS ≤ 512 MB; RSS okunamazsa −1 = "yok"), guard OK, temiz host. Kapı `soak_gate.py --stability`.
Kademe ve dünya bot özetinden (`qualityTier == Sinematik`, `worldHalfExtent == 55`; alan yoksa BLOCKED), soak komutu `-ddQualityTier sinematik`'i açıkça verir (K5); bellek ve log eşikleri koşudan önce donmuştur. 30 fps'te round süresinin 420 s zaman aşımına kalan payı raporlanır (K5): her tohum için en yavaş round süresi ve `420 − süre` (s, oran) `wp078_report` → `soakRoundTimeoutMargin`; rapor satırıdır, eşik (`QaRunBot.DefaultRoundTimeout = 420`) değişmez.
Rapor (kapı değil): gerçek bot tepeden ≥ 5 + TP ≥ 2 + FP ≥ 2; TP/FP ve Sinematik-dışı (Yüksek/Performans) kararlılık gözlemleri (K3 kullanıcı kararı: R1'de rapor); `[OutOfWorld] stuckRemove` koşu başına > 2 → S-03 bileti; `stuckRecoveries` + `unstickSweeps` (botun kendi kurtarmaları) raporlanır, kapı değildir (K6).

## 6 · Build ve boyut (A6)

dev + release (`tools/build.sh release`, LZ4HC) + release-probe (dağıtılmaz), aynı FINAL; editör hatası 0, damga temiz; release `totalSize` ≤ 5 GB (DEC-093; §10 K7). Ayak izi Δ rapor.

## 7 · Kullanıcı verisi ve bütünlük (A7)

Her Player/PlayMode koşusu `tools/userdata-guard.sh` snapshot → koşu → restore (verified=1; exit 30 bekle, 31 DUR → C-00); koşunun yarattığı dosyalar `created/`'e. `-ddCameraMode`/`-ddQualityTier` kayıtlı tercihi değiştirmez (UI dosyaları sandbox'ta). `ProjectIntegrityTests` PASS + LFS eşitliği. **C-00 "GUARD OK" demeden hiçbir A-koşusu başlamaz.**

## 8 · İş kalemleri (S-10, FINAL'dan önce) — durum 30.09

- **Tamam (dal `wp-078-accept-plan-r3`):** bot özetinde `qualityTier`/`worldHalfExtent`, TP/FP nişan hatası kamera kökenli, `-ddQaBotWeaponCycle`, zamanlı takılma satırları + `stuck_clusters.py`, rota koşusu (`-ddQaBotRouteTour`; kairo noktası düzeltildi), SpecialWeaponProbe DEC-097, `wp078_bot.py`, tek guard modülü `guard_receipt.py` (her yolda restore, SIGTERM 143, ana repo guard'ı), `wp078_perf` kademe/iptal/yeniden koşu + pencere guard makbuzu, `wp078_report` K1–K9, `wp078_accept.py` (zincir + manifest).
- **Açık:** A6 parite alanları (`development`, sahne listesi, scripting backend) DdBuild native raporunda yok → S-01; yoksa A6 BLOCKED. Rota kairo noktası Player'da yeniden doğrulanacak (m31 smoke: diğer 3 nokta 3 modda captured). `-ddRagdoll` main'de → AI_FLAGS'ta (A3 4 koşu). K5 "420 s zaman aşımı payı" rapor satırı.
- **Koşu:** FINAL belirlenince `python3 tools/qa/wp078_accept.py --final <SHA>` (varsayılan kanıt kökü `KADRO/…/wp-078/evidence/<SHA8>`); guard her zaman ana repo yolundan; Player koşuları main-ci sırasında beklenir; A4 yalnız sessiz pencerede.

## 9 · Rapor ve sahiplik (A8)

`wp078_report.py` (5e71a508; şema `dd-wp078-acceptance-v1`): manifest + kanıt kökü → `wp078-report.json`; eksik kanıt BLOCKED, kanıtlı ihlal FAIL, FAIL→sahip (bilinmiyorsa UNKNOWN). C-00'a ≤ 10 satır + FAIL→sahip tablosu. Kanıt `KADRO/…/wp-078/evidence/<FINAL>/`.

## 10 · Kararlar (C-00 30.09 kaydı `K/c-00/WP-078-K-KARARLARI-2026-09-30.md`: K1, K2, K4–K6, K8, K9 kabul (değişiklikle); K7 → S-01 (5 GB = 5 000 000 000 B); K3 kullanıcı kararı: R1'de rapor)

- **K1 Kapı perf'i release-probe'dan** — öneri **EVET**. Kullanıcının oynadığı build release'tir; dev build (DEVELOPMENT_BUILD) ek iz/ayırma yükü ve p99 sıçramaları taşır (WP-070 dev ölçümünde p99 kapıyı karartmıştı); release-probe = release ayarları + yalnız sonda (`DD_PERF_PROBE`), dağıtılmaz. Dev ölçüm yalnız özellik A/B deltaları için. **Çakışma:** `wp078_report` A6 probe build'i `mode == "dev"` bekliyor, `DdBuild` ise `release-probe` yazıyor → K1 kabul edilirse üretici hizalanmalı.
- **K2 Hücre başına 3 pencere** — **onaylandı**; `perf-targets minWindows 3` + contract `≥ 3` + PROJE-SISTEM-PROMPTU §15 ile zaten yürürlükte. En iyi pencere seçilmediği için 3 pencere tek pencerelik host gürültüsünü kapıda görünür kılar; 27 pencere ≈ 70 dk sessiz pencerede yapılabilir.
- **K3 TP/FP kararlılık — KULLANICI KARARI (30.09, C-00 aktarımı): R1'de RAPOR.** TP ve FP kararlılık koşuları (2 tohum × 12 round) FINAL'da yine yapılır; çökme, zaman aşımı ya da log hatası release'ten önce C-00 satırıyla kullanıcıya adıyla gösterilir ve sahibine iş açılır, release'i bekletmez. R2'de "MUST olsun mu" yeniden sorulur. DEC-091 değişmez.
- **K4 AI bayrak dumanı** — A3 `aiFlags` = main'de var olan bayraklar (bugün yalnız `-ddAiV3`) × 0/1. v1'deki 6 bayrak kodda yok; `wp078_report` 7 bayrak × 0/1 = 14 koşu istediği için FINAL'da daima BLOCKED olur → üreticinin bayrak listesi koddaki bayraklarla hizalanmalı.
- **K5 Soak kademesi** — öneri **Sinematik** (DEC-100 varsayılan, kullanıcının oynadığı). `wp078_report` A5 bugün `topdown/Yuksek` bekliyor → Sinematik'e hizalanmalı; ayrıca bot özetinde `qualityTier` alanı yok (yalnız PilotPerfProbe yazar) → §8 D3 eklemeden A5 her tier'de düşer.
- **K6 `stuckRecoveries = 0` şartı (wp078_report A2 ve A5)** — öneri **rapora çevir**: DEC-091 "zaman aşımı/takılma 0" round takılmasıdır (`fatalStuckSeconds`, timeout); `stuckRecoveries` botun başarılı kendini kurtarmasıdır — 24.09 PASS kararlılık koşularında 15 (tohum 61) ve 27 (tohum 26) idi → icat kapı (H1).
- **K7 5 GB birimi** — `wp078_report` 5 000 000 000 B, `build.sh` 5120 MB; release ≈ 1,1 GB → pratik etkisi yok; birim C-00 kaydıyla sabitlenmeli.
- **K8 Host-iptal defteri** — `wp078_report` defter girdisi almıyor ve tamamlanmamış her `perf-*` denemesini FAIL sayıyor (contract "C-00 defteri" diyor, kod yok). Öneri: `wp064_run`'ın `attempts-ledger.jsonl` kayıtları (`preflight-failed` exit 75; `failed` + `hostClean:false`) host-iptal defteri olarak okunsun; aksi hâlde yüklü makinede tek iptal A4'ü kalıcı olarak FAIL bırakır.
- **K9 Kademe başına takılma eşiği** — **kabul (değişiklikle)**: Sinematik `stallMs` 40 (DEC-100 p99 ≤ 40 ile aynı; 66,7 DEC'i gevşetirdiği için reddedildi), Yüksek/Performans 33,33; değişmez: `stallMs ≥ p99FilteredMs` (test).

## 11 · v1 → v2 farkları

Sinematik rapor → **kapı** (DEC-100) · 24 → **27** pencere, her hücre ≥ 3 · load1 ≤ 4 → **< 6** + 3 Unity + player slotu (C-00 26.09) · soak Yüksek → **Sinematik** · zincir yıldırım hop 6/8/10 → **maxTargets 8 kademesiz** · jel birikinti Pick(10/16/24) → **DEC-099: 12 s, eşzamanlı tavan 8** · Special cephane+süre (DEC-097) eklendi · AI bayrakları koddakine indirildi (K4) · A8 = `wp078_report.py` (üreticiyle çakışmalar K1, K4–K6, K8) · Sinematik p99 anlamı (K9) · C1/C2 geçti · v1 §8 kancaları main'de (S-01/02/03/04/05/06).
