# C00-AP026C — AP-026-B animasyon paketinin devralınıp tamamlanması

**Tarih:** 05.10.2026 · **Ajan:** C-00 character-animator (Claude, Opus) · **Görev:** AP-026C (Codex CX-AP026-B kapanışı KISMİ kalmıştı)
**Paket:** `K/asset-production/AP-026/` · **Manifest:** `manifest/AP026-manifest-v002.json` (SHA-256 `c2ff2bf4…d244ded9`) · **SHA listesi:** `exports/v002/SHA256SUMS` (355 kayıt, `shasum -c` 355/355 OK)

## 1. Sonuç

**Durum: Blender kaynak kapıları geçti (BLENDER_SOURCE_GATES_PASS). Unity alımı ve runtime ölçülmedi.**

- 142 LocomotionV4 geçişi 142/142 PASS: start 64/64, stop 64/64, pivot + turn 14/14.
- Codex'in üretemediği 23 start'ı ve tek L3 FAIL'i (`sprint_start_fwd_l`) ürettim. 24/24 klip üç bağımsız ölçümden geçti:
  - builder metrikleri;
  - NumPy ile gerçek FBX denetimi;
  - Blender geri içe alma.
- Mekanik doğrulayıcı 21/21 kontrol PASS: `validation/ap026c-validator-v002.json`.
- Paket **Unity/AAA kabulüne hazır değildir**. Ölçülmeyen kalemler §6'da.

## 2. Codex'in tamamladıkları (doğrulandı)

**Codex notu doğrulandı.** "64 start'tan 41 FBX üretildi; 40 tüm kapılarda PASS, 1 L3 FAIL; 23 üretilmedi" ifadesi doğru. Bağımsız kontrolüm (`04-tools/ap026c_gate_check.py`) ve Codex'in `fbx-offline-audit.json`'ı aynı sonucu veriyor.

- FAIL: `AP026_tp_sprint_start_fwd_l`, RightLowerLeg 22,95°/kare (son kare).
- Durma nedeni bellek çökmesi değil. İkinci 23'lük parti swap kapısında takıldı (12,96 GiB > 12); Blender hiç başlamadı.

Diğer Codex teslimleri. Ölçüler Codex'in kanıtından; kapıları ben yeniden koşturdum.

| Kapsam | Hedef | Codex sonucu | Ölçü |
|---|---|---|---|
| Rig şablonu | 77 kemik T-bind, FBX, Avatar eşlemesi | HAZIR (kaynak) | FBX roundtrip 0,0007 mm / 0,0012°; 53 Humanoid slotu |
| Stop (64–127) | 64 | 64/64 PASS | L3 ≤ 19,97°, L1t/L1 declared/sole/Hips kapıları PASS |
| Pivot / turn (128–141) | 14 | 14/14 PASS | Durağan taban adımı ≤ 0,0007 mm |
| Start (0–63) | 64 | 41 PASS (40 yeni + 1 eski), 23 FAIL | 22 eski kaynak zeminin altında (sole −2,7…−38,7 mm), 1 L3 |
| Reload FP + TP | 12 silah | 25 + 25 = 50 | Olay sözleşmesi PASS (aşağıda); SG1 shell loop 0,0004 mm / 0,06° |
| Mixamo loop + idle | 35 | 35/35 PASS | L1 avg ≤ 0,034, p95 ≤ 0,090, L2 ≤ 0,022; run_back 4,03 m/s (playback 1,029) |
| Zombi tier yürüyüşleri | 4 tier × 3 varyant | 12/12 PASS | L1 avg ≤ 0,019, p95 ≤ 0,081, L2 ≤ 0,008, loop 0 mm / 0,056°, sole ≥ −4,996 mm, 73 kemik |
| Executioner kadansı | Ölçek 1,48 | ✓ | 1,48 ölçek; 81,8 adım/dk; 1,70 m/s |
| L5 senkron çift payı | ≤ 0,10 | ✓ | En kötü 0,0974 (10 senaryo, 40/128 aktör; authoring simülasyonu) |

