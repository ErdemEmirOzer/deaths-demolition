# C00 — AP-027 v003g-recut: kuyruk mono uyumu — 05.10.2026

## ÖZET
Durum: **TESLİM — TailOpen / TailCorridor / TailHall, 10 silah, 180 dosya; 180/180 kabul. Unity içe alımı ve öznel dinleme YAPILMADI.**
- `exports/v003g-recut/`: v002 ile aynı ad ve klasör, 180 stereo WAV, 73.908.840 bayt.
  - `supersedes`: TailOpen için v002 (v003*'te TailOpen yoktu), Corridor/Hall için v003f-recut.
  - `MANIFEST.json` (65e3e012932e) + `SHA256SUMS` (fa74aae86f95) 181/181 OK.
- **Mono çentiği, (L+R)/2'nin stereo ortalamaya göre 1/3 oktav en kötü bandı:**

  | Katman | Önce: en derin / medyan | Sonra: en derin / medyan |
  |---|---:|---:|
  | Açık | -21,4 / -18,9 dB | **-4,8 / -2,6 dB** |
  | Koridor | -9,5 / -6,2 dB | **-1,2 / -0,8 dB** |
  | Salon | -9,6 / -8,0 dB | **-2,4 / -2,0 dB** |

  Hiçbir bantta -6 dB'den derin çentik yok. Bağımsız FFT kontrolünde en kötü bant -3,2 dB.
- **Geniş bant mono kaybı:** önce açık -5,2…-1,4, koridor -2,7…-2,3, salon -3,6…-3,1 dB. Sonra açık -2,5…-1,5, koridor -0,7…-0,6, salon -1,9…-1,7 dB (≤ 3 dB).
- **Etkin overlay, 1118 dosya:** son 5 ms > -40 dB kalan 0.
- Hash'ler değişmedi: v002 1273/1273 ve 1118/1118, v003-recut 231/231, v003b 188/188, v003c 11/11, v003d 7/7, v003e 13/13, v003f 121/121.

## 1. Teşhis
- v002'de bütün kuyrukların stereosu R = 0,94 × L'nin 37 örnek (0,77 ms) gecikmeli kopyası.
- Mono toplamda bu bir tarak filtre: çentikler 649, 1946, 3243… Hz'de. 1/3 oktavda 500, 630, 800, 2000 (ve kimi dosyada 3150) Hz bantları -6 dB'nin altına iniyor.
- Bu yüzden yalnız 400-900 Hz faz düzeltmesi yetmiyordu.
- v003f'nin gürültü uzatması zaten temizdi (en kötü -3,3 dB). Çentik, onun kayıtlı erken bölümündeydi.

## 2. Yöntem: mid/side
Araç: `04-tools/recut_end_decay_v003g.py` (f5244cc79ce1). Ölçüm tablosu: `validation/v003g-recut/mono-notch-before-after-v003g.csv`.
- **Kurulum:** L = M + S, R = M − S. Böylece (L+R)/2 = M tam olarak; hiçbir bantta faz çentiği oluşmaz. Bant başına mono/stereo = -10·log10(1 + |S/M|²).
- **M (mid):**
  - TailOpen: v002 L kanalı (mono kuyruğun kendisi).
  - Koridor/salon: v003f'nin kayıtlı erken bölümü + gürültü uzatması (aynı zincir).
- **S (side):**
  - Kayıtlı bölümlerde M'nin düz genlikli kısa allpass zinciri (1,0-3,7 ms, g 0,5) çıkışı.
  - Gürültü uzatmasında aynı zarfta bağımsız ikinci gürültü.
  - 150 Hz altı mono'da (HP150).
- **Seviye:** S/M enerji oranı 20 ms'lik zarfla zamana göre sabit tutuldu. Hedef L/R korelasyonu: açık 0,1, koridor 0,6, salon 0,2.
- **Tepe koruması:**
  - S ilk 40 ms'de raised-cosine ile devreye giriyor.
  - M'nin tepeye 6 dB yakın olduğu anlarda %30'a iniyor.
  - Böylece stereo tepe = M tepesi; TP sınırlı dosyalarda LUFS korunuyor.
- **Değişmeyenler:**
  - Mid'in ilk 80 ms'si önceki sürümle aynı (korelasyon ≥ 0,99); erken yansıma ofseti meta verisi açık 30 / koridor 11 / salon 21 ms.
  - Koridor/salonda RT_mid, S-08 metriği v003f sınıf hedeflerinde kalacak şekilde yeniden ayarlandı.
- **Yan etki:** L/R dengesi artık simetrik. v002'de R = 0,94 L idi (-0,5 dB).

## 3. Kabul (dosya başına, 180/180)
| Ölçüt | Açık (60) | Koridor (60) | Salon (60) |
|---|---|---|---|
| Mono 1/3 okt. bandı > -6 dB | ✔ | ✔ | ✔ |
| Geniş bant mono kaybı ≤ 3 dB | ✔ | ✔ | ✔ |
| Geç L/R korelasyonu (hedef) | 0,08-0,20 (0,1; v002 -0,28…0,43) | 0,59-0,62 (0,6) | 0,18-0,23 (0,2) |
| RT60 T20 mono (S-08) | 0,650-0,855 s | 1,17-1,33 s | 1,65-1,81 s |
| RT60 güç toplamı | — | aralıkta | aralıkta |
| LUFS (önceki ±0,5) | Δ -0,12…0,00 | Δ 0,00 | Δ 0,00 |
| Erken kısım / baştaki sessizlik ≤ 5 ms | ✔ | ✔ | ✔ |
| Son 5 ms / son örnek | ≤ -66,4 / ≤ -60 dB | ≤ -70,5 dB | ≤ -68,4 dB |
| TP / kırpma | ≤ -1,06 dBTP / 0 | ≤ -2,74 / 0 | ≤ -2,42 / 0 |
| Sıra açık < koridor < salon | ✔ | ✔ | ✔ |

Notlar:
- **TailOpen RT60:** yeni mono RT, mid'in (v002 L kanalı) RT'si; içerik değişmedi. En düşük değer **0,6504 s** (ar1 TailOpen_02), alt sınırda.
- **Eski ölçüm:** v002 stereo dosyalarda S-08 mono ölçümü 0,623 s'ye kadar iniyordu. Bu, tarak filtrenin ölçüme etkisiydi.

## 4. Açık
- S-08 oyunda, 3B pay 0,35/0,25 ile tekrar ölçmeli ve dinlemeli.
- Allpass yan kanalının geçicilerdeki etkisi ve simetrik L/R dengesi öznel olarak kontrol edilmeli.
- Kurulum, ağ, Unity, Blender ve git kullanılmadı.
