# C00 — AP-027 v003b-recut — 05.10.2026

## ÖZET
Durum: **TEKNİK TUR TESLİM — 187 dosya; 180/187 tüm ölçütleri geçti, 7 dosya karar bekliyor (§4). Unity içe alımı ve öznel dinleme YAPILMADI.**
- `exports/v003b-recut/`: yalnız v003-recut'tan farklı ya da yeni dosyalar. 169 tanesi v002'yi, 18 tanesi v003-recut'ı geçersiz kılıyor (`supersedes`). Ad ve klasör v002 ile aynı; `MANIFEST.json` + `SHA256SUMS` 188/188 OK.
- **Etkin overlay (v002 → v003-recut → v003b), 1118 dosya:** son 5 ms > -40 dB kalan **0** (719 v002 + 212 v003-recut + 187 v003b).
- Uzatma=0 / özgün pencere bit-exact kontrolü yeni zincirlerde **181/181**: fire_new stemleri 101, Faz A ar1/sg1 stemleri 30, FireTP/FP/Distance türetmeleri 42, SRC116 8.
- Hash'ler değişmedi: v002 1273/1273 ve 1118/1118, v003-recut 231/231.
- Bağımsız WAV ayrıştırıcıyla 187/187 geçti: son 5 ms ≤ -40, son örnek ≤ -60 dB, 48 kHz/24-bit PCM mono, TP ≤ -1 dBTP. Tam ölçek örnek 0.

## 1. Katman başına sonuç
| Katman | Dosya | Kabul | Yöntem | En kötü son 5 ms | Süre (s) | LUFS / seviye |
|---|---:|---:|---|---:|---|---|
| Mechanical (ar1) | 6 | 6 | yerinde | -40,5 | 0,049-0,055 | v002 ±0,00 LU |
| Fire (lmg2 01/04) | 2 | 1 | yeniden segmentasyon | -53,6 | 0,307 / 0,199 | 01: **-21,54** (tepe sınırlı), 04: -18,0 |
| DistanceFar | 9 | 5 | yeniden segm. 8 + türetme 1 | -46,9 | 0,067-0,198 | -25,45…-24,0 |
| DistanceMid (lmg2 04) | 1 | 1 | türetme | -47,0 | 0,198 | -22,0 |
| FireTP | 19 | 18 | düzeltilmiş ebeveynden türetme | -50,2 | 0,199-0,404 | -22,58…-18,0 |
| FireFP | 19 | 18 | düzeltilmiş ebeveynden türetme | -50,1 | 0,199-0,404 | -22,37…-18,0 |
| Body | 45 | 45 | devam 26, yerinde 17, segm. 2 | -48,2 | 0,199-0,559 | baş v002 ile 0,00 dB |
| Sub | 37 | 37 | devam 16, yerinde 19, segm. 2 | -42,3 | 0,198-0,558 | baş v002 ile 0,00 dB |
| MuzzleTransient | 49 | 49 | devam 21, yerinde 26, segm. 2 | -43,6 | 0,077-0,558 | baş v002 ile 0,00 dB |

TP en yüksek -1,06 dBTP. Notlar:
- **Devam fade'i:** 80 ms raised-cosine, ham kaydın gerçek devamı üzerinde. "Devam var" kararı ham pencerenin gerçekten uzamasına göre veriliyor; `resize` sıfır dolgusu sayılmıyor.
- **Stem seviyesi:** şartname F4'e göre stemlerin kendi LUFS hedefi yok ("Fire'a göre trim"). Bu yüzden yeniden normalize etmek yerine baş kazancı v002'ye eşlendi (-4,8…0 dB düzeltme). LUFS değişimi (-4,8…+2,1 LU) yalnız süre ve blok kapılaması etkisi; bilgi amaçlı manifestte duruyor.
- **FireTP/FP:** v002 türetmesiyle (band + 18 ms fade) v003-recut/v003b Fire'dan yeniden türetildi; ek fade gerekmedi.
- **LUFS toparlaması:** v003b'de uygulanmadı. Kabul edilen 7 Fire tepe azaltımı v003-recut'ta aynen duruyor.

## 2. C-00 maddeleri
1. **ar1 Mechanical:** v002 gövdesi üzerinde yerinde raised-cosine, 65-80 ms. Dosya 55 ms olduğu için eğrinin son kısmı dosyaya düşüyor. LUFS v002'ye geri getirildi (0,00 LU), son 5 ms ≤ -40,5.
   - 01/03/04/06 için 53,5-55 ms yeterli. 02 ve 05'te eşik ancak sondan 5,5/6 ms kısaltmayla (49-49,5 ms) sağlanıyor.
   - **Bedel:** v002'nin en yüksek bölümü dosya sonu. Fade onu bastırdığı için LUFS'u korumak üzere erken kısım +7,6…+12,7 dB yükseldi. Denge değişti; dinlenmeli.
