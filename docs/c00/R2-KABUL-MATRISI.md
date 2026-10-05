# R2 kabul matrisi (WP-091 · C-00B B-07 · 06.10.2026)

> **Ne için:** S-10'un R2 FINAL zincirine girdi. `tools/qa/r2-gates.json`'daki her kapı tek satırda: kimin ölçtüğü, neyle ölçtüğü, son değerin nerede durduğu ve FINAL'i neyin beklettiği.
> **Hazırlayan:** KADRO test-engineer (C-00B B-07, yerel ajan). Salt okuma derlemesidir; eşik değiştirmez. Eşik yalnız `r2-gates.json`'da, C-00 kaydıyla, ölçümden önce değişir (`tools/qa/r2-measure-contract.md` "Changing a threshold").
> **Durum anı:** 06.10 ~02:00. main `d95d237aa59404492b3a8c797e8b18a7733e3754`.
> **`173890ac` → `d95d237a` arası:** yalnız `docs/` ve `planning/` değişti (`git diff 173890ac d95d237a -- tools prototype` boş). Bu aralıktaki commit'ler: `81de6ad3` (B-08), `19baeee9` (BEKLEYEN §11'deki Z3b kaydının düzeltilmesi), `1e83c1dd` (`docs/c00/SABIT-KURALLAR-R2-2026-10-06.md`), `d0fe0a62` (BEKLEYEN §20), `d95d237a`.
> **Sonra gelen:** `db77df0a` (S-02 §19 G3/B1; çalışma kodu + yeni `Wp087B03GcTests.cs`). Matristeki hiçbir dosya:satır değişmedi.
> **Daha önce:** `18f3d01e`'den sonra C-00 Z3b düzeltmesini aldı. Değişen dosyalar `ZombieR2Tests.cs`, `r2-gates.json` ve `test_r2_gates.py`:
> - `1bca9052`: JSON eşikleri 24/822/98, `f755749e`'nin yeniden yazımı;
> - `8f10dddb`: `c37991f7` eşdeğeri, Z3b/Z4 kaynak kayıtta;
> - `173890ac`: `cf8346a0` eşdeğeri, test sabitleri.
>
> **Denetleyici:** `python3 tools/qa/r2_gates_lint.py --repo "<repo>" --json`
> - @ `18f3d01e`: ERROR 0 · WARN 14 · INFO 14, 15 `R2Olcum.Yaz` çağrısı. Görevde beklenen değerle aynı.
> - @ `173890ac`, `d95d237a` (araç ve test ağacı aynı) ve `db77df0a`: **ERROR 0 · WARN 14 · INFO 13, 14 çağrı.** Z4'ün null yolu (INFO KEY-NULL) kalktı.
>
> **GT karşı okuması:** GEÇTİ-NOTLU, kırmızı bayrak 0 (`K/c-00/R2/GT-B07-KARSI-OKUMA-06.10.md`). Bu sürümde düzeltilenler:
> - ŞART: B1 (iki hazırlık sütunu), B2 (N2-06 T1), B3 (13 kapı);
> - NOT: B4–B9;
> - BİLGİ: B11 ve B12.
>
> GT değerleri artefakt düzeyinde yeniden okudu. Ölçümleri Unity'de yeniden üretmedi; FINAL'de zincir yeniden ölçer.
> **Entegrasyon (GT B10):** Bu dal `18f3d01e` üstünde ve dal ağacındaki `tools/qa/r2-gates.json` hâlâ Z3b 14/450/88. main'e yalnız bu dalın docs commit'leri alınmalı (rebase ya da cherry-pick); ağaç alınmamalı.

## Kaynaklar ve kısaltmalar

