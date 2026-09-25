# Kalite kademeleri v2 — sözleşme (WP-075 · S-05) · v1 yayın 2026-09-24 · r2 2026-09-25 (DEC-100)

Kapsam: tüm M4-R1 paketleri (M4-R1 §4.7). Çerçeve: **DEC-100** (varsayılan **Sinematik**: 30 fps, ort ≤ 33,3 ms / p99 ≤ 40 ms, 1080p, referans Mac, kalite en üst düzey), **DEC-092** (60 fps: Yüksek ort ≤ 16,6 ms, p99 ≤ 20 ms, 1080p; ayrıca Performans), **DEC-093** (build ≤ 5 GB; ADR-015-1 v3). Görsel niyet: `WP-075-ATMOSPHERE-LOOK-V3-DIRECTION.md` Ek A §8. Kamera modu verisi: WP-070 (`f23f38e7`) §8/§10/§11 — `CameraModeProfile`, `CameraModeService.Profile(mode) / CurrentProfile / Changed`.

## 1 · Kademeler ve kare hedefleri (referans Mac, 1920×1080, 40 aktif düşman, PilotPerfProbe Run)
| Kademe | Amaç | Hedef |
|---|---|---|
| **Sinematik** (varsayılan, DEC-100) | hedef görünüm (Higgsfield hedef karelerine yaklaşma), kalite en üst düzey; 30 fps bütçesi içinde görsel kısma yok | **ort ≤ 33,3 ms, p99 ≤ 40 ms** — kapılı, üç kamera modunda; planlama toplamı **≤ 32,0 ms** (1,3 ms marj) |
| **Yüksek** (seçilebilir) | 60 fps oynanış | **ort ≤ 16,6 ms, p99 ≤ 20 ms** (DEC-092) — üç kamera modunda; planlama toplamı **≤ 16,0 ms** (0,6 ms marj, C-00 24.09) |
| **Performans** | yüksek yenileme / zayıf GPU | ort ≤ 8,0 ms, p99 ≤ 10 ms (DEC-088 eşiği; WP-070 D-21 ile uyumlu) |

## 2 · API (DD.Prototype.Core; S-05 Faz B ilk dilimi, WP-075 B1)
```csharp
public enum QualityTier { Performans = 0, Yuksek = 1, Sinematik = 2 }
public static class QualityTiers
{
    public static QualityTier Current { get; }                 // UiSettings.Quality → kademe
    public static event Action<QualityTier> Changed;           // UiSettings.QualityChanged üstünde
    public static T Pick<T>(T performans, T yuksek, T sinematik);
}
```
**Kural 1 — tek yazar.** Global Unity/URP değerleri (QualitySettings.lodBias, URP gölge mesafesi/kaskat/çözünürlük/yumuşaklık, render scale, MSAA, SSAO, bloom/grain/vinyet, sis, akış bütçesi) yalnız kalite modülü (S-05) yazar. Kamera-yerel değerler (near/far clip, occluder soldurma, look-ahead) yalnız WP-070 (S-01) yazar. Kalite modülü `QualityTiers.Changed` ve `CameraModeService.Changed` ile yeniden uygular; mod geçişinde WP-070 D-18 lerp'i izler (pop yok).
**Kural 2 — özellik sahibi okur.** Her paket kendi ağır özelliğinin kademe değerini `QualityTiers.Pick` ile okur, `Changed`'e abone olur (parçacık çarpanı, ragdoll tavanı, mermi izi sayısı, huzme örnek sayısı, sis levha sayısı, bitki yoğunluğu, impostor mesafesi …). Sabit kodlanmış kademe değeri 0 (değerler ScriptableObject'te).
**Kural 3 — seviyeler.** QualitySettings 6 → 3 seviye (Performans / Yüksek / Sinematik), her biri kendi URP varlığı (`DD_URP_Performans`, `DD_URP_Yuksek` = bugünkü `DD_URP_PipelineAsset`, `DD_URP_Sinematik`); kayıtlı eski indeks eşlemesi 0–1 → Performans, 2–3 → Yüksek, 4–5 → Sinematik. Ayar ekranındaki adlar (S-09) bu üçü. **Varsayılan (DEC-100):** QualitySettings varsayılan seviyesi ve kalite modülünün geri dönüşü Sinematik (S-05); ilk açılış / ayar sıfırlama varsayılanı Sinematik (S-09); perf kapısı Sinematik ≤ 33,3 / 40 ms + Yüksek ≤ 16,6 / 20 ms (S-10).