**Reload olay sözleşmesi:**
- `mag_hand_attach` id 79.
- sw_arc / sw_gel: detach → attach ≥ 6 kare (11/13 ve 10/12). sw_arc'ta bolt_release cancel'dan önce. sw_gel empty'de bolt_release var.
- `_tac` kliplerde bolt yok.
- SG1 shell: tek `shell_in`, `mag_insert` yok.
- rl_1 sırası: attach → rocket_in → rocket_seat.
- Bütün olay adları main'deki `CharacterAnimationEvents` alıcısında var (28/28).

**Main sözleşmeleri.** Main `29725393`'teki dosyalar Codex'in son yeniden denetimiyle (`contracts-final-reaudit-v002.json`) aynı SHA'da; o tarihten sonra değişmediler:
- LocomotionV4;
- CharacterAnimationEvents (BeatFamily);
- TwistCorrector;
- WeaponMagazineOwnership / WeaponMagazineVisual.

142 klip adı main formülüyle (`ClipName`) ve sözleşme CSV'siyle birebir eşleşiyor. Eşleşmeyen yok.

**Codex'te eksik kalanlar:**
- Codex'in 18:00 birleştirmesi 41 adayı (FAIL dahil) `exports/v002/hero/` içine koymuş, 23 eski start kalmış.
- Kök README'nin andığı `exports/v002/AP026-quality-v002.csv`, `validation/leader-check-v002.json`, `manifest/AP026-manifest-v002.json` ve `exports/v002/SHA256SUMS` hiç üretilmemiş.
- CX-AP026-B raporu yazılmamış.

## 3. Benim tamamladıklarım

### 3.1 23 eksik start + L3 düzeltmesi

**Yöntem:**
- Codex'in builder'ını aynen kullandım. Yalnız yolları değiştirdim (`04-tools/hero_start_candidates_v002c.py`): Codex aday klasörü ve blend'i salt okunur girdi, çıktılar `validation/hero-start-candidates-v002c/` altında.
- 5 parti koştu (6/6/6/5/1 klip). Her parti ayrı `slot.sh acquire blender ap026c` → disk/swap kapısı → `Blender -b -t 4` → release (`04-tools/ap026c_blender_run.sh`, `ap026c_start_batches.sh`). Kapı ölçümleri: disk 69–75 GiB, swap 8,9–11,5 GiB.
- Batch E (sprint_start_fwd_l) bir kez kuyruktayken durdurulup ızgara sırası değiştirilerek yeniden kuyruğa alındı. Blender o sırada başlamamıştı.

**L3 düzeltmesi (`sprint_start_fwd_l`):**
- Neden: klip tam sprint hızında sağ ayağın erken basış karesinde bitiyordu. Basan baldır doğal olarak ~23°/kare döner ve swing sınırlayıcısı basan bacağa uygulanmaz.
- Çözüm: süre/faz taraması, N = 24, faz seçimi 1. Klip artık uçuş fazında bitiyor (`source_phase_end` 23,0); loop basışı kendisi devralıyor.
- Sonuç: uzuv 19,40°, gövde 9,05°/kare. Motor terminal hızı (6,2 m/s) değişmedi.

**24 klibin ölçüleri:**
- Tamamı: `exports/v002/hero-v002c/hero-quality-metrics-v002c.csv`.
- En kötü değerler:
  - L3 uzuv 19,95°/kare (`sprint_start_left_r`, N = 31), gövde 9,05°/kare;
  - L1t 3,6e-5;
  - declared playback L1 avg 1,6e-5, p95 7,7e-5;
  - sole min +0,02 mm (önceki 22 eski kaynakta −2,7…−38,7 mm);
  - Hips min 0,668 m, dikey adım ≤ 0,033 m;
  - inverse-TRS ihlali 0.

**Bağımsız doğrulama:**
- NumPy FBX denetimi 24/24 PASS (`validation/hero-start-candidates-v002c/fbx-offline-audit.json`).
- Blender geri içe alma 24/24 PASS (`validation/hero-start-merge-audit-v002c.json`): 77 kemik, 0 mesh, FBX 7400, kare sayısı tutuyor, rest ≤ 0,0007 mm, eklem paritesi ≤ 0,0024 mm.
- 142 kanonik FBX'in hepsi, SHA'sı eşleşen bir FBX düzeyi denetimle kapsanıyor (118 Codex + 24 benim).

