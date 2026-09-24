# WP-078 — M4-R1 kabul planı (tasarım) · S-10 · v1 2026-09-24

Paket sahibi: S-10 · Koordinasyon: C-00 · Durum: **tasarım; kod yok.** Uygulama (bot uyarlaması, araç değişiklikleri, koşular) R1 paketleri geldikçe ayrı commit'lerle.
Çerçeve: M4-R1 §4 / §8 (`KADRO/.../m4-r1/M4-R1-REVIZYON-TURU.md`), **DEC-091** (soak ikiye ayrılır), **DEC-092** (60 fps), **DEC-093** (build ≤ 5 GB), **DEC-095** (iki katmanlı ağaç), `QUALITY-TIERS-V2.md` (kademe sözleşmesi), WP-070 direction §11–§12 (mod × kademe, bot üç modda), WP-074 B9 (soak ±55). Bu belge karar/gereksinim metni değiştirmez; eşikler kaynak DEC/sözleşmeden alıntıdır.

## 0 · Amaç ve kapsam

R1 turu sonunda **tek bir commit** üzerinde: (1) değişen süitler yeşil, (2) QaRunBot üç kamera modunda koşar, (3) projectile / ragdoll / Special / ağaç regresyonları kanıtlı, (4) kademe × mod perf matrisi DEC-092'ye göre ölçülmüş, (5) DEC-091 kararlılık soak'ı ±55 dünyada yeniden tabanlanmış, (6) release build ≤ 5 GB ve kullanıcı verisi korunmuş. Sonra release build → kullanıcı oynar → R2 (M4-R1 §8).

Kapsam dışı: paket içi birim testlerinin yeniden yazımı (sahipte kalır), görsel kalite yargısı (AD), denge değerleri (S-04/S-02).

## 1 · Kabul zinciri (sıra = bağımlılık)

| Adım | Ne | Koşul / eşik | Sahip testi ↔ S-10 |
|---|---|---|---|
| A0 | Taban | tek commit, `git status` boş, LFS maddeleşmiş, Library kökeni (WP-064 kapı v2 `library_provenance.py`) | S-10 |
| A1 | Değişen süitler | EditMode + PlayMode, tek koşu (HIZLI HAT §7), kırmızı → §9 sahip tablosu | sahip süitleri, S-10 tek koşu |
| A2 | Bot üç modda (§2) | TD/TP/FP × 5 round, takılma 0, kamera-duvar içi kare 0 (WP-070 K2) | S-01 kancaları, S-10 bot |
| A3 | Regresyon kapsamı (§3) | matris satırlarının her biri PASS ya da sahipli FAIL | sahip + S-10 uçtan uca |
| A4 | Perf matrisi (§4) | 3 kademe × 3 mod; kapı hücreleri DEC-092 / Performans eşiği | S-10 (wp064_run perf) |
| A5 | Soak yeniden taban (§5) | DEC-091 kararlılık 2 tohum × 12/12 **aynı commit**; gerçek bot raporu | S-10 |
| A6 | Build (§6) | release LZ4HC ≤ 5 GB (DEC-093), dev + release damga temiz | S-01 aracı, S-10 kanıt |
| A7 | Kullanıcı verisi & bütünlük (§7) | koşu öncesi/sonrası bayt eşit; `ProjectIntegrityTests` yeşil | S-10 |
| A8 | Rapor (§9) | `wp078-report.json` + ≤ 10 satır C-00; FAIL→sahip tablosu | S-10 |

Kontrol noktaları (tam zincir değil, yalnız etkilenen adım):
- **C1** WP-070 B7 + WP-069 B1 main'de → A2 duman (3 mod × 2 round), A4 yalnız Yüksek × 3 mod × 1 pencere (eğilim).
- **C2** WP-074 B1 greybox (S-05 SHA + yükleme bayrağı) → bot `WorldBounds` uyarlaması + WP-074 B9 soak (26 + 61, 12/12).
- **C3** tüm R1 paketleri entegre → A0–A8 tek seferde; sonrasında giren her paket yalnız etkilediği adımı geçersiz kılar (değişiklik-kapsamlı yeniden doğrulama).

## 2 · QaRunBot üç kamera modunda

