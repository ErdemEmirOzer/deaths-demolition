# C00 — AP-027 v003e-recut — 05.10.2026

## ÖZET
Durum: **TESLİM — sg1/sg2 DistanceFar_01-06 (6 + 6) fiziksel mesafe vekili. 12/12 kabul. Unity içe alımı ve öznel dinleme YAPILMADI.**
- `exports/v003e-recut/`: 12 WAV, 927.858 bayt. Manifestte `derived_proxy: SPAS 1m -> 50m`.
  - `supersedes`: 01-05 için v003-recut, 06 için v002.
  - `MANIFEST.json` (753716013970) + `SHA256SUMS` (abb536a3ae01) 13/13 OK.
- **Etkin overlay (v002 → v003 → v003b → v003c → v003d → v003e), 1118 dosya:** son 5 ms > -40 dB kalan **0**. Dağılım: 717 v002, 202 v003-recut, 177 v003b, 4 v003c, 6 v003d, 12 v003e.
- Hash'ler değişmedi: v002 1273/1273 ve 1118/1118, v003-recut 231/231, v003b 188/188, v003c 11/11, v003d 7/7.
- Bağımsız WAV ayrıştırıcıyla 12/12 geçti (mono, 48 kHz/24-bit PCM, kırpma 0).

## 1. Zincir
Araç: `04-tools/recut_end_decay_v003e.py` (45f73b7aca53); v003d zinciri üzerine kurulu. Ayrıntı ve tam parametreler: `validation/v003e-recut/build-log-v003e.json`, `search-v003e.json`.

Ortak zincir:
- SPAS 1 m doğrudan patlama,
- ISO 9613-1 hava soğurması (50 m, 20 °C, %70 RH),
- taş zemin yansıması (3,0-6,0 ms, r 0,45-0,60, hafif tarak),
- 4 erken yansıma,
- difüz kuyruk: 50 m kaydının kırpılmamış geç kuyruk dokusu, -62,7 dB/s ±%8,
- v002 DistanceFar zinciri (70-2600 Hz, -45 dB'de bitiş + 80 ms raised-cosine, -24 LUFS).

Ölçütü sağlamak için eklenen ton adımları:
- **Arama:** dosya başına D/R (erken/geç hedef -7…-3 dB), 200 Hz yakın alan gövde düzeltmesi (0…-8 dB) ve alçak raf (0/-3 dB) taranıyor. Atak ≤ 15 ms koşuluyla uzak atışın atağı korunuyor.
- **Ton eşleme:** gerçek 50 m geç kuyruğa göre EQ. Minimum faz, yumuşatılmış, ±6 dB sınırlı, ≥ 2 kHz'de 0 dB. Gerçekleşen aralık -6,0…+6,0 dB; sınıra yalnız sg1_01 (+6,0) ve sg1_05 (-6,0) dayanıyor.

Varyantlar:
- **Varyasyon:** her silahın kendi 6 satırlık tablosu var: zamanlama (zemin gecikmesi, erken yansıma gecikmeleri, doku başlangıcı), yansıma (r ve erken yansıma seviyeleri) ve ton (D/R, gövde düzeltmesi, EQ).
  - Ayrışma (hizalı en yüksek normalize çapraz korelasyon): sg1 içinde ≤ 0,31, sg2 içinde ≤ 0,28, aynı indeksli sg1-sg2 çiftlerinde 0,14-0,26.
- **sg2 çift namlu:** aynı mesafe modeli. Fark yalnız atak (kayıtlı 2,5 ms geçici vurgusu, +2,0…+4,0 dB) ve gövde (doğrudan pencere 18-21 ms; sg1'de 25 ms).
  - Yorum notu: "aynı mesafe zinciri" aynı fiziksel model olarak uygulandı. sg2 kendi zamanlama/yansıma satırlarını kullanıyor; aynı parametreleri kullansaydı sg1 ile 0,99 korelasyonlu, neredeyse aynı dosyalar çıkıyordu.

## 2. Ölçüm tablosu
Spektral sütun: 50-1600 Hz'deki en kötü bant farkı (≤ 3 dB). Ayrıntılı tablolar: `validation/v003e-recut/spectral-table-v003e.csv`, `end-decay-v003e-recut.csv`, `acceptance-v003e-recut.json`.

| Dosya | Son 5 ms dB | TP dBTP | LUFS | Süre s | Spektral dB | Erken/geç dB | Atak ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| sg1 DF_01 | -72,9 | -2,33 | -24,00 | 0,530 | 2,25 | -5,7 | 7 |
| sg1 DF_02 | -69,1 | -1,11 | -24,00 | 0,540 | 1,95 | -4,5 | 4 |
| sg1 DF_03 | -72,4 | -1,64 | -24,00 | 0,505 | 0,99 | -0,9 | 3 |
| sg1 DF_04 | -73,1 | -4,57 | -24,00 | 0,554 | 2,16 | -7,1 | 6 |
| sg1 DF_05 | -74,6 | -2,11 | -24,00 | 0,542 | 2,86 | -7,5 | 3 |
| sg1 DF_06 | -74,8 | -4,06 | -24,00 | 0,530 | 2,60 | -7,5 | 6 |
| sg2 DF_01 | -72,7 | -1,39 | -24,00 | 0,524 | 2,26 | -5,4 | 4 |
| sg2 DF_02 | -70,3 | -1,06 | -24,03 | 0,524 | 2,44 | -3,5 | 3 |
| sg2 DF_03 | -68,2 | -2,37 | -24,00 | 0,513 | 1,08 | -2,1 | 4 |
| sg2 DF_04 | -73,9 | -3,38 | -24,00 | 0,563 | 0,93 | -7,1 | 3 |
| sg2 DF_05 | -74,5 | -3,48 | -24,00 | 0,551 | 2,24 | -7,2 | 4 |
| sg2 DF_06 | -73,2 | -4,97 | -24,00 | 0,562 | 2,93 | -7,7 | 3 |

Tüm dosyalarda kırpma 0 ve son örnek ≤ -60 dB. LUFS -24,03…-24,00, yani ar1/lmg1 aralığında (-24…-27).

Kapsam dışı bantlar:
- **2-8 kHz (bilgi):** -1,2…+1,9 dB.
- **≥ 10 kHz (rapor):** -1,2…+6,9 dB. Bu bantlar DF zincirinin 2,6 kHz alçak geçirenin altında; seviye çok düşük.

Erken/geç oran -7,7…-0,9 dB; gerçek 50 m DF dosyaları -3,6 / -4,6 dB. Bilgi amaçlı; kabul ölçütü değil.

## 3. Sınırlar
- 12 dosya 2 gerçek SPAS atışından türetildi: `independent_six_events=false`, `physical_distance_calibration_verified=false`.
- Kaynaklar aynı Sonniss GDC 2016 kaydı; yeni kaynak yok, lisans değişmedi. SPAS 1 m'de 45 örneklik hafif tepe kırpması var (52 µs).
- 03-sources notu güncellendi: `03-sources/NOTE-v003d-SPAS-1m-turevi.md` (v003e bölümü). LICENSES dosyalarına dokunulmadı.
- Ton eşleme EQ'su, ölçütü sağlamak için gerçek geç kuyruğa uydurulmuş bir düzeltme. Fiziksel değil; manifestte dosya bazında yazılı.
- Kurulum, ağ, Unity, Blender ve git kullanılmadı.
- **Açık:** S-08 oyun içinde dinlemeli ve uzak/yakın geçişini kontrol etmeli.
