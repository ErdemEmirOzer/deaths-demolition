# C00 — AP-027 v003f-recut: TailCorridor / TailHall RT60 — 05.10.2026

## ÖZET
Durum: **TESLİM — 10 silah × (TailCorridor 6 + TailHall 6) = 120 dosya; 120/120 kabul. Unity içe alımı ve öznel dinleme YAPILMADI.**
- `exports/v003f-recut/`: v002 ile aynı ad ve klasör, `supersedes: v002`, 120 stereo WAV, 49.256.214 bayt.
  - `MANIFEST.json` (c4d3c8524da2) + `SHA256SUMS` (357c98b08acd) 121/121 OK.
- **RT60, S-08 yöntemiyle:**

  | Katman | v002 medyan | v003f medyan | v003f aralık | Hedef |
  |---|---:|---:|---:|---:|
  | Salon | 1,28 s | **1,76 s** | 1,65-1,81 | 1,6-1,9 |
  | Koridor | 1,06 s | **1,27 s** | 1,17-1,33 | 1,1-1,4 |

- **Etkin overlay (v002 → … → v003f), 1118 dosya:** son 5 ms > -40 dB kalan **0**.
- Hash'ler değişmedi: v002 1273/1273 ve 1118/1118, v003-recut 231/231, v003b 188/188, v003c 11/11, v003d 7/7, v003e 13/13.