2. **Sızıntılı 29 dosya:**
   - **18'i işlendi:**
     - Mechanical 6 (madde 1).
     - SRC116 uzak AK5 8 dosya. v002 başlangıçları gerçek atağın ~20-25 ms gerisindeydi ve dosyalar "k. atışın kuyruğu + (k+1). atışın atağı" içeriyordu. Yeni pencere gerçek atakla başlıyor ve sonraki atış atağından ≥3 ms önceki vadide bitiyor (66-94 ms, tam tek atış). DF_02 kısaltma gerektirmedi; baş -6,3 dB.
     - lmg2 Fire_01: tek atış, burst öncesi 0,900 s vadide (-55,9 dB) bitiyor.
     - lmg2 Fire_04: 1,742 s'deki güçlü sızıntıdan önce bitiyor.
     - lmg2 DistanceFar_04 ve DistanceMid_04 yeni Fire_04'ten türetildi (baş +0,8 dB). Aynı varyantın stemleri aynı pencereyi aldı.
   - **11'i işlenmedi; v003-recut duruyor:**
     - sg1/sg2 DistanceFar_01-05 (10): SPAS 50 m kaynağı **kırpılmış**. 10.470 tam ölçek örnek var ve 0,12-0,95 s boyunca 10 ms blokları 0…+1 dBFS'te. Atak/vadi yapısı olmadığından yeniden segmentasyon anlamsız.
     - ar1 Casing StoneSlate_05 (1): son 25 ms'deki geçici aynı düşüşün sekmesi; sonraki kaynak olayı +3,7 s uzakta.
3. **Kapsam genişletme:** FireTP 19, FireFP 19, Body 45, Sub 37, Muzzle 49, toplam 169 dosya; ayrıntı §1. RL1 stemleri sabit kodlu pencere nedeniyle yerinde fade aldı.
4. **7 Fire tepe azaltımı:** dokunulmadı.
5. **Teslim kümesi:** 187 dosya, v003-recut ile özdeş dosya yok. `supersedes` ve geçersiz kılınan dosyanın SHA'sı manifestte.

## 3. Yöntem ve kanıt
- **Araç:** `04-tools/recut_end_decay_v003b.py` (a5207f936759). v003 araçlarını içe aktarır; `--phase verify|build|package` aşamaları var. v002/v003 betikleri değiştirilmedi.
- **Ölçüm ve kanıt:** `validation/v003b-recut/` içinde:
  - `chain-reproduction-v002-v003b.json` (181/181 bit-exact),
  - `build-log-v003b.json` (pencereler, vadiler, baş yükselmesi, çakışmalar),
  - `end-decay-v003b-recut.*`, `acceptance-v003b-recut.json`,
  - `effective-overlay-end-decay.csv` (1118 dosya).
- **Teslim:** `exports/v003b-recut/` → 187 WAV, 8.193.266 bayt, `MANIFEST.json` (a9101f7d8279), `SHA256SUMS` (50dedaa1e5a8).
- **Lisans:** yeni kaynak yok. Kurulum, ağ, Unity, Blender ve git kullanılmadı.

## 4. Açık kalanlar (🔴 karar)
1. **ar1/lmg1 DistanceFar_04 ve _06 (4 dosya): baş ≤ +1 dB ve LUFS ±1,5 birlikte sağlanamıyor.**
   - Yeni pencere 66-78 ms'lik tek burst atışı; v002 penceresi iki atak içeriyordu.
   - Teslim edilen: LUFS -25,45 (tolerans kenarı), baş +1,87 / +2,05 dB.
   - Alternatif: baş +1 dB'de tutulursa LUFS -26,32 / -26,50 olur.
   - Öneri: teslim edilen durumu kabul etmek ya da burst sonu atışlarıyla kaynak seçimini yenilemek.
2. **lmg2 Fire_01, FireTP_01, FireFP_01: LUFS -21,54 / -22,58 / -22,37.**
   - Tek MG42 atışı tepe sınırlı; tepe v002 ile aynı (-1,06 dBTP).
   - -19,4'e çıkmak için 6,8-7,3 dB atak azaltımı gerekiyor; C-00'ın kabul ettiği 0,5 dB'nin çok üstünde, uygulanmadı. Spec satır 164'e göre sapma manifestte yazılı.
   - Seçenekler: (A) kabul, (B) 7 dB atak azaltımı (önerilmez), (C) v003-recut sürümüne dönüş (burst başlangıcını içerir).
3. **sg1/sg2 DistanceFar (10): kırpılmış kaynak.**
   - Öneri: SG uzak katmanını diğer silahlardaki gibi SPAS 1 m yakın Fire'dan mesafe vekili olarak türetmek ya da başka bir uzak av tüfeği kaydı kullanmak. Kaynak kararı gerekiyor.
4. **Bilgi notları:**
   - SRC116 DistanceFar_05 (ar1/lmg1) sızıntı listesinde değil ama aynı geç-başlangıç segmentasyonuna sahip.
   - lmg2 Fire_04 v002 varyantı içinde -9/-10 dB'lik zayıf burst atışlarını taşımaya devam ediyor (burst parçası kimliği).
   - ar1 Mechanical'ın dengesi değişti (madde 2.1). S-08 dinlemeli.
