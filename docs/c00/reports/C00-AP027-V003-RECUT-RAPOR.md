# C00 — AP-027 v003-recut: son-sönüm düzeltmesi — 05.10.2026

## ÖZET
Durum: **TEKNİK DÜZELTME TESLİM — 230/230 dosya ölçülerek kabul ölçütlerini geçti. Unity içe alımı, oyun içi kayıt ve öznel dinleme YAPILMADI.**
- S-08 kusuru yeniden üretildi. S-08 sayıları -30 dB eşiğinde birebir çıktı: Fire 19, DistanceFar 32, DistanceMid 14, Mechanical 6. Görevin -40 dB kabul eşiğinde kapsamdaki katmanlarda **230 dosya** kusurlu.
- `exports/v003-recut/`: 230 WAV, v002 ile aynı ad ve klasör yapısında (overlay). Yöntem dağılımı: **rerender 72** (tüm Link), **fade 158** (83'ü kayıtlı devam üzerinde, 75'i yerinde).
- Sonrası: son 5 ms ≤ -40 dB **230/230** (en kötü -42,6 dB), son örnek ≤ -60 dB 230/230, 48 kHz/24-bit PCM 230/230, TP ≤ -1 dBTP 230/230, Fire LUFS 28/28 ve kovan LUFS 73/73 tolerans içinde, tam ölçek örnek 0.
- v002 değişmedi: kanonik 1273/1273, export 1118/1118 SHA OK (iş öncesi ve sonrası).
- Doğal -60 dB sönüm Fire/kovan/uzak katmanlarda **0,53-1,96 s** sürüyor ya da hiç inmiyor (dış mekân kayıt tabanı -43…-59 dB). Bu yüzden 158 dosyada sonu 60-80 ms raised-cosine şekillendiriyor (aşağıda gerekçe).

## 1. Ölçüm (adım 1)
`04-tools/measure_end_decay_v003.py`, dosya başına şunları ölçer:
- son 5 ms tepesinin dosya tepesine göre dB değeri,
- 5 ms tepe zarfının (1 ms adım) tepeden sonra kalıcı olarak -60 dB altına indiği süre,
- son örnek seviyesi, LUFS (v002 ile aynı BS.1770 fonksiyonu) ve 4× dBTP.

v002 tablosu `validation/v003-recut/end-decay-v002.csv|json` içinde.

| Katman (v002) | Dosya | > -30 dB (S-08) | > -40 dB (kabul) | En kötü |
|---|---:|---:|---:|---:|
| Fire | 60 | 19 | **28** | -11,0 (lmg2_04) |
| DistanceFar | 60 | 32 | **33** | -10,7 (sg1_01) |
| DistanceMid | 60 | 14 | **18** | -23,1 |
| Mechanical | 60 | 6 | **6** | 0,0 (ar1_01, tepe seviyesinde biter) |
| Casing | 324 | 29 | **73** | -17,2 |
| Link | 72 | 24 | **72** | -22,9 |
| TailOpen/Corridor/Hall | 180 | 0 | 0 | ≤ -57,3 |

Kök neden: v002 zincirindeki sabit son fade'ler (12-35 ms doğrusal) sıfırla doldurulmuş (`resize`) dizinin sonuna uygulanıyor, sonra `trim` sıfırları kesiyor. Sonuç, kaynak penceresinin sonunda sert kesim.

## 2. Yöntem
`04-tools/recut_end_decay_v003.py` v002 betiklerini değiştirmez; bu betikler v002 SHA listesinde olduğu için onları içe aktarır. Tek açık parametre eklenir: `extra_s`, yani kaynak penceresi uzatması. v002 zincirindeki veri-bağımlı kararların hepsi reçetedeki değerlere dondurulur: low-band kazancı, crest/transient eşikleri, malzeme tepe eşlemesi ve shape_crest dalı.