**Seçim:** `-ddCameraMode topdown|tp|fp` (CameraModeService.CommandLineFlag; kayıtlı tercihi değiştirmez — A7'de doğrulanır). Bot, mod değişiminde değil **başlangıç modunda** koşar; mod geçişi (Pause seçici) ayrı bir PlayMode testi (WP-070 K3), bot kapsamı değil.

**Bot sözleşmesi (uygulama S-10; kancalar S-01 WP-070 B7 / A-9):**
1. *Hareket:* planlayıcı dünya uzayında istenen yönü üretmeye devam eder; sol çubuk = istenen yönün **kamera yaw'ına göre** dönüştürülmüş hâli (TD'de kimlik). Kaçış planlayıcısı (`QaBotBrain.ScoreDirection`) mod-bağımsız kalır.
2. *Nişan:* TD mevcut (sağ çubuk dünya yönü). TP/FP'de sağ çubuk kamera yaw/pitch hızını sürer; hedef = en yakın tehdit (`CameraView`), atış yalnız `TryGetAimRay` ışını hedef göğüs kapsülüne ≤ 1,5° iken. Nişan hatası ve "ateş-hattı kaçırma" nedenleri (mevcut `fireLine` sayacı) mod başına raporlanır.
3. *Arka tehdit:* FP/TP'de görüş dışındaki bedenler planlayıcıda aynen tehdit (dünya verisi); `RearThreat` beat'i (WP-076) bot için ek sinyal değil, yalnız HUD/ses kanıtı.
4. *Dünya sınırı:* halka yarıçapı `WorldBounds.HalfExtent` / `NavMeshInset`'ten (S-05 WP-074 B1); yoksa 45 m geri dönüş. Güvenli nokta ızgarası ve merkez çekimi bu değerle ölçeklenir.
5. *Kararlılık modu:* `-ddQaBotStability` üç modda aynı (ölümsüz, ayrı `stabilityPass`).

**Metrikler (bot-summary v3, mod alanı `cameraMode`):** round/ölüm/zaman aşımı, takılma (> 5 s konum değişimi < 0,5 m, oda içi ayrı sayılır — WP-074 B9), **kamera-duvar içi kare** (S-01 sondası: kamera kapsülü collider içinde; TP SphereCast itmesi sonrası), nişan hatası ort/p95, kaçırma nedenleri, 061L ödülsüz kaldırma (havuz − kill ve spawn − kill; §5).

**Kanıt kareleri:** mod başına ≥ 3 (avlu açık, koridor/çatı altı, ufuk — WP-070 §12), sabit rota noktalarında `capture-gameplay-<mod>-<nokta>.png`; FP'de WP-070 E-listesi (E-1 kapalı geometri, E-14 draw call) aynı rotada. Rota noktaları dünya v5 greybox sonrası sabitlenir (C2).

**Kabul:** TD/TP/FP × 5 round (normal mod, tohum 61), takılma 0, kamera-duvar içi kare 0 (WP-070 K2) + kararlılık modunda TP ve FP × tohum 26 × 12/12 (**rapor**, §5.3).

## 3 · Regresyon kapsamı

Kural: sahip testi birincil kanıt; S-10 yalnız uçtan uca (dev Player, bot/sonda) çapraz kontrol ekler ve süitleri **bir kez** koşar. Her satır: PASS · sahipli FAIL · N/A (paket girmedi).

