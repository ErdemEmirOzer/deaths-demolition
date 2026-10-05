# C00 — AP-027 v003d-recut — 05.10.2026

## ÖZET
Durum: **SG UZAK VEKİLİ TESLİM EDİLMEDİ — C-00 spektral ölçütü sağlanmadı. ar1 Mechanical kararı overlay'e uygulandı. Unity içe alımı ve öznel dinleme YAPILMADI.**
- `exports/v003d-recut/`: yalnız ar1 Mechanical_01-06. Bunlar v003-recut dosyalarının byte düzeyinde aynısı (114 ms, baş v002 ile aynı); `supersedes: v003c-recut`. 6/6 kabul; MANIFEST + SHA256SUMS 7/7 OK.
- **Etkin overlay (v002 → v003 → v003b → v003c → v003d), 1118 dosya:** son 5 ms > -40 dB kalan **0**. Dağılım: 719 v002, 212 v003-recut, 177 v003b, 4 v003c (DistanceFar_04/06), 6 v003d.
- **sg1/sg2 DistanceFar_01-06 vekili:** 12 aday üretildi, ölçüldü, ölçüt sağlanmadı. Adaylar yalnız dinleme için `validation/v003d-recut/candidates-not-delivered/` altında. SG uzak katmanında v003-recut (01-05) ve v002 (06) geçerli kalıyor.
- Hash'ler değişmedi: v002 1273/1273 ve 1118/1118, v003-recut 231/231, v003b 188/188, v003c 11/11.

## 1. Vekil zinciri
Araç: `04-tools/recut_end_decay_v003d.py`. Sentetik osilatör/gürültü yok; malzemenin tamamı kayıt kökenli.
- **Doğrudan:** SPAS-12 1 m patlamasının ilk 25 ms'si.
  - ISO 9613-1 hava soğurması (50 m, 20 °C, %70 RH): 1 kHz 0,25 dB, 2 kHz 0,45 dB, 4 kHz 1,15 dB, 8 kHz 3,88 dB, 16 kHz 14,05 dB. Tam değerler `proxy-comparison-v003d.json`'da.
  - Küresel yayılma 1 m → 50 m.
  - Yönelim eğimi 0 dB/okt. Taranan 0…-4 dB/okt aralığında en iyi sonuç buydu.
- **Zemin (taş avlu):** 3,0-6,0 ms gecikme, r = 0,45-0,60, 6 kHz LP; hafif tarak filtre.
- **Erken yansımalar:** 4 adet, 18-110 ms, -9…-19 dB, 2,5-4 kHz LP.
- **Difüz kuyruk:**
  - Doku: aynı oturumun 50 m kaydındaki kırpılmamış geç kuyruk (1,00-1,87 s), üstel zarfı düzleştirilmiş.
  - Sönüm: aynı ortamda ölçülen -62,7 dB/s (±8 %).
  - Seviye: erken/geç enerji oranı -5,1…-3,1 dB. Referans, gerçek 50 m tek atış DistanceFar dosyaları (lmg1 -3,6, ar1 -4,6 dB).
- **6 varyant / silah:** zamanlama, ton ve yansıma farklı. sg1 ve sg2 ayrı tablolar kullanıyor. 2 gerçek atıştan DSP türevi.
- **Sonrası:** v002 DistanceFar zinciri (70-2600 Hz), -45 dB'de bitiş + 80 ms raised-cosine, -24 LUFS.
- **Kaynak notu:** SPAS 1 m kaydı da tamamen temiz değil. İki patlama tepesinde 45 örnek ≥ 0,999 FS var; en uzun dizi 5 örnek (52 µs). 50 m kaydında bu sayı 10.500 (%13,2).

## 2. Karşılaştırma (kanıt: `validation/v003d-recut/proxy-comparison-v003d.json`)
| Ölçüt | Sonuç |
|---|---|
| Kırpma | **0** (gerçek 50 m: %13,2) — GEÇER |
| Son 5 ms / son örnek / TP | ≤ -70 dB / ≤ -60 dB / ≤ -1,06 dBTP — GEÇER |
| LUFS (ar1/lmg1 seti, -24…-27) | -24,66…-24,00 — GEÇER |
| Süre | 0,485-0,552 s (gerçek 50 m tek atış dosyası lmg1_01: 0,65-0,73 s) |
| Erken/geç oran | -5,7…-0,7 dB (gerçek uzak dosyalar -3,6 / -4,6) |
| Atak (%10→90) | medyan 8 ms (gerçek 50 m, kırpık: 6 ms) |
| Kuyruk sönümü (son fade hariç, EDC) | -93…-112 dB/s. Gerçek geç kuyruk -62,7 dB/s; fark erken yansımaların ve -45 dB bitişinin etkisi |
| **1/3 oktav, ≤ 3 dB/bant (2-8 kHz hariç)** | **KALIR**, aşağıda |

