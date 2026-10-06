# R2 kabul matrisi (WP-091 · C-00B B-07 · 06.10.2026 · S-10 güncellemesi 06.10)

> **Ne için:** S-10'un R2 FINAL zincirine girdi. `tools/qa/r2-gates.json`'daki her kapı tek satırda: kimin ölçtüğü, neyle ölçtüğü, son değerin nerede durduğu ve FINAL'i neyin beklettiği.
> **Hazırlayan:** KADRO test-engineer (C-00B B-07, yerel ajan). **Güncelleyen:** S-10/technical-writer (06.10 ~03:50, dal `wp-091-s10-r2-gates-r2`). Salt okuma derlemesidir; eşik değiştirmez. Eşik yalnız `r2-gates.json`'da, C-00 kaydıyla, ölçümden önce değişir (`tools/qa/r2-measure-contract.md` "Changing a threshold").
>
> **YENİDEN KESİM (S-10, 06.10 ~07:30; C-00 isteği, kural §3.16) — bu blok aşağıdakinin üstüne geçerlidir.**
> - **SON KESİM r6 (C-00 07:17, D8 guard tazelemesi): taban main `a9429acd`, dal `wp-091-s10-r2-gates-r6`.** `d49f7b2a..a9429acd`: `f30c5c26` + `78b6a3a2` (WP-088 r2 yönlendirici / C1 geçidi), `2daa78fe` + `2a891ec8` + `a9429acd` (WP-089 G4 D8 pencere defteri ve FIFO kuyruğu); `tools/qa`'da yalnız `oos_rule.py`, `rc.sh`, `tests/test_oos_rule.py`, bu dalın dosyalarına dokunmaz. r5 → r6: `961d75e4`→`5af3ebba`, `ed19b49d`→`addc25da`, `a34c2ba5`→`c848e88c`, `00f8c5fa`→`758ef67b`, `d85181d1`→`277605c1`, `71a5680c`→`24b6841c`, `74b1796a`→`bd03c4ab`, `eebcfc26`→`c0601a5b`, `9d8b52aa`→`a666cfa4`, `ccd29246`→`323322b5`, `0effb978`→`44e39683`, `0ecc1037`→`ce541df3`, `d3b024be`→`b877cd7c`, `57565106`→`90309f82`, `46ada9de`→`7f2bb967`, `b38d5713`→`49650397`, `46ef2485`→`679aa7d1`, `51c3523a`→`d1d5a4ae`. 18 çiftin hepsinde patch-id ve konu aynı; dalın toplam katkısının patch-id'si iki kesimde aynı. Aşağıdaki r5 SHA'ları bu eşlemeyle okunur; `d49f7b2a` tabanını anlatan madde r5 kesiminin kaydıdır. Lint r6 ucunda: `8ba256ec`'siz ERROR 1 (KEY-MISSING U1) · WARN 12 · INFO 20; **`8ba256ec`'li ERROR 0 · WARN 12 · INFO 20** — WARN: NO-WRITER H1b, H1c, H2, H4a, H6, L1, L1t, L2, L3, L4, L5, Z1.
> - **Yeni taban: main `d49f7b2a`.** (`c7848efa..d49f7b2a`: `67233774` S-08 S3 Dense-40 kaynak kuralı istisnası, `28af89c8` NavMesh binary, `d49f7b2a` BotInputLostIsAWaitTests hermetik — gt N-2 kapandı.) `86e427a2..c7848efa` arasında `tools/qa`'ya girenler: S-10 parti 3 QA araç düzeltmeleri (`49377ba0`, `77326d50`, `bb42b10e`, `c7848efa`; log_scan / aggregate / wp064_gate / lint sparse koruması), `a4066f92` (S-08 S3 yazıcısı `r2_s3.py`; S3 lint'te TOOL-WRITER), `d6b2543e` + `55844a03` (S-05 `r2_z3.py`; Z3 ve P3 CHAIN-MADE, zincirin `z3` aşaması bunu çağırır), `a7fe1e52` (Z3b `tests[]` = kendi yazıcısı `ZombieR2Tests.Z3b_40Zombi_SetPassPayi_DokuButcesi`; TEST-EXPLICIT kalktı), `a82a5c6c` (B-09 testleri). `tools/qa` dışı: `cfc42f69` (S-06 B2d/B2e — tutuş varsayılan AÇIK; H1'in env'siz oturumu artık tutuş açık ölçer), `283591b8`.
> - **S-10 dalı `wp-091-s10-r2-gates-r5`** (eski SHA → yeni): `bf177e7f`→`961d75e4`, `db2022af`→`ed19b49d`, `6e0b41d1`→`a34c2ba5`, `510c62f9`→`00f8c5fa`, `b6f4d355`→`d85181d1`, `5e213eb2`→`71a5680c`, `fa0e9ff1`→`74b1796a`, `61800dc0`→`eebcfc26`, `69a5755a`→`9d8b52aa`, `7b25a393`→`ccd29246`, `d495f245`→`0effb978`, `1e7abcc8`→`0ecc1037`, `d48627ae`→`d3b024be`, `7a203d74`→`57565106` (gt RB-1 düzeltmesi). Her çiftin +/- satırları ve mesajı birebir aynı; üç sözleşme çakışması iki tarafı da tutarak çözüldü (Z3 sonra U1; H1b/H6 sonra P3; P2 sonra S3); ara kesimler `wp-091-s10-r2-gates-r3` (`a7fe1e52`) ve `-r4` (`c7848efa`) C-00'ın taban düzeltmeleriyle yerini bu dala bıraktı, içerik aynı. Ek: `b38d5713` (yalnız test: z3 aşaması artık gerçek `r2_z3.py`'yi koştuğu için M5 testi `a4/perf-r1` yoluna bakar).
> - **Lint (bu dalın ucu, temiz kopya):** `8ba256ec`'siz ERROR 1 (KEY-MISSING U1) · WARN 11 · INFO 21; **`8ba256ec`'li ERROR 0 · WARN 11 · INFO 21** — WARN: NO-WRITER H1b, H2, H4a, H6, L1, L1t, L2, L3, L4, L5, Z1. S-09 zinciri (`7c85a9da → 4f359358 → bcde16dc → 8ba256ec → fd2bed18 → d966d2d9 → 3234ead7`) bu daldan önce girer (C-00 06:40); `2d566a0b` alınmaz.
> - **C-00 06.10 H1 / H1b / H1c kararı (bu dalın son commit'i):** H1 sol el ölçüsü avuç temas noktasından (bilekten değil) `grip_l`'e; H1b `palmToWeaponMaxDeg` ≤ 35 (GEÇİCİ); YENİ **H1c** (S-06): `elbowMaxDeg` ≤ 165, `reachMarginMinCm` ≥ 1 (S-02'nin erişim ön çalışmasının tanımı), yazıcısız (lint WARN NO-WRITER). H1 satırındaki "`elbowMaxDeg` ≤ 165 kontrolüne dönecek (PLANNED-CHECK)" artık H1c'dir; H1 dirseği yalnız raporlar. Kapı sayısı 36 (aşağıdaki tablolar 35'i gösterir). Lint bu commit'te: `8ba256ec`'siz ERROR 1 (KEY-MISSING U1) · WARN 12 · INFO 20; **`8ba256ec`'li ERROR 0 · WARN 12 · INFO 20** (WARN'a NO-WRITER H1c eklendi, INFO'dan PLANNED-CHECK H1 düştü).
> - **H1 / H1b / H1c yazıcıları (WP-083 B2f, main `18320dab`):** H1 rapora `holdOn` ekledi (§6.13). H1b ve H1c `measuredBy` bu dalda, zincirin r2 aşamasında tam adla. H1c testi `R2HeroOlcumTests.H1c_SolKol_Erisim_TumKlasikSilahlar_DurusYuruyus`. Lint ERROR 0 · WARN 9 (NO-WRITER H2, H4a, H6, L1, L1t, L2, L3, L4, L5).
> - **C-00 06.10 14:08 kaydı (S3, p5):** +0,8 dB sonrası üstten marj +1,13 ✓, M ✓, A9 ✓; TP payı 0,05–0,09 dB (çok ince), kök neden isabet yığını, S-08 kaynağında düzeltir (hedef TP ≤ −1,5); kapı değeri FINAL'in temiz penceresinde yazılır → S3 satırı.
> - **C-00 06.10 13:30 kayıtları:** Z3 kapsamı (`materialsPerFamilyMax` zırh + tier dahil ≤ 4; `atlasesPerFamilyMax` yalnız aile atlasları ≤ 2; rapor `armorTierAtlasesTotal`) ve z3 aşaması `z3-static.json`'u bağlar → Z3 satırı. Perf kademe eşleşmesi (Yüksek DEC-092 16,6 / 20; 8,0 ms Performans DEC-088) → P1, P4 satırı. C-00 14:50: +0,5 ms R2 kapısı değil; S-05'in kaskad probunun yerel kabul ölçütü.
> - **C-00 06.10 13:05 kaydı: DEC-077 (Z1) gerçek kırmızı.** Bağlama hatası kapandıktan sonra `583ed244`'te siluet 0,234, bant 0,014 (min 0,007); üç check de eşiğin altında. Eşik düşürülmez, düzeltme zırh okunurluk tarafında (`840e3768` gövdesi; Z1 satırı (3)). S3'ün +0,8 dB'i main'de (`96507557`), doğrulama ölçümü bekleniyor. Taban main `3b0e4327` (dal `wp-091-s10-r2-followup-r4`).
> - **C-00 06.10 10:15 kayıtları:** (1) MD2 (WP-084 1c): fit C'de dō üst giysiyle örtülür (önden %4–27, arkadan %21–78); A/B ≤ %3 assert, fit C bilinen kırmızı + rapor anahtarı, kalıcı çözüm AP-008D dō refit → Z1 satırı. (2) S3 üstten `NearFieldDb` +0,8 dB kararı; S-08 doğrulama penceresinde ölçer → S3 satırı.
> - **S-08 p4 ölçümleri (C-00 06.10 08:46):** S1 0,3095, S2 0/0/0/0 ve S4 v2 → **PASS**. S3 → **FAIL**: `weaponMarginLu` +0,40 < +1 (N-6, gerçek bulgu); TP −1,36 dBTP, M en çok −13,68 LUFS ve A9 GR 0 eşik içinde. S3 KİRLİ (pencerenin load1 en büyüğü 54, S4 adımında; S3 adımları 20–47). `r2_s3` ölçütü değişti (`8626d147`): yabancı Unity / Player / Blender / ffmpeg; yük ve CPU boşta yalnız rapor. S1/S2/S4 load1 22–54 altında ölçüldü (gt KP-15). C-00 06.10 kararıyla (`8626d147`) yük ölçüt değil; r2_s3 ve zincirin r2q denetimi yabancı Unity / Player / Blender / ffmpeg'e bakar. Değerler S1–S4 satırlarında; §2 sayımları yeniden hesaplanmadı.
> - **C-00 06.10 H1b kaydı (`wp-091-s10-r2-followup`, r6'dan sonra):** H1b'ye `palmGapMaxCm` ≤ 1,5 check'i ve `wristClampShare`, `wristMaxDeg`, `thetaSwitchCount` rapor anahtarları eklendi; eksen tanımı ve eşik türetme kuralı sözleşmede. H1c erişim payı canlı iskeletin kemik boylarıyla, IK'nın bilek hedefi `WeaponGripIk.LastWristGoal`'a. `measuredBy` (H1b, H1c) ve H1 `holdOn` S-06 B2f'ten SONRA ayrı commit'te.
> - **Aşağıdaki satırlardan bu blokla değişenler:** H1 (varsayılan yol tutuş açık, `cfc42f69`), S3 (yazıcı main'de, değer ölçülmedi), Z3 ve P3 (yazıcı main'de; Z3'ün statik anahtarları S-05'in EditMode testi `Z3CharacterBudgetTests…` AP-025 alımıyla gelene kadar BLOCKED — zincire `measuredBy` ile bağlanması önerildi), Z3b (zorunlu test = yazıcı, `measuredBy` oturumu koşar). Hazırlık sayımları (§2) bu bloğa göre YENİDEN HESAPLANMADI; S3, Z3, P3, Z3b satırlarının hazırlık hücreleri eski durumu gösterir.

> **Durum anı: entegrasyon sonrası beklenen ağaç; main'de olmayanlar SHA ile.** Üç parçadan oluşur:
> - **main `86e427a2`** (S-10 dalının tabanı). `86e427a2..c9dfd891` arasında `tools/qa` farkı yok. Bu aralıktaki commit'ler: `7897f642` (WP-085 B5, AP-027 v003g-recut kuyrukları; S1, S2, S4 ile ilgili), `0e0a7629` ve `4680b537` (codex denetimi), `c9dfd891` (kural kitabı dersleri).
> - **+ S-10 dalı `wp-091-s10-r2-gates-r2`** (main'de değil, uç `1e7abcc8`):
>   - parti 1: `bf177e7f`, `db2022af`, `6e0b41d1`, `510c62f9`, `b6f4d355`;
>   - parti 2: `5e213eb2`, `fa0e9ff1`, `61800dc0`, `69a5755a`, `7b25a393`, `d495f245`, `1e7abcc8`.
> - **+ S-09 `8ba256ec`** (dal `wp-081-cine-b`, main'de değil): U1'in iki ölçüm yazıcısı. `db2022af` ile ardışık girer, önce `8ba256ec` (`db2022af` gövdesi).
>
> **Sonra gelen (durum anına dahil değil):** `a82a5c6c` (`tools/qa/tests/`'e iki yeni test dosyası, `test_aggregate.py` ve `test_log_scan.py`; r2 araçlarına dokunmaz) ve `283591b8` (WP-084 TierLit tanı testleri). main şimdi `283591b8`.
> **main'de olmayan öteki teslimler** (satırlarda SHA ile):
> - S-03 `wp-084-s03-slice1`: `e81b9500`, `3f4c737a`, `840e3768`. Teslim edilmedi (`K/wp-084/DEVIR-06.10.md`). Dalın ilk commit'i `d0d0580b`'nin eşdeğeri main'de: `283591b8` (aynı patch-id).
> - S-05 `wp-091-s05-z3-r2`: `99a33758`, `e6c8b755` (`tools/qa/r2_z3.py`).
> - S-08 `wp-085-b03-gc`: `98e3dffd` (S4 kaynak assert'i).
> - S-06 `wp-083-b2e`: WIP `dee54ffa` + 06.10 düzeltmeleri, commit'lenmemiş (`K/wp-083/DEVIR-06.10.md`).
>
> **Önceki sürüm:** durum anı 06.10 01:45, main `d95d237a` (B-07: `642c3e1d`…`86e427a2`). `d95d237a..86e427a2` arasında main'e şunlar girdi: `db77df0a` ve `35a00e65` (S-02 §19 G3/B1 ve B2), `56805c09` + `16a95f0e` (Z1 DEC-077 eşikleri). Z1 satırı önceki sürümde bu iki commit'i içermiyordu.
>
> **Denetleyici:** `python3 tools/qa/r2_gates_lint.py --repo "<repo>" --json`. Bu güncellemede koşuldu; ağaçlar scratch'te `git archive` ya da kopyayla kuruldu.
> - main `86e427a2` ve `283591b8`: ERROR 0 · WARN 15 · INFO 13, 14 `R2Olcum.Yaz` çağrısı.
>   - WARN: EXPLICIT-UNRUN H1 · NO-WRITER H2, H4a, L1, L1t, L2, L3, L4, L5, P2, P3, S3, Z1, Z3 · TEST-EXPLICIT Z3b.
> - S-10 ucu `1e7abcc8`, `8ba256ec`'siz: ERROR 1 (KEY-MISSING U1) · WARN 15 · INFO 17.
>   - Zincir bu ağaçta başlamaz: `wp078_accept --round R2 --dry-run` exit 3, "REFUSED: r2_gates_lint ERROR 1 … KEY-MISSING U1".
> - **Durum anı ağacı** (`1e7abcc8` + `8ba256ec`'nin `UiTextInventoryTests.cs`'i): **ERROR 0 · WARN 15 · INFO 17, 14 çağrı.**
>   - WARN: NO-WRITER H1b, H2, H4a, H6, L1, L1t, L2, L3, L4, L5, P3, S3, Z1, Z3 · TEST-EXPLICIT Z3b.
>   - main'e göre: H1 EXPLICIT-UNRUN ve P2 NO-WRITER kalktı (P2 artık INFO CHAIN-MADE); H1b ve H6 NO-WRITER geldi.
> - Bilgi, deneme birleştirmesi (scratch; entegrasyon değil): durum anı ağacı + S-05 `e6c8b755` → ERROR 0 · WARN 13 · INFO 20 (Z3 ve P3 CHAIN-MADE).
>   - `r2-gates.json` çakışmasız birleşti.
>   - `r2-measure-contract.md`'de iki bitişik-ekleme çakışması çıktı (H1b/H6 ↔ P3 ve U1 ↔ Z3 bölümleri). Deneme iki tarafı da tuttu.
> - Önceki sürümde: @ `18f3d01e` WARN 14 · INFO 14 (15 çağrı); @ `173890ac`, `d95d237a`, `db77df0a` WARN 14 · INFO 13 (14 çağrı).
>
> **GT karşı okuması (önceki sürüm, B-07):** GEÇTİ-NOTLU, kırmızı bayrak 0 (`K/c-00/R2/GT-B07-KARSI-OKUMA-06.10.md`). O sürümde düzeltilenler: ŞART B1–B3, NOT B4–B9, BİLGİ B11 ve B12. GT değerleri artefakt düzeyinde yeniden okudu, Unity'de yeniden üretmedi. **Bu güncellemeyi GT okumadı.**
> **Entegrasyon:** bu sürüm `wp-091-s10-r2-gates-r5` dalında (main `d49f7b2a` üstünde); dalın commit'leriyle birlikte, S-09 zincirinden sonra alınır.

## Kaynaklar ve kısaltmalar

- **Sözleşme:** `tools/qa/r2-gates.json`, `tools/qa/r2-measure-contract.md`, `tools/qa/r2_gates.py`, `tools/qa/r2_gates_lint.py`, `tools/qa/wp078_accept.py`, `tools/qa/wp078_perf.py`, `tools/qa/wp078_perf_report.py`, `tools/qa/wp078_report.py`, `tools/qa/r2_load_time.py`, `tools/ci-isolated.txt`. S-05 dalında: `tools/qa/r2_z3.py`.
- **Plan:**
  - `K/wp-078/R2-KABUL-ZINCIRI.md` (06.10 güncellemesi), `K/wp-078/R2-KABUL-TASLAK.md`, `K/wp-078/SUPERSEDED-4c48563e.md`;
  - `docs/c00/R2-PLAN.md`, `docs/c00/BEKLEYEN-MESAJLAR.md` §7, §11, §15, §18, §19, §20;
  - `docs/c00/BRIFING-YENI-HESAP-2026-10-05.md` §6, §7, §9, §14;
  - `docs/c00/SABIT-KURALLAR-R2-2026-10-06.md` Ç1, Ç21.
- **Kanıt:**
  - `K/wp-*/evidence/**/r2-*.json`: 58 adet `dd-r2-measure-v1` artefaktı. Önceki sürümdeki 56'ya ek olarak `K/wp-084/evidence/r2-840e3768/r2-Z1.json` ve `r2-Z4.json` (06.10 03:11–03:12). 58'inin de `samples` alanı > 0 tamsayı (bu güncellemede tarandı);
  - `K/ci/<sha>/`: main-ci;
  - `K/wp-078/evidence/84941661/`: R1 FINAL'';
  - KADRO incelemeleri: `K/wp-078/kadro-0610/` (gt parti 1, test-engineer P2, game-test-engineer P2/Z3);
  - devir notları: `K/wp-083/DEVIR-05.10.md`, `K/wp-083/DEVIR-06.10.md`, `K/wp-084/DEVIR-06.10.md`, `K/wp-085/DEVIR-05.10.md`.
- `K/` = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/`
- `PM/` = `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Tests/PlayMode/`
- `EM/` = `prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Tests/EditMode/`
- `APM/` = `prototype/DeathsDemolition_PlayableConcept/Assets/_Art/Tests/PlayMode/`
- **Satır numaraları** main `86e427a2` içindir. `d95d237a` ile aynıdır: matrisin andığı test dosyalarının hiçbiri `d95d237a..283591b8` arasında değişmedi. İstisnalar:
  - U1 satırları `8ba256ec`'nindir;
  - `tools/qa/*.py` satırları S-10 ucunundur (`git -C <worktree> show 1e7abcc8:<yol>`).
- **"main'de"** = `git merge-base --is-ancestor <sha> main` ya da `git cherry main <sha> <sha>~1` sonucu `-` (eşdeğer yama).
- **Oturum:** `measuredBy` olan kapıyı zincir kendi guard'lı oturumunda koşar:
  - aşama `r2` (A3'ten sonra) ya da `r2q` (A4, B9 ve Z3'ten sonra, sessiz pencere);
  - her oturum, öteki sahiplerin anahtarları silinmiş olarak başlar: `/usr/bin/env -u …`, kaynak `OWNER_SWITCHES` (`DD_HOLD_IK`, `DD_EDC_WEAPONS`) + her `measuredBy.env` adı; oturum yalnız kendi `env`'ini taşır (`d495f245`, `tools/qa/wp078_accept.py:84`, `:1056-1063`).
  - `measuredBy` yoksa artefakt yalnız sahibin `<E>/r2/` koşusundan gelir. O koşu `DD_R2_COMMIT=<FINAL>` ile yapılmalıdır, yoksa sonuç "başka commit = FAIL" olur.
- **Yargıç (`fa0e9ff1`):** `samples` alanı var ama sonlu ve > 0 bir sayı değilse kapı BLOCKED olur ("nothing was measured"). `samples` alanı olmayan artefakt eskisi gibi yargılanır. Zincirin P2 ve P5 artefaktları `samples` taşır (`61800dc0`).

**Okuma kuralı:**
- **"Son ölçülen değer"** kayıttaki en yeni geçerli değerdir. Hiçbiri FINAL commit'inde değildir; FINAL'de hepsi yeniden ölçülür.
- **İki hazırlık sütunu var** (GT B1, C-00 06.10).
- **"FINAL'e hazır (katı tanım)"** birincil sütundur.
  - **EVET:** yazıcı ve oturum var, son ölçüm main soyunda ve eşik içinde, **bekleyen teslim yok ve R2 ölçümü var.** Bekleyen teslimi ya da R2 ölçümü olmayan kapı EVET olamaz.
  - **KOŞULLU:** altyapı hazır ama bir teslim, yeniden ölçüm, ilk R2 ölçümü ya da kırılganlık bekliyor. Rapor kapıları da burada; FINAL'i düşürmezler.
  - **HAYIR:** BLOCKED ya da FAIL beklenir.
  - Sayım: **EVET 1 · KOŞULLU 12 · HAYIR 22.**
  - Önceki sürüm 1 / 12 / 20 idi. Değişen: H1 HAYIR → KOŞULLU; Z1 KOŞULLU → HAYIR; yeni H1b ve H6 HAYIR.
- **"Durum anı ağacı FINAL olsaydı"** ikincil sütundur. Önceki adı "Bugünkü main FINAL olsaydı"; tanım aynı, yargılanan ağaç durum anı ağacıdır. Bekleyen teslimleri yok sayar ve şunu sorar: zincir bu ağacı yargılasaydı, son geçerli ölçüme göre ne çıkardı?
  - Sayım: **EVET 8 · KOŞULLU 5 · HAYIR 22.**
  - Önceki sürüm 8 / 5 / 20 idi. Değişen: Z1 EVET → HAYIR; P2 KOŞULLU → EVET; H1 HAYIR → KOŞULLU; yeni H1b ve H6 HAYIR.
  - HAYIR 22 iki okumada da aynı kapılardır.

---

## 1. Ana matris (35 kapı)

| Kapı | N2 | Sahip | Ölçen test (tam ad · dosya:satır) | R2Olcum.Yaz | Anahtar(lar) | Eşik / ölçüt | Report-only? | Son ölçülen değer · kaynak | İnsan kapısı | Engel / bağımlılık | FINAL'e hazır (katı tanım) | Durum anı ağacı FINAL olsaydı |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **H1** | N2-05 (N2-04 hero v2 ile dirsek) | S-06 | `DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1_ElTutamak_TumKlasikSilahlar_DurusYuruyus` · PM/R2HeroOlcumTests.cs:60 (`[Explicit]`, Diagnostics; yalnız TP, duruş + yürüyüş). **`measuredBy` var (`6e0b41d1`):** tam ad, playmode, `quiet:false`, **`env` yok** → aşama `r2`, kendi guard'lı oturumu | var · PM/R2HeroOlcumTests.cs:115 | `maxCm`; rapor `elbowMaxDeg` | `maxCm` ≤ 1,0 cm. **Tanım karar B (`bf177e7f`):** varsayılan oyuncu yolu, iki el; sağ el `socket_weapon_r` ↔ tanımın tutamak noktası `mount.TransformPoint(GripOffset)` (`marker_grip` değil), sol el destek noktasına. `elbowMaxDeg` hero v2 alımından sonra ≤ 165 kontrolüne dönecek (lint INFO PLANNED-CHECK) | kısmen (`elbowMaxDeg`) | **Varsayılan yolda (tutuş kapalı) ölçülmedi; r2-H1.json yok.** Tutuş açıkken son değer: `maxCm` **0,40**, `elbowMaxDeg` **173,1°**. Kaynak: `K/wp-083/evidence/b2m-r2.log` (`wp083.h1 hold=on`, 06.10 02:36). Koşu S-06'nın commit'lenmemiş ağacında, dal `wp-083-b2e` (`K/wp-083/DEVIR-06.10.md`). Önceki: 0,40 @ b2i (05.10 18:56); `elbowMaxDeg` 152,7° hero-v2-reach (`K/wp-082/evidence/hero-v2-reach/README.md:22`) | hayır (H5 ayrı) | (1) Oturum `env`'siz koşar; zincir `DD_HOLD_IK`'yı `env -u` ile siler (`d495f245`). Durum anı ağacında testin SetUp'ı `WeaponHoldIk.Enabled = (DD_HOLD_IK == "1")` yapar (PM/R2HeroOlcumTests.cs:42), yani tutuş kapalıdır; main varsayılanı da kapalı (`…/Characters/WeaponHoldIk.cs:35`). (2) Varsayılanı açan S-06 B2d-on/B2e (WIP `dee54ffa`, main'de değil; B2d `9f87b7d4` main'de) gelince aynı oturum tutuşu açık ölçer, sözleşme değişmez (`6e0b41d1` gövdesi). (3) gt B5 (`K/wp-078/OUTBOX-PENDING.md` 1): `dee54ffa`'daki R2HeroOlcumTests `elbowMaxDeg` yazmıyor. B2e yeni dalda kurulurken yazım kaybolursa lint KEY-MISSING (ERROR) verir. (4) Hero v2 (AP-024C, WP-082) alımından sonra yeniden ölçüm ve dirsek kontrolü. (5) `4c48563e` (`wp-091-r2-h1-def`) ALINMAZ (`K/wp-078/SUPERSEDED-4c48563e.md`); yerine `bf177e7f` | **KOŞULLU**: yazıcı ve oturum var; S-06 B2d-on/B2e teslimi ve varsayılan yolda ilk R2 ölçümü bekleniyor | **KOŞULLU**: varsayılan yolda (tutuş kapalı) değer yok; sonuç ölçülmeden bilinmez |
| **H1b** | N2-05 (kayıtta N2 kodu yok; WP-083 B2e AD MUST-2) | S-06 | `DD.Prototype.Tests.PlayMode.R2HeroOlcumTests.H1b_SolEl_Kavrama_TumKlasikSilahlar_DurusYuruyus` (WP-083 B2f `e8f3ae19` / `18320dab`, `[Explicit]`, Diagnostics). **`measuredBy`:** tam ad, playmode, `quiet:false`, env yok → aşama `r2` | var (B2f; anahtarlar sözleşmeyle birebir, lint ERROR 0) | `palmToWeaponMaxDeg`, `fingertipGapMaxCm`, `palmGapMaxCm`; rapor `weaponsMeasured`, `framesMeasured`, `wristClampShare`, `wristMaxDeg`, `thetaSwitchCount` | ≤ 35° (GEÇİCİ) · ≤ 1,5 cm · ≤ 1,5 cm. C-00 06.10 H1b kaydı (a)–(d): avuç noktası P'den kavranan elemanın kendi eksenine inen dik (yön silah çerçevesinden: el kundağında +Z, rl_1'de +Y; çizgi P istasyonundaki LOD0 kesit merkezinden, namludan değil); bağlayıcı eşik = düzeltme sonrası AD-iyi karelerin en yükseği + 5°, tavan 30°; thetaSwitchCount hedefi (duruşta 0, yürüyüş döngüsü başına ≤ 1) not; eski `palmUpMaxDeg` ≤ 30 AD'nin iyi dediği lmg_2'de 40° okudu, rl_1 dikey tutamağında anlamsızdı. 35 kesin değil: S-06 iyi karelerden türetir, C-00 yazar. Kapsam: ön tutamaklı her klasik silah (rl_1 dahil), duruş + yürüyüş, tutuş IK'sı tam ağırlıkta. C-00 06.10 kaydı (`510c62f9`) | kısmen (5 rapor) | **ölçülmedi** | hayır (kaynak AD MUST-2) | (1) S-06 testi ve `measuredBy` aynı commit'te gelir; yazmayan bir `measuredBy` lint ERROR'dur (`510c62f9` gövdesi). (2) Kaynak kayıtta: silah ve kare > 0 olmadan artefakt yazılmaz. (3) Plan: WP-083 B2f (sol el dönüş hedefi silah çerçevesinde), B2e girdikten sonra main'den yeni dalda (`K/wp-083/DEVIR-06.10.md`) | **KOŞULLU → bilinen kırmızı (S-06, adıyla):** B (smg_1) kavrama kapalı, B2e eli (parmak ucu 14 cm, avuç 108°); lmg_2 kavrama kapalı (avuç 116°, avuç boşluğu 4,1 cm); sr_1 parmak ucu 2,6 cm | **HAYIR** (FAIL beklenir: S-06'nın adıyla bilinen kırmızıları) |
| **H2** | N2-05 | S-06 | `DD.Prototype.Tests.PlayMode.R2HeroGovdeTests.H2_SilahGovdeGomulmesi_TumKlasikSilahlar_DurusYuruyus` (WP-083 R2 `f114d24c`, `[Explicit]`). **`measuredBy`:** tam ad, playmode, `quiet:false` → aşama `r2` | var (`f114d24c`) | `clipFrames` | == 0 (silah gövdesi ↔ omuz/göğüs kapsülü; TASLAK:21) | hayır | **ölçülmedi.** KundakOmuz PASS b2i (`K/wp-083/evidence/b2i-r2.xml`), H2'nin yerini tutmaz | hayır | S-06: bugün lmg_1 / lmg_2'de kırmızı olabilir; ilk değer r2w1 penceresinde | **KOŞULLU**: yazıcı + oturum var; lmg_1 / lmg_2 riski (S-06) | **KOŞULLU** (lmg_1 / lmg_2 riski) |
| **H3** | N2-05 | S-02 (temas S-06) | `DD.Prototype.Tests.PlayMode.WeaponReloadMagazineTests.H3_SarjorluSilahlar_VuruslarTam_HavadaKareYok` · PM/WeaponReloadMagazineTests.cs:188 (CI'da) | var · PM/WeaponReloadMagazineTests.cs:244 (`double?`) | `missingEvents`, `contactDeviationFramesMax`, `floatingMagazineFrames` | == 0 · ≤ 2 kare · == 0 | hayır | 0 / **null** / 0 @ `ed8adfdc` (main): `K/wp-069b5/evidence/b6h3-measure/r2-H3.json`. Bu artefakt `5a45e3aa`'dan **önce** yazıldı (12:51 < 14:57). `5a45e3aa` main'de; main'deki yazıcı artık null yazmıyor, temas yoksa 16 yazar (GT B8). Temas probu sonrası (S-06 tanı, R2 dışı): TP en çok 0 kare (14 vuruş, 7/7 bağlı), FP 0 kare ama yalnız 6 vuruş (3/7 bağlı). Kaynak: `K/wp-083/evidence/b2g-r2.xml` (`wp083.h3contact`) | hayır | Prob değerli bir r2-H3.json henüz yok. Bağımlılıklar: S-06 B2d-on/B2e (temas IK'sı) teslimi, AP-026 reload klipleri (hero v2), §12 olay sözleşmesi (main'de) | **KOŞULLU**: main'deki yazıcı `5a45e3aa`'dan beri sayı yazıyor. El yuvaya ulaşmazsa `NoContact` = 16 → FAIL (PM/MagazineContactProbe.cs:33). Vuruş eksikse assert artefakttan önce kırılır → BLOCKED (PM/WeaponReloadMagazineTests.cs:240-245). S-06 temas IK'sı (B2d-on/B2e) main'de değil; sayısal r2-H3.json hiç yok | **KOŞULLU**: temas ≤ 2 kare → PASS; el yuvaya ulaşmazsa 16 → FAIL; vuruş eksikse BLOCKED. Temas IK'sı main'de olmadığı için FAIL olasılığı yüksek (GT B8) |
| **H4** | N2-05 | S-02 | `DD.Prototype.Tests.PlayMode.WeaponModelSliceTests.FP_NamluKalkisi_Tablo_HerSilah` · PM/WeaponModelSliceTests.cs:720 (`[Explicit]`, Diagnostics); ayrıca `tests[]` zorunlu kimliği | var · PM/WeaponModelSliceTests.cs:810 | `yawRollExcessDegMax` | ≤ 1,0° | hayır | **0,1** @ `1acc3155` (main): `K/wp-069b5/evidence/b6sh-measure/r2-H4.json` | hayır | S-02 tepme→pivot WIP `6ea4492a` ve hero v2 FP'yi değiştirir → FINAL'de yeniden ölçülür | **KOŞULLU**: `6ea4492a` ve hero v2 sonrası yeniden ölçüm bekleniyor | **EVET** (0,1 @ `1acc3155`) |
| **H4a** | N2-05 | S-06 | `DD.Art.Tests.PlayMode.AnimationTests.FpKollar_Kadraj_KolPayi10_KameraMesafesi15_ElSilahta` (WP-083 R2 `6711f57f`; `[Explicit]` değil, CI'da da). **`measuredBy`:** tam ad, playmode, `quiet:false`, env yok → aşama `r2`. Kollar açık modda ölçer (C-00 06.10: kolların göründüğü tek mod; varsayılan yolda kapalı) | var (`6711f57f`; anahtarlar sözleşmeyle birebir) | `armOffscreenFrames`, `armInCameraFrames` | == 0 · == 0 | hayır | **ölçülmedi** (S-06'nın ilk Unity değeri r2w1 penceresinde; 14:17 koşusundan beklenen 3 karede 0 / 0) | hayır | Öneri (S-06, karar C-00'da): `tests: [tam ad]` eklenirse testin kendi kabulü (kol ekran payı ≤ %10, el ≤ 1 cm, dinlenme ≤ 0,3 cm) kapıya girer; bugün anahtarlar bunları kapsamaz | **KOŞULLU**: yazıcı ve oturum var; ilk değer bekleniyor | **KOŞULLU** (beklenen 0 / 0) |
| **H4b** | N2-05 | S-02 | `DD.Prototype.Tests.PlayMode.WeaponModelSliceTests.FP_AnahtarPenceresindeAtes_KlipSizintisiYok` · PM/WeaponModelSliceTests.cs:836 (CI'da); `tests[]` zorunlu | var · PM/WeaponModelSliceTests.cs:902 | `leakDegMax` | ≤ 1,0° | hayır | **0** @ `1acc3155` (main): `K/wp-069b5/evidence/b6sh-measure/r2-H4b.json` | hayır | H4 ile aynı (FINAL'de yeniden) | **KOŞULLU**: H4 ile aynı teslimleri bekliyor | **EVET** (0 @ `1acc3155`) |
| **H5** | N2-04 | S-06 | AD turu → `<E>/r2/reviews/H5.json` (dd-r2-review-v1) | — | `verdict`, `must[]` | KABUL, ya da KABUL_DÜZELTMELERLE + bütün MUST kapalı | — (insan) | **yok** (K'de dd-r2-review-v1 dosyası yok). Öncül: G1b = V1 + koyu omuz (BRIFING §9) | **EVET** (AD; KADRO art-director, Opus; SABIT-KURALLAR Ç1) | AP-024C iş 2/3/5 sürüyor (`K/c-00/aktif-ajanlar/C-00.md`, 06.10 00:49) → WP-082 alımı → FINAL commit'inde AD | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |
| **H6** | N2-05 (kayıtta N2 kodu yok; WP-083 B2e AD MUST-3) | S-06 | `DD.Prototype.Tests.PlayMode.R2HeroGovdeTests.H6_TpSilahGorunurlugu_TumKlasikSilahlar_DurusYuruyus` (WP-083 R2 `f114d24c`, `[Explicit]`; grafik aygıtı ister, PlayMode `-nographics`'siz koşar). **`measuredBy`:** tam ad, playmode, `quiet:false` → aşama `r2` | var (`f114d24c`; `r2-H6-perweapon.csv` yan dosya, bağlanmaz) | `visibleShareMin`, `muzzleInsideSilhouetteFrames`; rapor `weaponsMeasured`, `secondWorstShare` | ≥ 0,25 · == 0. Kapsam: omuz üstü TP oyun kamerası, her klasik silah, duruş + yürüyüş. Silah başına tablo artefaktın yanında (`r2-H6-perweapon.csv`). C-00 06.10 kaydı (`510c62f9`) | kısmen (2 rapor) | **ölçülmedi** | hayır (kaynak AD MUST-3) | S-06: bugün kırmızı beklenir (uzun silahlar gövde arkasında; çözüm WP-082 hero v2) | **HAYIR → bilinen kırmızı** (uzun silahlar gövde arkasında, WP-082) | **HAYIR** (FAIL beklenir) |
| **L1** | N2-06 | S-06 | **yok.** R2 anahtarı yazmayan adaylar: `DD.Art.Tests.PlayMode.AnimationLocomotionV3Tests.Oyuncu_YuruyusteBasanAyakKilitli_KaymaAz` · APM/AnimationLocomotionV3Tests.cs:169; `DD.Art.Tests.PlayMode.AnimationV002Tests.Oyuncu_KosuHalkasi_6_2ms_AyakKaymasi_2cmAlti_OranTavani` · APM/AnimationV002Tests.cs:191 | **yok** | `avg`, `p95` | ≤ 0,05 · ≤ 0,10 | hayır | **oyun içinde ölçülmedi.** Çevrimdışı FBX ön kanıtı: hero döngüleri avg ≤ 0,034 / p95 ≤ 0,090; zombi avg ≤ 0,019 / p95 ≤ 0,081. Kaynak: `K/c-00/codex/reports/C00-AP026C-RAPOR.md:34-35`; ":155 runtime L1/L1t/L3 ölçülmedi" | hayır | WP-083 kahraman klip alımı (`AP-026/exports/v002/hero-v002c` indeksi) + ölçüm testi | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L1t** | N2-06 | S-06 | **yok** | **yok** | `avg`; rapor `p95` | ≤ 0,10 | kısmen (`p95`) | **oyun içinde ölçülmedi.** Çevrimdışı L1t 3,6e-5: `C00-AP026C-RAPOR.md:78` | hayır | L1 ile aynı | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L2** | N2-06 | S-06 | **yok** | **yok** | `deviationMax` | ≤ 0,08 | hayır | **oyun içinde ölçülmedi.** Çevrimdışı hero ≤ 0,022, zombi ≤ 0,008: `C00-AP026C-RAPOR.md:34-35` | hayır | L1 ile aynı | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L3** | N2-06 | S-06 | **yok** | **yok** | `limbDegPerFrameMax`, `torsoDegPerFrameMax`, `transitionToSteadyRatioMax` | ≤ 20 °/kare · ≤ 10 °/kare · ≤ 1,5 | hayır | **oyun içinde ölçülmedi.** Çevrimdışı 142/142 geçiş PASS; uzuv 19,95 °/kare, gövde 9,05 °/kare (`C00-AP026C-RAPOR.md:77`). Reload klipleri kapı dışında, uzuv tepe 13,6–39,9 °/kare (`:126`) | hayır | L1 ile aynı | **HAYIR**: yazıcı yok → BLOCKED | **HAYIR** (BLOCKED) |
| **L4** | N2-06 | S-06 | `DD.Prototype.Tests.PlayMode.R2LokomotionIkincilTests.L4_IkincilHareket_DurusSonrasiOturma` (WP-083 R2 `c001a5e1`, `[Explicit]`, rapor). **`measuredBy`:** tam ad, playmode, `quiet:false` → aşama `r2`. Bugün yay katmanı yok; LocomotionLean'i ölçer (tek prosedürel gövde katmanı) | var (`c001a5e1`) | `amplitude`, `settleSeconds` | rapor (genlik > 0, sönüm ≤ 1 s; TASLAK:35) | **evet** (rapor kapısı) | **ölçülmedi** | hayır | Rapor kapısı, düşmez; ilk değer r2w1 penceresinde | **EVET** (rapor; yazıcı + oturum var) | **EVET** (rapor) |
| **L5** | N2-06 | S-06 | `DD.Prototype.Tests.PlayMode.R2LokomotionIkincilTests.L5_Kalabalik_FazSenkronuCiftleri` (WP-083 R2 `c001a5e1`, `[Explicit]`). **`measuredBy`:** tam ad, playmode, `quiet:false` → aşama `r2`. 40 zombi GateMix kalabalığı, en kötü örnek kare | var (`c001a5e1`) | `syncedPairShare` | ≤ 0,10 | hayır | **ölçülmedi** | hayır | İlk değer S-06'nın r2w1 penceresinde | **KOŞULLU**: yazıcı + oturum var; değer bekleniyor | **KOŞULLU** |
| **L6** | N2-06 | S-06 | AD turu → `<E>/r2/reviews/L6.json` | — | `verdict`, `must[]` | R1 ↔ R2 yürüme/koşma yan yana | — (insan) | **yok** | **EVET** (AD) | WP-083 alımı → R1/R2 video paneli | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |
| **Z1** | N2-03 | S-03 | `DD.Art.Tests.PlayMode.ArmorTests.Okunabilirlik_DEC077_Zirh_DeltaV_AltKumas_KomsuBant_ZeminHalkasi_Siluet` · APM/ArmorTests.cs:273. `tests[]` zorunlu; A3 izole oturumu: `tools/ci-isolated.txt` 2. girdi. Durum anı ağacında test DEC-077'yi assert etmiyor ve yazmıyor | **yok** (lint WARN NO-WRITER). Yazıcı ve `measuredBy` S-03'ün `840e3768`'inde: dal `wp-084-s03-slice1`, main'de değil, teslim edilmedi | `silhouetteChange`, `deltaVBandMean`, `deltaVBandMin`; rapor `deltaVUnderMean`, `deltaVUnderMin`, `deltaVGround`, `zombieEnvContrastMin`, `zombieEnvContrastMean` | ≥ 0,30 · ≥ 0,12 · ≥ 0,08. DEC-077, kullanıcı 16.09; main `56805c09`. `16a95f0e` `measuredBy`'yi yazıcıyla aynı commit'e bıraktı | kısmen (5 rapor) | **GERÇEK KIRMIZI (C-00 06.10 13:05, DEC-077):** bağlama hatası kapandıktan sonra @ `583ed244` (dal `wp-084-s03-slice1c`, main'de değil): `silhouetteChange` 0,2335 (≥ 0,30 ✗), `deltaVBandMean` 0,0138 (≥ 0,12 ✗), `deltaVBandMin` 0,0074 (≥ 0,08 ✗); 12 figür; rapor alt kumaş 0,0907 / 0,0702, zemin 0,0356, zombi↔çevre kontrast en az / ort. 0,0073 / 0,0776. Kaynak: `K/wp-084/evidence/r2measure-583ed244/r2-Z1.json`. Önceki @ `ca421d0c`: 0,2552 / 0,0182 / 0,0020 (`…/r2measure-ca421d0c/`). Daha önce: **FAIL.** Deterministik yazıcıyla `r2-Z1.json` @ `840e3768`: **0,2321 / 0,0155 / 0,00078** (12 figür; rapor: alt kumaş ort./min 0,0935 / 0,0621, zemin 0,0341). Kaynak: `K/wp-084/evidence/r2-840e3768/r2-Z1.json`. Uç ağacın yargıcı (`r2_gates`), test Passed sayılsa bile üç check'i FAIL verir. Ayrıca: v002'de 0,265 / 0,019 / 0,000 (`56805c09` gövdesi); R1 (`84941661`) aynı kodla iki koşu siluet 0,286 / 0,279, bant ort. 0,014 / 0,015 (`840e3768` gövdesi), R1 de DEC-077 altında | kısmen (okunurluk algısı → Z5) | (1) **Kapsam boşluğu (C-00 06.10):** R1 FINAL'' A3'teki Passed (`84941661`), DEC-077 eşiklerini hiç assert etmeyen testindi. Test yalnız siluet > 0,02'yi kontrol ediyor, bandı eski bir AD önerisine karşı raporluyordu (`56805c09` gövdesi). (2) Yazıcı + `measuredBy` (`840e3768`) teslimi. (3) Düzeltme AP-008C zırh okunurluk geçişi; eşik düşürülmez, Z1 kayıtta açık kırmızı (`840e3768` gövdesi). (4) `840e3768` öncesi tek commit iki koşuda 0,299 / 0,264 verdi; poz deterministik değildi (SABIT-KURALLAR §5.18) (5) **MD2 (C-00 06.10 10:15, WP-084 1c):** fit C'de (H2; Ultra her zaman C) zırhlı gövdenin dō'su üst giysiyle örtülür: önden %4–27, arkadan %21–78; A/B temiz (≤ %2,3). Karar: A/B'de görünüm başına ≤ %3 assert (S-03); fit C bilinen kırmızı, rapor anahtarı; kalıcı çözüm AP-008D dō refit. Rapor anahtarı R2Olcum ile bir R2 kapısına yazılacaksa adı ve kapısı S-03'ten gelir ve `r2-gates.json` satırıyla ardışık girer (yoksa lint KEY-EXTRA) | **HAYIR**: yazıcı main'de (WP-084 `e3946b7b`); DEC-077 gerçek kırmızı (üç check de altında, `583ed244`); fit C dō örtüşmesi bilinen kırmızı (MD2) | **HAYIR** (FAIL, DEC-077 gerçek kırmızı) |
| **Z2** | N2-03 | S-03 | `DD.Prototype.Tests.PlayMode.ZombieR2Tests.Z2_TierCiftleri_Ayrisir` · PM/ZombieR2Tests.cs:72 (fikstür `[Explicit]` :36) | var · PM/ZombieR2Tests.cs:127 | `indistinctTierPairs` | == 0 (siluet, luma ya da kroma; çubuklar **GEÇİCİ** onaylı, ZombieR2Tests.cs:44-49) | hayır | **0** @ `bc07ef0d` (eşdeğeri main'de), R1 görünümü: `K/wp-088/evidence/r2gates/r2-Z2.json` | kısmen (→ Z5) | AP-025C/D alımından sonra yeniden ölçüm; çubukların C-00 tarafından kesinleşmesi | **KOŞULLU**: zombi v2 sonrası yeniden ölçüm; çubuklar geçici | **EVET** (0 @ `bc07ef0d`, eşdeğeri main'de) |
| **Z3** | N2-03 | S-05 | Test yok. Yazıcı S-05'in `tools/qa/r2_z3.py` aracı; onu zincirin **`z3` aşaması** çağırır (`7b25a393`, `tools/qa/wp078_accept.py:939-990`) | **yok** durum anı ağacında (lint WARN NO-WRITER). `r2_z3.py` S-05 dalında: `99a33758` + `e6c8b755` @ `wp-091-s05-z3-r2`, main'de değil. O dalla birlikte lint INFO CHAIN-MADE | `setPassRatio`, `batchRatio`, `trianglesRatio`, `characterTextureMaxPx`, `atlasesPerFamilyMax`, `mapsPerAtlasMax`, `shippedCharacterMB`, `bonesMax`, `materialsPerFamilyMax`; rapor `materialsPerFamilyCrowdTotal`, `armorTierAtlasesTotal` | ≤ 1,10 · ≤ 1,10 · ≤ 1,15 · ≤ 2048 · ≤ 2 · ≤ 3 · ≤ 400 · ≤ 74 · ≤ 4. Kapsam (C-00 06.10 13:30, S-05): `materialsPerFamilyMax` zırh ve tier parçaları dahil (≤ 4); `atlasesPerFamilyMax` yalnız ailenin kendi atlasları (gövde, yüz, giysi; ≤ 2); zırh + tier atlasları rapor `armorTierAtlasesTotal` | kısmen (2 rapor: `materialsPerFamilyCrowdTotal`, `armorTierAtlasesTotal`; statik anahtarlar gibi `z3-static.json`'da > 0) | **R2 ölçülmedi.** R1 tabanı (SetPassAttribution @ `84941661`): kare 78 / 2 636 / 2 316 (`R2-KABUL-TASLAK.md:14`; `K/wp-078/evidence/r2-baseline-setpass-84941661/capture/setpass-toggles.tsv`) | hayır | (1) **`z3` aşaması (`7b25a393`):** FINAL ve kanıtlı R1 probe build'i ABBA dönüşümlü koşar. Ayarlar `r2_z3.py` sabitlerinden gelir: `Z3_START`, `Z3_TIER`, `Z3_MODES`, `MIN_PAIRS`, `Z3_R2`, `Z3_R1`. Poz `-ddPilotStart` ile verilir, iki build'de `-ddArmor all`. Sonra `r2_z3.py --evidence <E> --commit <FINAL> [--r1-commit] [--b9-poses]` r2-Z3.json ve r2-P3.json'u yazar. (2) Araç, bir sabit ya da R1 build kanıtı yoksa hiçbir Z3 penceresi koşmaz; log nedenini yazar ve Z3 raporda BLOCKED adıyla çıkar (`wp078_accept.py:950`, `:958`, `:960`). (3) S-05 dalında sabitler var: `Z3_START = "0,-4,0"`, `Z3_TIER = "sinematik"`, `Z3_MODES = ("topdown", "tp")`, `MIN_PAIRS = 5`; kökler `z3/perf` ve `z3/perf-r1`, raporun kökleriyle aynı (`wp078_report.py:236`). (4) Statik anahtarlar `<E>/r2/z3-static.json`'dan gelir (AP-025 alımından sonra bir EditMode testi yazar); dosya yoksa r2-Z3.json yazılmaz. (5) Tanımlar C-00 06.10 (BEKLEYEN §20). Z3 ölçümü sayaçtır, sessiz makine şartı yok. Zincirin `z3` aşaması `<E>/r2/z3-static.json`'u da kayda bağlar (C-00 06.10 13:30); yazıcısı S-05'in EditMode testi `Z3CharacterBudgetTests` (gelecek) | **HAYIR**: yazıcı durum anı ağacında yok → BLOCKED | **HAYIR** (BLOCKED) |
| **Z3b** | N2-03 | S-03 | `DD.Prototype.Tests.PlayMode.ZombieR2Tests.Z3b_40Zombi_SetPassPayi_DokuButcesi` · PM/ZombieR2Tests.cs:136. Ayrıca `tests[]` zorunlu: `DD.Art.Tests.PlayMode.SetPassAttributionTests.SetPass_Kaynaklari_40ZombiTamZirh` · APM/SetPassAttributionTests.cs:75 (fikstür `[Explicit]` :40) | var · PM/ZombieR2Tests.cs:177 (kaynak assert'leri :169-175, yazımdan önce) | `sharedTextureGpuMB`, `uniqueTexturesPerZombie`, `zombieSetPass`, `zombieDraws`, `frameSetPass`; rapor `zombieSetPassWorst`, `zombieDrawsWorst`, `sharedTextureGpuMBWorst` | ≤ 96 · == 0 · ≤ 24 · ≤ 822 · ≤ 98 (`1bca9052`, 06.10 01:10; test sabitleri :54) | kısmen (3 `*Worst`) | **21 / 732 / 92**, doku 88,01 MB, benzersiz 0 (en kötü 20 / 772) → yeni eşik içinde. Ama commit alanı `"x"` (40-hex değil, 05.10 14:56): `K/wp-088/evidence/r2gates-v2/r2-Z3b.json`. **R1 sabit karışım tabanı:** iki koşu var, r1a 16 / 564 / 87 ve r1b 15 / 567 / 88 (`K/wp-088/evidence/r1/r1{a,b}-playmode.log`, `r2.z3b.counters`; `f755749e` gövdesi). Eşik tabanı iki koşunun en kötüsü: **16** / 567 / 88 (`r2-gates.json` Z3b başlığı, TASLAK:5). R1 artefaktı yalnız r1b'yi taşır (15 / 567 / 88, 64,01 MB): `K/wp-088/evidence/r1/r2/r2-Z3b.json`. Bu artefaktın commit alanı **geçersiz**: `84941661cd00…0` (42 karakter; R1 FINAL `84941661622c…`) | hayır | (1) Zorunlu test JSON'da hâlâ `SetPassAttributionTests.SetPass_Kaynaklari_40ZombiTamZirh`; hiçbir oturum onu adıyla koşmuyor → BLOCKED (lint WARN TEST-EXPLICIT). C-00 kaydı: zorunlu test `ZombieR2Tests.Z3b_40Zombi_SetPassPayi_DokuButcesi` olacak, S-05'in Z3/P3 commit'inden sonra (BEKLEYEN §20). Satırı C-00 yazar (`K/wp-084/DEVIR-06.10.md` K2). **Bekliyor:** S-05'in commit'leri (`99a33758`, `e6c8b755`) dalda; `tests[]` değişmedi. (2) Zombi v2 alımı (AP-025C/D) sonrası yeniden ölçüm | **HAYIR**: zorunlu test hiçbir oturumda koşmuyor → BLOCKED | **HAYIR** (BLOCKED) |
| **Z4** | N2-03 | S-03 | `DD.Prototype.Tests.PlayMode.ZombieR2Tests.Z4_HerAile_YuzGozDis` · PM/ZombieR2Tests.cs:309 | var · PM/ZombieR2Tests.cs:347 (kaynak assert'i :322-323: yüz modülü yoksa FAIL, artefakt yok) | `familiesMissingFace` | == 0 | hayır | **FAIL, S-03 dalında:** `familiesMissingFace` **3** @ `840e3768` (dal `wp-084-s03-slice1`, teslim edilmedi): `K/wp-084/evidence/r2-840e3768/r2-Z4.json`. Durum anı ağacında ölçülmedi | kısmen (→ Z5) | AP-025 yüz modülleri (`kind "face"`) + S-03 SlotFace alımı (dilim 1 `3f4c737a`, aynı dalda) | **HAYIR**: durum anı ağacında yüz modülü yok → BLOCKED; S-03 dalında 3 → FAIL | **HAYIR** (BLOCKED) |
| **Z5** | N2-03 | S-03 | AD turu → `<E>/r2/reviews/Z5.json` | — | `verdict`, `must[]` | sinematik figür + oyun içi görünüm | — (insan) | **yok.** Öncül: AP-025C C-00 kabulü (BRIFING §9 Zombiler) | **EVET** (AD) | AP-025D sürüyor (`aktif-ajanlar/C-00.md` 06.10 00:49); S-03 alımı | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |
| **S1** | N2-07 | S-08 | `DD.Prototype.Tests.PlayMode.AudioSliceTests.SilahKiti_GercekRpm_OyunIci_Kayit` · PM/AudioSliceTests.cs:1648 (`[Explicit]`, Diagnostics, 30 dk). Oturum `r2q`; env `DD_KIT_WEAPONS=all`, `DD_KIT_VARIANTS=AC`, `DD_AP027_DIR={KPROJ}/asset-production/AP-027/exports/v001-prototip` | var · PM/AudioSliceTests.cs:1890 | `below400ShareMin`; rapor `below400ShareMinV4` | ≥ 0,20 | kısmen (`…V4`) | **p4: 0,3095 → PASS** (`samples` 24; ≥ 0,20). S-08 p4 (06.10 08:16–08:42, S-08'in pencere koşumu; commit `bdd82691` @ `wp-085-r2-s3`: main `67233774` ile patch eşdeğeri, ağaç 14 dosyada farklı (shader + tools/qa), ses yolu aynı — gt KP-17): `K/wp-085/evidence/pencere-p4/r2/r2-S1.json`. Rapor anahtarı `below400ShareMinV4` bu dosyada yok. Yük bağlamı (`pencere-p4/load.txt`, gt KP-15): kit adımı load1 24,8–36,9 — yük artık ölçüt değil (C-00 06.10, `8626d147`: ölçüt yabancı Unity / Player / Blender / ffmpeg). p4'ün `cpu.txt`'i ağır süreci kendi/yabancı ayırmadan sayar ("heavy"), bu yüzden yeni ölçüt p4'e geriye uygulanamaz. S-08'in kalite kapıları geçti. Önceki: 0,1985 FAIL @ `0040c85f` (kit-v3, v4 öncesi) | kısmen (kit dinleme kullanıcıda) | v4 oyunda; v003g-recut kuyrukları da main'de (`7897f642`, durum anına dahil). p4 (06.10) v4 oyundayken ölçüldü: 0,3095. v4 oyundayken "varyant C = v001-prototip" ne ölçüyor, C-00 bakmalı | **EVET**: yazıcı + `r2q` oturumu var, son değer eşik içinde (FINAL'de yeniden ölçülür) | **EVET** (p4 0,3095) |
| **S2** | N2-07 | S-08 | S1 ile aynı test ve oturum | var · PM/AudioSliceTests.cs:1891 | `fireVoiceDeficit`, `transients`, `loopStarts`, `rejectedNotStolen` | hepsi == 0 | hayır | **p4: 0/0/0/0 → PASS** (`samples` 52). S-08 p4 (06.10 08:16–08:42, S-08'in pencere koşumu; commit `bdd82691` @ `wp-085-r2-s3`: main `67233774` ile patch eşdeğeri, ağaç 14 dosyada farklı (shader + tools/qa), ses yolu aynı — gt KP-17): `K/wp-085/evidence/pencere-p4/r2/r2-S2.json`. Önceki kırılganlık: `rejectedNotStolen` 1 ve 4 (`ad0d564d`, `adf99ac2`); kit-v3 ve p4'te 0. Yük bağlamı (`pencere-p4/load.txt`, gt KP-15): kit adımı load1 24,8–36,9 — yük artık ölçüt değil (C-00 06.10, `8626d147`: ölçüt yabancı Unity / Player / Blender / ffmpeg). p4'ün `cpu.txt`'i ağır süreci kendi/yabancı ayırmadan sayar ("heavy"), bu yüzden yeni ölçüt p4'e geriye uygulanamaz. S-08'in kalite kapıları geçti | hayır | v003g main'de (`7897f642`); sessiz pencerede yeniden ölçüm; kök neden notu | **EVET**: son iki ölçüm 0; kırılganlık izlenir | **EVET** (p4 0/0/0/0) |
| **S3** | N2-07 | S-08 | Test yok. Yazıcı S-08'in aracı `tools/qa/r2_s3.py` (main `a4066f92`; lint INFO TOOL-WRITER): dense-40 Player + A9 + dinleme paketi | — (araç yazar) | `truePeakDbtp`, `momentaryMaxLufs`, `preLimiterGrDb`, `weaponMarginLu` | ≤ −1,0 dBTP · ≤ −13,0 LUFS · ≤ 1,0 dB · ≥ 1,0 LU | hayır | **p5 (06.10 ~13:40–14:05, +0,8 dB sonrası; kapı dosyası YAZILMADI):** üstten silah marjı +1,13 LU birleşik ✓ (AB1 +0,91, AB2 +1,27); M en çok −13,87 LUFS ✓; A9 GR 0 ✓; **TP payı çok ince**: geçerli alımlarda tp −2,17, fp −1,05, üstten#2 −1,09 dBTP (≤ −1,0 sınırına 0,05–0,09 dB). Üstten#1 alımı geçersiz (yoğun pencere 11,1 s < 35 s; o alım −0,88): r2_s3 kaynak bulgusuyla hiçbir şey yazmadı, S3 BLOCKED. Kaynak: `K/wp-085/evidence/pencere-p5/r2-s3.txt`, `r2-s3-dry1.txt`, `r2-s3-dry2.txt`; birleşik marj ve KİRLİ 16/136 `K/wp-085/DEVIR-06.10.md`:48. Önceki: **p4: FAIL** — `truePeakDbtp` −1,357 ✓ · `momentaryMaxLufs` −13,682 ✓ · `preLimiterGrDb` 0,0 ✓ · **`weaponMarginLu` +0,396 ✗** (≥ 1,0). En kötü mod topdown W/R +0,40 LU (R1 mix B +1,57), fp +2,02. S-08 p4 (06.10 08:16–08:42, S-08'in pencere koşumu; commit `bdd82691` @ `wp-085-r2-s3`: main `67233774` ile patch eşdeğeri, ağaç 14 dosyada farklı (shader + tools/qa), ses yolu aynı — gt KP-17): `K/wp-085/evidence/pencere-p4/r2/r2-S3.KIRLI.json` + `K/wp-085/evidence/pencere-p4/r2-s3.txt`. **KİRLİ** (eski ölçütle; `8626d147`'den beri yük ölçüt değil): `r2_s3` pencerenin en büyük load1'ini aldı (54,06; o değer S4 adımında). S3'ün kendi adımları: dense 20,4–37,6, A9 29,5–46,9, dinleme 24,0–44,8 (gt KP-15). Araç dosyayı `r2-S3.json` olarak yazmadı, kapıya bağlanmaz. C-00 06.10 08:46: marj gerçek bulgu (N-6). R1 kapanışı: −3,57 / −2,73 dBTP, M −14,85 / −14,65 LUFS, marj +1 LU | hayır | (1) Kök neden isabet yığını (C-00 06.10 14:08): S-08 kaynağında düzeltir, hedef TP ≤ −1,5 dBTP. (2) Kapı değeri FINAL'in temiz penceresinde yazılır. (3) KİRLİ ölçütü `8626d147` + kapsama kuralı `3b0e4327`. Önceki: (1) KİRLİ ölçütü değişti (`8626d147`): yabancı Unity / Player / Blender / ffmpeg; yük ve CPU boşta yalnız rapor. (2) **Mix kararı (C-00 06.10 10:15):** üstten `NearFieldDb` +0,8 dB, main'de (`96507557`); S-08 doğrulama penceresinde ölçer (hedef silah marjı ≥ +1 LU). (3) `3b0e4327`: host örnekleri erken biterse ya da boşluk bırakırsa koşu KİRLİ | **KOŞULLU**: marj, M ve A9 p5'te geçti; TP 0,05–0,09 dB payla sınırda, kaynak düzeltmesi bekleniyor; değer FINAL temiz penceresinde | **KOŞULLU** (p5: marj / M / A9 geçer; TP 0,05–0,09 dB payla sınırda, kaynak düzeltmesi bekleniyor) |
| **S4** | N2-07 | S-08 | `DD.Prototype.Tests.PlayMode.AudioSliceTests.MekanKuyrugu_EDC_Olcum` · PM/AudioSliceTests.cs:1918 (`[Explicit]`, 40 dk); oturum `r2q` | var · PM/AudioSliceTests.cs:2044 | `open/corridor/hallRt60Min/MaxS`, `orderViolations`; rapor `*Edc30MsMin` | açık 0,56–1,08 · koridor 0,88–1,68 · salon 1,28–2,28 s · ihlal == 0 | kısmen (3 `Edc30`) | **p4: PASS** — açık 0,656–0,812, koridor 1,154–1,333, salon 1,653–1,839 s, ihlal 0; `*Edc30MsMin` 256,7 / 557,1 / 732,1 (`samples` 240). S-08 p4 (06.10 08:16–08:42, S-08'in pencere koşumu; commit `bdd82691` @ `wp-085-r2-s3`: main `67233774` ile patch eşdeğeri, ağaç 14 dosyada farklı (shader + tools/qa), ses yolu aynı — gt KP-17): `K/wp-085/evidence/pencere-p4/r2/r2-S4.json`. Yük bağlamı (`pencere-p4/load.txt`, gt KP-15): S4 adımı load1 22,1–54,1 — yük artık ölçüt değil (C-00 06.10, `8626d147`: ölçüt yabancı Unity / Player / Blender / ffmpeg). p4'ün `cpu.txt`'i ağır süreci kendi/yabancı ayırmadan sayar ("heavy"), bu yüzden yeni ölçüt p4'e geriye uygulanamaz. S-08'in kalite kapıları geçti. Önceki: v3f @ `3f74e3f4` eşik içinde; v3g KİRLİ (yük ~105) yalnız sıralama bilgisi; `cl` koşusu LFS pointer kaynaklı geçersiz | hayır | (1) v003g overlay (180 kuyruk + M/S mono) main'de: `7897f642`, durum anına dahil. Gövdesindeki S4 değerleri KİRLİ damgalı ve yukarıdaki v3g koşusunun yuvarlanmış değerleriyle aynı; temiz (sessiz) ölçüm yok. Ön koşul: `git lfs ls-files` pointer kontrolü. (2) **Boş ölçüm:** `DD_EDC_WEAPONS` hiçbir silahla eşleşmezse test `float.MaxValue` / 0 yer tutucularını yazıyordu ve bunlar her check'i geçiyordu (BEKLEYEN §15). Yargıç artık `samples` ≤ 0 ise BLOCKED verir (`fa0e9ff1`). S-08'in kaynak assert'i yazımdan önce: `98e3dffd` @ `wp-085-b03-gc`, main'de değil | **EVET**: v003g main'de, p4 eşik içinde; boş ölçüm kapısı (`samples`) yargıçta | **EVET** (p4) |
| **S5** | N2-07 | S-08 | `DD.Prototype.Tests.PlayMode.AudioSliceTests.KovanV2_AyniKareIkiKabukAyriDuser_GecikmeBandi_HavuzTavani_SifirAyirma` · PM/AudioSliceTests.cs:989 (CI'da); oturum `r2` | var · PM/AudioSliceTests.cs:1089 | `dropDelayMsMin/Max`, `surfaceMismatches`, `poolMax`, `gcBytesPerCasing`, `rocketCasingSounds`; rapor `queueMax` | ≥ 250 · ≤ 700 · 0 · ≤ 3 · 0 · 0 | kısmen (`queueMax`) | **620–680 ms, 0, 3, 0, 0**, queueMax 16 @ `79819ca7` (eşdeğeri main'de): `K/wp-085/evidence/cl2/r2/r2-S5.json`. `poolMax` sınırda (3 = 3) | hayır | yok. B-03 B2 (kovan zemin maskesi) main'de: `35a00e65`, ölçümden sonra girdi, durum anına dahil. O commit'in koşu listesinde S5 testi yok; FINAL'de yeniden ölçülür. G4 değişirse `gcBytesPerCasing` 0 korunmalı (BEKLEYEN §19) | **EVET** | **EVET** |
| **S6** | N2-07 | S-08 | dinleme → `<E>/r2/reviews/S6.json` | — | `verdict`, `must[]` | R1 v3 ↔ R2 v4 A/B | — (insan) | **yok.** Öncül: kit-v3 (`K/wp-085/evidence/b4/kit-v3/HAZIR.md`) | **EVET** (kullanıcı / ses konseyi) | ar_1/sg_1 v4 dinleme kiti hazırlanmadı (DEVIR kalan 6) | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |
| **P2** | — (WP-091; N2-03/04 doku ve rig etkisi) | S-10 | **Zincire bağlı (`5e213eb2`).** R2 turunda `--r1-evidence`/`--r1-commit` ile a4 iki komut koşar. (1) `wp078_perf.py --baseline-bin <R1>/build-probe --baseline-out <E>/a4/perf-r1 --baseline-commit <R1>`: R2/R1 pencereleri ABBA dönüşümlü (`69a5755a`). (2) `wp078_perf_report.py --baseline … --r2-measure <E>/r2/r2-P2.json --commit <FINAL> --sha256 … --baseline-commit … --baseline-sha256 …`. Kaynak: `tools/qa/wp078_accept.py:882-931` | C# yok; zincir yazar (lint INFO CHAIN-MADE). Python yazıcı `tools/qa/wp078_perf_report.py:334-342`; `samples` = iki taraftaki sağlam pencere sayısı | `flaggedCells`, `worstDeltaMs`, `worstDeltaPct` | rapor: +%10 **ve** +1,0 ms birlikte aşan hücre işaretlenir | **evet** | **ölçülmedi.** R1 FINAL'' faz 2 koşulmadı. R1 probe build `K/wp-078/evidence/84941661/build-probe/`: birim kaydı `complete` @ `84941661`, build-report release-probe damgası `84941661622c`, `app-manifest.sha256` var. Hash doğrulaması (`r1_build_proof`) bu güncellemede koşulmadı | hayır | (1) R1 build hiçbir pencereden önce kendini kanıtlar, kuru koşuda da (`r1_build_proof`, `wp078_accept.py:251-286`): birim kaydı, hash'ler, release-probe damgası, WP-079H app manifesti. Kanıtlanmazsa A4 yalnız FINAL'i koşar, P2 BLOCKED adıyla. (2) Her iki tarafta pencere bütünlüğü şart. Bozuk pencere ya da bir tarafta tier'ın `minWindows` sağlam penceresinden az hücre varsa r2-P2.json yazılmaz; P2 BLOCKED adıyla, nedenler stderr'de. (3) R1 = FINAL reddedilir (64); devam eden A4 kaydedildiği tabanı korur (64). (4) K8: B9 ve R1 kökleri A4'ü bloke etmez, A4'ün yerine de geçemez. A4 kapısı yalnız FINAL pencereleriyle yargılanır | **KOŞULLU**: rapor; zincir üretir, R2'de hiç ölçülmedi | **EVET** (zincir üretir; `--r1-*` bayrakları, R1 build kanıtı ve sağlam pencerelerle) |
| **P3** | — (WP-091; N2-03 dünya ve zombi yükü) | S-05 (zincir tarafı S-10) | `r2_z3.py` (S-05) ← zincirin `z3` aşaması, B9 pencerelerinden (`<E>/b9/<poz>/`; `--b9-poses K/wp-078/evidence/b9-poses/b9-poses.json`). b9 artık a4'ten sonra kendi aşaması (`7b25a393`; `tools/qa/wp078_accept.py:933-937`, `:1014-1035`) | **yok** durum anı ağacında (lint WARN NO-WRITER). S-05 dalında (`e6c8b755`) lint INFO CHAIN-MADE | `cells` | rapor | **evet** | **ölçülmedi** | hayır | `r2_z3.py` durum anı ağacında yok: `z3` aşaması r2-P3.json yazmaz, log nedenini yazar (`wp078_accept.py:950`). b9 yalnız `<E>/stages/b9.json` yazar (RECORDED). Araç S-05 dalında: `99a33758`, `e6c8b755` | **KOŞULLU**: rapor; üreten araç durum anı ağacında yok, boş kalır | **KOŞULLU** (rapor, boş) |
| **P5** | N2-02 / N2-03 (yeni dokular → yükleme) | S-09 (zincir üretir) | `tools/qa/r2_load_time.py` ← `wp078_accept` aşama `r2` (`tools/qa/wp078_accept.py:1082-1102`), `<E>/{a2,a3}/*/ui-data/load-timing.json` | zincir (lint INFO CHAIN-MADE) | `ratio`, `floorLimited`, `r2MedianSeconds`, `r1MedianSeconds`, `r2RawMedianSeconds` | rapor (TASLAK'taki "≤ +%20" C-00 kararıyla rapora çevrildi) | **evet** | **R2 ölçülmedi.** R1 tabanı hazır: 10 `load-timing.json` @ `84941661` (`K/wp-078/evidence/84941661/a2`, `/a3`) | hayır | Zincir komutunda `--r1-evidence K/wp-078/evidence/84941661 --r1-commit 84941661622cfbec8db796b639e4032df4457903` şart; yoksa BLOCKED (`wp078_accept.py:1087`) | **KOŞULLU**: R2'de hiç ölçülmedi (zincir üretecek) | **EVET** (zincir üretir; `--r1-*` bayraklarıyla) |
| **U1** | N2-01 | S-09 | `DD.Prototype.Tests.PlayMode.UiTextInventoryTests.U1_GorunurHarf_AnaMenuPanellerVeSecim_EnTr` · PM/UiTextInventoryTests.cs:85 @ `8ba256ec` (`[Explicit]` :83) | var · PM/UiTextInventoryTests.cs:147 @ `8ba256ec` | `u1_default`, `u1_same_content`, `lettersPerScreenReductionMin`, `unnamedSelectables`; rapor `lettersTotalReduction` (ikisinin küçüğü) | ≥ 0,40 · ≥ 0,40 (U1b) · ≥ 0,25 · == 0. Kaynak: `db2022af`, C-00 05.10 kararı (BEKLEYEN §11) | kısmen (`lettersTotalReduction`) | **0,4222 / 0,1306 / 0 / 0 → FAIL** (`K/wp-068s9/evidence/wp081/r2-u1s/r2-U1.json`). Uç ağacın yargıcı: "u1_same_content 0,1306 ≥ 0,4 false; lettersPerScreenReductionMin 0 ≥ 0,25 false"; `u1_default` geçer. Artefaktın commit alanı `bcde16dc` (`8ba256ec`'in ebeveyni). Ölçüm commit'lenmemiş ağaçta alındı (dosya 17:33, `8ba256ec` 17:33:50); ölçülen kod `8ba256ec` içeriği. Kanıt kalitesi notu (GT B4). Tek ölçümlü eski değer: main `09a21785` 0,1306 / 0 / 0 (`…/r2-cine2/r2-U1.json`) | kısmen (60 s MP4 kullanıcıya, BRIFING §6) | (1) **Entegrasyon sırası:** `8ba256ec` ve `db2022af` ardışık girer, önce `8ba256ec`. Tek başına ikisi de lint ERROR verir: `8ba256ec` main'de KEY-EXTRA, `db2022af` main'de KEY-MISSING (`db2022af` gövdesi). (2) U1b ≥ 0,40 ve ekran başına ≥ 0,25 için Seçim + paneller v4 | **HAYIR**: FAIL (`u1_same_content`, `lettersPerScreenReductionMin`) | **HAYIR** (FAIL) |
| **U2** | N2-01 | S-09 | `DD.Prototype.Tests.PlayMode.UiOverflowMatrixTests.U2_Tasma_TrEn_Metin1ve125_MacUi12` · PM/UiOverflowMatrixTests.cs:85 (`[Explicit]`) | var · PM/UiOverflowMatrixTests.cs:148 | `overflows` | == 0 | hayır | **0** @ `09a21785` (main): `K/wp-068s9/evidence/wp081/r2-cine2/r2-U2.json` (geçmiş 20 → 12 → 0, b4r1–b4r3) | hayır | S-09 WIP (sinematik B + yastıklar `a1f50b99`, ikon v2 `4895ac1d`) girince yeniden | **KOŞULLU**: S-09 WIP'i girince yeniden | **EVET** (0 @ `09a21785`) |
| **U3** | N2-02 | S-09 | `DD.Prototype.Tests.EditMode.LoadingKeyArtTests.Rotasyon_ArdisikTekrarYok_YirmiYuklemedeHerSanatEnAzIkiKez` · EM/LoadingKeyArtTests.cs:169 (CI'da); `tests[]` zorunlu | var · EM/LoadingKeyArtTests.cs:234 | `consecutiveRepeats`, `minPerAny20`, `distinctSha256` | == 0 · ≥ 3 · ≥ 5 | hayır | 0 / 3 / **2 → FAIL** @ `09a21785` (main): `K/wp-068s9/evidence/wp081/r2-cine2/r2-U3.json` | hayır | S-09 yükleme sanatı alımı (`AP-028/exports/v002-upscale/Loading/`, 5/5 kabul, BRIFING §6) | **HAYIR**: `distinctSha256` 2 → FAIL | **HAYIR** (FAIL) |
| **U4** | N2-01 | S-09 | AD turu → `<E>/r2/reviews/U4.json` | — | `verdict`, `must[]` | menü v4 AAA sunum | — (insan) | **yok.** Öncül: kullanıcı sinematik 5/5 çekim onayı (BEKLEYEN §8), B + yastıklar (BRIFING §9) | **EVET** (AD) | S-09 tek teslim + MP4 + ikon v2 | **HAYIR**: review dosyası yok → BLOCKED | **HAYIR** (BLOCKED) |

### Satır notları (karar değiştiren bulgular)

- **Z3b eşiği: bu çalışma sırasında kapandı.**
  - `18f3d01e`'de BEKLEYEN §11 "Z3b eşikleri … main'de (f755749e)" diyordu. O an bu doğru değildi: JSON ve test 14/450/88'di, `f755749e` yalnız `wp-091-r2-z3b-fixed` dalındaydı (`git cherry` `+`).
  - C-00, `1bca9052` (06.10 01:10) ile JSON'u 24/822/98'e çekti; `8f10dddb` ve `173890ac` S-03'ün `c37991f7`/`cf8346a0` değişikliklerini getirdi (`git cherry` `-`). `tools/qa/tests/test_r2_gates.py:34` artık 24'ü sabitliyor.
  - BEKLEYEN §11 kaydı `19baeee9` ile düzeltildi.
  - Z3b'nin kalan engeli zorunlu test satırı. C-00 kaydı: zorunlu test `ZombieR2Tests.Z3b_…` olacak (BEKLEYEN §20). S-05'in Z3/P3 commit'leri (`99a33758`, `e6c8b755`) dalda; satır JSON'da hâlâ SetPassAttribution.
- **Z1: R1'deki Passed bir kapsam boşluğuydu (C-00 06.10).**
  - Önceki sürüm Z1'i "TEST-ONLY, R1 A3 Passed" diye okudu. O test DEC-077 eşiklerini (kullanıcı 16.09) hiç assert etmiyordu: yalnız siluet > 0,02'yi kontrol ediyor, bandı eski bir AD önerisine karşı raporluyordu. Bu yüzden hiç kırmızı olmadı (`56805c09` gövdesi).
  - `56805c09` eşikleri JSON'a check olarak koydu. `16a95f0e` `measuredBy`'yi geri aldı, çünkü yazmayan bir `measuredBy` lint ERROR'dur; S-03 onu yazıcıyla aynı commit'te ekler.
  - S-03'ün deterministik yazıcısı (`840e3768`, main'de değil) v002'de 0,2321 / 0,0155 / 0,00078 ölçtü → FAIL. R1 de aynı kodla DEC-077 altında. Düzeltme AP-008C; eşik düşürülmez.
- **P2 zincire bağlandı (`5e213eb2`).**
  - Önceki sürümün bulgusu ("a4, R2 turunda tek taraflı koşuyor") kapandı. `--r1-evidence`/`--r1-commit` verilince a4, R1 FINAL'' probe build'ini FINAL'inkiyle dönüşümlü koşar ve r2-P2.json'u yalnız sağlam pencerelerden yazar.
  - Sıra ABBA'dır (R2 R1 R1 R2 …), başlangıç tarafı hücreden hücreye değişir. Bir çift sessiz parti sınırını aşmaz (`69a5755a`).
  - Z3'ün "≥ 5 çift medyan" ölçümü a4'ün çiftlerini kullanmaz; kendi `z3` aşamasında, sabit pozda ve `-ddArmor all` ile koşar (`7b25a393`).
- **H1'in varsayılan yolu.**
  - "Kanıt bayraksız oyuncu yolundan" kuralına göre H1, oyuncunun aldığı tutuşla ölçülür: `measuredBy` `env` taşımaz (`6e0b41d1`). `DD_HOLD_IK` sahibin A/B kanıtıdır, kapı değeri değil.
  - Durum anı ağacında varsayılan kapalı. Varsayılanı açan değişiklik S-06 WIP'inde ("B2d-on/B2e", `dee54ffa`); B2d'nin kendisi (`9f87b7d4`) main'de. B2d-on/B2e girince aynı oturum tutuşu açık ölçer.
  - Zincirin kabuğunda `DD_HOLD_IK` export edilmiş olsa bile oturuma sızmaz (`env -u`, `d495f245`).
- **Yargıç: "hiçbir şey ölçülmedi" BLOCKED'dır (`fa0e9ff1`).** S4 testi, hiçbir silah ölçmediğinde `float.MaxValue` / 0 yer tutucularını yazıyor ve bunlar her S4 check'ini geçiyordu (BEKLEYEN §15). Artık `samples` ≤ 0 ya da sayı değilse kapı BLOCKED olur. K'deki 58 artefaktın hepsinde `samples` > 0; hiçbir eski değer değişmez.
- **H3 artık null değil, sayı yazıyor (GT B8).**
  - Son artefakt null, ama `5a45e3aa`'dan önceki yazıcıyla yazıldı.
  - main'deki yazıcı el yuvaya ulaşmazsa `NoContact` = 16 yazar → FAIL.
  - S-06 temas IK'sı main'e girmeden H3, FINAL'de büyük olasılıkla FAIL verir.

### TASLAK ↔ JSON ↔ ZİNCİR farkları (JSON bağlayıcı; belgeler bayat)

| Konu | `R2-KABUL-TASLAK.md` / `R2-KABUL-ZINCIRI.md` | durum anı `r2-gates.json` |
|---|---|---|
| H3 sahibi | ZINCIRI §4: S-06 → ✅ 06.10'da S-02'ye düzeltildi | S-02 (C-00 05.10, `a7f19398`) |
| Z3 sahibi | ZINCIRI §4: S-03 (S-05 render) → ✅ 06.10'da S-05'e düzeltildi | S-05 |
| P5 sahibi / türü | TASLAK §5: ≤ +%20; ZINCIRI §4'te yoktu → ✅ 06.10'da eklendi (S-09, zincir üretir) | S-09, rapor |
| S4 | TASLAK:57 EDC −30 dB (v1) | RT60 T20 (v2, `3f74e3f4`) |
| S5 havuz | TASLAK:58 tavan 8 | `poolMax` ≤ 3 (`5b315d04`) |
| Z3b | TASLAK:5 → 24/822/98 | 24/822/98 (`1bca9052`, 06.10). Uyumlu |
| U1 | BEKLEYEN §11: iki ölçümün en küçüğü | ✅ uyumlu (`db2022af`): iki check, her biri ≥ 0,40; `lettersTotalReduction` (= küçüğü) rapor |
| Z1 | TASLAK:43: "mevcut ArmorTests eşikleri … korunur", a3 izole oturumu; Passed yeterdi | `56805c09`: üç DEC-077 check'i; yazıcı gerekir, Passed tek başına BLOCKED |
| H1b, H6 | TASLAK'ta yok | C-00 06.10 kaydı (`510c62f9`), S-06 kapıları. ✅ ZINCIRI §4'e eklendi |
| P1, P4 | TASLAK §5 kapı | JSON'da yok; R1 kapıları A4 (perf-targets) ve A5 (`soak_gate`) taşır. **Kademe eşleşmesi (C-00 06.10 13:30, `tools/qa/perf-targets.json`):** Sinematik DEC-100 ort ≤ 33,3 / p99f ≤ 40 ms · **Yüksek DEC-092 ort ≤ 16,6 / p99f ≤ 20 ms** · Performans DEC-088 ort ≤ 8,0 / p99f ≤ 10 ms. 8,0 ms Performans'ın eşiğidir, Yüksek'in değil. R2'nin perf kapısı yalnız bu kademe eşikleridir. +0,5 ms R2 kapısı değildir: S-05'in kaskad probunun yerel kabul ölçütüdür (C-00 06.10 14:50). P2 rapor olarak kalır: hücre başına `worstDeltaMs`, bayrak +%10 ve +1,0 ms |
| Bot nişanı `meanAimErrorDeg` ≤ +%25 | TASLAK §7 rapor | JSON'da yok, ölçen yok |
| H4a, L4 | ZINCIRI §4 kapı tablosunda yoktu (GT B12) → ✅ 06.10'da eklendi | H4a S-06 kapı, L4 S-06 rapor |
| İnsan kapısı gözden geçireni | sözleşme örneği "AD Fable" (`r2-measure-contract.md` Human gates) | SABIT-KURALLAR Ç1 (06.10): AD ve ses kapıları KADRO ajanlarıyla, Opus'ta |

---

## 2. Özet sayım

Sayımlar durum anı ağacınındır (`r2-gates.json` @ `1e7abcc8`, lint @ `1e7abcc8` + `8ba256ec`).

| Sayım | Değer | Kapılar |
|---|---|---|
| Kapı (toplam) | **35** | 26 `gate` · 4 `report` (L4, P2, P3, P5) · 5 `human` (H5, L6, Z5, S6, U4). Yeni: H1b, H6 (`510c62f9`) |
| C# yazıcısı olan | **14** kapı / 14 çağrı | H1, H3, H4, H4b, Z2, Z3b, Z4, S1, S2, S4, S5, U1, U2, U3 |
| Yazıcısız (lint NO-WRITER) | **14** | H1b, H2, H4a, H6, L1, L1t, L2, L3, L4, L5, Z1, Z3, S3, P3. Z3 ve P3'ün yazıcısı (`r2_z3.py`) S-05 dalında; Z1'inki S-03'ün `840e3768`'inde |
| Zincirin yazdığı (lint INFO CHAIN-MADE) | 2 | P2 (a4, `5e213eb2`), P5 (aşama `r2`) |
| İnsan kapısı (yazıcı gerekmez) | 5 | H5, L6, Z5, S6, U4 |
| `measuredBy` ile zincirde koşan | **14 kapı / 13 oturum** | `r2` (11): H1, H3, H4, H4b, Z2, Z3b, Z4, S5, U1, U2, U3 · `r2q` (2): S1+S2 (tek oturum), S4 |
| Yazıcısı var, `measuredBy` yok | 0 | önceki sürümde H1 (lint EXPLICIT-UNRUN); `6e0b41d1` ile kapandı |
| Hiç ölçülmemiş (K'de değer yok) | **20** | otomatik 15: H1b, H2, H4a, H6, L1, L1t, L2, L3, L4, L5, Z3, S3, P2, P3, P5 · insan 5 |
| Değeri olan | 15 | 14'ü `dd-r2-measure-v1` artefaktı (H3, H4, H4b, Z1, Z2, Z3b, Z4, S1, S2, S4, S5, U1, U2, U3); H1 yalnız log. Z1 ve Z4'ün artefaktı S-03 dalında (`840e3768`) |
| Son değeri eşik dışında | **5** | S1 (0,1985), U1 (`u1_same_content` 0,1306, `lettersPerScreenReductionMin` 0), U3 (`distinctSha256` 2), Z1 (0,2321 / 0,0155 / 0,00078 @ `840e3768`), Z4 (`familiesMissingFace` 3 @ `840e3768`). Z3b `18f3d01e` eşiğine göre dışındaydı, `1bca9052` eşiğine göre içinde |
| Son değeri BLOCKED olan | 1 | H3 (`contactDeviationFramesMax` null; eski yazıcı. main'deki yazıcı temas yoksa 16 → FAIL yazar) |
| İnsan kapısı | **5** (+ 2 insan bağlantılı) | H5, L6, Z5, U4 (AD) · S6 (kullanıcı / ses konseyi) · bağlantılı: S1/S2 kit dinleme, U1 MP4 |
| **FINAL'e hazır (katı tanım)**, birincil | **EVET 1 · KOŞULLU 12 · HAYIR 22** | EVET: S5 · KOŞULLU: H1, H3, H4, H4b, L4, Z2, S2, S4, P2, P3, P5, U2 · HAYIR: H1b, H2, H4a, H5, H6, L1, L1t, L2, L3, L5, L6, Z1, Z3, Z3b, Z4, Z5, S1, S3, S6, U1, U3, U4 |
| **Durum anı ağacı FINAL olsaydı**, ikincil | **EVET 8 · KOŞULLU 5 · HAYIR 22** | EVET: H4, H4b, Z2, S4, S5, P2, P5, U2 · KOŞULLU: H1, H3, L4, S2, P3 · HAYIR: aynı 22 |

---

## 3. Sahip başına açık iş

| Sahip | Kapı | İş | Kaynak |
|---|---|---|---|
| **S-02** | H3 | `contactDeviationFramesMax` sayısal olan bir r2-H3.json. Yazıcı `5a45e3aa`'dan beri sayı yazar; temas IK'sı (S-06) main'e girmeden 16 → FAIL. FP vuruş bağının 3/7 olduğu bir tanı koşusu var | `K/wp-083/evidence/b2g-r2.xml`, PM/MagazineContactProbe.cs:33 |
| | H4, H4b | `wp-087-heldkick` (`6ea4492a`) girince FINAL'de yeniden ölçüm. Sol el atış payı ≤ 1 cm (H1 kapsamı dışında, N2-05) | `K/wp-087/DEVIR-05.10.md` |
| | — | B-03: G3, B1 (`db77df0a`) ve B2 (`35a00e65`) main'de, durum anına dahil | BEKLEYEN §19 |
| **S-03** | Z1 | WP-084 `wp-084-s03-slice1` teslimi: `840e3768` yazıcı + `measuredBy` + DEC-077 assert'leri + deterministik R1 tabanı. Değer FAIL (0,2321 / 0,0155 / 0,00078); düzeltme AP-008C, eşik düşmez | `840e3768` gövdesi; `K/wp-084/DEVIR-06.10.md` |
| | Z3b | Zorunlu test satırını C-00 yazar (K2); S-03 `r2-gates.json`'a dokunmaz. `c37991f7`/`cf8346a0` main'de eşdeğer (`8f10dddb`/`173890ac`) | `K/wp-084/DEVIR-06.10.md` K2; lint WARN TEST-EXPLICIT |
| | Z4 | Yüz modülleri + SlotFace alımı (dilim 1 `3f4c737a`); `840e3768`'de `familiesMissingFace` 3 | BRIFING §7 S-03; `K/wp-084/evidence/r2-840e3768/` |
| | Z2 | Zombi v2 ile yeniden ölçüm; Z2 çubuklarını C-00'a kesinlet | PM/ZombieR2Tests.cs:44-49 |
| | Z5 | AD dosyası (AP-025D sonrası) | — |
| | — | G1 GC: `e81b9500` aynı dalda, teslim edilmedi | BEKLEYEN §19; `K/wp-084/DEVIR-06.10.md` |
| | — | Devir notu var: `K/wp-084/DEVIR-06.10.md` (önceki sürümde eksikti) | BRIFING §14 |
| **S-05** | Z3 | `tools/qa/r2_z3.py` dalda: `99a33758` + `e6c8b755` @ `wp-091-s05-z3-r2`, main'de değil. Zincirin okuduğu sabitler var (`Z3_START = "0,-4,0"` vd.). Statik anahtarlar: AP-025 alımından sonra `z3-static.json`'u yazan EditMode testi ve onun `measuredBy`'si | BEKLEYEN §20; `e6c8b755` |
| | P3 | r2-P3.json aynı araçtan (`r2_z3.py`), B9 pencerelerinden | BEKLEYEN §20 |
| | — | §17 shader incelemesi | BEKLEYEN §17 |
| **S-06** | H1, H3 | Varsayılan açık tutuşu ve temas IK'sını (B2d-on/B2e) temiz commit'le teslim et; `dee54ffa` WIP'i alınmaz. B2e'de `elbowMaxDeg` yazımı korunmalı (gt B5; yoksa lint KEY-MISSING). Hero v2 sonrası dirsek ≤ 165 | `K/wp-083/DEVIR-06.10.md`; `K/wp-078/OUTBOX-PENDING.md` 1 |
| | H1b | Test (`R2HeroOlcumTests.H5_SolEl_Kavrama…`, `[Explicit]`, `R2Olcum.Yaz`, kaynak kayıtta: silah ve kare > 0) + `measuredBy` aynı commit'te. Plan WP-083 B2f | `510c62f9` gövdesi; DEVIR-06.10 MUST-2 |
| | H6 | Test (`R2HeroOlcumTests.H6_TpSilahGorunurlugu…`) + `measuredBy` aynı commit'te. Plan WP-082 + AP-024C pivot tablosu | `510c62f9` gövdesi; DEVIR-06.10 MUST-3 |
| | H2 | Ölçüm testi (silah gövdesi ↔ gövde kapsülleri) | TASLAK:21 |
| | H4a | `AnimationTests.FpKollar_Kadraj_…`'ya `R2Olcum.Yaz("H4a")` | APM/AnimationTests.cs:660 |
| | L1, L1t, L2, L3, L4, L5 | WP-083 klip alımından sonra oyun içi ölçüm testleri; L5 için 40 zombi çift payı | `C00-AP026C-RAPOR.md:155` |
| | H5, L6 | AD dosyaları | — |
| | — | G5 | BEKLEYEN §19 |
| **S-08** | S1 | v4 ile yeniden ölçüm (sessiz; v003g main'de, `7897f642`); ≥ 0,20 tutmuyorsa kök neden notu | `K/wp-085/DEVIR-05.10.md` |
| | S2 | `rejectedNotStolen` 1/4 kırılganlığının kök nedeni | `…/b4/r2`, `…/b5r/r2` |
| | S3 | Yazıcı (Dense-40 Player, `r2_s3.py`) | DEVIR kalan 7 |
| | S4 | Kaynak assert'i teslimi: `98e3dffd` @ `wp-085-b03-gc`. v003g üstünde temiz (sessiz) ölçüm; LFS pointer kontrolü | BEKLEYEN §15; DEVIR kalan 1–2 |
| | S6 | ar_1/sg_1 v4 dinleme kiti | DEVIR kalan 6 |
| | — | G2, G4 | BEKLEYEN §19 |
| **S-09** | U1 | `8ba256ec` teslimi (`db2022af` ile ardışık, önce `8ba256ec`); U1b ≥ 0,40 ve ekran başına ≥ 0,25 (Seçim + paneller v4) | BEKLEYEN §11; `db2022af` gövdesi |
| | U3 | 5 yükleme sanatının alımı (`distinctSha256` ≥ 5) | BRIFING §6 N2-02 |
| | U2 | WIP girince yeniden | — |
| | U4 | AD + MP4 | — |
| **S-10** | H1 | ✅ `measuredBy` (`6e0b41d1`; `env` yok = varsayılan oyuncu yolu; S-06 `dee54ffa` varsayılanı açar) + karar B tanımı (`bf177e7f`). `4c48563e` ALINMAZ | lint EXPLICIT-UNRUN kalktı; `K/wp-078/SUPERSEDED-4c48563e.md` |
| | P2 | ✅ a4 bağlantısı (`5e213eb2`) + ABBA dönüşümlü koşucu (`69a5755a`) | lint INFO CHAIN-MADE |
| | U1 | ✅ iki ölçüm kaydı (`db2022af`; H4b pin düzeltmesi `b6f4d355`) | BEKLEYEN §11 |
| | H1b, H6 | ✅ satırlar (`510c62f9`); `measuredBy` S-06'nın yazıcısıyla gelir | C-00 06.10 kaydı |
| | Z3, P3 | ✅ zincirde `b9` ve `z3` aşamaları (`7b25a393`); S-05'in `r2_z3.py`'sinin main'e girmesi bekleniyor | BEKLEYEN §20 |
| | — | ✅ yargıç: `samples` ≤ 0 → BLOCKED (`fa0e9ff1`, kayıt düzeltmesi `61800dc0`) | BEKLEYEN §15 |
| | — | ✅ lint ön adımı: R2 turunda lint ERROR → exit 3, hiçbir aşama başlamaz; oturumlar `env -u` ile başlar (`d495f245`; test düzeltmesi `1e7abcc8`) | OTURUM-ACILIS S-10; gt B6 |
| | — | ✅ `R2-KABUL-ZINCIRI.md` 06.10 güncellemesi: §4'te sahipler H3 → S-02, Z3 → S-05, P2 → S-10, P3 → S-05 (zincir tarafı S-10), P5 → S-09 (zincir üretir); eklenen kapılar H1b, H4a, H6, L4, P5 | §1 fark tablosu |
| | Z3b | **açık:** `tests[]` satırı `ZombieR2Tests.Z3b_…` olacak; C-00'da (C-00 kaydı, S-05'in Z3/P3 commit'inden sonra) | BEKLEYEN §20, lint TEST-EXPLICIT |
| | P2, P5, Z3, P3 | **açık:** FINAL komutunda `--r1-evidence` / `--r1-commit` (P2, P5, Z3) ve `--b9-poses` (b9, P3) | `wp078_accept.py:583-599`, `:1087`, `:960` |
| **C-00** | Z3b | ✅ eşik main'de (`1bca9052`); BEKLEYEN §11 kaydı `19baeee9` ile düzeltildi. Kalan: `tests[]` → `ZombieR2Tests.Z3b_…` (C-00 yazar) | §1 not; `K/wp-084/DEVIR-06.10.md` K2 |
| | U1 | ✅ iki ölçüm anahtarı sözleşmede (`db2022af`, S-10). Entegrasyonda `8ba256ec` ile ardışık | `db2022af` gövdesi |
| | entegrasyon | S-10 dalının 12 commit'i sırayla, önce `8ba256ec`. S-05 dalı (`e6c8b755`) ile deneme birleştirmesi: `r2-gates.json` çakışmasız; `r2-measure-contract.md`'de iki bitişik-ekleme çakışması (H1b/H6 ↔ P3, U1 ↔ Z3) | bu güncellemenin scratch denemesi |
| | H1 | Hero v2 alımında dirsek kontrolünün açılması | `r2-measure-contract.md` "H1 (hand to grip, decision B; elbow report)" |
| | S1 | v4 oyundayken varyant C'nin anlamı | `measuredBy.env` |
| | P1, P4, nişan | R1 kapılarıyla taşındığının kaydı; nişan raporunun sahibi | TASLAK §5, §7 |
| | T4 | Karar verildi (SABIT-KURALLAR Ç21): kitler R2 FINAL'de tek görselli sayfada toplanır; kullanıcı koşmazsa sınıf "beyan + deterministik eş" olur. Kalan iş: §5.3 kitinin o sayfaya alınması | SABIT-KURALLAR Ç21; `system/18` §6 değişmez 5 |
| **S-01** | zincir | Guard D8/D9 durumu; her FINAL oturumu guard'lı. Son compile-check: `7897f642` PASS (unity exit=0, outcome=ok, errorsCS=0, 06.10 02:33; `K/ci/compile-7897f642da-20261005T232119Z/timeline.txt`). `8ba256ec`'li ağaç için compile-check yok → FINAL SHA'da yeniden | BRIFING §7, §14 |
| **S-07** | — | R2 kapısı yok (WP-086). Kovan görseli ↔ ses hizası için kapı önerisi açık | BEKLEYEN "S-10 → S-07" |
| **S-04** | — | R2 kapısı yok | BRIFING §7 |

---

## 4. R2 FINAL zinciri: önerilen sıra ve ön koşullar

**A. FINAL SHA seçilmeden önce: main'e girecekler.** Sıra bağımlılığa göre.

1. S-09 `8ba256ec`, hemen ardından S-10 dalı `wp-091-s10-r2-gates-r2`'nin 12 commit'i sırayla (`bf177e7f` … `1e7abcc8`). İkisi tek başına lint ERROR verir (U1 KEY-EXTRA / KEY-MISSING; `db2022af` gövdesi).
2. S-06 B2d-on/B2e (WIP `dee54ffa`; B2d `9f87b7d4` main'de): tutuş varsayılan açık + temas IK'sı (H1, H3). Sonra B2f (H1b; `K/wp-083/DEVIR-06.10.md`).
3. S-02 `wp-087-heldkick`, S-06 tesliminin üstüne (S-06 notu "önce B2d/B2e"). Etkiler: H1 atış, H4.
4. AP-024C → WP-082 hero v2 alımı (H1 dirsek, H2, H4a, H5, H6) ve WP-083 klip alımı (L1–L6).
5. S-03 `wp-084-s03-slice1` (`e81b9500`, `3f4c737a`, `840e3768`; teslim edilmedi; `d0d0580b`'nin eşdeğeri main'de, `283591b8`): Z1 yazıcısı + `measuredBy`, SlotFace (Z4), G1. Ardından zombi v2 alımı (Z1–Z5) ve AP-008C zırh okunurluğu (Z1). Z3b eşiği ve kaynak assert'leri main'de (`1bca9052`, `8f10dddb`, `173890ac`).
6. S-05 `wp-091-s05-z3-r2` (`99a33758`, `e6c8b755`): `r2_z3.py` (Z3, P3). Ardından C-00'ın Z3b `tests[]` kaydı.
7. S-08: v003g overlay main'de (`7897f642`). Kalanlar: S4 kaynak assert'i `98e3dffd` (`wp-085-b03-gc`) ve S3 yazıcısı.
8. S-09 tek teslim (yükleme sanatları, sinematik B, ikon v2).
9. Henüz olmayan yazıcılar: H1b, H2, H4a, H6, L1, L1t, L2, L3, L4, L5, S3. Z1'inki `840e3768`'de, Z3/P3'ünki S-05 dalında.

**B. Sözleşme düzeltmeleri.** Her biri ölçümden önce, C-00 kaydıyla, `r2-gates.json` + `tests/test_r2_gates.py` + `r2-measure-contract.md` birlikte değişir.
- ~~Z3b eşikleri~~: yapıldı (`1bca9052`).
- ~~H1 `measuredBy`~~: yapıldı (`6e0b41d1`, `env` yok).
- ~~U1 anahtarları~~: yapıldı (`db2022af`).
- ~~H1b ve H6 satırları~~: yapıldı (`510c62f9`).
- Z3b `tests[]` satırı → `ZombieR2Tests.Z3b_…` (C-00 kaydı, BEKLEYEN §20; S-05'in Z3/P3 commit'inden sonra).
- Yeni yazıcılar için `measuredBy`, yazıcıyla aynı commit'te (H1b, H6, Z1 ve sonrakiler). Yazmayan bir `measuredBy` lint ERROR'dur (`16a95f0e`).
- Hero v2 sonrası H1 `elbowMaxDeg` ≤ 165 kontrolü.

**C. Çıkış kapısı.**
- **Lint ön adımı zincirin içinde (`d495f245`).** R2 turunda `wp078_accept` START'tan önce `r2_gates_lint`'i FINAL ağacında koşar (`tools/qa/wp078_accept.py:478-481`, `:516-536`):
  - ERROR varsa exit 3: hiçbir aşama başlamaz, kanıt dizinine dokunulmaz, ERROR kodları yazılır;
  - WARN'lar log'a ve `<E>/r2-gates-lint.json`'a yazılır;
  - R1 turu lint koşmaz;
  - uç ağaçta `8ba256ec`'siz kuru koşu bunu gösterdi: exit 3, "KEY-MISSING U1" (bu güncellemede koşuldu).
- `python3 tools/qa/r2_gates_lint.py --strict` → WARN 0 hedeflenir. Kalan WARN'lar C-00'ın kabul listesine adıyla yazılır.
- `python3 -m unittest discover -s tools/qa/tests` yeşil. Bu biçimi S-10 commit'leri kullandı (`7b25a393` gövdesi: 465 OK); bu belge testleri koşmadı.
- `tools/compile-check.sh <FINAL>` PASS. Son PASS `7897f642`; `8ba256ec`'li ağaç derlenmedi.
- FINAL'de main-ci (A1 paritesi).

**D. Makine.**
- Disk ≥ 15 GB, swap ≤ 12 GB (`df -k`).
- Guard temiz (GUARD-STOP yok).
- R1 kaynakları yerinde: `K/wp-078/evidence/84941661/{build-probe,a2,a3}`. R1 probe build A4 ve Z3'ten önce kendini kanıtlar (`r1_build_proof`).
- Sessiz pencere ilan edilir: Codex/Blender yok, kopya yok.
- **Sessiz faz iki oturuma bölünebilir:** önce a4, sonra b9 + z3. a4, b9 ve z3 sessiz pencereyi kendileri alır (`tools/qa/wp078_accept.py` başlık notu, `7b25a393`).
- Makine sessizleşmezse `wp078_perf` `--quiet-wait-max` (vars. 1800 s) sonunda bütün slotları geri verir ve `--quiet-pause` (vars. 180 s) sonra yeniden dener (`69a5755a`).

**E. Koşu.** `wp078_accept.py` STAGE_ORDER, `tools/qa/wp078_accept.py:77`:

`a6 → a0 → a1 → a2 → a3 → r2 → a5 → a4 → b9 → z3 → r2q → a8`

Önce `--dry-run` ile oturum listesi doğrulanır. Kuru koşu da lint ön adımını ve R1 build kanıtını yapar.

```
python3 tools/qa/wp078_accept.py --final <FINAL 40-hex> --round R2 \
  --r1-evidence K/wp-078/evidence/84941661 --r1-commit 84941661622cfbec8db796b639e4032df4457903 \
  --b9-poses K/wp-078/evidence/b9-poses/b9-poses.json --dry-run
```

- `--r1-evidence` / `--r1-commit`: P2 (a4), P5 (r2) ve Z3 (z3) için şart. Yoksa üçü de BLOCKED adıyla (`wp078_accept.py:583-599`, `:1087`, `:960`).
- `--b9-poses`: b9 pencereleri ve P3 için (`r2_z3.py`'ye de geçer).
- İki oturumlu koşuda aşamalar `--stages` ile seçilir. Bu biçim bu güncellemede kuru koşuyla denenmedi.

| # | Aşama | R2 kapıları | Not |
|---|---|---|---|
| 0 | R2 lint ön adımı (START'tan önce) | — | ERROR → exit 3, hiçbir aşama başlamaz; WARN'lar `<E>/r2-gates-lint.json` (`d495f245`) |
| 1 | a6 build'ler (dev, probe, release) | — | P2 ve Z3'ün R2 tarafı probe build'i |
| 2 | a0 provenance · a1 main-ci paritesi · a2 bot | P5 R2 örnekleri (a2) | — |
| 3 | a3 bayraklar + izoleler | **Z1** zorunlu testi (A3 izole XML; değer için r2-Z1.json da gerekir), P5 (a3) | — |
| 4 | **r2** (11 oturum) | H1, H3, H4, H4b, Z2, Z3b, Z4, S5, U1, U2, U3 + P5 (zincir) | Her oturum öteki sahiplerin anahtarları silinmiş başlar (`env -u`, `d495f245`). Ağırlık sırası (KADRO adım 4): bugün kırmızı ya da BLOCKED olanlar önce (U1, U3, Z3b, H3) |
| 4b | **sahip koşuları** → `<E>/r2/` (`DD_R2_MEASURE_DIR`, `DD_R2_COMMIT=<FINAL>`) | H1b, H2, H4a, H6, L1, L1t, L2, L3, L4, L5, Z1 (sessiz değil) | `measuredBy` gelmediyse. Başka commit = FAIL |
| 5 | a5 soak (sessiz) | (P4 R1 kapısı) | — |
| 6 | **a4** perf (sessiz) | P2 | R2/R1 ABBA dönüşümlü, `--r1-*` ile (`5e213eb2`, `69a5755a`). A4 kapısı yalnız FINAL pencereleriyle yargılanır; bozuk pencere ya da eksik hücre → r2-P2.json yok (BLOCKED adıyla) |
| 7 | **b9** (sessiz) | (P3 girdisi) | B9 poz pencereleri `<E>/b9/<poz>/`; `stages/b9.json` (RECORDED). a4'ten ayrı aşama (`7b25a393`) |
| 8 | **z3** (sessiz pencere) | Z3, P3 | FINAL ve R1 probe ABBA, `r2_z3.py` sabitleriyle (S-05 dalında: poz `0,-4,0`, sinematik, topdown + tp, ≥ 5 çift, `-ddArmor all`). Sonra `r2_z3.py` → r2-Z3.json, r2-P3.json. Araç yoksa pencere koşmaz, ikisi BLOCKED adıyla. Z3 ölçümü sayaçtır, sessizlik şartı yok (BEKLEYEN §20); aşama yine de pencereyi alır |
| 9 | **r2q** (sessiz) | S1 + S2 (tek kit oturumu), S4 | + S3 (Dense-40 Player, yazıcı gelirse sahip koşusu) |
| 10 | insan kapıları → `<E>/r2/reviews/<id>.json` | H5, L6, Z5, S6, U4 | FINAL commit'inde. MUST/SHOULD maddeleri ölçüte ve kareye bağlı |
| 11 | a8 `--round R2` | rapor | R2 özeti: FAIL > BLOCKED > PASS; rapor kapıları düşürmez |

Sonra release mac + win64 (A6 paritesi) ve portföy 10 kare.

---

## 5. KADRO Genel Test Algoritması: kapsam matrisi (N2 × T1–T5)

Değerler: `var` = R2 zincirinde ölçen var · `kısmen` = ölçen var ama kapsam ya da yazıcı eksik · `boşluk` = gerekli, yok · `gereksiz` = gerekçeli. Sahiplik (`system/18` §3 adım 6): T1/T2 → A2, T3/T5 → GT, T4 → **kullanıcı**.

| N2 | T1 Sözleşme | T2 Davranış | T3 Eşik | T4 Deneyim | T5 Dayanıklılık |
|---|---|---|---|---|---|
| **N2-01** Menü | var: U1 `unnamedSelectables` (okuyucu adı) | **boşluk**: menü v4 akış ve odak davranışının R2 kapısı yok; WP-081'in kendi testleri zincire bağlı değil | kısmen: U1 eşik dışında (`u1_same_content`, ekran başına); iki ölçüm sözleşmede (`db2022af`) | kısmen: U4 AD; kullanıcı yalnız MP4 görüyor | var: U2 (TR/EN × 1,25 × Mac 1,2) |
| **N2-02** Yükleme | var: U3 `distinctSha256` | var: U3 rotasyon | var: U3 `minPerAny20`; P5 rapor | **boşluk**: yükleme ekranının insan onayı yok (U4 menüyü kapsar) | var: U3 500 tohum × 200 yükleme (TASLAK:77) |
| **N2-03** Zombi | kısmen: Z3 (doku, kemik, materyal) durum anı ağacında yazıcısız (`r2_z3.py` S-05 dalında); Z4 yazıcısı var, durum anı ağacında ölçülmedi (S-03 dalında 3 → FAIL) | var: Z2 | kısmen: Z1 artık eşikli kapı (`56805c09`) ama yazıcısız. R1'deki Passed DEC-077'yi hiç assert etmiyordu: **kapsam boşluğu, C-00 06.10**. Z3b zorunlu testi oturumsuz; **Z3 boşluk** (zincirde `z3` aşaması var, yazıcı yok) | kısmen: Z5 AD; "sinematik figürler" yalnız AD'de | kısmen: Z3b en kötü karışım rapor; Z3 yok |
| **N2-04** Kahraman | **boşluk**: rig/avatar sözleşmesinin R2 kapısı yok (öncül: AP-024C roundtrip, BRIFING §6) | **boşluk**: hero v2 alım davranışı kapısız; `Wp082HeroV2ErisimTests` `[Explicit]`, zincirde değil | kısmen: yalnız H1 `elbowMaxDeg` (rapor) | kısmen: H5 AD; kullanıcı G1/G1b seçti | **boşluk**: TASLAK §7 bot nişanı ≤ +%25 JSON'da yok |
| **N2-05** Tutuş + şarjör | var: H3 `missingEvents` (§12 olay sözleşmesi) | var: H3 `floating`, H4b | kısmen: H1 (yalnız TP × duruş/yürüyüş; TASLAK 10 × TP/FP × 5 durum ister), H4 var; **H2, H4a ve yeni H1b (sol el kavrama), H6 (TP görünürlük) yazıcısız** | **boşluk**: tutuş ve reload için insan kapısı yok (R1 kullanıcı notu "silahlar omzunda") | **boşluk**: atış altında tutuş (ar_2 bilinen kırmızı), reload ortasında kamera (PM/WeaponReloadMagazineTests.cs:254) R2 dışında |
| **N2-06** Locomotion | **boşluk** (GT B2): Unity içe aktarım sözleşmesi (Avatar valid/isHuman, sıkıştırma, klip olayları → AudioEventMap) ölçülmedi (`C00-AP026C-RAPOR.md:155`). Çevrimdışı 142/142 FBX denetimi bunu kapsamaz | **boşluk**: L1–L5 yazıcısız | **boşluk**: L1, L1t, L2, L3, L5 yazıcısız; yalnız çevrimdışı FBX değerleri | kısmen: L6 AD | **boşluk**: L1t/L3 geçiş pencereleri tanımlı, ölçen yok; L5 40 zombi yok |
| **N2-07** Silah sesi + kovan | var: S2 sayaçlar, S5 `surfaceMismatches` | var: S2, S5 | kısmen: S1 eşik altında ve v4'le ölçülmedi; S4 var; **S3 boşluk** | var: S6 (kullanıcı), kit henüz yok | kısmen: **S3 Dense-40 tepe ve kırpma boşluk**; S2 kırılgan; S5 `queueMax` rapor |

**Hücre sayımı (35):** var 12 · kısmen 12 · **boşluk 11** · gereksiz 0. Bu güncellemede hücre değerleri değişmedi; yalnız açıklamalar güncellendi.
- Boşluklar: N2-01: 1 · N2-02: 1 · N2-03: 0 · N2-04: 3 · N2-05: 2 · N2-06: 4 · N2-07: 0.
- İlk sürümde N2-06 T1 "gereksiz" yazılmıştı; GT B2 ile boşluğa çevrildi.
- "Kısmen" hücrelerin içinde 7 yazıcısız kapı daha var:
  - N2-05 T3: H2, H4a, H1b, H6;
  - N2-03 T1/T3/T5: Z3; N2-03 T3: Z1;
  - N2-07 T3/T5: S3.
  - L1–L5 zaten boşluk hücrelerinde.
- H1b ve H6'nın N2 eşlemesi kayıtta yok. Burada N2-05'e yazıldılar, çünkü kaynakları WP-083 B2e (tutuş) AD MUST-2 ve MUST-3.
- Boşluk sayısı G4 ağırlığına eklenir (`system/16` §2.2).

### 5.1 En önemli üç boşluk

Risk ağırlığı `system/18` adım 4'e göre hesaplandı: kritiklik × 2 + yeni alan + geçmiş bayrak + tek koşu.

1. **N2-06 Locomotion: T1, T2, T3 ve T5 oyun içinde hiç ölçülmüyor.** Ağırlık 10.
   - T1: Unity içe aktarım sözleşmesi (Avatar, sıkıştırma, klip olayları) ölçülmedi.
   - L1, L1t, L2, L3 ve L5'in yazıcısı yok. Elde yalnız AP-026C çevrimdışı FBX değerleri var ve rapor "runtime L1/L1t/L3 ölçülmedi" diyor (`C00-AP026C-RAPOR.md:155`).
   - Kullanıcının "çok amatör" notunun (R2-PLAN §1) tek oyun içi kanıtı bugün L6 AD turu.
2. **N2-05 Tutuş: kullanıcının birebir şikâyeti ("silahlar omzunda") eşikte ölçülmüyor.** Ağırlık 10.
   - H2 (omza ve göğse gömülme) ile H4a (FP kol kadrajı) yazıcısız. 06.10'da eklenen H1b (sol el kavrama) ve H6 (TP silah görünürlüğü) da yazıcısız.
   - H1 artık zincirde koşuyor (`6e0b41d1`), ama yalnız TP × duruş/yürüyüş ölçüyor ve durum anı ağacının varsayılan yolunda tutuş kapalı.
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
  - Tutuş açıkken (S-06 WIP), yalnız TP duruş/yürüyüşte ölçüldü; b2i'de rl_1 tutuşu kapalıydı. Varsayılan yolda (tutuş kapalı) ölçüm yok.
  - Kapsam: FP, koşu, nişan ve atış yok; atışta sol el 1,6–9,6 cm (`K/wp-083/DEVIR-05.10.md`, b6hk). b2l2'de C (pompalı) atışta 2,68 cm (`K/wp-083/DEVIR-06.10.md`).
  - `elbowMaxDeg` b2m'de 173,1°: hero v2 sonrası açılacak ≤ 165 kontrolünün üstünde (bugün rapor).
  - Çürütme: kapsam TASLAK'a genişleyince > 1,0 cm.
- **Z1:** R1 FINAL'' A3'teki Passed bir eşik kanıtı değildi; test DEC-077'yi assert etmiyordu (`56805c09` gövdesi). `840e3768`'den önce aynı commit iki koşuda 0,299 ve 0,264 siluet verdi. Deterministik kodla R1'de iki koşu 0,286 / 0,279 (fark 0,007). Tek koşu yeterli kanıt değildir (§5.18).
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