| Alan | Sahip kanıtı (süit / test) | S-10 uçtan uca çapraz kontrol |
|---|---|---|
| **Projectile** (WP-069 ADR-069-1) | sabit adım 1/120 s, kare başına ≤ 8 alt adım; süpürme (Raycast/SphereCast NonAlloc); tavan 256; `DamageTransaction` kimliği; 0 B/kare (WP-059D kalıbı: tek karede N atış + kontrol + pozitif kontrol) | bot koşusunda aktif mermi tepe sayısı / taşma 0 / isabet oranı; 10 silahın koşu içi mermi hızı = katalog değeri (±%1, `ArsenalCatalog` ↔ log); menzil sonu yok olma |
| **Penetrasyon + bölgesel zırh** | WP-069 §276 yeni satır: sr_1 dört gövde, 3.'sü kasklı; WP-006 zırh testleri yeşil | — |
| **Launcher rl_1** | patlama bölge dağılımı (WP-006), kurma 4,0 m, kendine hasar %45 / tavan 72 | gerçek bot raporunda kendine hasar ölümleri; > %5 ise WP-069 §1110 tetik → S-02 |
| **10 silah + 2 Special** (N1/N12) | `WeaponDefinition` doğrulaması (coreBranches, keystone), reload/şarjör/atış hızı test satırları | bot her silahı ≥ 1 round kullanır (silah döngüsü bayrağı, S-10); ateş/reload olay sayıları ses/VFX olay yüzeyine eşit (WP-072/073 kapsam matrisi) |
| **Special: jel** | birikinti tavanı Pick (10/16/24), kayma | kademe başına tavan aşılmaz (log sayacı) |
| **Special: zincir yıldırım** | hop Pick (6/8/10), LOS | kademe başına hop tavanı; `-ddChainHops 0` = 4 hop, LOS yok (bayrak A/B) |
| **Ragdoll / ölüm** (WP-071, WP-076) | `RagdollProxyPool.Capacity` Pick (2/6/10), taşan ölüm yazılı düşüş klibi, `RagdollReset` 200 döngü + 0 B (R3), cesette hasar olayı yok, NavMesh oyulmaz | soak'ta eşzamanlı ragdoll tepe ≤ kademe tavanı, havuz sızıntısı yok (döngü sayısı ↔ aktif); 4 ölüm animasyonu dağılımı (bot kill günlüğü) |
| **Zombi saldırı ×5** (N11, WP-076) | `AttackStyleProfile` tier izinleri; telegraph = klip olayları (DEC-029) | gerçek bot raporunda ölüm nedeni stil dağılımı; `-ddAiV3 0` = WP-040 davranışı |
| **Animasyon v3** | kadans bandı 0,85–1,20, ayak kilidi, moonwalk önleme (WP-071 K3) | — (AD) |
| **Kamera** (WP-070 K1–K9) | TD regresyonu kare farkı 0, D-20 nişan sözleşmesi, ayar kalıcılığı test kökü, FP görüntü modeli | A2 + A4 + A7 |
| **Dünya v5** (WP-074 B2/B9) | 18/18 kapı yolu, oran ≤ 1,45 | A5 soak (oda içi takılma > 5 s = 0) |
| **AI v3 bayrakları** | `-ddAiV3 / -ddRagdoll / -ddCorpseLinger / -ddChainHops / -ddGelPuddles / -ddCrowdNeighbours / -ddSpawnLos` | her bayrak `0` ve `1` ile Player açılır, 1 round tamamlanır (bayrak duman matrisi, dev build) |
| **Gelişim ağacı v2** (DEC-095, WP-077) | koşu içi dal/keystone, meta ağaç `ApplyRun` sırası, harcama payı R1–R15 %65–80 (simülasyon) | yeni XP eğrisi + Kül tempo telemetrisi 5 gerçek bot koşusundan (WP-077 §306); aday profil klonu kullanıcı profiline dokunmaz (A7) |
| **Ses v3** | World kapasitesi, ses CPU ≤ 0,3 ms (Performans) | perf matrisinde ses payı raporu |
| **UI** | ADR-024-2 tarama 0 (UI Input System okumaz), kamera seçici | — |
| **Okunabilirlik** (DEC-077) | `ArmorTests.Okunabilirlik` | TD kapısı değişmez (MUST); TP/FP kanıt kareleri AD'ye rapor |

## 4 · Perf ölçüm protokolü (hedeften bağımsız)

**Eşikler veriden okunur, protokol sabittir.** `tools/qa/perf-targets.json` (DEC kimliğiyle): kademe → {ort ms, p99 süzülmüş ms, kapı/rapor}. Bugünkü içerik QUALITY-TIERS-V2 §1'den: Performans 8,0 / 10 **kapı**; Yüksek 16,6 / 20 **kapı** (DEC-092); Sinematik 22 / 28 **rapor** (taban 45 fps). Hedef değişirse yalnız bu dosya değişir.

