# C00 — AP-027 v003c-recut — 05.10.2026

## ÖZET
Durum: **TEKNİK TUR TESLİM — 10/10 dosya ölçütleri geçti. SG uzak kaynağı bulunamadı; v003-recut kalıyor. Unity içe alımı ve öznel dinleme YAPILMADI.**
- `exports/v003c-recut/`: yalnız v003b'den değişen 10 dosya (`supersedes: v003b-recut`). İçerik: ar1 Mechanical 6, ar1/lmg1 DistanceFar_04/06 4. MANIFEST + SHA256SUMS 11/11 OK; 87.710 bayt.
- **Etkin overlay (v002 → v003-recut → v003b → v003c), 1118 dosya:** son 5 ms > -40 dB kalan **0** (719 v002 + 212 v003-recut + 177 v003b + 10 v003c).
- Hash'ler değişmedi: v002 1273/1273 ve 1118/1118, v003-recut 231/231, v003b 188/188. SRC116 zinciri uzatma=0 ile hâlâ bit-exact (4/4).
- Bağımsız WAV ayrıştırıcıyla 10/10 geçti: son 5 ms ≤ -40, son örnek ≤ -60 dB, 48 kHz/24-bit PCM mono, TP ≤ -1 dBTP. v003b ile özdeş dosya yok.

## 1. C-00 maddeleri
1. **ar1 Mechanical 01-06:** v003b ile aynı yerinde raised-cosine (65-80 ms) ve aynı kısaltmalar (01: 1,5 ms; 02: 5,5 ms; 05: 6 ms). Yeniden normalize edilmedi.
   - Dosya, v002 × fade eğrisinin birebir aynısı (sapma 0 LSB); skaler kazanç 0,00 dB.
   - Fade eğrisi dosyadan uzun (60-80 ms > 55 ms) olduğu için eğrinin ilk örnekteki kazancı -0,7…-3,4 dB. Bu skaler bir kazanç değil, fade'in kendisi.
   - Bilgi: LUFS v002'ye göre -7,6…-12,7 LU düştü (-61,8…-66,7). Bu değerler `lufs_change_vs_v002_info` alanında. TP -35,8…-42,7 dBTP; son 5 ms -40,5…-41,0; süre 49,0-54,9 ms.
2. **ar1/lmg1 DistanceFar_04 ve _06:** baş tam +1,0 dB.
   - LUFS -26,32 (04) ve -26,50 (06); manifestte `lufs_tolerance_exception` (hedef -24 ±1,5; sapma -2,32 / -2,50 LU).
   - Son 5 ms -53,1 / -49,9; süre 0,078 / 0,067 s.
3. **lmg2 Fire/FireTP/FireFP_01:** dokunulmadı, v003b sürümü geçerli. Manifestte `untouched_by_decision` olarak kayıtlı.
4. **SG uzak katmanı:** `/Users/Erdem/DD-Sources/sonniss/` altındaki 47 WAV tarandı. Kırpılmamış, 30-80 m uzaklıkta pompalı/av tüfeği kaydı **yok**:
   - SPAS-12 50 m (mevcut kaynak): kırpılmış; 10.500 örnek ≥ 0,999 FS, 0,12-0,95 s'nin %13,2'si.
   - Mossberg 12-Gauge, kapalı ofis koridoru, 20 m bitişik oda: kırpılmamış (tepe -1,00 dBFS) ama kapalı alan ve 30 m'nin altında.
   - Diğer aday kayıtlar yakın mesafeli: SPAS-12 3 m ve 1 m, Remington depo 10 m, TS Sound yakın.
   - Ağdan indirme bu görevde yasak. Sonuç: sg1/sg2 DistanceFar_01-05 v003-recut'ta kalıyor; LICENSES dosyalarına satır eklenmedi. Arama kaydı `MANIFEST.json → sg_far_source_search`.
5. **Hash'ler:** yukarıda; hiçbiri değişmedi.

## 2. Kanıt
- **Araç:** `04-tools/recut_end_decay_v003c.py` (b221ca929542). v003b araçlarını içe aktarır; `--phase build|package`.
- **Ölçüm:** `validation/v003c-recut/` içinde:
  - `chain-reproduction-v002-v003c.json`,
  - `build-log-v003c.json`,
  - `end-decay-v003c-recut.*`,
  - `acceptance-v003c-recut.json`,
  - `effective-overlay-end-decay.csv`.
- **Teslim:** `exports/v003c-recut/MANIFEST.json` (f6b8141067ea), `SHA256SUMS` (2ba1f9a2f3a6).
- Kurulum, ağ, Unity, Blender ve git kullanılmadı.

## 3. Açık kalanlar
1. **sg1/sg2 DistanceFar_01-05:** kırpılmış kaynak (v003-recut). Uygun kayıt için C-00 kaynak edinimi gerekiyor. Alternatif: SPAS 1 m yakın Fire'dan mesafe vekili. DistanceFar_06 da aynı kırpık kaynaktan geliyor (son-sönüm ölçütünü geçtiği için kapsam dışıydı).
2. **ar1 Mechanical:** seviye v002 ile aynı, ancak fade dosyanın tamamını kapsıyor. Mekanik katman algısal olarak zayıfladı (LUFS -8…-13 LU). S-08 dinlemeli.
3. **lmg2 _01 üçlüsü:** C-00 kararıyla oyunda havuzdan çıkarılacak; dosyalar değişmedi.
