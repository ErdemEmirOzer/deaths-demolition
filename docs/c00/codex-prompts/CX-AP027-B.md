# CX-AP027-B — Silah sesleri v4: dinleme kiti (G4) + kusur kapatma + 10 silah kaynak seti · Faz B

> Önce `K/c-00/codex/CODEX-SISTEM-R2.md` ve `K/c-00/codex/KADRO-CODEX-AJANLARI.md`'yi oku (K = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition`). Sondaki **HIZ MODU 1–7 bağlayıcı**: ayrı inceleme alt ajanı yok, kaliteyi lider ölçümle kontrol eder, teslimden önce tek mekanik `dd_validator`.
> Paket: **AP-027** · Alan: `K/asset-production/AP-027/` · Faz A raporun: `K/c-00/codex/reports/CX-AP027-A-RAPOR-05.10-1.md` · Tasarım belgelerin: `01-design/AP027-A-*.md` · Prototip: `exports/v001-prototip/` (92 WAV)
> Kaynak kararı K3 aynen: **Sonniss GDC** (gerekirse CC0 tamamlayıcı). Seed Audio / AI ses SFX için YASAK. Ham dosyalar `/Users/Erdem/DD-Sources/sonniss/` altında.

## Main'de şu an olanlar (S-08, 05.10)
- **B1 (af9b89f5):** hibrit kaldırıldı, her atış tam Fire. B kit ölçümü: AR1 basılı LF %22,3, SMG1 %9,8, LMG1 %29,0.
- **B2 (f5e0b13d):** kovan genişletmesi. sg_2 iki kabuk 100–130 ms ara, sert yüzeyde tek sekme, kuyruk 16.
- **B3 (fcaa9d29):** katman yolu hazır ve senin adlarınla eşlendi; sesler gelene kadar sessiz.
  - Mechanical her atış: üstten / TP / FP için −10 / −7 / −3 dB.
  - Silahın kendi mekân kuyruğu, erken yansıma ofsetiyle: açık 30, koridor 11, salon 21 ms.
  - Mesafe bantları: yakın < 12 m, orta < 35 m, uzak.