- **Doğrulama:** `extra_s=0` ve v002 son-fade'leriyle zincir, zinciri olan **177/177 dosyada v002 PCM'yi bit-exact** üretir (`chain-reproduction-v002.json`). Yalnız bit-exact zincirler uzatıldı.
- **rerender (72):** zarf, katman tavanı içinde -60 dB altına iniyor. 60 ms raised-cosine yalnız -60 dB altındaki kuyruğa uygulandı. Link doğal sönümü 0,317 s'de -60 dB'ye iniyor.
- **fade / kayıtlı devam (83):** kaynak devam ediyor ama doğal -60 tavan içinde yok. v002 gövdesi korunur, raised-cosine kaydın gerçek devamı üzerinde uygulanır: 80 ms, Mechanical'da 60 ms. Kapsam: Casing 73, Mechanical 6, Fire 3 (smg2_04-06), DistanceFar 1 (lmg1_01).
- **fade / yerinde (75):** kaynak devam edemiyor. v002 PCM sonuna, sonu geçen en kısa 60-80 ms raised-cosine uygulandı. Kapsam: Fire 25, DistanceFar 32, DistanceMid 18.
- **Başın korunması:** yeniden render edilen başlar v002 ile yalnız skaler kazançla farklı (kalıntı ≤ -124 dB).
- **Seviye:** v002 reçetesindeki aynı `final_pcm` hedefi kullanıldı: Fire -18, Casing -24, Distance -24/-22, Link -30, Mechanical hedefsiz (yalnız TP tavanı).

| Katman | Düzeltilen | rerender | fade-devam | fade-yerinde | Süre değişimi (ms) | Sonra en kötü son-5 ms |
|---|---:|---:|---:|---:|---|---:|
| Fire | 28 | 0 | 3 | 25 | -4 … +28 | -47,1 |
| DistanceFar | 33 | 0 | 1 | 32 | -18 … +78 | -42,6 |
| DistanceMid | 18 | 0 | 0 | 18 | -3 … -2 | -43,1 |
| Mechanical | 6 | 0 | 6 | 0 | +59 (55 → 114 ms) | -47,5 |
| Casing | 73 | 0 | 73 | 0 | +65 … +78 | -59,5 |
| Link | 72 | 72 | 0 | 0 | +166 … +196 (→ 0,345 s) | -72,3 |

Fire ses formülü `ceil((D/0,97+0,05)/period)` hiçbir otomatik silahta artmadı (smg2 0,31 → 0,338 s; yine 4).

## 3. Doğal sönüme (-60 dB) neden ulaşılamadı
Kanıt dosyası `validation/v003-recut/natural-decay-study.json`; ölçüm aynı dondurulmuş zincirlerle, pencere 2 s'ye açılarak yapıldı.

- **Kovan:** -60 dB'ye yalnız 39/73'te 0,53-1,58 s'de iniyor, kalanında taban -48…-59 dB (karo üstünde yuvarlanma). Şartname yüzey toplam süresi 0,18-0,40 s. Tavan: yüzey üst sınırı + 80 ms.
- **Fire (smg1/smg2/ar1_06):** -40 dB'ye 0,53-0,83 s'de, -60 dB'ye 0,87-1,96 s'de iniyor ya da hiç inmiyor (orman ortamı tabanı ≈ -53 dB). Bu, kuru Fire içine dış mekân yankısını taşımak demek (şartname §3/§5 kuyruk ayrımı) ve ses bütçesini büyütür. **Muhafazakâr seçim:** Fire tavanı = max(v002 süresi, şartname kuru süre üst hedefi). Bu yüzden smg1_01-05 ve ar1_06 yerinde fade aldı, smg2 +28 ms.
- **Mechanical ar1:** on-gun kayıtta bir sonraki atışa kadar taban -21…-26 dB (namlu patlaması sönümü). -60 dB yok.
- **lmg1 DistanceFar_01:** vadi yankısı, taban -43 dB.
- **Kaynakla sınırlı (yerinde fade):**
  - sonraki algılanan atış: HK416 (ar1/lmg1 Fire), AK5 50 m burst (SRC116), SPAS uzak/yakın, MP5 75 m;
  - RL1'in sabit kodlu .309-.530 s penceresi (.589 s'de impact geçicisi);
  - LMG2 MG42 burst kaydı (devamında başka atışlar var, uzatılmadı);
  - DistanceMid ve vekil DistanceFar'ların ebeveyn Fire'ı kaynakla sınırlı;
  - ar1 Fire_01/03/04 ve sg1 Fire_01'de Faz A zinciri yeniden kurulmadı; bunlar zaten sonraki atışla sınırlı.

**Düzeltilemeyen dosya yok.** Kabulü geçmeyen dosya: 0.

