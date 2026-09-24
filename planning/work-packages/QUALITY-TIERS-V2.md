# Kalite kademeleri v2 — sözleşme (WP-075 · S-05) · v1 yayın 2026-09-24

Kapsam: tüm M4-R1 paketleri (M4-R1 §4.7). Çerçeve: **DEC-092** (60 fps: Yüksek ort ≤ 16,6 ms, p99 ≤ 20 ms, 1080p; ayrıca Performans), **DEC-093** (build ≤ 5 GB; ADR-015-1 v3). Görsel niyet: `WP-075-ATMOSPHERE-LOOK-V3-DIRECTION.md` Ek A §8. Kamera modu verisi: WP-070 (`f23f38e7`) §8/§10/§11 — `CameraModeProfile`, `CameraModeService.Profile(mode) / CurrentProfile / Changed`.

## 1 · Kademeler ve kare hedefleri (referans Mac, 1920×1080, 40 aktif düşman, PilotPerfProbe Run)
| Kademe | Amaç | Hedef |
|---|---|---|
| **Sinematik** | fragman/yakın plan, güçlü makine; görünümün tamamı | DEC-092 üstü serbest, raporlanır; taban 45 fps (ort ≤ 22 ms, p99 ≤ 28 ms) |
| **Yüksek** (varsayılan) | hedef görünüm, oynanış | **ort ≤ 16,6 ms, p99 ≤ 20 ms** (DEC-092) — üç kamera modunda |
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
**Kural 3 — seviyeler.** QualitySettings 6 → 3 seviye (Performans / Yüksek / Sinematik), her biri kendi URP varlığı (`DD_URP_Performans`, `DD_URP_Yuksek` = bugünkü `DD_URP_PipelineAsset`, `DD_URP_Sinematik`); kayıtlı eski indeks eşlemesi 0–1 → Performans, 2–3 → Yüksek, 4–5 → Sinematik. Ayar ekranındaki adlar (S-09) bu üçü.