## 3 · Mod × kademe birleşim formülü (WP-070 §8/§11 ile ortak)
- `lodBias = max(1,0; kademe.LodBiasTaban × mod.LodBiasFactor)` — taban Performans 1,0 / Yüksek 2,0 / Sinematik 3,0 (DEC-100; r1 2,5); mod çarpanı Tepeden 1,0 / TP 0,6 / FP 0,7 → Yüksek TP 1,2, FP 1,4; Sinematik TP 1,8, FP 2,1; Performans her modda 1,0.
- `gölgeMesafesi = min(kademe.GölgeTavanı, mod.ShadowDistance)`; `kaskat = min(kademe.KaskatTavanı, mod.ShadowCascades)` — kademe tavanları Performans 45 m / 2 · Yüksek 60 m / 4 · Sinematik 120 m / 4 (DEC-100; r1 80 m); mod değerleri (S-01) Tepeden 60 m / 2 · TP 60 m / 3 · FP 70 m / 4.
- `farClip` = mod değeri (Tepeden 300 m, TP/FP 450 m — ufuk plakaları r 420 içeride); Performans'ta da 450 m (plakalar ucuz; WP-070'teki 350 m önerisi gerekmez).
- Sis: Tepeden 52 → 210 m (REQ-104 değişmez); TP/FP 45 → 235 m + yükseklik sisi (Performans'ta yükseklik sisi kapalı) — WP-075 Ek A §5.

## 4 · Kademe tabloları
### 4.1 Render tabanı (kalite modülü yazar)
| Ayar | Performans | Yüksek | Sinematik |
|---|---|---|---|
| Render scale | 0,85 | 1,0 | 1,0 |
| MSAA | kapalı (FXAA) | 2× | 4× |
| HDR | açık | açık | açık |
| Ana gölge | 2048, sert, güç 0,65 | 2048, yumuşak, güç 0,75 | 4096, yumuşak (yüksek), güç 0,75, 4 kaskat, 120 m |
| SSAO | kapalı | 1,2 / 0,30 m (Depth, blur Low) | 2,0 / 0,40 m (Depth+Normals, blur High, tam çözünürlük) |
| Bloom | 3 iter, scatter 0,6 | eşik 1,10 / 0,55 / 0,68 / 4 iter | aynı |
| Film grain | kapalı | Thin1 0,10 | Thin1 0,18 |
| Vinyet | açık | açık | açık |
| DoF / motion blur (TP/FP) | kapalı | kapalı | WP-075 Ek A §7 |
| Yansıma probları | 6 × 128 | 10 × 128 | ≥ 16 × 512 (saray, avlu, kapılar, havuzlar, kairō) |
| Planar (koi havuzları) | kapalı (prob) | 256 px, karakter yok | 1024 px + karakter |
| Doku akış bütçesi (ADR-015-1 v3) | 512 MB, `globalTextureMipmapLimit` 1 izinli | 1024 MB | 3072 MB (4K zemin/kahraman tam mip) |
| Lightmap / probe yoğunluğu (B8) | tek bake, üç kademede ortak | aynı | aynı |
| LUT v2 (DD_Look_v2) | açık | açık | açık |

### 4.2 Görünüm özellikleri (özellik sahibi `Pick` ile okur)
| Özellik | Sahip | Performans | Yüksek | Sinematik |
|---|---|---|---|---|
| Gök bandı v2 + bulut düzlemleri | S-05 WP-075 | bant + 1 düzlem | bant + 2 düzlem | 2 düzlem + parallaks |
| Bulut gölgesi cookie | S-05 | tam | tam | tam |
| Hacimsel ışık (DEC-102: raymarch, ana ışık gölge haritası × bulut cookie, yükseklik + bölgesel sis yoğunluğu; tüm modlar) | S-05 WP-075 | kapalı | ¼ çözünürlük, 12 adım | ½ çözünürlük, 24 adım (karesel aralık; r2: 32 adım release p99 kapısını kırdı) |
| Huzme kartları | S-05 | 6, alfa ×0,7 (hacimsel ışığın yerine) | — (DEC-102: hacimsel) | — (DEC-102: hacimsel) |
| Ekran-uzayı huzme (TP/FP) | S-05 | kapalı | — (DEC-102: hacimsel) | — (DEC-102: hacimsel) |
| Yer sisi levhaları | S-05 | 4, alfa ×0,6 | 7, alfa ×0,8 | 10 |
| Yükseklik sisi (TP/FP) | S-05 | kapalı | açık | açık |
| Bitki yoğunluğu / impostor mesafesi | S-05 WP-074 | ×0,6 / 40 m | ×1,0 / 60 m | ×1,75 / 120 m |
| Atmosfer parçacıkları (× cookie) | S-07 WP-073 | ×0,4 | ×0,7 | ×1,0 |
| Eşzamanlı ragdoll (`RagdollProxyPool.Capacity`; taşan ölüm yazılı düşüş klibi; `-ddRagdoll`) | S-06 WP-071 (uygular) | 2 | 6 | 10 (S-03 / S-06 uzlaştı, C-00; S-06 önerisi 14 → S-03 / C-00 onayı bekliyor) |
| Ceset tavanı / ömür (s); ceset–ceset çarpışması | S-06 | 6 / 2,5; ✘ | 10 / 4; ✔ | 24 / 8; ✔ (DEC-100, S-06 25.09) |
| Animasyon toplamı (40 zombi) | S-06 | — | ≈ 1,9 ms (toplam 16,0'ı aşarsa VFX'ten sonra kısılan: **≤ 1,6 ms**) | **≤ 2,5 ms** (DEC-100, S-06 25.09) |
| Foot IK gövde/kare (ayak kilidi) | S-06 | 4 (yalnız oyuncu) | 12 (oyuncu + ≤ 8) | oyuncu + tüm LOD0 (DEC-100) |
| İkincil zincir gövde/kare (`SecondaryChainDriver`) | S-06 | 16 | 40 | 80 (DEC-100) |
| Look-at gövde/kare | S-06 | 4 | 12 | 30 (DEC-100) |
| Twist çözümü | S-06 | yalnız oyuncu | yalnız oyuncu | yalnız oyuncu (zombi rig v002'de twist kemiği yok; S-06 düzeltmesi 25.09) |
| Zombi çene (rig v002 K10) | S-06 | kapalı | LOD0 | LOD0 + LOD1 |
| Animator tam kare mesafesi TP/FP (ötesi 15 Hz Far); tepeden Far LOD | S-06 | ≤ 14 m; ≥ 1 | ≤ 22 m; ≥ 2 | ≤ 40 m; ≥ 4 (DEC-100) |
| Yerinde dönüş/pivot klipleri | S-06 | ✘ (slerp) | ✔ | ✔ |
| FP kol viewmodel Animator (≈ 0,15 ms) | S-06 | açık | açık | açık |
| AI tick bütçesi (ragdoll hariç, 40 beden) `-ddAiV3` | S-03 WP-076 | 0,55 ms | 0,75 ms | 0,85 ms |
| Ceset ömrü çarpanı (tavan S-06 satırı) `-ddCorpseLinger` | S-03 | ×0,6 | ×1,0 | ×1,25 |
| Zincir yıldırım sekme `-ddChainHops` | S-03 | 6 | 8 | 10 |
| Jel birikinti tavanı `-ddGelPuddles` | S-03 | 10 | 16 | 24 |
| Kalabalık komşu tavanı `-ddCrowdNeighbours` | S-03 | 6 | 10 | 14 |
| PathQueriesPerTick / spawn LOS linecast `-ddSpawnLos` | S-03 | 4 / 9 | 6 / 18 | 6 / 18 |
| Mermi (projectile) simülasyonu — oyun kuralı, **kademesiz** (tavan 256 mermi, 120 Hz sabit adım, 0 B/kare; isabet/menzil kademeyle değişmez) | S-02 WP-069 | aynı | aynı | aynı — ms: B1 ölçümü (bekleniyor) |
| Mermi izi şeritleri (Yüksek hedef 0,25 ms) | S-07 WP-073 | 256, parıltısız, HDR ×0,5 | 256 tam | 256 tam |
| Namlu ateşi + duman + ışık (0,20 + 0,20 ms) | S-07 | ışık her 2. atışta, 2 ışık, duman 0,25 s | her atış, 4 ışık, 0,5 s | 8 ışık yumuşak gölge, 0,8 s + pus |
| Kovanlar (0,05) | S-07 | 24, 3 s | 48, 5 s | 96, 8 s |
| Darbe parçacıkları (0,35) | S-07 | ×0,4, 24 eşzamanlı | ×1,0, 40 | ×1,3, 48 |
| Dekaller (0,05) | S-07 | 64, 8 s | 128, 12 s | 256, 12 s |
| Kan damlacıkları (0,20) | S-07 | 48 | 96 | 128 |
| Patlama (0,30, tepe 0,6) | S-07 | 5 katman, 1 tam | 7 katman, 2+1 | 8 katman + gölge |
| Roket izi (0,05) | S-07 | 1 s | 2 s | 3,5 s |
| Raiju yayları (0,25) | S-07 | 6 / 12 | 8 / 24 | 8 / 32 |
| Namekuji birikintileri (0,25) | S-07 | 12 düz | 12 kabarcık | 12 + erime |
| Atmosfer parçacıkları, canlı (0,60; × cookie) | S-07 | 600 | 1 800 | 3 000 |
| **VFX toplamı** | S-07 | ≤ 1,2 ms | ≤ 3,0 ms (Yüksek toplamı 16,0'ı aşarsa ilk kısılan: **≤ 2,2 ms**) | ≤ 5,5 ms (yalnız raporlanır) |
| Ses | S-08 | kademe satırı yok (ses CPU'su ≈ 0,02 ms/atış, ses bütçesi kademeden bağımsız; Performans ölçümü > 0,3 ms ise World kapasitesi 24 → 16) | | |

**DEC-100 (25.09):** Sinematik varsayılan ve kapılıdır; bu tablodaki Sinematik hücrelerini sahipleri 30 fps bütçesine göre cömertleştirir (S-06 / S-07 / S-03 / S-02; C-00 iletimi); "yalnız raporlanır" notları Sinematik için geçersizdir.

**Hiçbir kademede kaybedilmez:** ıslak arduvaz değeri/parlaması; bulut gölgesi cookie; LUT v2 + sis rengi #D0D7DE + mesafe rampası (90 m %24); ufuk plakaları + gök bandı; imza huzmeler (batı bambu ≥ 3, doğu sakura ≥ 2 kart); değer sırası ve DEC-077 okunabilirlik eşikleri.

## 5 · Ölçüm ve raporlama (her R1 ağır özelliği)
- A/B: özellik `-dd<Özellik> 0|1` bayrağı veya kademe anahtarı ile; PilotPerfProbe Run, 1080p, 10 s ısınma + 60 s pencere, tam 40, temiz pencere (`tools/qa/wp064_run.py perf`, `-ddCameraMode` ile üç mod); kademe × mod başına ≥ 2 pencere.
- Rapor satırı (paket kaydı + `quality-tiers-v2.json`): özellik · kademe · mod · Δms ort · Δms p99 · GPU Δms · kanıt yolu.
- Bütçe paylaşımı (öneri, C-00 hakemliğinde; Yüksek, tepeden, ölçülü taban 7,8 ms): dünya v5 ≤ +3,5 ms · atmosfer v3 ≤ +2,0 ms · VFX/anim/AI/UI/ses ≤ +3,0 ms · pay 0,2 ms. Aşan özellik kendi kademesinde kısar (M4-R1 §4.8); başka paketin bütçesinden alınmaz.
- **Rakamların anlamı:** §4.2'deki ms değerleri o sistemin kademedeki **toplam** maliyetidir (ör. AI tick 0,75 ms = 40 beden için tüm AI), delta değildir. Taban kapanış ölçümü (≈ 7,9 ms, WP-064 r3) bugünkü animasyon / AI / VFX maliyetini zaten içerir. Paya sayılan, özelliğin A/B ölçümündeki **Δms**'idir (R1 toplamı eksi tabandaki bugünkü maliyeti). Önceki "≈ 5,65 ms > +3,0 ms" notu toplamı delta payıyla karşılaştırıyordu; kaldırıldı.
- **C-00 kararı (24.09):** DEC-092 toplamı sabit, pay artışı yok. Yüksek kademe toplamı ≤ 16,0 ms (0,6 ms marj). Aşım olursa kesme sırası: önce VFX (S-07, Yüksek ≤ 2,2 ms), sonra animasyon (S-06, Yüksek ≤ 1,6 ms); AI 0,75 ms kalır. Sinematik yalnız raporlanır. S-05'in dünya (+3,5) ve atmosfer (+2,0) payları tavandır; 16,0'a sığmak için ölçülüp kullanılmayan kısım bırakılır. Kesin rakamlar WP-078 ölçümünde (kademe × mod, ≥ 3 pencere) doğrulanır.
- **DEC-100 (kullanıcı 25.09):** varsayılan kademe Sinematik; kapı ort ≤ 33,3 / p99 ≤ 40 ms (üç kamera modunda, WP-078), planlama toplamı ≤ 32,0 ms. Pay önerisi (C-00 hakemliğinde; Yüksek payları değişmez): dünya v5 ≤ +8,0 · atmosfer v3 ≤ +5,0 · VFX ≤ 5,5 · animasyon ≤ 2,5 · AI 0,85 ms. 30 fps bütçesi içinde görsel kısma yapılmaz; aşımda önce ölçüm, sonra C-00. Görsel kalite atağının kanıt kareleri Sinematik kademede alınır, Yüksek karşılaştırma olarak yanında.