### 3.2 Kanonik 142 birleştirme

Codex dosyalarının üzerine yazmamak için birleştirme bir ek katman (`exports/v002/hero-v002c/`) olarak yapıldı.
- 24 FBX buraya kondu. `exports/v002/hero/` içindeki aynı adlı 24 dosya manifestte `superseded_by_hero-v002c` olarak işaretli.
- `hero-canonical-index-v002c.json`: 192 klibin tek geçerli yolu ve SHA'sı.
- `anim-events-transition-v002c.json`: 142 klip. Olaylar her klipte zamana göre sıralı (`time_s = frame/30`); örnek sayısı ve `virtual_motor` uzunluğu eşleşiyor.
- `entry-blend-contract-v002c.json` ve birleşik metrikler: `validation/hero-transition-metrics-v002c.json`, `hero-summary-v002c.json`, `ap026c-gate-142-v002c.json`.
- Yeni kanonik Blender kitaplığı: `05-blender/hero-transition-final-v002c.blend` (142 action). Codex'in `hero-transition-final-v002.blend` dosyası değişmedi; SHA kontrolü yapıldı.

### 3.3 Zombi

Eksik yok, yeni üretim gerekmedi. AP-022 v008 temelli 73 kemikli 12 klip; Executioner 1,48; L5 0,0974. SHA'lar kontrol edildi.

### 3.4 Kalite tablosu, doğrulayıcı, manifest

- `exports/v002/AP026-quality-v002.csv`: 241 satır, 0 FAIL. Codex'in `package_quality` mantığı birleşik metriklerle (`04-tools/ap026c_package_quality_v002c.py`).
- `validation/ap026c-validator-v002.json`: 21 kontrol PASS. İçerik:
  - FBX 7400 (264 dosya);
  - main adları;
  - indeks SHA;
  - 142 kapı;
  - olay sıralaması;
  - reload sözleşmesi;
  - zombi/L5/1,48;
  - Mixamo;
  - rig;
  - Codex dosyalarının değişmediği.
- `manifest/AP026-manifest-v002.json`: 295 dosya, durum ve alım kuralı. `exports/v002/SHA256SUMS`: 355 kayıt (teslim, manifest, v002c kanıtı, araçlar).
- Bu oturumda yazılan dosyaların listesi: `validation/ap026c-outputs.json`. 18:05 sonrası değişen başka dosya yok.

## 4. Envanter: kapsam ve kalan eksik

| Kalem | Hedef | Durum |
|---|---|---|
| Start 0–63 | 64 | ✅ 64/64 PASS (40 Codex + 24 AP-026C) |
| Stop 64–127 | 64 | ✅ 64/64 |
| Pivot 128–139 / turn 140–141 | 12 + 2 | ✅ 14/14 |
| FP + TP reload, 12 silah | 25 + 25 | ✅ Olay sözleşmesi PASS; L3 reload için kapı değil (uzuv tepe 13,6–39,9°/kare, yalnız bilgi) |
| Zombi tier yürüyüşleri | 4 × 3 | ✅ |
| Executioner 1,48 | — | ✅ |
| L5 ≤ 0,10 | — | ✅ 0,0974 (simülasyon) |
| Manifest + SHA256SUMS | — | ✅ |

## 5. Unity alım notu (S-06)

1. **Kahraman FBX listesi.** Yalnız `exports/v002/hero-v002c/hero-canonical-index-v002c.json` içindeki `path` alanlarını alın: 142 geçiş + 50 reload.
   - `exports/v002/hero/` içindeki 24 eski start'ı almayın.
   - Olaylar: `hero-v002c/anim-events-transition-v002c.json` ve `hero/anim-events-reload.json`. Frame 0 tabanlı, `time_s = frame/30`.