## 1. Teşhis
- **Üretim:** v002'de kuyruklar IR konvolüsyonu değil, kayıt; `build_weapon_set_v002.tails_and_perspective` + `tune_tail`.
  - Koridor: SRC114 (uzun koridorda tek atış bırakışı). Salon: SRC115 (katakomb burst'ünün son atış bırakışı).
  - Zincir: bant geçiren → RT hedefi için üstel zarf `exp(-alpha·t)` (alpha ≥ -3/s) → 60 ms doğrusal fade → R = L, 37 örnek gecikmeli × 0,94 → -20 LUFS.
- **Sorunlar:**
  - Kayıtlı bırakışlar hızlı sönüyor (-40 dB ≈ 0,5 s). alpha ≈ -2/s ile 2,6 s'lik pencere sonuna doğru kaydın tabanı **~+46 dB yükseltilmiş**.
  - v002 T30 değerleri: koridor 5,0-6,6 s, salon 7,7-7,9 s. Geç bölüm düzlük; T20 düşük kalmış.
- **Ölçüm yöntemi:**
  - S-08'in sayıları L+R mono toplam, 4. derece 500-1000 Hz, Schroeder T20 ile birebir tekrar üretildi: v002 salon 1,278 / koridor 1,064 (S-08: 1,29 / 1,05).
  - Codex'in kanal ortalaması + 2. derece ölçümü (1,49 / 1,18) bunu göstermiyordu. 37 örnek kaydırma mono toplamda 649 Hz'de -24 dB tarak çentiği de yaratıyor.
  - Kabul, S-08 yöntemiyle ve ayrıca kanal güç toplamıyla yapıldı.

## 2. Yöntem
Araç: `04-tools/recut_end_decay_v003f.py` (73b4f16370fc). v002 kuyruk zinciri uzatma=0'da **120/120 bit-exact** (`validation/v003f-recut/chain-reproduction-v002-tails.json`).
- **Erken kısım:** v002 ile birebir aynı (koridor 0-0,15 s, salon 0-0,20 s; ilk 80 ms korelasyonu 1,0000). Erken yansıma yapısı korunuyor. Ön gecikme meta verisi koridor 11 ms / salon 21 ms; WAV'da sessizlik yok, baştaki sessizlik ≤ 5 ms.
- **Geç kuyruk:**
  - 60 ms eşit güçlü çapraz geçişle STFT'de rastgele fazlı, tohumlu gürültü.
  - Genlik, kaydın geçiş anındaki 1/3 oktav yumuşatılmış spektrumu; seviye eşlenmiş.
  - Frekans kutusu başına üstel sönüm, RT eğrisi: 63 Hz ×1,20 · 250 ×1,08 · 500-1k ×1,0 · 4k ×h · 8k ×0,8h. h sentezde 0,45 / 0,40; ölçülen 4k/1k oranı şartnameye (0,55 / 0,50) oturuyor.
  - v002 bant sınırları aynı.
- **Kayıt tabanı:** geçişten sonra kullanılmıyor. T30/T20 = 0,94-1,05, yani doğrusal sönüm; gürültü tabanı yükseltilmedi, tersine kaldırıldı.
- **Stereo:** geç kuyrukta L/R kısmi ilintisiz, şartname genişliğine göre (koridor ρ 0,6 ≈ genişlik 0,4; salon ρ 0,2 ≈ genişlik 0,8). Erken kısım v002 stereo yapısında.
- **Hedef RT:** orta bant RT'si, S-08 metriği sınıf hedefine eşit olana kadar ikiye bölmeyle ayarlandı. Sınıf hedefleri C-00 aralığında ve şartnamedeki sınıf sırasına uygun:
  - Koridor: AR 1,22 · SMG 1,17 · LMG 1,27 · SG 1,31 · SR 1,33 · RL 1,29 s.
  - Salon: AR 1,70 · SMG 1,65 · LMG 1,76 · SG 1,80 · SR 1,81 · RL 1,78 s.
  - Aynı sınıftaki silahlar (ar1/ar2, smg1/2, lmg1/2, sg1/2) v002'deki gibi aynı dosyayı paylaşıyor: 72 benzersiz dosya.
- **Bitiş ve seviye:** -60 dB'de doğal bitiş + 60 ms raised-cosine. -20 LUFS: dosya Fire'dan 2 LU aşağı; runtime ıslak kazancı -8 / -6 dB ile gövdeye göre **-10 / -8 dB** (şartname).

## 3. Kabul (dosya başına, 120/120)
Ölçüm tablosu: `validation/v003f-recut/tail-measurements-v003f.csv`; kabul: `acceptance-v003f-recut.json`.

| Ölçüt | Koridor (60) | Salon (60) |
|---|---|---|
| T20 RT60, mono (S-08) | 1,17-1,33 s ✔ | 1,65-1,81 s ✔ |
| T20 RT60, güç toplamı | 1,14-1,36 s ✔ | 1,63-1,88 s ✔ |
| EDC R² / T30/T20 | ≥ 0,994 / ≤ 1,05 ✔ | ≥ 0,992 / ≤ 1,05 ✔ |
| HF 4k/1k (şartname ±0,15) | 0,49-0,61 (0,55) ✔ | 0,50-0,59 (0,50) ✔ |
| Alçak 250/1k ≥ 0,9 | ✔ | ✔ |
| LUFS / gövdeye göre ıslak | -20,0 / -10 dB ✔ | -20,0 / -8 dB ✔ |
| Erken yansıma ofseti (11 / 21 ms meta, erken kısım = v002) | ✔ | ✔ |
| Son 5 ms / son örnek | ≤ -65,9 / ≤ -60 dB ✔ | ≤ -68,9 / ≤ -60 dB ✔ |
| TP / kırpma | ≤ -2,37 dBTP / 0 ✔ | ≤ -1,57 dBTP / 0 ✔ |
| Sıra açık < koridor < salon (silah × indeks) | 120/120 ✔ (medyan 0,815 < 1,27 < 1,76 s) | |

Süre: v002 2,6 s (yükseltilmiş taban dahil) → koridor 1,12-1,39 s, salon 1,49-1,74 s.

## 4. Notlar
- **Sentetik gürültü:** geç kuyruk uzatmasında C-00 izniyle kullanıldı; manifestte `synthetic_noise_used: true` ve dosya başına tohum yazılı. Yeni kaynak yok; LICENSES değişmedi.
- **Mono uyum:** erken kısımda v002'nin 37 örnek kaydırmalı stereo yapısı kaldığı için ilk 0,15-0,20 s'de mono toplamda 649 Hz tarak çentiği sürüyor. Geç kuyrukta yok. TailOpen'da da aynı yapı var. S-08 mono uyum testinde değerlendirmeli.
- **Açık:**
  - S-08 oyun içi dinleme ve yeniden ölçüm (aynı yöntem).
  - AudioReverbZone ile çift yankı kontrolü.
  - Uzayan kuyruk süresine göre ses (voice) bütçesi.