## 4. Dikkat gerektiren noktalar (🔴 karar/dinleme)
1. **v002 segmentasyon sızıntısı (29 dosya):** son 25 ms içinde dosya tepesine 3 dB yakın bir geçici var, yani sonraki olayın atağı v002 penceresine girmiş.
   - Etkilenenler: ar1/lmg1 DistanceFar_02/03/04/06; sg1/sg2 DistanceFar_01-05 (SPAS uzak "olaylarının" bir kısmı yankı); lmg2 Fire_01/04, DistanceFar_04, DistanceMid_04; ar1 Mechanical_01-06; ar1 Casing StoneSlate_05.
   - Yerinde fade bu atağı bastırıyor. LUFS yeniden normalize edildiğinden kısa uzak dosyaların başı v002'ye göre +1…+5,6 dB yükseldi (en çok ar1/lmg1 DistanceFar_03).
   - Yalnız ar1/lmg1 DistanceFar_02'de 80 ms fade de eşiği geçemedi. Sızan atak öncesindeki vadiye kadar 17,4 ms kısaltıldı (92 → 74 ms).
   - Öneri: yeniden segmentasyon (Codex).
2. **ar1 Mechanical (6):** v002 kesiminden 4-7 ms sonra on-gun kayıtta tepe seviyesinde bir mekanik geçici var; büyük olasılıkla sürgü taşıyıcısı dönüşü. Devam üzerinde fade bunu içeri alıyor. Dosya 55 → 114 ms oldu, LUFS -54,2…-52,6 → -50,1…-48,3 (+3,6…+5,7 LU), baş aynı. Duyulur bir içerik değişikliği; S-08 dinlemeli. Kalıcı çözüm izole mekanizma kaydı (v002 notu da bunu söylüyor).
3. **LUFS toparlama (7 Fire):** RL1 ×6 ve SG2_02 v002'de tepe sınırlı, -19,4…-19,5 LUFS'taydı. Fade sonrası -19,5 altına düştüler. v002'nin kendi loudness-recovery kontrolcüsü (oran 3, 6 ms) en küçük eşikle uygulandı: tepe azaltımı 0,33 dB (RL1) ve 0,50 dB (SG2_02). Sonuç -19,23…-19,34 LUFS.
4. **Kapsam dışı ama aynı kusur v002'de duruyor (> -40 / > -30 dB):** FireTP 19/10, FireFP 19/10, Body 45/36, Sub 37/26, MuzzleTransient 49/36 (her biri 60 dosya). FireTP/FP kamera modlarında çalınıyorsa oyunda kesim duyulmaya devam eder. Aynı betik bu katmanlara genişletilebilir; karar C-00'da.
5. **İçerik tekrarı (bilgi):** v002'nin 1118 dosyasından 706'sı benzersiz. Link 6 benzersiz × 12; kovanlar ar1/ar2/lmg1/lmg2/sr1 arasında aynı reçeteden. v003-recut 230 dosyada 109 benzersiz SHA var.

## 5. Teslim ve kanıt
- Ses + manifest: `asset-production/AP-027/exports/v003-recut/`.
  - 230 WAV, 10.070.926 bayt.
  - `MANIFEST.json` (10d606fb2e26): dosya başına v002 kaynağı ve SHA, yöntem, önce/sonra son-5-ms, t60, süre, LUFS, TP, fade ayrıntısı, kabul.
  - `SHA256SUMS` (cb00469f6580): 231/231 OK.
- Ölçüm: `validation/v003-recut/` içinde:
  - `end-decay-v002.*`, `end-decay-v003-recut.*`,
  - `chain-reproduction-v002.json`, `natural-decay-study.json`,
  - `recut-log-v003.json`, `acceptance-v003-recut.json`.
- Araçlar (`04-tools/`):
  - `measure_end_decay_v003.py` (00c159580364),
  - `recut_end_decay_v003.py` (da398f31f18b; `--phase verify|study|probe|build`),
  - `package_v003_recut.py` (11a3148616c2).
- Yeniden üretim deterministik: iki build aynı WAV SHA'larını verdi.
- Bağımsız kontrol: ayrı WAV ayrıştırıcıyla 230/230 geçti, hepsi mono. Uzatılan bölgelerde tek-örnek tık yok.
- Lisans: yeni kaynak yok; segmentler v002 ile aynı Sonniss kayıtlarından (`03-sources/LICENSES-v002.csv`).
- Yapılmayanlar: kurulum, ağ, Unity, Blender, oyun deposunda git. dd_validator (Codex aracı) çalıştırılmadı. Öznel dinleme ve oyun içi dense/limiter doğrulaması S-08'e aittir.