- **Sözleşme:** `tools/qa/r2-gates.json`, `tools/qa/r2-measure-contract.md`, `tools/qa/r2_gates.py`, `tools/qa/r2_gates_lint.py`, `tools/qa/wp078_accept.py`, `tools/qa/wp078_perf_report.py`, `tools/qa/r2_load_time.py`, `tools/ci-isolated.txt`.
- **Plan:**
  - `K/wp-078/R2-KABUL-ZINCIRI.md`, `K/wp-078/R2-KABUL-TASLAK.md`;
  - `docs/c00/R2-PLAN.md`, `docs/c00/BEKLEYEN-MESAJLAR.md` §7, §11, §18, §19, §20;
  - `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §6, §7, §9, §14;
  - `docs/c00/SABIT-KURALLAR-R2-2026-10-06.md` Ç1, Ç21.
- **Kanıt:**
  - `K/wp-*/evidence/**/r2-*.json`: 56 adet `dd-r2-measure-v1` artefaktı. 55'i ilk sürümde tarandı; 56.'sı `K/wp-085/evidence/v3g/r2/r2-S4.json` (06.10 01:12), GT bulgusu B5;
  - `K/ci/<sha>/`: main-ci;
  - `K/wp-078/evidence/84941661/`: R1 FINAL'';
  - devir notları: `K/wp-083/DEVIR-05.10.md`, `K/wp-085/DEVIR-05.10.md`.
- `K/` = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/`
- `PM/` = `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Tests/PlayMode/`
- `EM/` = `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Tests/EditMode/`
- `APM/` = `prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Tests/PlayMode/`
- Satır numaraları main `d95d237a` içindir. `173890ac` ile aynıdır (`git show main:<yol>`).
- **"main'de"** = `git merge-base --is-ancestor <sha> main` ya da `git cherry main <sha> <sha>~1` sonucu `-` (eşdeğer yama).
- **Oturum:** `measuredBy` olan kapıyı zincir kendi guard'lı oturumunda koşar: aşama `r2` (A3'ten sonra) ya da `r2q` (A4'ten sonra, sessiz pencere). `measuredBy` yoksa artefakt yalnız sahibin `<E>/r2/` koşusundan gelir. O koşu `DD_R2_COMMIT=<FINAL>` ile yapılmalıdır, yoksa sonuç "başka commit = FAIL" olur.

**Okuma kuralı:**
- **"Son ölçülen değer"** kayıttaki en yeni geçerli değerdir. Hiçbiri FINAL commit'inde değildir; FINAL'de hepsi yeniden ölçülür.
- **İki hazırlık sütunu var** (GT B1, C-00 06.10).
- **"FINAL'e hazır (katı tanım)"** birincil sütundur.
  - **EVET:** yazıcı ve oturum var, son ölçüm main soyunda ve eşik içinde, **bekleyen teslim yok ve R2 ölçümü var.** Bekleyen teslimi ya da R2 ölçümü olmayan kapı EVET olamaz.
  - **KOŞULLU:** altyapı hazır ama bir teslim, yeniden ölçüm, ilk R2 ölçümü ya da kırılganlık bekliyor. Rapor kapıları da burada; FINAL'i düşürmezler.
  - **HAYIR:** BLOCKED ya da FAIL beklenir.
  - Sayım: **EVET 1 · KOŞULLU 12 · HAYIR 20.**
- **"Bugünkü main FINAL olsaydı"** ikincil sütundur. Bekleyen teslimleri yok sayar ve şunu sorar: zincir bugünkü main'i yargılasaydı, son geçerli ölçüme göre ne çıkardı?
  - Sayım: **EVET 8 · KOŞULLU 5 · HAYIR 20.**
  - HAYIR 20 iki okumada da aynı kapılardır.

---

## 1. Ana matris (33 kapı)

| Kapı | N2 | Sahip | Ölçen test (tam ad · dosya:satır) | R2Olcum.Yaz | Anahtar(lar) | Eşik / ölçüt | Report-only? | Son ölçülen değer · kaynak | İnsan kapısı | Engel / bağımlılık | FINAL'e hazır (katı tanım) | Bugünkü main FINAL olsaydı |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **H1** | N2-05 (N2-04 hero v2 ile dirsek) | S-06 | `DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus` · PM/R2HeroOlcumTests.cs:60 (`[Explicit]`, Diagnostics; yalnız TP, duruş + yürüyüş) | var · PM/R2HeroOlcumTests.cs:115 | `maxCm`; rapor `elbowMaxDeg` | `maxCm` ≤ 1,0 cm. `elbowMaxDeg` hero v2 alımından sonra ≤ 165 kontrolüne dönecek (lint INFO PLANNED-CHECK) | kısmen (`elbowMaxDeg`) | `maxCm` **0,40** (C sol el; rl_1 tutuş kapalı). Kaynak: `K/wp-083/evidence/b2i-r2.log` (`wp083.h1`, 05.10 18:56, S-06 WIP `wp-083-b1`), XML Passed. **r2-H1.json yok.** `elbowMaxDeg` en büyük 152,7° (tutuş açık, b6eh): `K/wp-082/evidence/hero-v2-reach/README.md:22` | hayır (H5 ayrı) | (1) `measuredBy` yok, test `[Explicit]` → zincir koşmaz (lint WARN EXPLICIT-UNRUN). (2) main'de `WeaponHoldIk.Enabled = false` (`…/Characters/WeaponHoldIk.cs:35`); test tutuşu yalnız `DD_HOLD_IK=1` ile açıyor (R2HeroOlcumTests.cs:42). "Varsayılan açık" kararı S-06 WIP `dee54ffa`'da (B2d-on/B2e), main'de değil; B2d `9f87b7d4` main'de. (3) hero v2 (AP-024C, WP-082) alımından sonra yeniden ölçüm ve dirsek kontrolü. (4) `4c48563e` (`wp-091-r2-h1-def`) ALINMAYACAK; H1 tanım metni main'den yeni dalda yeniden yazılacak (BEKLEYEN §20) | **HAYIR**: `measuredBy` yok → BLOCKED; varsayılan yolda tutuş main'de kapalı | **HAYIR** (BLOCKED) |
| **H2** | N2-05 | S-06 | **yok.** İlgili ama H2 metriği değil: `DD.Prototype.Tests.PlayMode.WeaponModelSliceTests.HerSilah_AP013Modeli_Elde_Soketler_OrtoKare_KundakOmuz` · PM/WeaponModelSliceTests.cs:1636 (kundak ↔ omuz) | **yok** (lint NO-WRITER) | `clipFrames` | == 0 (silah gövdesi ↔ omuz/göğüs kapsülü; TASLAK:21) | hayır | **ölçülmedi.** KundakOmuz PASS b2i (`K/wp-083/evidence/b2i-r2.xml`), H2'nin yerini tutmaz | hayır | S-06 ölçüm testi + hero v2 alımı | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **H3** | N2-05 | S-02 (temas S-06) | `DD.Prototype.Tests.PlayMode.WeaponReloadMagazineTests.H3_SarjorluSilahlar_VuruslarTam_HavadaKareYok` · PM/WeaponReloadMagazineTests.cs:188 (CI'da) | var · PM/WeaponReloadMagazineTests.cs:244 (`double?`) | `missingEvents`, `contactDeviationFramesMax`, `floatingMagazineFrames` | == 0 · ≤ 2 kare · == 0 | hayır | 0 / **null** / 0 @ `ed8adfdc` (main): `K/wp-069b5/evidence/b6h3-measure/r2-H3.json`. Bu artefakt `5a45e3aa`'dan **önce** yazıldı (12:51 < 14:57). `5a45e3aa` main'de; main'deki yazıcı artık null yazmıyor, temas yoksa 16 yazar (GT B8). Temas probu sonrası (S-06 tanı, R2 dışı): TP en çok 0 kare (14 vuruş, 7/7 bağlı), FP 0 kare ama yalnız 6 vuruş (3/7 bağlı). Kaynak: `K/wp-083/evidence/b2g-r2.xml` (`wp083.h3contact`) | hayır | Prob değerli bir r2-H3.json henüz yok. Bağımlılıklar: S-06 B2d-on/B2e (temas IK'sı) teslimi, AP-026 reload klipleri (hero v2), §12 olay sözleşmesi (main'de) | **KOŞULLU**: main'deki yazıcı `5a45e3aa`'dan beri sayı yazıyor. El yuvaya ulaşmazsa `NoContact` = 16 → FAIL (PM/MagazineContactProbe.cs:33). Vuruş eksikse assert artefakttan önce kırılır → BLOCKED (PM/WeaponReloadMagazineTests.cs:240-245). S-06 temas IK'sı (B2d-on/B2e) main'de değil; sayısal r2-H3.json hiç yok | **KOŞULLU**: temas ≤ 2 kare → PASS; el yuvaya ulaşmazsa 16 → FAIL; vuruş eksikse BLOCKED. Temas IK'sı main'de olmadığı için FAIL olasılığı yüksek (GT B8) |
| **H4** | N2-05 | S-02 | `DD.Prototype.Tests.PlayMode.WeaponModelSliceTests.FP_NamluKalkisi_Tablo_HerSilah` · PM/WeaponModelSliceTests.cs:720 (`[Explicit]`, Diagnostics); ayrıca `tests[]` zorunlu kimliği | var · PM/WeaponModelSliceTests.cs:810 | `yawRollExcessDegMax` | ≤ 1,0° | hayır | **0,1** @ `1acc3155` (main): `K/wp-069b5/evidence/b6sh-measure/r2-H4.json` | hayır | S-02 tepme→pivot WIP `6ea4492a` ve hero v2 FP'yi değiştirir → FINAL'de yeniden ölçülür | **KOŞULLU**: `6ea4492a` ve hero v2 sonrası yeniden ölçüm bekleniyor | **EVET** (0,1 @ `1acc3155`) |
| **H4a** | N2-05 | S-06 | **yok.** Aday: `DD.Art.Tests.PlayMode.AnimationTests.FpKollar_Kadraj_KolPayi10_KameraMesafesi15_ElSilahta` · APM/AnimationTests.cs:660. Kol piksel ≤ %10 ve kamera ≥ 15 cm assert eder, anahtar yazmaz (BEKLEYEN "S-10 → S-06") | **yok** (lint NO-WRITER) | `armOffscreenFrames`, `armInCameraFrames` | == 0 · == 0 | hayır | **ölçülmedi** | hayır | S-06: adaya `R2Olcum.Yaz("H4a", …)` eklenmeli; S-10: `measuredBy` | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **H4b** | N2-05 | S-02 | `DD.Prototype.Tests.PlayMode.WeaponModelSliceTests.FP_AnahtarPenceresindeAtes_KlipSizintisiYok` · PM/WeaponModelSliceTests.cs:836 (CI'da); `tests[]` zorunlu | var · PM/WeaponModelSliceTests.cs:902 | `leakDegMax` | ≤ 1,0° | hayır | **0** @ `1acc3155` (main): `K/wp-069b5/evidence/b6sh-measure/r2-H4b.json` | hayır | H4 ile aynı (FINAL'de yeniden) | **KOŞULLU**: H4 ile aynı teslimleri bekliyor | **EVET** (0 @ `1acc3155`) |
| **H5** | N2-04 | S-06 | AD turu → `<E>/r2/reviews/H5.json` (dd-r2-review-v1) | — | `verdict`, `must[]` | KABUL, ya da KABUL_DÜZELTMELERLE + bütün MUST kapalı | — (insan) | **yok** (K'de dd-r2-review-v1 dosyası yok). Öncül: G1b = V1 + koyu omuz (BRIFING §9) | **EVET** (AD; KADRO art-director, Opus; SABIT-KURALLAR Ç1) | AP-024C iş 2/3/5 sürüyor (`K/c-00/aktif-ajanlar/C-00.md`, 06.10 00:49) → WP-082 alımı → FINAL commit'inde AD | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L1** | N2-06 | S-06 | **yok.** R2 anahtarı yazmayan adaylar: `DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Oyuncu_YuruyusteBasanAyakKilitli_KaymaAz` · APM/AnimationLocomotionV3Tests.cs:169; `DD.Art.Tests.PlayMode.AnimationV002Tests.Oyuncu_KosuHalkasi_6_2ms_AyakKaymasi_2cmAlti_OranTavani` · APM/AnimationV002Tests.cs:191 | **yok** | `avg`, `p95` | ≤ 0,05 · ≤ 0,10 | hayır | **oyun içinde ölçülmedi.** Çevrimdışı FBX ön kanıtı: hero döngüleri avg ≤ 0,034 / p95 ≤ 0,090; zombi avg ≤ 0,019 / p95 ≤ 0,081. Kaynak: `K/c-00/codex/reports/C00-AP026C-RAPOR.md:34-35`; ":155 runtime L1/L1t/L3 ölçülmedi" | hayır | WP-083 kahraman klip alımı (`AP-026/exports/v002/hero-v002c` indeksi) + ölçüm testi | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L1t** | N2-06 | S-06 | **yok** | **yok** | `avg`; rapor `p95` | ≤ 0,10 | kısmen (`p95`) | **oyun içinde ölçülmedi.** Çevrimdışı L1t 3,6e-5: `C00-AP026C-RAPOR.md:78` | hayır | L1 ile aynı | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L2** | N2-06 | S-06 | **yok** | **yok** | `deviationMax` | ≤ 0,08 | hayır | **oyun içinde ölçülmedi.** Çevrimdışı hero ≤ 0,022, zombi ≤ 0,008: `C00-AP026C-RAPOR.md:34-35` | hayır | L1 ile aynı | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L3** | N2-06 | S-06 | **yok** | **yok** | `limbDegPerFrameMax`, `torsoDegPerFrameMax`, `transitionToSteadyRatioMax` | ≤ 20 °/kare · ≤ 10 °/kare · ≤ 1,5 | hayır | **oyun içinde ölçülmedi.** Çevrimdışı 142/142 geçiş PASS; uzuv 19,95 °/kare, gövde 9,05 °/kare (`C00-AP026C-RAPOR.md:77`). Reload klipleri kapı dışında, uzuv tepe 13,6–39,9 °/kare (`:126`) | hayır | L1 ile aynı | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L4** | N2-06 | S-06 | **yok** | **yok** | `amplitude`, `settleSeconds` | rapor (genlik > 0, sönüm ≤ 1 s; TASLAK:35) | **evet** (rapor kapısı) | **ölçülmedi** | hayır | WP-083 ikincil hareket katmanı + iz kaydı | **KOŞULLU**: rapor kapısı; değer yoksa satır BLOCKED görünür ama R2 özetini düşürmez (`r2_gates.summarize`) | **KOŞULLU** (rapor, boş) |
| **L5** | N2-06 | S-06 | **yok.** Aday: `DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Zombi_KadansBandi_KalabalikFarkliFaz_Kayar` · APM/AnimationLocomotionV3Tests.cs:75. 6 zombi, faz ≥ 4 assert eder; 40 zombide çift payını ölçmez | **yok** | `syncedPairShare` | ≤ 0,10 | hayır | **ölçülmedi** | hayır | Zombi klipleri (S-03 + S-06, dilim 3) + 40 zombi ölçüm testi | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L6** | N2-06 | S-06 | AD turu → `<E>/r2/reviews/L6.json` | — | `verdict`, `must[]` | R1 ↔ R2 yürüme/koşma yan yana | — (insan) | **yok** | **EVET** (AD) | WP-083 alımı → R1/R2 video paneli | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |
| **Z1** | N2-03 | S-03 | `DD.Art.Tests.PlayMode.ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet` · APM/ArmorTests.cs:273. A3 izole oturumu: `tools/ci-isolated.txt` 2. girdi | gerekmez (TEST-ONLY) | (zorunlu test `Passed`) | DEC-077 eşikleri yeni malzemeyle korunur | hayır | **Passed**, R1 FINAL'' A3 @ `84941661` (01.10 10:10Z): `K/wp-078/evidence/84941661/a3/a3-isolated-2-Okunabilirlik_DEC077_…`. Zombi v2 malzemesiyle koşulmadı | kısmen (okunurluk algısı → Z5) | S-03 zombi v2 alımı (SlotFace, `_ZOMBIE_MASKS`, AP-025C/D) sonrası A3'te | **KOŞULLU**: zombi v2 malzemesiyle henüz koşulmadı | **EVET** (R1 A3 Passed; a3 izole oturumu bağlı) |
| **Z2** | N2-03 | S-03 | `DD.Prototype.Tests.PlayMode.ZombieR2Tests.Z2_TierCiftleri_Ayrisir` · PM/ZombieR2Tests.cs:72 (fikstür `[Explicit]` :36) | var · PM/ZombieR2Tests.cs:127 | `indistinctTierPairs` | == 0 (siluet, luma ya da kroma; çubuklar **GEÇİCİ** onaylı, ZombieR2Tests.cs:44-49) | hayır | **0** @ `bc07ef0d` (eşdeğeri main'de), R1 görünümü: `K/wp-088/evidence/r2gates/r2-Z2.json` | kısmen (→ Z5) | AP-025C/D alımından sonra yeniden ölçüm; çubukların C-00 tarafından kesinleşmesi | **KOŞULLU**: zombi v2 sonrası yeniden ölçüm; çubuklar geçici | **EVET** (0 @ `bc07ef0d`, eşdeğeri main'de) |
| **Z3** | N2-03 | S-05 | **yok** | **yok** (lint NO-WRITER) | `setPassRatio`, `batchRatio`, `trianglesRatio`, `characterTextureMaxPx`, `atlasesPerFamilyMax`, `mapsPerAtlasMax`, `shippedCharacterMB`, `bonesMax`, `materialsPerFamilyMax` | ≤ 1,10 · ≤ 1,10 · ≤ 1,15 · ≤ 2048 · ≤ 2 · ≤ 3 · ≤ 400 · ≤ 74 · ≤ 4 | hayır | **R2 ölçülmedi.** R1 tabanı (SetPassAttribution @ `84941661`): kare 78 / 2 636 / 2 316 (`R2-KABUL-TASLAK.md:14`; `K/wp-078/evidence/r2-baseline-setpass-84941661/capture/setpass-toggles.tsv`) | hayır | Yazıcı main'de yok. Plan (BEKLEYEN §20, S-05 06.10): (1) yazıcı S-05'in `tools/qa/r2_z3.py` aracı (main'de yok); (2) oranlar PilotPerfProbe 04-window aşamasından gelir (release-probe, `-ddPilotStart` B9 pozu, Sinematik, TP ve TD); (3) spawn zarı sabit değil → en az 5 dönüşümlü R2/R1 çiftin medyanı, iki build'de `-ddArmor all`; (4) sessiz makine gerekmez (`quiet:false`); (5) statik anahtarlar AP-025 alımından sonra (`z3-static.json`); (6) tanımları C-00 06.10'da verdi. Zincire bağlamak S-10'un işi | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **Z3b** | N2-03 | S-03 | `DD.Prototype.Tests.PlayMode.ZombieR2Tests.Z3b_40Zombi_SetPassPayi_DokuButcesi` · PM/ZombieR2Tests.cs:136. Ayrıca `tests[]` zorunlu: `DD.Art.Tests.PlayMode.SetPassAttributionTests.SetPass_Kaynaklari_40ZombiTamZirh` · APM/SetPassAttributionTests.cs:75 (fikstür `[Explicit]` :40) | var · PM/ZombieR2Tests.cs:177 (kaynak assert'leri :169-175, yazımdan önce) | `sharedTextureGpuMB`, `uniqueTexturesPerZombie`, `zombieSetPass`, `zombieDraws`, `frameSetPass`; rapor `zombieSetPassWorst`, `zombieDrawsWorst`, `sharedTextureGpuMBWorst` | ≤ 96 · == 0 · ≤ 24 · ≤ 822 · ≤ 98 (`1bca9052`, 06.10 01:10; test sabitleri :54) | kısmen (3 `*Worst`) | **21 / 732 / 92**, doku 88,01 MB, benzersiz 0 (en kötü 20 / 772) → yeni eşik içinde. Ama commit alanı `"x"` (40-hex değil, 05.10 14:56): `K/wp-088/evidence/r2gates-v2/r2-Z3b.json`. **R1 sabit karışım tabanı:** iki koşu var, r1a 16 / 564 / 87 ve r1b 15 / 567 / 88 (`K/wp-088/evidence/r1/r1{a,b}-playmode.log`, `r2.z3b.counters`; `f755749e` gövdesi). Eşik tabanı iki koşunun en kötüsü: **16** / 567 / 88 (`r2-gates.json` Z3b başlığı, TASLAK:5). R1 artefaktı yalnız r1b'yi taşır (15 / 567 / 88, 64,01 MB): `K/wp-088/evidence/r1/r2/r2-Z3b.json`. Bu artefaktın commit alanı **geçersiz**: `84941661cd00…0` (42 karakter; R1 FINAL `84941661622c…`) | hayır | (1) Zorunlu SetPassAttribution testini hiçbir oturum adıyla koşmuyor → BLOCKED (lint WARN TEST-EXPLICIT). C-00 kaydı: zorunlu test `ZombieR2Tests.Z3b_40Zombi_SetPassPayi_DokuButcesi` olacak, S-05'in Z3/P3 commit'inden sonra (BEKLEYEN §20). (2) Zombi v2 alımı (AP-025C/D) sonrası yeniden ölçüm | **HAYIR**: zorunlu test hiçbir oturumda koşmuyor → BLOCKED | **HAYIR** (BLOCKED) |
| **Z4** | N2-03 | S-03 | `DD.Prototype.Tests.PlayMode.ZombieR2Tests.Z4_HerAile_YuzGozDis` · PM/ZombieR2Tests.cs:309 | var · PM/ZombieR2Tests.cs:347 (kaynak assert'i :322-323: yüz modülü yoksa FAIL, artefakt yok) | `familiesMissingFace` | == 0 | hayır | **ölçülmedi** (r2-Z4.json yok) | kısmen (→ Z5) | AP-025 yüz modülleri (`kind "face"`) + S-03 SlotFace alımı | **HAYIR**: yüz modülü yok → BLOCKED | **HAYIR** (BLOCKED) |
| **Z5** | N2-03 | S-03 | AD turu → `<E>/r2/reviews/Z5.json` | — | `verdict`, `must[]` | sinematik figür + oyun içi görünüm | — (insan) | **yok.** Öncül: AP-025C C-00 kabulü (BRIFING §9 Zombiler) | **EVET** (AD) | AP-025D sürüyor (`aktif-ajanlar/C-00.md` 06.10 00:49); S-03 alımı | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |
| **S1** | N2-07 | S-08 | `DD.Prototype.Tests.PlayMode.AudioSliceTests.SilahKiti_GercekRpm_OyunIci_Kayit` · PM/AudioSliceTests.cs:1648 (`[Explicit]`, Diagnostics, 30 dk). Oturum `r2q`; env `DD_KIT_WEAPONS=all`, `DD_KIT_VARIANTS=AC`, `DD_AP027_DIR={KPROJ}/asset-production/AP-027/exports/v001-prototip` | var · PM/AudioSliceTests.cs:1890 | `below400ShareMin`; rapor `below400ShareMinV4` | ≥ 0,20 | kısmen (`…V4`) | **0,1985 → FAIL** (ar_1 FP A 0,198); V4 0,2809 @ `0040c85f` (main). Kaynak: `K/wp-085/evidence/b4/kit-v3/r2-S1.json` + `HAZIR.md` (yalnız ar_1 + sg_1) | kısmen (kit dinleme kullanıcıda) | v4 oyunda (v003f main) ama S1 v4 ile yeniden ölçülmedi. Önce v003g overlay (DEVIR kalan 1–2), sessiz makine. v4 oyundayken "varyant C = v001-prototip" ne ölçüyor, C-00 bakmalı | **HAYIR**: 0,1985 < 0,20 → FAIL | **HAYIR** (FAIL) |
| **S2** | N2-07 | S-08 | S1 ile aynı test ve oturum | var · PM/AudioSliceTests.cs:1891 | `fireVoiceDeficit`, `transients`, `loopStarts`, `rejectedNotStolen` | hepsi == 0 | hayır | **0/0/0/0** @ `0040c85f`: `K/wp-085/evidence/b4/kit-v3/r2-S2.json`. **Kırılgan:** `rejectedNotStolen` önceki iki koşuda 1 ve 4: `…/b4/r2/r2-S2.json` (`ad0d564d`), `…/b5r/r2/r2-S2.json` (`adf99ac2`) | hayır | v003g sonrası sessiz pencerede; kök neden notu | **KOŞULLU**: kırılgan; v003g sonrası yeniden | **KOŞULLU** (kırılgan) |
| **S3** | N2-07 | S-08 | **yok** | **yok** (lint NO-WRITER) | `truePeakDbtp`, `momentaryMaxLufs`, `preLimiterGrDb`, `weaponMarginLu` | ≤ −1,0 dBTP · ≤ −13,0 LUFS · ≤ 1,0 dB · ≥ 1,0 LU | hayır | **R2 ölçülmedi.** R1 kapanışı: −3,57 / −2,73 dBTP, M en çok −14,85 / −14,65 LUFS, marj +1 LU (`R2-KABUL-TASLAK.md:56`) | hayır | "dense-40 Player, r2_s3.py önerildi, yazılmadı" (`K/wp-085/DEVIR-05.10.md` kalan 7); Player + guard (`player-run.sh`); sessiz | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **S4** | N2-07 | S-08 | `DD.Prototype.Tests.PlayMode.AudioSliceTests.MekanKuyrugu_EDC_Olcum` · PM/AudioSliceTests.cs:1918 (`[Explicit]`, 40 dk); oturum `r2q` | var · PM/AudioSliceTests.cs:2044 | `open/corridor/hallRt60Min/MaxS`, `orderViolations`; rapor `*Edc30MsMin` | açık 0,56–1,08 · koridor 0,88–1,68 · salon 1,28–2,28 s · ihlal == 0 | kısmen (3 `Edc30`) | **Geçerli son:** açık 0,717–0,811, koridor 1,129–1,321, salon 1,622–1,862 s, ihlal 0 @ `3f74e3f4` (main): `K/wp-085/evidence/v3f/r2/r2-S4.json`. Sonraki `cl` koşusu (`79819ca7`, koridor 0,159 / salon 0,15 / ihlal 10) LFS pointer kaynaklı, geçersiz (`K/wp-085/DEVIR-05.10.md`). **v3g koşusu, KİRLİ (06.10 01:12):** açık 0,715–0,822, koridor 1,154–1,333, salon 1,653–1,819 s, ihlal 0 (eşik içinde). Dosya `K/wp-085/evidence/v3g/r2/r2-S4.json`, commit alanı `worktree:25bf74cd…+uncommitted`. C-00 01:19'da KİRLİ damgası vurdu: yük ~105, yalnız sıralama bilgisi, kapı değeri değil (`K/wp-085/evidence/v3g/S4-KIRLI.md`) | hayır | v003g overlay (180 kuyruk + M/S mono) alımı sonrası yeniden. Ön koşul: `git lfs ls-files` pointer kontrolü | **KOŞULLU**: v003g sonrası temiz (sessiz) ölçüm bekleniyor | **EVET** (v3f @ `3f74e3f4` eşik içinde) |
| **S5** | N2-07 | S-08 | `DD.Prototype.Tests.PlayMode.AudioSliceTests.KovanV2_AyniKareIkiKabukAyriDuser_GecikmeBandi_HavuzTavani_SifirAyirma` · PM/AudioSliceTests.cs:989 (CI'da); oturum `r2` | var · PM/AudioSliceTests.cs:1089 | `dropDelayMsMin/Max`, `surfaceMismatches`, `poolMax`, `gcBytesPerCasing`, `rocketCasingSounds`; rapor `queueMax` | ≥ 250 · ≤ 700 · 0 · ≤ 3 · 0 · 0 | kısmen (`queueMax`) | **620–680 ms, 0, 3, 0, 0**, queueMax 16 @ `79819ca7` (eşdeğeri main'de): `K/wp-085/evidence/cl2/r2/r2-S5.json`. `poolMax` sınırda (3 = 3) | hayır | yok. B-03 G4/B2 (BEKLEYEN §19) değişirse `gcBytesPerCasing` 0 korunmalı | **EVET** | **EVET** |
| **S6** | N2-07 | S-08 | dinleme → `<E>/r2/reviews/S6.json` | — | `verdict`, `must[]` | R1 v3 ↔ R2 v4 A/B | — (insan) | **yok.** Öncül: kit-v3 (`K/wp-085/evidence/b4/kit-v3/HAZIR.md`) | **EVET** (kullanıcı / ses konseyi) | ar_1/sg_1 v4 dinleme kiti hazırlanmadı (DEVIR kalan 6) | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |
| **P2** | — (WP-091; N2-03/04 doku ve rig etkisi) | S-10 | `tools/qa/wp078_perf.py --baseline-bin/--baseline-out/--baseline-commit` (R1/R2 dönüşümlü) + `tools/qa/wp078_perf_report.py --baseline <R1 kök> --r2-measure <E>/r2/r2-P2.json --commit <FINAL>` | C# yok; Python yazıcı `tools/qa/wp078_perf_report.py:225`. Lint yalnız C# tarar, bu yüzden NO-WRITER | `flaggedCells`, `worstDeltaMs`, `worstDeltaPct` | rapor: +%10 **ve** +1,0 ms birlikte aşan hücre işaretlenir | **evet** | **ölçülmedi.** R1 FINAL'' faz 2 koşulmadı; R1 probe build: `K/wp-078/evidence/84941661/build-probe/` | hayır | **Zincire bağlı değil:** `wp078_accept` a4, R2 turunda `--baseline-bin`/`--r2-measure` geçmiyor (`tools/qa/wp078_accept.py:744-760`) → r2-P2.json oluşmaz | **KOŞULLU**: rapor; zincire bağlı değil, boş kalır | **KOŞULLU** (rapor, boş) |
| **P3** | — (WP-091; N2-03 dünya ve zombi yükü) | S-05 | `wp078_accept --b9-poses K/wp-078/evidence/b9-poses/b9-poses.json` → `<E>/b9/<poz>/` (`tools/qa/wp078_accept.py:789-810`) | **yok.** b9 aşaması `<E>/stages/b9.json` yazar, r2-P3.json yazmaz | `cells` | rapor | **evet** | **ölçülmedi** | hayır | r2-P3.json'u yazan araç main'de yok. Plan: S-05'in `tools/qa/r2_z3.py` aracı P3'ü de yazacak (BEKLEYEN §20, main'de yok). Zincire bağlamak S-10'un işi | **KOŞULLU**: rapor; üreten araç yok, boş kalır | **KOŞULLU** (rapor, boş) |
| **P5** | N2-02 / N2-03 (yeni dokular → yükleme) | S-09 (zincir üretir) | `tools/qa/r2_load_time.py` ← `wp078_accept` aşama `r2` (`tools/qa/wp078_accept.py:848-871`), `<E>/{a2,a3}/*/ui-data/load-timing.json` | zincir (lint INFO CHAIN-MADE) | `ratio`, `floorLimited`, `r2MedianSeconds`, `r1MedianSeconds`, `r2RawMedianSeconds` | rapor (TASLAK'taki "≤ +%20" C-00 kararıyla rapora çevrildi) | **evet** | **R2 ölçülmedi.** R1 tabanı hazır: 10 `load-timing.json` @ `84941661` (`K/wp-078/evidence/84941661/a2`, `/a3`) | hayır | Zincir komutunda `--r1-evidence K/wp-078/evidence/84941661 --r1-commit 84941661622cfbec8db796b639e4032df4457903` şart; yoksa BLOCKED (`wp078_accept.py:860`) | **KOŞULLU**: R2'de hiç ölçülmedi (zincir üretecek) | **EVET** (zincir üretir; `--r1-*` bayraklarıyla) |
| **U1** | N2-01 | S-09 | `DD.Prototype.Tests.PlayMode.UiTextInventoryTests.U1_GorunurHarf_AnaMenuPanellerVeSecim_EnTr` · PM/UiTextInventoryTests.cs:82 (`[Explicit]`) | var · PM/UiTextInventoryTests.cs:142 | `lettersTotalReduction`, `lettersPerScreenReductionMin`, `unnamedSelectables` | ≥ 0,40 · ≥ 0,25 · == 0 | hayır | **main `09a21785`: 0,1306 / 0 / 0 → FAIL** (`K/wp-068s9/evidence/wp081/r2-cine2/r2-U1.json`). **S-09'un iki ölçümü** (`…/r2-u1s/r2-U1.json`): u1_default 0,4222, u1_same_content 0,1306. Artefaktın commit alanı `bcde16dc`; bu, `8ba256ec`'in ebeveyni. Ölçüm commit'lenmemiş ağaçta alındı (dosya 17:33, `8ba256ec` 17:33:50); ölçülen kod `8ba256ec` içeriği. `8ba256ec` main'de değil. Kanıt kalitesi notu (GT B4) | kısmen (60 s MP4 kullanıcıya, BRIFING §6) | `8ba256ec`/`bcde16dc` teslimi. BEKLEYEN §11'deki "U1'i iki ölçümün en küçüğüne bağla" kararı JSON'da yok (C-00 kaydı gerek). U1b ≥ 0,40 için Seçim + paneller v4 | **HAYIR**: 0,1306 / 0 → FAIL | **HAYIR** (FAIL) |
| **U2** | N2-01 | S-09 | `DD.Prototype.Tests.PlayMode.UiOverflowMatrixTests.U2_Tasma_TrEn_Metin1ve125_MacUi12` · PM/UiOverflowMatrixTests.cs:85 (`[Explicit]`) | var · PM/UiOverflowMatrixTests.cs:148 | `overflows` | == 0 | hayır | **0** @ `09a21785` (main): `K/wp-068s9/evidence/wp081/r2-cine2/r2-U2.json` (geçmiş 20 → 12 → 0, b4r1–b4r3) | hayır | S-09 WIP (sinematik B + yastıklar `a1f50b99`, ikon v2 `4895ac1d`) girince yeniden | **KOŞULLU**: S-09 WIP'i girince yeniden | **EVET** (0 @ `09a21785`) |
| **U3** | N2-02 | S-09 | `DD.Prototype.Tests.EditMode.LoadingKeyArtTests.Rotasyon_ArdisikTekrarYok_YirmiYuklemedeHerSanatEnAzIkiKez` · EM/LoadingKeyArtTests.cs:169 (CI'da); `tests[]` zorunlu | var · EM/LoadingKeyArtTests.cs:234 | `consecutiveRepeats`, `minPerAny20`, `distinctSha256` | == 0 · ≥ 3 · ≥ 5 | hayır | 0 / 3 / **2 → FAIL** @ `09a21785` (main): `K/wp-068s9/evidence/wp081/r2-cine2/r2-U3.json` | hayır | S-09 yükleme sanatı alımı (`AP-028/exports/v002-upscale/Loading/`, 5/5 kabul, BRIFING §6) | **HAYIR**: `distinctSha256` 2 → FAIL | **HAYIR** (FAIL) |
| **U4** | N2-01 | S-09 | AD turu → `<E>/r2/reviews/U4.json` | — | `verdict`, `must[]` | menü v4 AAA sunum | — (insan) | **yok.** Öncül: kullanıcı sinematik 5/5 çekim onayı (BEKLEYEN §8), B + yastıklar (BRIFING §9) | **EVET** (AD) | S-09 tek teslim + MP4 + ikon v2 | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |

### Satır notları (karar değiştiren bulgular)

- **Z3b eşiği: bu çalışma sırasında kapandı.**
  - `18f3d01e`'de BEKLEYEN §11 "Z3b eşikleri … main'de (f755749e)" diyordu. O an bu doğru değildi: JSON ve test 14/450/88'di, `f755749e` yalnız `wp-091-r2-z3b-fixed` dalındaydı (`git cherry` `+`).
  - C-00, `1bca9052` (06.10 01:10) ile JSON'u 24/822/98'e çekti; `8f10dddb` ve `173890ac` S-03'ün `c37991f7`/`cf8346a0` değişikliklerini getirdi (`git cherry` `-`). `tools/qa/tests/test_r2_gates.py:34` artık 24'ü sabitliyor.
  - BEKLEYEN §11 kaydı `19baeee9` ile düzeltildi.
  - Z3b'nin kalan engeli zorunlu test oturumu. C-00 kaydı: zorunlu test `ZombieR2Tests.Z3b_…` olacak (BEKLEYEN §20).
- **P2 zincire bağlı değil.**
  - R2-KABUL-ZINCIRI §3.2–3.3 karşılaştırma aracını ve dönüşümlü koşucuyu "✅" işaretliyor; araçlar main'de var. §1(iv) ise bu koşuyu pencerede yapılacak bir plan adımı olarak yazıyor (GT B12).
  - Ama `wp078_accept` a4, R2 turunda tek taraflı koşuyor ve `--r2-measure` vermiyor. P2 rapor satırı boş kalır; Z3'ün "≥ 5 çift medyan" kararı da bu koşucuya dayanıyor.
- **H1'in varsayılan yolu.**
  - "Kanıt bayraksız oyuncu yolundan" kuralına göre H1, tutuş varsayılan açıkken ölçülmeli.
  - main'de varsayılan kapalı. Varsayılanı açan değişiklik S-06 WIP'inde ("B2d-on/B2e", `dee54ffa`); B2d'nin kendisi (`9f87b7d4`) main'de. Zincire `measuredBy` eklenince `env` ya `DD_HOLD_IK=1` taşımalı ya da B2d-on/B2e önce main'e girmeli.
- **H3 artık null değil, sayı yazıyor (GT B8).**
  - Son artefakt null, ama `5a45e3aa`'dan önceki yazıcıyla yazıldı.
  - main'deki yazıcı el yuvaya ulaşmazsa `NoContact` = 16 yazar → FAIL.
  - S-06 temas IK'sı main'e girmeden H3, FINAL'de büyük olasılıkla FAIL verir.

### TASLAK ↔ JSON ↔ ZİNCİR farkları (JSON bağlayıcı; belgeler bayat)

| Konu | `R2-KABUL-TASLAK.md` / `R2-KABUL-ZINCIRI.md` | main `r2-gates.json` |
|---|---|---|
| H3 sahibi | ZINCIRI §4: S-06 | S-02 (C-00 05.10, `a7f19398`) |
| Z3 sahibi | ZINCIRI §4: S-03 (S-05 render) | S-05 |
| P5 sahibi / türü | TASLAK §5: ≤ +%20, ZINCIRI §4'te yok | S-09, rapor |
| S4 | TASLAK:57 EDC −30 dB (v1) | RT60 T20 (v2, `3f74e3f4`) |
| S5 havuz | TASLAK:58 tavan 8 | `poolMax` ≤ 3 (`5b315d04`) |
| Z3b | TASLAK:5 → 24/822/98 | 24/822/98 (`1bca9052`, 06.10). ZINCIRI'de karşılığı yok; uyumlu |
| U1 | BEKLEYEN §11: iki ölçümün en küçüğü | tek ölçüm; `u1_default`/`u1_same_content` anahtarı yok |
| P1, P4 | TASLAK §5 kapı | JSON'da yok; R1 kapıları A4 (perf-targets) ve A5 (`soak_gate`) taşır |
| Bot nişanı `meanAimErrorDeg` ≤ +%25 | TASLAK §7 rapor | JSON'da yok, ölçen yok |
| H4a, L4 | ZINCIRI §4 kapı tablosunda yok (GT B12) | H4a S-06 kapı, L4 S-06 rapor |
| İnsan kapısı gözden geçireni | sözleşme örneği "AD Fable" (`r2-measure-contract.md` Human gates) | SABIT-KURALLAR Ç1 (06.10): AD ve ses kapıları KADRO ajanlarıyla, Opus'ta |

---

## 2. Özet sayım

| Sayım | Değer | Kapılar |
|---|---|---|
| Kapı (toplam) | **33** | 24 `gate` · 4 `report` (L4, P2, P3, P5) · 5 `human` (H5, L6, Z5, S6, U4) |
| C# yazıcısı olan | **14** kapı / 14 çağrı | H1, H3, H4, H4b, Z2, Z3b, Z4, S1, S2, S4, S5, U1, U2, U3 |
| Yazıcısız (lint NO-WRITER) | **12** | H2, H4a, L1, L1t, L2, L3, L4, L5, Z3, S3, P2*, P3 (*P2'nin Python yazıcısı var, zincir çağırmıyor) |
| Yazıcı gerekmeyen | 2 + 5 | Z1 (TEST-ONLY), P5 (zincir) + 5 insan kapısı |
| `measuredBy` ile zincirde koşan | **13 kapı / 12 oturum** | `r2`: H3, H4, H4b, Z2, Z3b, Z4, S5, U1, U2, U3 · `r2q`: S1+S2 (tek oturum), S4 |
| Yazıcısı var, `measuredBy` yok | 1 | H1 (lint EXPLICIT-UNRUN) |
| Hiç ölçülmemiş (K'de değer yok) | **19** | otomatik 14: H2, H4a, L1, L1t, L2, L3, L4, L5, Z3, Z4, S3, P2, P3, P5 · insan 5 |
| Değeri olan | 14 | 12'si `dd-r2-measure-v1` artefaktı (H3, H4, H4b, Z2, Z3b, S1, S2, S4, S5, U1, U2, U3); H1 yalnız log; Z1 R1 XML'i |
| Son değeri eşik dışında | **3** | S1 (0,1985), U1 (0,1306 / 0), U3 (`distinctSha256` 2). Z3b `18f3d01e` eşiğine göre dışındaydı, `1bca9052` eşiğine göre içinde |
| Son değeri BLOCKED olan | 1 | H3 (`contactDeviationFramesMax` null; eski yazıcı. main'deki yazıcı temas yoksa 16 → FAIL yazar) |
| İnsan kapısı | **5** (+ 2 insan bağlantılı) | H5, L6, Z5, U4 (AD) · S6 (kullanıcı / ses konseyi) · bağlantılı: S1/S2 kit dinleme, U1 MP4 |
| **FINAL'e hazır (katı tanım)**, birincil | **EVET 1 · KOŞULLU 12 · HAYIR 20** | EVET: S5 · KOŞULLU: H3, H4, H4b, L4, Z1, Z2, S2, S4, P2, P3, P5, U2 · HAYIR: H1, H2, H4a, H5, L1, L1t, L2, L3, L5, L6, Z3, Z3b, Z4, Z5, S1, S3, S6, U1, U3, U4 |
| **Bugünkü main FINAL olsaydı**, ikincil | **EVET 8 · KOŞULLU 5 · HAYIR 20** | EVET: H4, H4b, Z1, Z2, S4, S5, P5, U2 · KOŞULLU: H3, L4, S2, P2, P3 · HAYIR: aynı 20 |

---

## 3. Sahip başına açık iş

| Sahip | Kapı | İş | Kaynak |
|---|---|---|---|
| **S-02** | H3 | `contactDeviationFramesMax` sayısal olan bir r2-H3.json. Yazıcı `5a45e3aa`'dan beri sayı yazar; temas IK'sı (S-06) main'e girmeden 16 → FAIL. FP vuruş bağının 3/7 olduğu bir tanı koşusu var | `K/wp-083/evidence/b2g-r2.xml`, PM/MagazineContactProbe.cs:33 |
| | H4, H4b | `wp-087-heldkick` (`6ea4492a`) girince FINAL'de yeniden ölçüm. Sol el atış payı ≤ 1 cm (H1 kapsamı dışında, N2-05) | `K/wp-087/DEVIR-05.10.md` |
| | — | B-03: G3 ve B1 main'de (`db77df0a`, durum anından sonra); B2 açık | BEKLEYEN §19 |
| **S-03** | Z3b | S-10 ile SetPassAttribution zorunlu test kararı. `c37991f7`/`cf8346a0` main'de eşdeğer (`8f10dddb`/`173890ac`) | lint WARN TEST-EXPLICIT |
| | Z4 | Yüz modülleri + SlotFace alımı, sonra ölçüm | BRIFING §7 S-03 |
| | Z2, Z1 | Zombi v2 ile yeniden ölçüm; Z2 çubuklarını C-00'a kesinlet | PM/ZombieR2Tests.cs:44-49 |
| | Z5 | AD dosyası (AP-025D sonrası) | — |
| | — | G1 GC | BEKLEYEN §19 |
| | — | **Devir notu eksik** | BRIFING §14 |
| **S-05** | Z3 | `tools/qa/r2_z3.py` (oran kısmı şimdi, statik anahtarlar AP-025 alımından sonra); 9 anahtar, R1 tabanına oran; "kaynak kayıtta var" assert'i (`achievedCount == 40`) | BEKLEYEN §7, §20 |
| | P3 | r2-P3.json aynı araçtan (`r2_z3.py`) | BEKLEYEN §20 |
| | — | §17 shader incelemesi | BEKLEYEN §17 |
| **S-06** | H1, H3 | Varsayılan açık tutuşu ve temas IK'sını (B2d-on/B2e) temiz commit'le teslim et; `dee54ffa` WIP'i alınmaz. Hero v2 sonrası dirsek ≤ 165 | `K/wp-083/DEVIR-05.10.md` |
| | H2 | Ölçüm testi (silah gövdesi ↔ gövde kapsülleri) | TASLAK:21 |
| | H4a | `AnimationTests.FpKollar_Kadraj_…`'ya `R2Olcum.Yaz("H4a")` | APM/AnimationTests.cs:660 |
| | L1, L1t, L2, L3, L4, L5 | WP-083 klip alımından sonra oyun içi ölçüm testleri; L5 için 40 zombi çift payı | `C00-AP026C-RAPOR.md:155` |
| | H5, L6 | AD dosyaları | — |
| | — | G5 | BEKLEYEN §19 |
| **S-08** | S1 | v003g sonrası v4 ile yeniden ölçüm (sessiz); ≥ 0,20 tutmuyorsa kök neden notu | `K/wp-085/DEVIR-05.10.md` |
| | S2 | `rejectedNotStolen` 1/4 kırılganlığının kök nedeni | `…/b4/r2`, `…/b5r/r2` |
| | S3 | Yazıcı (Dense-40 Player, `r2_s3.py`) | DEVIR kalan 7 |
| | S4 | v003g sonrası yeniden; LFS pointer kontrolü | DEVIR kalan 1–2 |
| | S6 | ar_1/sg_1 v4 dinleme kiti | DEVIR kalan 6 |
| | — | G2, G4 | BEKLEYEN §19 |
| **S-09** | U1 | `8ba256ec`/`bcde16dc` teslimi; U1b ≥ 0,40 (Seçim + paneller v4) | BEKLEYEN §11 |
| | U3 | 5 yükleme sanatının alımı (`distinctSha256` ≥ 5) | BRIFING §6 N2-02 |
| | U2 | WIP girince yeniden | — |
| | U4 | AD + MP4 | — |
| **S-10** | H1 | `measuredBy` (tam ad; tutuş açık olsun diye `env` ya da önce B2d/B2e) | lint EXPLICIT-UNRUN |
| | Z3b | `tests[]` satırı `ZombieR2Tests.Z3b_…` olacak (C-00 kaydı, S-05'in Z3/P3 commit'inden sonra) | BEKLEYEN §20, lint TEST-EXPLICIT |
| | P2 | a4'e R2 turunda `--baseline-bin` + `--r2-measure` bağlantısı ya da elle adım | `wp078_accept.py:744` |
| | P3, Z3 | S-05'in `r2_z3.py` aracını zincire bağla (Z3 ≥ 5 dönüşümlü çift, `-ddArmor all`) | BEKLEYEN §20 |
| | P5 | `--r1-evidence` / `--r1-commit` bayrakları | `wp078_accept.py:860` |
| | — | `R2-KABUL-ZINCIRI.md` §4 kapı tablosunu güncelle: sahipler H3, Z3, P5; eksik kapılar H4a, L4 | §1 fark tablosu |
| **C-00** | Z3b | ✅ eşik main'de (`1bca9052`); BEKLEYEN §11 kaydı `19baeee9` ile düzeltildi | §1 not |
| | U1 | Sözleşmeye iki ölçüm anahtarı (kayıtlı commit) | BEKLEYEN §11 |
| | H1 | Hero v2 alımında dirsek kontrolünün açılması | `r2-measure-contract.md` "H1 elbow" |
| | S1 | v4 oyundayken varyant C'nin anlamı | `measuredBy.env` |
| | P1, P4, nişan | R1 kapılarıyla taşındığının kaydı; nişan raporunun sahibi | TASLAK §5, §7 |
| | T4 | Karar verildi (SABIT-KURALLAR Ç21): kitler R2 FINAL'de tek görselli sayfada toplanır; kullanıcı koşmazsa sınıf "beyan + deterministik eş" olur. Kalan iş: §5.3 kitinin o sayfaya alınması | SABIT-KURALLAR Ç21; `system/18` §6 değişmez 5 |
| **S-01** | zincir | Guard D8/D9 durumu; her FINAL oturumu guard'lı. Son compile-check: `173890ac` PASS (unity exit=0, errorsCS=0, 06.10 01:14; `K/ci/compile-173890acc8-20261005T221228Z/timeline.txt`). `db77df0a` çalışma kodunu değiştirdi → FINAL SHA'da yeniden | BRIFING §7, §14 |
| **S-07** | — | R2 kapısı yok (WP-086). Kovan görseli ↔ ses hizası için kapı önerisi açık | BEKLEYEN "S-10 → S-07" |
| **S-04** | — | R2 kapısı yok | BRIFING §7 |

---

## 4. R2 FINAL zinciri: önerilen sıra ve ön koşullar

**A. FINAL SHA seçilmeden önce: main'e girecekler.** Sıra bağımlılığa göre.

1. S-06 B2d-on/B2e (WIP `dee54ffa`; B2d `9f87b7d4` main'de): tutuş varsayılan açık + temas IK'sı (H1, H3).
2. S-02 `wp-087-heldkick`, S-06 tesliminin üstüne (S-06 notu "önce B2d/B2e"). Etkiler: H1 atış, H4.
3. AP-024C → WP-082 hero v2 alımı (H1 dirsek, H2, H4a, H5) ve WP-083 klip alımı (L1–L6).
4. S-03 zombi v2 alımı (Z1–Z5). Z3b eşiği ve kaynak assert'leri main'de (`1bca9052`, `8f10dddb`, `173890ac`).
5. S-08 v003g overlay (S1, S2, S4) ve S3 yazıcısı.
6. S-09 tek teslim (U1 iki ölçüm, yükleme sanatları, sinematik B, ikon v2).
7. Henüz olmayan yazıcılar: H2, H4a, L1, L1t, L2, L3, L4, L5, Z3, S3.

**B. Sözleşme düzeltmeleri.** Her biri ölçümden önce, C-00 kaydıyla, `r2-gates.json` + `tests/test_r2_gates.py` + `r2-measure-contract.md` birlikte değişir.
- ~~Z3b eşikleri~~: yapıldı (`1bca9052`).
- H1 `measuredBy` (+ `env`).
- Z3b `tests[]` satırı → `ZombieR2Tests.Z3b_…` (C-00 kaydı, BEKLEYEN §20; S-05'in Z3/P3 commit'inden sonra).
- U1 anahtarları.
- Yeni yazıcılar için `measuredBy`.
- Hero v2 sonrası H1 `elbowMaxDeg` ≤ 165 kontrolü.

**C. Çıkış kapısı.**
- `python3 tools/qa/r2_gates_lint.py --strict` → WARN 0 hedeflenir. Kalan WARN'lar C-00'ın kabul listesine adıyla yazılır.
- `python3 -m unittest tools/qa/tests/test_r2_gates.py tools/qa/tests/test_r2_gates_lint.py` yeşil. Bu biçimi C-00 `1bca9052` gövdesinde kullandı; bu belge testleri koşmadı.
- `tools/compile-check.sh <FINAL>` PASS.
- FINAL'de main-ci (A1 paritesi).

**D. Makine.**
- Disk ≥ 15 GB, swap ≤ 12 GB (`df -k`).
- Guard temiz (GUARD-STOP yok).
- R1 kaynakları yerinde: `K/wp-078/evidence/84941661/{build-probe,a2,a3}`.
- Sessiz pencere ilan edilir: Codex/Blender yok, kopya yok.

**E. Koşu.** `wp078_accept.py` STAGE_ORDER, `tools/qa/wp078_accept.py:69`. Önce `--dry-run` ile oturum listesi doğrulanır.

```
python3 tools/qa/wp078_accept.py --final <FINAL 40-hex> --round R2 \
  --r1-evidence K/wp-078/evidence/84941661 --r1-commit 84941661622cfbec8db796b639e4032df4457903 \
  --b9-poses K/wp-078/evidence/b9-poses/b9-poses.json --dry-run
```

| # | Aşama | R2 kapıları | Not |
|---|---|---|---|
| 1 | a6 build'ler (dev, probe, release) | — | P2'nin R2 tarafı probe build'i |
| 2 | a0 provenance · a1 main-ci paritesi · a2 bot | P5 R2 örnekleri (a2) | — |
| 3 | a3 bayraklar + izoleler | **Z1** (A3 izole XML), P5 (a3) | — |
| 4 | **r2** (10 oturum) | H3, H4, H4b, Z2, Z3b, Z4, S5, U1, U2, U3 + P5 (zincir) | Ağırlık sırası (KADRO adım 4): bugün kırmızı ya da BLOCKED olanlar önce (U1, U3, Z3b, H3) |
| 4b | **sahip koşuları** → `<E>/r2/` (`DD_R2_MEASURE_DIR`, `DD_R2_COMMIT=<FINAL>`) | H1, H2, H4a, L1, L1t, L2, L3, L4, L5 (sessiz değil) | `measuredBy` gelmediyse. Başka commit = FAIL |
| 5 | a5 soak (sessiz) | (P4 R1 kapısı) | — |
| 6 | a4 perf (sessiz) + b9 | P3 (yalnız `stages/b9.json`) | **P2 için dönüşümlü R1/R2 elle:** `wp078_perf.py --baseline-bin K/wp-078/evidence/84941661/build-probe …` + `wp078_perf_report.py --baseline … --r2-measure <E>/r2/r2-P2.json`. Z3 de bu dönüşümlü koşucuyla ölçülür: en az 5 çift, `-ddArmor all`. Z3 sessiz makine istemez (BEKLEYEN §7, §20) |
| 7 | **r2q** (sessiz) | S1 + S2 (tek kit oturumu), S4 | + S3 (Dense-40 Player, yazıcı gelirse sahip koşusu) |
| 8 | insan kapıları → `<E>/r2/reviews/<id>.json` | H5, L6, Z5, S6, U4 | FINAL commit'inde. MUST/SHOULD maddeleri ölçüte ve kareye bağlı |
| 9 | a8 `--round R2` | rapor | R2 özeti: FAIL > BLOCKED > PASS; rapor kapıları düşürmez |

Sonra release mac + win64 (A6 paritesi) ve portföy 10 kare.

---

## 5. KADRO Genel Test Algoritması: kapsam matrisi (N2 × T1–T5)

Değerler: `var` = R2 zincirinde ölçen var · `kısmen` = ölçen var ama kapsam ya da yazıcı eksik · `boşluk` = gerekli, yok · `gereksiz` = gerekçeli. Sahiplik (`system/18` §3 adım 6): T1/T2 → A2, T3/T5 → GT, T4 → **kullanıcı**.

| N2 | T1 Sözleşme | T2 Davranış | T3 Eşik | T4 Deneyim | T5 Dayanıklılık |
|---|---|---|---|---|---|
| **N2-01** Menü | var: U1 `unnamedSelectables` (okuyucu adı) | **boşluk**: menü v4 akış ve odak davranışının R2 kapısı yok; WP-081'in kendi testleri zincire bağlı değil | kısmen: U1 eşik dışında; iki ölçüm sözleşmede yok | kısmen: U4 AD; kullanıcı yalnız MP4 görüyor | var: U2 (TR/EN × 1,25 × Mac 1,2) |
| **N2-02** Yükleme | var: U3 `distinctSha256` | var: U3 rotasyon | var: U3 `minPerAny20`; P5 rapor | **boşluk**: yükleme ekranının insan onayı yok (U4 menüyü kapsar) | var: U3 500 tohum × 200 yükleme (TASLAK:77) |
| **N2-03** Zombi | kısmen: Z3 (doku, kemik, materyal) yazıcısız; Z4 yazıcısı var, ölçülmedi | var: Z2 | kısmen: Z1, Z3b var (Z3b zorunlu testi oturumsuz); **Z3 boşluk** | kısmen: Z5 AD; "sinematik figürler" yalnız AD'de | kısmen: Z3b en kötü karışım rapor; Z3 yok |
| **N2-04** Kahraman | **boşluk**: rig/avatar sözleşmesinin R2 kapısı yok (öncül: AP-024C roundtrip, BRIFING §6) | **boşluk**: hero v2 alım davranışı kapısız; `Wp082HeroV2ErisimTests` `[Explicit]`, zincirde değil | kısmen: yalnız H1 `elbowMaxDeg` (rapor) | kısmen: H5 AD; kullanıcı G1/G1b seçti | **boşluk**: TASLAK §7 bot nişanı ≤ +%25 JSON'da yok |
| **N2-05** Tutuş + şarjör | var: H3 `missingEvents` (§12 olay sözleşmesi) | var: H3 `floating`, H4b | kısmen: H1 (yalnız TP × duruş/yürüyüş; TASLAK 10 × TP/FP × 5 durum ister), H4 var; **H2 ve H4a boşluk** | **boşluk**: tutuş ve reload için insan kapısı yok (R1 kullanıcı notu "silahlar omzunda") | **boşluk**: atış altında tutuş (ar_2 bilinen kırmızı), reload ortasında kamera (PM/WeaponReloadMagazineTests.cs:254) R2 dışında |
| **N2-06** Locomotion | **boşluk** (GT B2): Unity içe aktarım sözleşmesi (Avatar valid/isHuman, sıkıştırma, klip olayları → AudioEventMap) ölçülmedi (`C00-AP026C-RAPOR.md:155`). Çevrimdışı 142/142 FBX denetimi bunu kapsamaz | **boşluk**: L1–L5 yazıcısız | **boşluk**: L1, L1t, L2, L3, L5 yazıcısız; yalnız çevrimdışı FBX değerleri | kısmen: L6 AD | **boşluk**: L1t/L3 geçiş pencereleri tanımlı, ölçen yok; L5 40 zombi yok |
| **N2-07** Silah sesi + kovan | var: S2 sayaçlar, S5 `surfaceMismatches` | var: S2, S5 | kısmen: S1 eşik altında ve v4'le ölçülmedi; S4 var; **S3 boşluk** | var: S6 (kullanıcı), kit henüz yok | kısmen: **S3 Dense-40 tepe ve kırpma boşluk**; S2 kırılgan; S5 `queueMax` rapor |

**Hücre sayımı (35):** var 12 · kısmen 12 · **boşluk 11** · gereksiz 0.
- Boşluklar: N2-01: 1 · N2-02: 1 · N2-03: 0 · N2-04: 3 · N2-05: 2 · N2-06: 4 · N2-07: 0.
- İlk sürümde N2-06 T1 "gereksiz" yazılmıştı; GT B2 ile boşluğa çevrildi.
- "Kısmen" hücrelerin içinde 4 yazıcısız kapı daha var: H2 ve H4a (N2-05 T3), Z3 (N2-03 T1/T3/T5), S3 (N2-07 T3/T5). L1–L5 zaten boşluk hücrelerinde.
- Boşluk sayısı G4 ağırlığına eklenir (`system/16` §2.2).

### 5.1 En önemli üç boşluk

Risk ağırlığı `system/18` adım 4'e göre hesaplandı: kritiklik × 2 + yeni alan + geçmiş bayrak + tek koşu.

1. **N2-06 Locomotion: T1, T2, T3 ve T5 oyun içinde hiç ölçülmüyor.** Ağırlık 10.
   - T1: Unity içe aktarım sözleşmesi (Avatar, sıkıştırma, klip olayları) ölçülmedi.
   - L1, L1t, L2, L3 ve L5'in yazıcısı yok. Elde yalnız AP-026C çevrimdışı FBX değerleri var ve rapor "runtime L1/L1t/L3 ölçülmedi" diyor (`C00-AP026C-RAPOR.md:155`).
   - Kullanıcının "çok amatör" notunun (R2-PLAN §1) tek oyun içi kanıtı bugün L6 AD turu.
2. **N2-05 Tutuş: kullanıcının birebir şikâyeti ("silahlar omzunda") eşikte ölçülmüyor.** Ağırlık 10.
   - H2 (omza ve göğse gömülme) ile H4a (FP kol kadrajı) yazıcısız.
   - H1 yalnız TP × duruş/yürüyüş ölçüyor ve main'in varsayılan yolunda tutuş kapalı.
   - Tutuş ve reload için T4 (insan) kapısı yok.
3. **T4 Deneyim: dört insan kapısı AD incelemesi, kullanıcı değil.**
   - Gözden geçiren: KADRO art-director, Opus (SABIT-KURALLAR Ç1).
   - Etkilenen notlar: N2-01, N2-03, N2-04, N2-06.
   - KADRO değişmez 5'e göre "kullanıcının koşmadığı deneyim testi geçmiş sayılmaz". R2 notlarının hepsi kullanıcı notu, ama zincirde kullanıcı T4'ü yalnız S6 (ses).
   - C-00 kuralı (SABIT-KURALLAR Ç21, 06.10): kitler R2 FINAL'de tek görselli sayfada toplanır; kullanıcı koşmazsa sınıf "beyan + deterministik eş" olur. Boşluk böylece görünür kalır ama süreci durdurmaz.

Ek (4. sırada): **N2-07 T5.** S3 Dense-40 tepe ve limiter yazıcısız. "Yankılı, gerçekçi" ses v4 ile kalabalıkta kırpılıyor mu, ölçülmüyor.

### 5.2 Çürütme ve kırılganlık notları (T3, adım 7)

- **S1:** 0,1985, eşiğe 0,0015 uzakta; tek ölçüm, iki silah. Çürütme: v4 ile 10 silahta herhangi bir `below400ShareMin` < 0,20.
- **S2:** `rejectedNotStolen` 6 farklı koşunun ikisinde > 0. Kapı tek koşuya bağlı, **kırılgan**.
- **S5:** `poolMax` = 3, sınırda. Çürütme: Dense-40'ta ya da LMG'de dördüncü eşzamanlı kovan sesi.
- **H1 0,40 cm:**
  - rl_1 tutuş kapalıyken, yalnız TP duruş/yürüyüşte ölçüldü.
  - Kapsam: FP, koşu, nişan ve atış yok; atışta sol el 1,6–9,6 cm (`K/wp-083/DEVIR-05.10.md`, b6hk).
  - Çürütme: kapsam TASLAK'a genişleyince > 1,0 cm.
- **Z3b:** son artefaktın `commit` alanı `"x"`, yeniden üretilebilir kanıt değil. R1 artefaktının commit alanı da geçersiz (42 karakter). R1 tabanı iki koşuda 16 ve 15 SetPass verdi; eşik en kötüsünden (16) türetildi. Çürütme: sabit karışımda R1 koşuları arası fark 1 SetPass; R2 değeri 24'e 1–2 yaklaşırsa tek koşu yetmez.
- **U2:** b4r1→b4r3'teki 20 → 12 → 0 serisi aynı commit'te (`240b69f1`) bir geliştirme izi; kırılganlık değil. Sonraki üç commit'te (`7a72b7c2`, `1040f9f9`, `09a21785`) hep 0 (GT B12). Her UI teslimi sonrası yine de yeniden ölçülür.

### 5.3 Kullanıcı deneyim kiti (GT sürümü, 10 adım; `system/18` adım 8)

İlk sürümdeki kit KADRO standardını karşılamıyordu (GT B9): birleşik sorular vardı, beklenen sonuç yazılmamıştı. Yerine GT'nin sürümü (`K/c-00/R2/GT-B07-KARSI-OKUMA-06.10.md` §5) konuldu.
- Her adım tek davranış ve yazılı beklenen sonuç içerir.
- Ortam: FINAL release build, Sinematik, 1920×1080.
- Her adım "geçti / kaldı / not" ile işaretlenir; "kaldı" işaretlenen adım BULGU olur.
- Sunum: R2 FINAL'deki tek görselli sayfa (SABIT-KURALLAR Ç21).

1. Ana menüyü aç. **Beklenen:** ikonlar yazıdan baskın; ilk bakışta 3'ten fazla yazı satırı yok. (N2-01)
2. Art arda 5 kez run başlat. **Beklenen:** yükleme ekranında aynı resim üst üste iki kez çıkmaz. (N2-02)
3. TP'de 2 m'den bir zombinin yüzüne bak. **Beklenen:** göz ve diş seçilebiliyor. (N2-03)
4. Kahramanı TP'de 10 s izle. **Beklenen:** palet dünyayla uyumlu, karakter fantastik görünüyor. (N2-04)
5. ar_1'i al ve TP'de dur. **Beklenen:** silah iki elde; omza ya da göğse gömülmüyor. (N2-05)
6. ar_1'de reload yap. **Beklenen:** şarjör elde taşınıyor, hiçbir karede havada kalmıyor. (N2-05)
7. Kahramanla koşup aniden dur. **Beklenen:** ayak yerde kaymıyor. (N2-06)
8. 40 zombilik dalgayı 10 s izle. **Beklenen:** yan yana iki zombi aynı adımla yürümüyor. (N2-06)
9. ar_1 ile koridorda kısa bir seri at. **Beklenen:** atıştan sonra belirgin bir yankı kuyruğu duyuluyor. (N2-07)
10. **Serbest not:** Rahatsız eden ya da AAA hissini bozan başka bir şey gördün mü?