**Matris:** 3 kademe × 3 mod = 9 hücre (WP-070 D-22). Pencere: kapı hücreleri (Yüksek × 3, Performans × 3) **≥ 3 tam pencere**, Sinematik hücreleri ≥ 2 (sözleşme §5 tabanı ≥ 2). Her tam deneme sayılır (en iyisi seçilmez); iptal yalnız host nedeniyle (deneme defteri, WP-064 kapı v2).

**Senaryo (her pencere):** PilotPerfProbe `-ddPilotScene Run`, 1920×1080 pencereli, **tam 40 aktif** (yük bütünlüğü kontrolü), 10 s ısınma + 60 s pencere, `-ddCameraMode <mod>` + kademe anahtarı (`QualityTiers`, kayıtlı ayara yazmadan). TP/FP'de görüş karşılaştırılabilir olsun diye **sabit kamera programı**: avlu merkezinde 60 s'de 360° yaw taraması, pitch 0 (S-01 sonda kancası; ekrandaki düşman sayısı raporlanır). Oynanış penceresi (bot sürer) yalnız rapor.

**Ölçülenler:** ort, medyan, ham p99, süzülmüş p99 (> 33,3 ms kareler; dışlama ≤ %1, CSV'den yeniden türetilir), > 33 ms sayısı, GPU kare ms (FrameTiming), ana/render iş parçacığı CPU, GC B/kare, draw call, set-pass, ekranda/görünür düşman, eşzamanlı ragdoll/mermi tepe.

**Geçme:** kapı hücresinde **her** tam pencere ort ≤ eşik ve süzülmüş p99 ≤ eşik. Rapor hücresi eşiği aşarsa not düşülür, kapı değildir.

**Build türü (açık konu → S-01):** PilotPerfProbe ve QaRunBot `DEVELOPMENT_BUILD || UNITY_EDITOR` altında; WP-070 ölçümünde dev build p99'u kapıyı karartıyor. Öneri: `tools/build.sh release-probe` = release ayarları (development kapalı, LZ4HC) + yalnız sonda derleme sembolü (`DD_PERF_PROBE`), **dağıtılmaz**. Kapı sayıları release-probe'dan; A/B deltaları dev'den (sahipler, sözleşme §5). Karar gelene kadar kapı dev build'le, rapor bu sınırlamayı yazar.

**Host ve makine:** referans = geliştirme Mac'i (MacBookPro18,3, Apple M1 Pro; DEC-092). Her denemede kayıt: model, macOS, ekran yenileme, güç adaptörü (`pmset -g batt`), ısıl durum (`pmset -g therm`), load1. Ön koşul: load1 ≤ 4,0, Unity/Blender/Player süreci yok, slot yok, 3 dk sessiz (WP-064 dersi); pencereler arası 60 s soğuma. Tahmini süre: 24 pencere × ~2,5 dk + 2 build ≈ 1,5 saat sessiz pencere.

**Özellik payları:** sahipler kendi `-dd<Özellik> 0|1` deltalarını sözleşme §5 satırıyla yazar; S-10 C3'te toplamı doğrular: Yüksek TD taban + Σ pay ≤ 16,6 ms (dünya ≤ +3,5 · atmosfer ≤ +2,0 · VFX/anim/AI/UI/ses ≤ +3,0 · pay 0,2). Aşan paket kendi kademesinde kısar (§4.8); S-10 bütçe taşımaz.

## 5 · Soak (DEC-091) yeniden taban

1. **Tek commit.** WP-064 kapanışı üç commit'e yayıldı (perf 96e3c17, stab61 58be2c2, stab26 a1397d3). R1'de kararlılık 2 tohum (61 + 26) × 12/12 + perf aynı C3 commit'inde. Koşucu `wp064_run.py` düzenine geçer (attempt.json, preflight, host monitor, defter); S-10 `soak.sh` yalnız geçiş dönemi.
2. **Kapsam (MUST, DEC-091 değişmez):** tepeden mod, Yüksek kademe (varsayılan), ±55 dünya, `-ddQaBotStability`; zaman aşımı/takılma 0, Player logunda hata 0, bellek büyümesi sınırlı, kullanıcı verisi korunur, temiz host.
3. **Rapor (kapı değil):** TP ve FP × tohum 26 × 12/12 kararlılık; Sinematik kademe × tohum 61 × 12/12 kararlılık (en büyük havuzlar — sızıntı avı). MUST'a yükseltme C-00/kullanıcı kararıdır (§10).
4. **Bellek eşikleri:** büyüme ölçütleri (Mono r2→r12 ≤ 48 MB ve ≤ 4 MB/round, toplam ayırma ≤ 256 MB, RSS ≤ 512 MB) mutlak değil büyüme olduğundan yeni havuzlarla geçerli kalır; C2 soak'ında ölçülen değer + %50 pay ile yeniden onay, gerekirse `soak_gate.py` eşik güncellemesi ayrı commit.
5. **Log örüntüleri (ek):** fizik eklem/rigidbody hataları, `Failed to create agent`, NavMesh `SetDestination` hataları, projectile taşma uyarısı, havuz tükenmesi.
6. **061L kaldırmaları:** round başına havuz − kill ve spawn − kill raporlanır; koşu başına > 2 ise S-03 bileti (kararlılık oyun önlemiyle taşınıyorsa görünür olsun).
7. **Gerçek bot raporu:** tepeden ≥ 5 koşu (medyan/min/maks, ölüm round'u ve nedeni, stil dağılımı) + TP ve FP × 2 koşu; kapı değil.

## 6 · Build ve boyut

Release `tools/build.sh release` (LZ4HC, DEC-090) ≤ **5 GB** (DEC-093) — kapı; dev build aynı commit, damga temiz (`-dirty` yok), editör hatası 0. `build-report.sh` ayak izi: doku/mesh/ses payı + önceki R1 C1 değerine göre büyüme (rapor).

## 7 · Kullanıcı verisi ve bütünlük

Her Player koşusunda iki oyuncu klasöründe ui-settings / load-timing / input-settings / input-overrides / ui-hints yedek → geri yükleme **bayt eşit**; koşunun oluşturduğu dosyalar kanıta taşınır (silinmez). Ek: `-ddCameraMode` kayıtlı kamera tercihini değiştirmez (koşu öncesi/sonrası ui-settings eşit); meta ağaç aday profil klonu gerçek profile yazmaz. `ProjectIntegrityTests` + LFS işaretçi/nesne eşitliği.

## 8 · Gerekli kancalar (sahiplere istek)

| Kanca | Sahip | Neden |
|---|---|---|
| Bot için kamera yaw/pitch sürme + `TryGetAimRay`, kamera-duvar içi sondası | S-01 WP-070 B7 | §2, K2 |
| PilotPerfProbe sabit kamera programı (360° yaw), mod-farkında aşamalar (FP occluder-fade "beklenen") | S-01 / S-05 | §4 |
| `release-probe` build türü | S-01 | §4 |
| `WorldBounds.HalfExtent / NavMeshInset` + greybox yükleme bayrağı | S-05 WP-074 B1 | §2, §5 |
| Mermi sayaçları (aktif, tepe, taşma) Player loguna/özete | S-02 WP-069 | §3 |
| Ragdoll havuz sayaçları (aktif, tepe, taşma, döngü) | S-06 WP-071 | §3, §5 |
| 061L kaldırma sayacını round sonu log satırına yaz | S-03 | §5.6 |
| XP / Kül telemetrisi alanları | S-04 WP-077 | §3 |

## 9 · Rapor ve sahiplik

`wp078-report.json` (şema `dd-wp078-acceptance-v1`): commit, build hash'leri, A0–A8 kontrol listesi, perf matrisi (hücre × pencere), soak ve rapor koşuları, kanıt yolları. C-00'a ≤ 10 satır + **FAIL → sahip** tablosu (WP-039 kalıbı; sahip = paketi yazan oturum). Kanıt: `KADRO/.../wp-078/evidence/<commit>/`.

## 10 · Açık konular (C-00)

1. Kapı perf'i release-probe'dan mı (öneri) dev'den mi? (S-01 aracı gerekir.)
2. TP/FP kararlılık soak'ı R1'de rapor (öneri) mı MUST mı? MUST ise DEC-091 değişikliği → kullanıcı kararı.
3. Kapı hücresi pencere sayısı 3 (öneri; sözleşme tabanı 2).
4. Sabit kamera programı yerine bot oynanışı penceresinin kapı sayılması istenirse varyans kabulü gerekir (bot zamanlaması tohumlu değil).