2. **Eksen ve ölçek.**
   - Kaynak Z-up / −Y ileri / metre. FBX `axis_forward=-Z`, `axis_up=Y`, ölçek 1, 30 fps, iskelet-yalnız, 77 kemik (zombi 73).
   - Hips 0,93 m, Head ucu 1,80 m.
   - Ek dönüş veya ölçek eklemeyin. Avatar'ı Create From This Model ile kurup `isValid` / `isHuman` doğrulayın.
3. **Giriş harmanı.**
   - Start'lar ve ön-düzeltmeli stop'lar yalnız bildirilen 0,3 s smoothstep giriş harmanıyla (f0–f9, `u*u*(3-2*u)`) kabul edildi.
   - Start girişi `AP026_tp_idle_03`, faz 0. Stop girişi `entry-blend-contract-v002c.json` içindeki faz ve hız.
   - Solo oynatım desteklenmez. Farklı harman süresi veya eğrisi kullanılacaksa yeniden ölçün.
4. **Süreler.** Sabit değil: `sprint_start_fwd_l` 24 kare, `sprint_start_left_r` 31 kare, bazı sprint/run start'lar 31/37 kare. `samples` ve `virtual_motor` alanlarını kullanın.
5. **Kök hareketi.** Klipler in-place. Motor tek translasyon sahibidir. `source_phase_end`, loop'un devralma fazıdır.
6. **IK ve ikincil hareket.**
   - Reload penceresinde LH IK ağırlığı JSON'daki `lh_ik_curve` örnekleriyle 1→0→1 olmalı.
   - `lh_release` = ağırlığın 0'a ulaştığı kare; `lh_grab` = 0'dan yükselişin başladığı kare.
   - Twist sahibi TwistCorrector. Aynı kemiğe baked twist ile birlikte yazılmamalı.
7. **Zombi.** Klipler eski 73 kemikli AP007 profilindedir. AP-025 O1 gövdesine (77 kemik şablon) retarget ve Avatar ayrı iştir. Faz planı: `zombie/zombie_crowd_phase_plan_v002.json`.

## 6. Açıklar ve ölçülmeyenler

- **Unity tarafı (S-06):** Avatar valid/isHuman, sıkıştırma, OnAnimatorIK + WeaponGripIk etkileşimi, runtime L1/L1t/L3 ve üç oyun kamerası ölçülmedi.
- **Görsel kabul:** son mesh deformasyonu, silah/şarjör mesh teması, ayakkabı zemin teması ve AAA görsel kabul ölçülmedi. İskelet tanı görselleri bunların kanıtı değildir.
- **L5:** authoring simülasyonudur; Unity sürü ölçümü yapılmadı.
- **Zombi retargeti:** 73 → AP-025 77 kemik profiline retarget yapılmadı.
- **Kök README:** `AP-026/README.md` hâlâ Codex'in "KISMİ" metnini ve üretilmemiş `leader-check-v002.json` referansını taşıyor. Kurala göre üzerine yazmadım. Güncel durum bu raporda ve manifestte. C-00 isterse README'yi güncelleyebilir.
- **Mixamo M02:** kök hareketli ikinci sürüm hâlâ yok. Bu teslim için gerekmedi; ileride native hız ölçümü gerekirse C-00 indirmeli.
- **Codex raporu:** `CX-AP026-B-RAPOR` yazılmamıştı. Bu rapor onun yerine geçer.
- **Kod deposu:** oyun deposuna, Unity'ye ve git'e yazılmadı. Kurulum, satın alma ve hesap işlemi yapılmadı. Yeni dış kaynak kullanılmadı; LICENSES değişmedi.

## 7. Ana dosyalar

- Rapor: `K/c-00/codex/reports/C00-AP026C-RAPOR.md`
- Ek katman ve alım notu: `AP-026/exports/v002/hero-v002c/` (`README.md`, indeks, olaylar, CSV, 24 FBX)
- Doğrulama: `AP-026/validation/ap026c-validator-v002.json`, `ap026c-gate-142-v002c.json`, `hero-start-merge-audit-v002c.json`, `hero-start-candidates-v002c/`
- Araçlar: `AP-026/04-tools/ap026c_*.{sh,py}`, `hero_start_candidates_v002c.py`, `hero_start_offline_audit_v002c.py`, `hero_merge_start_v002c.py`