## 3 · Mod × kademe birleşim formülü (WP-070 §8/§11 ile ortak)
- `lodBias = max(1,0; kademe.LodBiasTaban × mod.LodBiasFactor)` — taban Performans 1,0 / Yüksek 2,0 / Sinematik 2,5; mod çarpanı Tepeden 1,0 / TP 0,6 / FP 0,7 → Yüksek TP 1,2, FP 1,4; Sinematik FP 1,75 (WP-070 öneri 1,6 — ölçülür); Performans her modda 1,0.
- `gölgeMesafesi = min(kademe.GölgeTavanı, mod.ShadowDistance)`; `kaskat = min(kademe.KaskatTavanı, mod.ShadowCascades)` — kademe tavanları Performans 45 m / 2 · Yüksek 60 m / 4 · Sinematik 80 m / 4; mod değerleri (S-01) Tepeden 60 m / 2 · TP 60 m / 3 · FP 70 m / 4.
- `farClip` = mod değeri (Tepeden 300 m, TP/FP 450 m — ufuk plakaları r 420 içeride); Performans'ta da 450 m (plakalar ucuz; WP-070'teki 350 m önerisi gerekmez).
- Sis: Tepeden 52 → 210 m (REQ-104 değişmez); TP/FP 45 → 235 m + yükseklik sisi (Performans'ta yükseklik sisi kapalı) — WP-075 Ek A §5.

## 4 · Kademe tabloları
### 4.1 Render tabanı (kalite modülü yazar)
| Ayar | Performans | Yüksek | Sinematik |
|---|---|---|---|
| Render scale | 0,85 | 1,0 | 1,0 |
| MSAA | kapalı (FXAA) | 2× | 4× |
| HDR | açık | açık | açık |
| Ana gölge | 2048, sert, güç 0,65 | 2048, yumuşak, güç 0,75 | 4096, yumuşak (yüksek), güç 0,75 |
| SSAO | kapalı | 1,2 / 0,30 m (Depth, blur Low) | 1,5 / 0,35 m (Depth+Normals, blur Medium) |
| Bloom | 3 iter, scatter 0,6 | eşik 1,10 / 0,55 / 0,68 / 4 iter | aynı |
| Film grain | kapalı | Thin1 0,10 | Thin1 0,18 |
| Vinyet | açık | açık | açık |
| DoF / motion blur (TP/FP) | kapalı | kapalı | WP-075 Ek A §7 |
| Yansıma probları | 6 × 128 | 10 × 128 | 10 × 256 |
| Planar (koi havuzları) | kapalı (prob) | 256 px, karakter yok | 512 px + karakter |
| Doku akış bütçesi (ADR-015-1 v3) | 512 MB, `globalTextureMipmapLimit` 1 izinli | 1024 MB | 2048 MB |
| LUT v2 (DD_Look_v2) | açık | açık | açık |

### 4.2 Görünüm özellikleri (özellik sahibi `Pick` ile okur)
| Özellik | Sahip | Performans | Yüksek | Sinematik |
|---|---|---|---|---|
| Gök bandı v2 + bulut düzlemleri | S-05 WP-075 | bant + 1 düzlem | bant + 2 düzlem | 2 düzlem + parallaks |
| Bulut gölgesi cookie | S-05 | tam | tam | tam |
| Huzme kartları | S-05 | 6, alfa ×0,7 | 10 | 15 |
| Ekran-uzayı huzme (TP/FP) | S-05 | kapalı | 20 örnek, 0,22 | 32 örnek, 0,35 |
| Yer sisi levhaları | S-05 | 4, alfa ×0,6 | 7, alfa ×0,8 | 7 |
| Yükseklik sisi (TP/FP) | S-05 | kapalı | açık | açık |
| Bitki yoğunluğu / impostor mesafesi | S-05 WP-074 | ×0,6 / 40 m | ×1,0 / 60 m | ×1,0 / 80 m |
| Atmosfer parçacıkları (× cookie) | S-07 WP-073 | ×0,4 | ×0,7 | ×1,0 |
| Eşzamanlı ragdoll (`RagdollProxyPool.Capacity`; taşan ölüm yazılı düşüş klibi) | S-06 WP-071 | 2 | 6 | 10 |
| Ceset tavanı / ömür (s); ceset–ceset çarpışması | S-06 | 6 / 2,5; ✘ | 10 / 4; ✔ | 16 / 6; ✔ |
| Foot IK gövde/kare (ayak kilidi) | S-06 | 4 (yalnız oyuncu) | 12 (oyuncu + ≤ 8) | 24 (oyuncu + LOD0 zombi) |
| İkincil zincir gövde/kare (`SecondaryChainDriver`) | S-06 | 16 | 40 | 60 |
| Look-at gövde/kare | S-06 | 4 | 12 | 20 |
| Twist çözümü | S-06 | yalnız oyuncu | oyuncu + LOD0 | oyuncu + LOD0 + LOD1 |
| Animator tam kare mesafesi TP/FP (ötesi 15 Hz Far); tepeden Far LOD | S-06 | ≤ 14 m; ≥ 1 | ≤ 22 m; ≥ 2 | ≤ 30 m; ≥ 3 |
| Yerinde dönüş/pivot klipleri | S-06 | ✘ (slerp) | ✔ | ✔ |
| FP kol viewmodel Animator (≈ 0,15 ms) | S-06 | açık | açık | açık |
| Mermi izi, darbe VFX, AI tick, Special etkileşimleri | S-02 / S-07 / S-03 | paket kendi satırlarını bu sözleşmeye ekler | | |
| Ses | S-08 | kademe satırı yok (ses CPU'su ≈ 0,02 ms/atış, ses bütçesi kademeden bağımsız; Performans ölçümü > 0,3 ms ise World kapasitesi 24 → 16) | | |

**Hiçbir kademede kaybedilmez:** ıslak arduvaz değeri/parlaması; bulut gölgesi cookie; LUT v2 + sis rengi #D0D7DE + mesafe rampası (90 m %24); ufuk plakaları + gök bandı; imza huzmeler (batı bambu ≥ 3, doğu sakura ≥ 2 kart); değer sırası ve DEC-077 okunabilirlik eşikleri.

## 5 · Ölçüm ve raporlama (her R1 ağır özelliği)
- A/B: özellik `-dd<Özellik> 0|1` bayrağı veya kademe anahtarı ile; PilotPerfProbe Run, 1080p, 10 s ısınma + 60 s pencere, tam 40, temiz pencere (`tools/qa/wp064_run.py perf`, `-ddCameraMode` ile üç mod); kademe × mod başına ≥ 2 pencere.
- Rapor satırı (paket kaydı + `quality-tiers-v2.json`): özellik · kademe · mod · Δms ort · Δms p99 · GPU Δms · kanıt yolu.
- Bütçe paylaşımı (öneri, C-00 hakemliğinde; Yüksek, tepeden, ölçülü taban 7,8 ms): dünya v5 ≤ +3,5 ms · atmosfer v3 ≤ +2,0 ms · VFX/anim/AI/UI/ses ≤ +3,0 ms · pay 0,2 ms. Aşan özellik kendi kademesinde kısar (M4-R1 §4.8); başka paketin bütçesinden alınmaz.
- Bildirilen tahminler (40 zombi, Yüksek): animasyon (S-06) ≈ 1,9 ms (pay ≤ 2,4; Sinematik ≈ 3,4 / Performans ≈ 0,7) — "VFX/anim/AI/UI/ses +3,0 ms" payının çoğu; C-00 hakemliğinde yeniden paylaştırma gerekebilir (ölçüm dilim sonlarında).
