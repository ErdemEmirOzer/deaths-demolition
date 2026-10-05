# WP-083 S-06 devir — 05.10 (hesap limiti)

- **Worktree:** `K/wp-083/worktree/deaths-demolition`, dal `wp-083-b1`.
  - **WIP commit `dee54ffa180aea5e9b4dccbb8d5ec62b968bc3ee`**; üstü 9f87b7d4 (B2d, main'de).
  - Worktree temiz.
- **Ayrıntılı geçmiş:** `K/wp-083/S06-DEVIR-05.10.md`; tasarım `S06-SG1-LHIK-TASARIM.md`; AP-026 alım notu `K/c-00/codex/reports/C00-AP026C-RAPOR.md` §5.
- **Guard:** temiz. GUARD-STOP yok. Son koşuların hepsi verified=1, OOS=0. Runner (`tools/unity.sh`) değişmedi; C-00 kararı: dokunma, yeni exit 32 olursa dur ve bildir.

## WIP içeriği (B2d-on / B2e)

- **WeaponHoldIk:**
  - varsayılan açık (yalnız TP);
  - roll'süz çözüm;
  - silah başına satırlar:
    - C +6 cm yanal;
    - rl_1 = **omuz satırı** (`Shouldered`, `ShoulderRest` / `ShoulderGripAhead`, tüp ekseni sağ omuzdan);
  - klibin tepmesi pivota taşınır: `WeaponMountAlignment.HoldDelta`, eğim farkı düşülür.
- **WeaponMountAlignment:**
  - tutuş ağırlığıyla pivot dönüşüne karışım;
  - HoldDelta (ayrı dinlenme referansı).
- **WeaponGripIk:**
  - destek hedefi rig.Body çerçevesinde;
  - SolveNoRoll;
  - HandInWeapon getter.
- **MagazineContactIk** (H3 temas, TP + FP). PSV / FPArms bağlantıları.
- **R2HeroOlcumTests:**
  - H1, H3 TP / FP, kare, roll, atış testleri;
  - tanı testleri: `SupportWhy`, `Tani_Ar2_AtisSatiri_*`, `Tani_Rl1_OmuzSatiri_*`, `TubeFit`, `OrthoSide`.
- **AnimationTests:** burst 1/60 sabit kare.

## Ölçümler

| ölçüm | sonuç | koşu / not |
|---|---|---|
| H1 | **0,40 cm PASS** | b2i, rl_1 tutuş kapalıyken |
| KundakOmuz (tüm silahlar) | **PASS** | b2i, rl_1 kapalıyken |
| sg_2 tepmesi | **PASS** | b2i |
| Prototype PM (Core / WeaponModel / Reload) | 31/32 | b2g; tek kırmızı sg_2 tepmesiydi, düzeltildi; tam set yeniden koşulmadı |
| Animation PM (LocomotionV3 + AnimationTests) | 25/25 | b2g |
| H3 TP / FP, roll, kare | PASS | b2g r2, eski kod |
| **ar_2 atış (sol el)** | **12,6 cm** | **BİLİNEN KIRMIZI** (C-00 kararı c + b) |

**ar_2 atışı.** Kök neden S-02'nin WeaponVisualRig birikimli tepmesi (WeaponBody 16,4 cm / 14,8°). (b) paketi S-02'de: ArmsCarryKick + KickLocalOffset / Rotation + birikim üst sınırı. S-06 tarafı WeaponHoldIk'te pivotu tepmeyle iter. API önerisi S-02'ye gönderildi. Tarama b2j: ar_2 satırı çözmüyor.

## rl_1 omuz satırı (C-00 ONAY)

C-00 ölçütleri:
- tüp ↔ baş ≥ 2 cm;
- tüp ↔ omuz teması ≤ 1 cm;
- nişangâh göz hizasında;
- sol el ön kabzada;
- v10 TP + yan kare.

**Varsayılan WIP'te b2n'den seçildi: `DefaultShoulderRest` = (0,005, 0,05, 0), `DefaultShoulderGripAhead` = 0,15.**
- Ölçüler: baş 3,5 cm, omuz 0,6 cm, nişangâh gözün 6,4 cm altında, iki el 0.
- Bu son düzenleme derlenmedi ve koşulmadı.
- C-00'a not: nişangâh göz hizasının 6,4 cm altında. Omuz ≤ 1 cm ile birlikte sağlanamıyor (u12'de nişangâh +0,6 ama omuz 5,2 cm). Kafa eğimi veya ölçüt kararı C-00'da.

b2m taraması (ahead 0,15; yarıçap 5 cm tahmini, ortografik karede çap ~9,5 cm):

| u | s | baş | omuz | nişangâh |
|---|---|---|---|---|
| 8 | −2 | 2,4 | 1,7 | −3,4 |
| 8 | 0 | 4,0 | 3,1 | −3,4 |
| 10 | −2 | 3,8 | 3,4 | −1,4 |
| 12 | −2 | 5,0 | 5,2 | +0,6 |