- **B4 (2963b1af + 554b4ef4):** oyun içi gerçek RPM kiti.
  - Kayıtlar: `K/wp-085/evidence/b4/kit/`: `Kit_*` (tam mix), `Stem_*` (yalnız silah bus'ı), `.markers.txt`, `.telemetry.tsv`.
  - Bugün A (mevcut oyun) ve B (eski sesler) var.
  - C (senin v4) oyuna S-08 B5 ile kit-only varyant olarak alınacak ve aynı oyun içi koşulla kaydedilecek.

## ⚠ ACİL (C-00 05.10 öğleden sonra): mevcut oyun içi kit kayıtları GEÇERSİZ
- `K/wp-085/evidence/b4/kit/` ve `b4/kit-r1-ab/` altındaki WAV'larda silah sesi yok: silah stem tepesi −76 dBFS, tam mix'te duyulan kalabalık ve ortam.
- Kök neden S-08 testinde: bus taban değerleri −80 dB'de dondu.
- Bu dosyalarla kit üretme, ölçüm yapma, kullanıcıya sunulacak çıktı hazırlama.
- S-08 düzeltip yeniden kaydedecek. Kayıt başına "silah sesi var" kapısı: atış penceresi tepesi, atış öncesinden ≥ 6 dB yüksek. Geçerli kayıtların yeri ve `HAZIR` notu sana C-00 üzerinden iletilecek.
- Birleştiricine de aynı kapıyı koy: silah stem tepesi ile atış öncesi arasındaki fark < 6 dB olan girdiyi REDDET ve raporla.
- O zamana kadar birleştiriciyi sentetik ya da kendi offline render'ınla test et. Kusur kapatma ve SMG1 / 7 silah kaynak işine devam et.

## Adımlar
1. **Dinleme kiti birleştirici (ÖNCE).** `04-tools/render_listening_kit.py`:
   - Girdi: S-08'in oyun içi `Kit_*` / `Stem_*` kayıtları (A, B ve gelince C) + markers/telemetry.
   - Çıktı:
     - perspektif başına (üstten, FP) A/B/C sırası;
     - loudness eşit ±0,5 LU;
     - 1 kHz / 200 ms bip ayraçlı;
     - aynı kalabalık ve kovan arka planı;
     - ayrıca etiketsiz "kör" sırada bir kopya.
   - C kayıtları gelmeden birleştiriciyi A/B ile çalışır hâle getir.
   - C gelince kit WAV'larını `exports/v002-kit/` altına üret. C-00 bunları kullanıcıya oynatılabilir sayfayla sunar (**G4**).
2. **Faz A açık kusurları:**
   - SG Mechanical04 arka trim 5,90 ms;
   - 4 kovan loudness dışı (AR1 Stone 01/02/05, Wood02);
   - Fire crest (AR 2/6, SG 5/6 hedef altı).
3. **SMG1 öncelikli kaynak:**
   - B1 ölçümünde düşük bant %9,8; hedef ≥ %20.
   - smg_1 tam katman seti: Fire / Body / Sub / MuzzleTransient / Mechanical + TailOpen + Distance + kovan.
4. **Kalan 7 silah:** ar_2, smg_2, sg_2, lmg_1, lmg_2, sr_1, rl_1.
   - Aynı katman şeması: koridor ve salon kuyruğu, TP perspektifi, mesafe.
   - Kalibre/yüzey kovanları. rl_1'de kovan yok.
   - Sıra: ar_2 → lmg_1 → sr_1 → smg_2 → lmg_2 → sg_2 → rl_1.
   - G4 kararı gelince yönü ona göre ayarla. Karar gelmeden kaynak toplama ve kesim serbest; final miks kazancını G4 sonrasına bırak.
5. **Kazanç önerisi (Unity sahibi S-08 karar verir):** Faz A raporundaki ortak FireTrim ön hesabını 10 silah için tabloya dök. Tepe payı ve dense-40 notlarıyla birlikte.

## Format ve kapılar (değişmedi)
- 48 kHz / 24-bit PCM WAV; tepe ≤ −1 dBTP.
- Fire −18 ±1,5 LUFS; kovan −24 ±1,5 LUFS.
- Açık kuyruk 0,8 s ±%20.
- Uç sessizlik ≤ 5 ms (eşik tanımını Faz A'daki gibi açık yaz).
- Basılı tutmada 400 Hz altı pay ≥ %20 (R2 S1); her otomatik silah için ölç.

## Teslimatlar
- `exports/v002-kit/` (G4 kiti).
- `exports/v002/` (10 silah seti; silah başına klasör).
- `manifest/AP027-manifest-v002.json` + `SHA256SUMS`; `03-sources/LICENSES.csv` + indirme makbuzları.
- Unity'ye alma yok; içe alım S-08'in (WP-085 B5+).

## Durma ve rapor
- Kit hazır olunca raporun dışında `exports/v002-kit/HAZIR.md` bırak ve set işine devam et.
- 10 silah seti ve kusurlar bitince raporu yaz ve DUR.
- Rapor: `K/c-00/codex/reports/CX-AP027-B-RAPOR-<gg.aa>-1.md`. Son mesaj tek satır: rapor yolu.

## 05.10 ek — R2 S4 kuyruk kapısı (S-08 ölçümü, bugünkü v3 kuyruklar KALIYOR)
- Ölçü: mekân kuyruğu sönüm süresi (EDC), ms.
- Eşikler: açık min ≥ 500 · koridor min ≥ 750 · salon min ≥ 1100 · mekân sırası ihlali 0 (açık < koridor < salon).
- Bugün v3: açık 531 ✓, koridor 722 ✗, salon 962–1750 (min ✗), sıra ihlali 2 ✗.
- Senin koridor ve salon kuyrukların bu eşikleri her silahta karşılamalı. Kendi Schroeder/EDC ölçümünü teslim tablosuna koy.

## 05.10 ek 2 — GEÇERLİ kit kayıtları HAZIR: `K/wp-085/evidence/b4/kit-v2/`
- 24 WAV + `HAZIR.md`. Silah her `Stem_*`'te −6…−12 dBFS.
- "Silah sesi var ≥ 6 dB" kapısını **yalnız `Stem_*` üzerinde** uygula. `Kit_*` tam mix'te kalabalık tepeleri tek atışa 1–6 dB yakın; orada kapı geçerli kaydı da reddeder.
- Geçerli S1 ölçümleri: A ar_1 0,184 / 0,162 · C 0,271 / 0,274.
- G4 kitini bu kayıtlarla üret → `exports/v002-kit/` + `HAZIR.md`. C-00 kullanıcıya oynatılabilir sayfayla sunacak.

## 05.10 ek 2 — GEÇERLİ kit kayıtları HAZIR: `K/wp-085/evidence/b4/kit-v2/`
- 24 WAV + `HAZIR.md`. Silah her `Stem_*`'te −6…−12 dBFS.
- "Silah sesi var ≥ 6 dB" kapısını **yalnız `Stem_*` üzerinde** uygula. `Kit_*` tam mix'te kalabalık tepeleri tek atışa 1–6 dB yakın; orada kapı geçerli kaydı da reddeder.
- Geçerli S1 ölçümleri: A ar_1 0,184 / 0,162 · C 0,271 / 0,274.
- G4 kitini bu kayıtlarla üret → `exports/v002-kit/` + `HAZIR.md`. C-00 kullanıcıya oynatılabilir sayfayla sunacak.