1/3 oktav sonucu (referans: gerçek 50 m kaydının kırpık 0,11-0,62 s bölgesi, aynı DF bant filtresinden geçirilmiş):
- **50-1600 Hz:** en kötü fark +7,1 dB (sg1, 630 Hz) ve +7,3 dB (sg2). 3 dB'yi aşan bantlar: sg1'de 125, 400, 500, 630, 1250, 1600 Hz; sg2'de bunlara ek olarak 80 Hz.
- **10-16 kHz:** -24…-27,5 dB.

Gerekçe:
- Ölçüt, kırpık bir referansa göre tanımlı. %13 kırpma enerjiyi düzleştirip harmonik ekler. Gerçek kaydın kendi kırpılmamış geç kuyruğu bile kırpık bölgesinden 125-630 Hz'de 4-8 dB, 10 kHz üstünde 24-31 dB ayrışıyor.
- Vekil, kırpılmamış geç kuyruk spektrumuna 50-1600 Hz'de çoğunlukla 1-2 dB yakın (125-250 Hz'de +3…+7,6).
- D/R (-6…+10 dB) ve yönelim eğimi (0…-4 dB/okt) ızgarasında en iyi sonuç bile 50-1600 Hz'de 6,6 dB, 10 kHz üstünde 24 dB. Fiziksel parametrelerle ölçüt sağlanamıyor.
- Vekili aynı oranda kırpıp karşılaştırınca da fark ≤ 7,4 dB (LF/MF), 13-15 dB (HF).

Sonuç: kural gereği teslim edilmedi.

## 3. ar1 Mechanical (C-00 kararı)
- v003-recut sürümü overlay'e geri getirildi: 6 dosya, SHA'ları v003-recut ile aynı.
- Bu dosyalar v003b ve v003c'deki yerinde fade sürümlerinin yerine geçiyor; v003c'deki ar1 Mechanical böylece reddedilmiş oluyor.
- v003c DistanceFar_04/06 kabul edildiği için olduğu gibi geçerli.

## 4. Dosyalar
- **Teslim:** `exports/v003d-recut/MANIFEST.json` (77b62de20de7), `SHA256SUMS` (c082b25d6223).
- **Ölçüm:** `validation/v003d-recut/` içinde:
  - `proxy-comparison-v003d.json`, `build-log-v003d.json`,
  - `end-decay-v003d-recut.*`, `end-decay-candidates-not-delivered.*`,
  - `acceptance-v003d-recut.json`, `effective-overlay-end-decay.csv`,
  - `candidates-not-delivered/` (12 WAV).
- **Kaynak notu:** `03-sources/NOTE-v003d-SPAS-1m-turevi.md` ("SPAS 1 m türevi", aday, teslim edilmedi). Yeni kaynak yok, lisans aynı. Mevcut LICENSES dosyaları v002 hash listesinde olduğu için değiştirilmedi; not ayrı dosya olarak eklendi.
- **Araç:** `04-tools/recut_end_decay_v003d.py` (434cf7395961). Kurulum, ağ, Unity, Blender ve git kullanılmadı.

## 5. Açık karar (🔴)
SG uzak katmanı (sg1/sg2 DistanceFar_01-05 v003-recut, _06 v002) hâlâ kırpık kaynaktan geliyor. Seçenekler:
- **(A)** Adaylar dinlenip spektral ölçüt "kırpılmamış geç kuyruğa göre" yeniden tanımlanırsa (fark ≤ 3 dB çoğunlukla sağlanıyor; 125-250 Hz +3…+7,6) teslim edilebilir.
- **(B)** 30-80 m'de gerçek, kırpılmamış bir uzak av tüfeği kaydı edinilir. Bu ağ erişimi gerektirir.
- **(C)** Mevcut durum korunur.