İnce tarama b2n (u 5–8 cm × s −1,5 / −0,5 / +0,5 cm):
- log `evidence/b2n-r2.log` (`wp083.rl1diag`);
- kareler `evidence/b2d-compare/v10c/`.

Ölçütü sağlayan (baş ≥ 2, omuz ≤ 1) ve nişangâhı en yüksek adayı seç, `DefaultShoulderRest` / `DefaultShoulderGripAhead`'e yaz. Sol el a ≤ 0,20'de yetişiyor.

**Gerilim:** omuz teması için tüp alçalmalı, nişangâh için yükselmeli. Göz hizasını tam tutmak gerekirse C-00'a sor.

## Sıradakiler

1. (Yapıldı: rl_1 varsayılanı b2n'den.) Nişangâh ↔ göz kararı için C-00'a sor.
2. Derle:
   ```
   DD_RSP_ALIAS="DD.Prototype.Tests.Shared=DD.Art.Tests" DD_EXTRA_REFS="$O/DD.Prototype.Tests.Shared.dll" python3 K/wp-083/tools/offline_compile.py <proj> $O DD.Prototype.Core,DD.Prototype.Runtime,DD.Art.Runtime,DD.Art.Pipeline.Editor,DD.Prototype.Tests.Shared,DD.Art.Tests,DD.Art.Tests.EditMode,DD.Prototype.Qa,DD.Prototype.Tests
   ```
3. `K/wp-083/tools/b2g.sh`: tam PM seti + R2. Atış testinde ar_2 bilinen kırmızı. Testi, bilinen-kırmızı listesine göre ar_2'yi rapor edip ayrı satırda gösterecek şekilde düzenle: `Ignore` yok, gerekçe gövdede. rl_1 KundakOmuz + H1 omuz satırıyla yeniden.
4. Kare testine rl_1 TP + yan kare (v10); `Kare_TutusOnceSonra` rl_1'i zaten içeriyor.
5. Art* + Audio EM.
6. Teslim:
   - plumbing commit main üstüne + dal;
   - konu "WP-083 …(C-00 05.10)", gövdede `Test:` / `Guarded:`;
   - SHA'yı rev-parse çıktısından kopyala, C-00'a gönder;
   - S-10'a ölçüm test adları + SHA.
7. Bu WIP commit'i main'e ALINMAZ. Teslim commit'i ayrı, temiz bir commit olur.

**Kural:** koşan betik bitmeden worktree'ye yazma (05.10'da bir kez ihlal edildi, sonuçlar etkilenmedi).

## C-00 kararı (05.10, devirden sonra)

- **rl_1: tüp omuzda kalır.**
  - Kabul edilen ölçüler: omuz 0,6 cm, baş 3,5 cm.
  - Nişangâh ↔ göz 6,4 cm, bu teslimde **bilinen sapma**; gerekçe commit gövdesinde: "gerçekte tüp yerinde kalır, kişi başını nişangâha eğer".
- **Sonraki iş:** rl_1 nişan alırken prosedürel baş ve boyun eğimi. Hedefler:
  - göz ↔ nişangâh ≤ 2 cm;
  - baş eğimi ≤ 15°;
  - yanak tüpe değmez.

## S-02 (b) paketi durumu (05.10, devirde)

- **S-02 WIP:** `6ea4492a` @ `wp-087-heldkick`, main'de değil. Not: `K/wp-087/DEVIR-05.10.md`.
- **Rig tarafı:** HoldShare (tutuş ağırlığı), HeldKick (geri m / yukarı °), tutuş payında birikim tavanı 1,25×.
- **⚠ Çakışma riski:** S-02, WeaponHoldIk'e de RecoilSource / LastRecoil ekledi (pivot nişan boyunca geri + namlu yukarı). B2d/B2e WIP'imiz (dee54ffa) ile çakışabilir.
  - S-02 önerisi: bu kısım bizim adlarla (ArmsCarryKick / KickLocalOffset / KickLocalRotation) yeniden kurulsun; WIP'i yalnız yön olarak al.
  - Sıra: önce B2d/B2e teslimi main'e, sonra S-02 dalı onun üstüne.
- **b6hk ölçümü:**
  - sağ el ≤ 0,02 cm (kayma bitti);
  - sol el henüz değil: lmg_2 9,6, sg_2 3,8, lmg_1 3,7, rl_1 3,3, ar_2 1,6 cm (önce 12,6);
  - pivot en çok 10,5 cm / 9,4°.
  - rl_1 artık omuz satırında; S-02 ölçümü hangi tutuşla alındı, kontrol et.
- **Kök adayları:** pivot payı çok büyük (AR his satırı 0,10 m/atış, lmg de aynı) ya da WeaponGripIk hedefinin aynı karede yakalanması. Mutlak tavan (~5 cm / 5°) denenecek.
- **S-06 tarafında bakılacak:** pivot itildiğinde destek hedefi (rig.Body) ile sol omuz arası. `R2HeroOlcumTests.SupportWhy` atış testinde bunu gösterir (shortfall / bodyVsRig).
