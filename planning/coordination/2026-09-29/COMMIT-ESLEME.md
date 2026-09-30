# Codex dönemi commit eşlemesi — `0af8269a..422de54a` (49 commit)

**Kaynak:** C-00 30.09 Codex denetimi. Altı alanda denetim yapıldı ve her birini bulguları çürütmeye çalışan bir doğrulayıcı inceledi: 106 doğrulanmış bulgu (kritik 0, yüksek 9, orta 58, düşük 39), 87 plan satırı. Plan dosyası `~/Desktop/KADRO/context/projects/deaths-demolition/c-00/CODEX-DUZELTME-PLANI-2026-09-30.md`; sahip başına listeler aynı yerde `codex-duzeltme/<OTURUM>.md`. Bu ek, Codex denetiminin K-02 satırıdır (WP-081, C-00 30.09).

**Okuma**
- **Tarih:** main'e giriş (committer) zamanı, +03.
- **WP:** mesajda yazan kimlik. Mesajda kimlik yoksa "(ç)" işaretli; bu durumda kimlik dosya alanından çıkarılmıştır (49 commit'in 38'inde kimlik yok).
- **Sahip:** dosya alanına göre oturum.
- **Test/kanıt:** commit gövdesinde yazan; yazmıyorsa "yok". Codex kanıtlarının çoğu `/private/tmp` altındaydı ve artık yok (K-04).
- **Durum:**
  - "tutuldu" = main'de kalıyor.
  - "düzeltme:" = plan satırları. Y = yüksek önem; M = orta ve düşük önem; K = C-00 süreç satırları.
  - "kayıtsız" = Codex'in kendi defterinde (`intake-ledger.json`, `commit-audit.md`, `MAIN-STATUS.md`) geçmeyen son 9 commit.
- Geçmiş yeniden yazılmadı, etiketler silinmedi.

## Önce: kayıtsız 9 commit

`66bc6776` (WP-073 B3) · `b2be2562` (geri alındı) · `f94ccab3` (WP-071 S06 tanısı) · `a97727fc` (park) · `74f62dd6` (geri alındı) · `d6a73dc3` (CI süit kaydı) · `f386107b` (WP-077 pin API) · `8ccceae5` (WP-068 pin/gezinme) · `422de54a` (GameFeel fikstürü 7→4 m).

## Tablo

| # | commit | tarih | konu (kısa) | WP | sahip | test/kanıt | güncel durum |
|---:|---|---|---|---|---|---|---|
| 1 | e5888d47 | 28.09 20:07 | QA bot spawn sayımı + [Progress] satırı (Claude commit'i) | WP-076 | S-03 | PlayMode QaRunBotSliceTests 4/4 (guard'lı, scope editor) | tutuldu, düzeltme: M-31 |
| 2 | 92eb636e | 28.09 20:19 | wp078_perf guard kapsamı ve çıkış kodu | WP-078 | S-10 | yok | tutuldu, düzeltme: M-14 |
| 3 | 2b574fb2 | 28.09 20:28 | TR taşma: BIRAK / GERİ AL / DÖV | WP-068 | S-09 | capture taşma logu (tr-caps2); test satırı yok | tutuldu, düzeltme: M-69 (DÖV kalır) |
| 4 | 9b0a396e | 28.09 20:28 | VfxWeaponCaptureAgent fire10 (Claude S-07 commit'i) | WP-073 | S-07 | dev build derlemesi exit 0; capture koşusu yok | tutuldu, düzeltme: M-55 |
| 5 | eb2f8bb1 | 28.09 20:28 | launcher bastırma + pompalı kovan atımı | WP-073 (ç) | S-07 | yok | tutuldu, düzeltme: M-21, M-61 |
| 6 | 19cb757a | 28.09 22:40 | görünür TR arayüz metinleri + TR başlık yüzü SourceSans3 | WP-068 (ç) | S-09 | 38 seçili EditMode + guard'lı ağaç/HUD capture, taşma 0 | tutuldu, düzeltme: M-64 (TR başlık: kullanıcı kararı 30.09), M-65, M-66, M-67, M-69 |
| 7 | 7bcb94fd | 28.09 23:10 | V6 havuz korumaları köprü güvertesinden uzak | WP-075 köprü (ç) | S-05 | yok | tutuldu, düzeltme: M-39, M-40 |
| 8 | 4934e80f | 28.09 23:10 | köşe yakını gerekli NavMesh dönüşleri | WP-076 (ç) | S-03 | yok | tutuldu, düzeltme: M-23, M-24 |
| 9 | 12c7215c | 28.09 23:10 | dönüş ilerlemesi + flank hedefi yürünür zeminde | WP-076 (ç) | S-03 | yok | tutuldu, düzeltme: M-23, M-25 |
| 10 | a17cb73d | 28.09 23:10 | temas hasarı testi can tabanı | WP-069 (ç) | S-02 | yok | tutuldu, düzeltme: M-20 |
| 11 | 78eae80d | 28.09 23:44 | AP-023 v013 birleşimi + V6 köprü geometrisi | WP-074 B5 (ç) | S-05 (+P-05) | yok | tutuldu, düzeltme: M-32, M-41, M-42, M-45, M-46, M-47 (W-09 çürütüldü) |
| 12 | 030cf3f4 | 29.09 00:56 | gevşetme geçişlerinde iç kota | WP-076 (ç) | S-03 | yok | tutuldu, düzeltme: Y5, M-26 (C-00 30.09: K10 ≤0,45, %50 tavan yalnız yedek) |
| 13 | 122cf7d1 | 29.09 01:23 | compile/CI checkout lease'i | WP-079 (ç) | S-01 | yok | tutuldu, düzeltme: Y1 (kapandı, WP-079G `edb4dd69`), M-05, M-06, M-07 |
| 14 | 69d46a07 | 29.09 01:50 | opt-in fixed3 kapı seti probu | WP-076 (ç) | S-03 | yok | tutuldu, düzeltme: Y6, M-28 |
| 15 | df082391 | 29.09 01:59 | GameFeel fikstürü gerçek gövde isabeti | WP-069 (ç) | S-02 | yok | tutuldu; **derlenmez (CS0104), bisect'te `git bisect skip df082391`** (K-01); düzeltme: Y4 revert zinciri |
| 16 | ff06d579 | 29.09 01:59 | GameFeel hasar bölgeleri tam nitelendi | WP-069 (ç) | S-02 | yok | tutuldu (df082391 derleme hatasını kapattı; GameFeel kırmızısını kesinleştirdi, CC-N2), düzeltme: Y4, K-08 |
| 17 | c6fc439e | 29.09 04:41 | HUD durumu Awake'te (cherry-pick) | WP-068 (ç) | S-09 | hedefli 2/2 PASS, istisna 0; özgün sözleşme FAIL'leri açık | tutuldu, düzeltme: K-08 |
| 18 | 92232235 | 29.09 04:42 | üç iç spawn kapısı taşıma + yerel ağaç temizliği (cherry-pick) | WP-074/WP-076 (ç) | S-05 + S-03 | yok | tutuldu, düzeltme: Y7, M-27, M-47, K-07 (C-00 30.09: kapı seti korunur, nihai ölçüm Y7 sonrası) |
| 19 | b9eb99fd | 29.09 05:09 | TP Special bağlamaları, ff06 tabanı (S-10 doğrudan cherry-pick) | WP-071 (ç) | S-06 | yok (intake'te 1 test; sayaç testleri 877a9aef main-ci'de PASS) | tutuldu, TP Special varsayılan KAPALI; düzeltme: Y8, M-48, M-49, M-52; M-51 dalda kapalı |
| 20 | c34e48f8 | 29.09 05:31 | kapsama eşiği %7 (cherry-pick) | WP-076 (ç) | S-03 | yok | tutuldu, düzeltme: M-29 |
| 21 | 3c7b4872 | 29.09 05:31 | kapsama hata mesajı %7 (cherry-pick) | WP-076 (ç) | S-03 | yok | tutuldu, düzeltme: M-29 |
| 22 | 3c0875e1 | 29.09 05:47 | kontroller alt bilgisi kırpılması + profil ödülleri TR (cherry-pick) | WP-068 (ç) | S-09 | yok | tutuldu, düzeltme: M-68, M-69 |
| 23 | 24901e25 | 29.09 07:16 | Codex koordinasyon kaydı: dispatch + intake defteri | WP-078 (yanlış kimlik) | C-00 (Codex) | belge; bağlantı/SHA kontrolü (Codex beyanı) | tarihsel kayıt (WP-081 işareti); K-03, K-04 |
| 24 | dd4e5bff | 29.09 07:17 | gök panoraması açılımı + capture kanıtı | WP-075 gök (ç) | S-05 | yok | tutuldu, düzeltme: M-43 (kullanıcı 30.09 teyit etti; AD turu), M-44, M-45, M-47 |
| 25 | 5f40e9c6 | 29.09 07:19 | opt-in eski skin tanısı (AnimationTests) | WP-071 (ç) | S-06 | yok | tutuldu, düzeltme: Y6, Y8, M-50 |
| 26 | 51209880 | 29.09 07:19 | opt-in C1 gözlemcisi | WP-076 (ç) | S-03 | yok | tutuldu, düzeltme: Y6, M-10, M-22, M-30 |
| 27 | 641d1858 | 29.09 07:22 | integrate.sh durum okunamazsa kapalı kal | WP-079 (ç) | S-01 | yok | tutuldu, düzeltme: M-08 (kapandı, WP-079G `05cb9912`) |
| 28 | 95c93072 | 29.09 07:27 | Codex kaydı: dört intake + kaynak koruma | WP-078 (yanlış kimlik) | C-00 (Codex) | belge; bağımsız inceleme (Codex beyanı) | tarihsel kayıt; K-03, K-04 |
| 29 | c29b4108 | 29.09 07:27 | açılış yükleme bekçisi | WP-068 (ç) | S-09 | yok | tutuldu, düzeltme: M-70 (sözleşme korunur) |
| 30 | a8dc1687 | 29.09 07:30 | Codex kaydı: beş intake partisi dondurma | WP-078 (yanlış kimlik) | C-00 (Codex) | belge | tarihsel kayıt; K-03 |
| 31 | 53f74cd7 | 29.09 07:40 | combat CI tanı gözlemcileri | WP-069 (ç) | S-02 | yok | tutuldu, düzeltme: Y4, M-18, M-19 |
| 32 | c8b7adae | 29.09 07:42 | kalabalık ızgara kovaları korunur | WP-076 (ç) | S-03 | yok | tutuldu (planda bulgu yok) |
| 33 | 877a9aef | 29.09 07:44 | Codex kaydı: yedi intake + taban doğrulaması | WP-078 (yanlış kimlik) | C-00 (Codex) | yok (main-ci XML'i Y8 sayaç kanıtı) | tarihsel kayıt; K-03, K-06 |
| 34 | f794a92c | 29.09 08:42 | RC deneme durumu Player çıkış makbuzuna bağlı | WP-079 (ç) | S-01 (+S-10) | yok | tutuldu, düzeltme: Y2, M-09. **DEC-091** (RC kararlılık tohumları 61/26, gerçek bot yalnız rapor) ve **DEC-093** (release tavanı 5120 MB; dbf9d8f4 ile 5×10⁹ B) uygulamaları bu commit'te kayıtsız paketlendi |
| 35 | 418daef0 | 29.09 08:44 | test koşularına yalnız bağlı opt-in tanı argv'si | WP-079 (ç) | S-01 | yok | tutuldu (WP-079F sonrası test.sh bu hâlde), düzeltme: M-10 |
| 36 | 9782f5e5 | 29.09 08:44 | RT10: değiştiricisiz Ocak harcamadan reddedilir | WP-077 | S-04 | yok | tutuldu, düzeltme: M-37 (RT10 yarım) |
| 37 | 2a87fab7 | 29.09 08:44 | RT6: round başına tek erişilebilir pin | WP-077 | S-04 | yok | tutuldu, düzeltme: M-33, M-34, M-35, M-36, M-38 |
| 38 | 8c7c22c1 | 29.09 08:44 | Ocak round sınırında BUY kapalı (RT9) | WP-068 (ç) | S-09 | yok | tutuldu, düzeltme: M-63 (RT9 yarım) |
| 39 | 5e71a508 | 29.09 08:44 | A3 ek hükümleri ve kaynak iddiaları korunur (wp078_report) | WP-078 (ç) | S-10 | yok | tutuldu, düzeltme: M-15 (birim kısmı dbf9d8f4 ile kapandı) |
| 40 | c95e37c7 | 29.09 08:48 | Codex kaydı: ikinci kurtarma dalgası | WP-078 (yanlış kimlik) | C-00 (Codex) | S04 EM 39/39 + 13 yeni, S09 PM 3/3 guard'lı (Codex beyanı) | tarihsel kayıt; K-03, K-04 |
| 41 | 66bc6776 | 29.09 09:00 | launcher B3 (ExplosionDirector, RocketVisual, capture ajanı) | WP-073 (ç) | S-07 | yok | **kayıtsız** · tutuldu, düzeltme: M-54, M-55, M-56, M-57, M-58, M-59, M-60, M-62 |
| 42 | b2be2562 | 29.09 12:37 | test.sh S06 tüketici baytları izin listesi | WP-079 (ç) | S-01 (S-06 tüketici) | yok | **kayıtsız** · **GERİ ALINDI** (WP-079F `5593a255`); kalan: M-50 |
| 43 | f94ccab3 | 29.09 12:39 | S06 ham capture'a modül metaverisi (AnimationTests) | WP-071 (ç) | S-06 | yok | **kayıtsız** · tutuldu, düzeltme: Y8 (kalır/park kararı), M-50 |
| 44 | a97727fc | 29.09 12:44 | native observation sözleşmeleri | WP-079 (ç) | S-01 | yok | **kayıtsız** · **PARK** (fed44c64 revert, H1); M-13 kapalı |
| 45 | 74f62dd6 | 29.09 14:43 | guard protokolü + UTC yazıcı tanısı | WP-079 (ç) | S-01 | yok | **kayıtsız** · **GERİ ALINDI** (WP-079F `d0b987c2`); yeniden teslim kabul listesi: Y3, M-04 |
| 46 | d6a73dc3 | 29.09 14:45 | A3 CI süit kaydı + sahiplik (ci-suites/ci-owners) | WP-078/WP-079 (ç) | S-01 (+S-10 A3) | yok | **kayıtsız** · tutuldu, düzeltme: M-11 |
| 47 | f386107b | 29.09 14:48 | pin API + fikstür (Source20) | WP-077 (ç) | S-04 | yok | **kayıtsız** · tutuldu, düzeltme: M-33, M-34, M-36, K-08; main-ci 710aeaaa kırmızısı NodeCardTests.CanSetPin → S-04 |
| 48 | 8ccceae5 | 29.09 15:04 | ağaç gezinme + pin eylemi (source19) | WP-068 (ç) | S-09 (+S-04) | yok | **kayıtsız** · tutuldu, düzeltme: M-34, K-08; main-ci 710aeaaa'da RunTreePinActionRegressionTests ×6 kırmızı → S-09 + S-04 |
| 49 | 422de54a | 29.09 15:06 | gövde koridoru fikstürü 7→4 m (source19) | WP-069 (ç) | S-02 | yok (4 m Unity'de hiç koşmadı) | **kayıtsız** · tutuldu, düzeltme: Y4 (7 m'ye dönüş S-02 WP-069 B5 dalında), K-08 |

## Etiketler (silinmedi)

Bu etiketler wp-* adlandırma kuralına uymuyor: 16'sı `codex-*`, 25'i `wpNNN-…-2026092x` biçiminde. Etiketi olmayan 8 commit: c6fc439e, 92232235, b9eb99fd, c34e48f8, 3c7b4872, 3c0875e1, 24901e25, 95c93072.

- e5888d47 `codex-wp076-ab-20260928` · 92eb636e `codex-wp078-guard-20260928` · 2b574fb2 `codex-wp068-tr-20260928` · 9b0a396e `codex-wp073-capture-20260928`
- eb2f8bb1 `codex-wp073-casing-20260928` · 19cb757a `codex-wp068-visible-tr-20260928` · 7bcb94fd `codex-wp075-bridge-20260928` · 4934e80f `codex-wp076-corner-20260928`
- 12c7215c `codex-wp076-flank-20260928` · a17cb73d `codex-wp069-contact-baseline-20260928` · 78eae80d `codex-wp038m-v013-compose-20260929` · 030cf3f4 `codex-s03-innerquota-20260929`
- 122cf7d1 `codex-ci-checkout-lease-20260929` · 69d46a07 `codex-s03-fixed3-probe-20260929` · df082391 `codex-s02-gamefeel-body-20260929` · ff06d579 `codex-s02-gamefeel-types-20260929`
- dd4e5bff `wp075-sky-intake-main-20260929` · 5f40e9c6 `wp071-palm-observer-main-20260929` · 51209880 `wp076-contact-observer-main-20260929` · 641d1858 `wp079-integrate-status-main-20260929`
- c29b4108 `wp068-boot-watchdog-main-20260929` · a8dc1687 `wp078-main-salvage-20260929` · 53f74cd7 `wp069-combat-observers-main-20260929` · c8b7adae `wp076-bucket-capacity-main-20260929`
- 877a9aef `wp078-main-salvage-r2-20260929` · f794a92c `wp079-rc-native-exit-main-r2-20260929` · 418daef0 `wp079-diagnostic-argv-main-r2-20260929` · 9782f5e5 `wp077-rt10-null-main-r2-20260929`
- 2a87fab7 `wp077-rt6-pin-main-r2-20260929` · 8c7c22c1 `wp068-hearth-limit-main-r2-20260929` · 5e71a508 `wp078-a8-r6-main-20260929` · c95e37c7 `wp078-main-salvage-r3-20260929`
- 66bc6776 `wp073-launcher-b3-main-r4-source-intake-20260929` · b2be2562 `wp079-s06-consumer-helper-main-r4-20260929` · f94ccab3 `wp071-loaded-module-metadata-main-r4-20260929`
- a97727fc `wp079-native-observation-main-r4-20260929` · 74f62dd6 `wp079-guard-clock-main-r4-20260929` · d6a73dc3 `wp078-canonical-ci-main-r4-20260929`
- f386107b `wp077-pin-api-main-r4-source20-20260929` · 8ccceae5 `wp068-nav-pin-main-r4-source21-20260929` · 422de54a `wp069-fixture-main-r4-source22-20260929`

## Notlar

- **Süreç ihlalleri (Y8, K-01):**
  - b9eb99fd, 5f40e9c6 ve f94ccab3 integrate.sh dışında doğrudan main'e alındı.
  - df082391 derlenmeden main'e girdi.
  - Kural: doğrudan main'e yazılan her commit için `integrate.sh --compile` ya da main-ci derleme adımı zorunlu.
- **İlk tam CI (main-ci 710aeaaa, 30.09):** kırmızı. EditMode 1516/1518, PlayMode 219/227, derleme 0.
  - Kırmızılar sahiplerde: NodeCardTests → S-04; RunTreePinActionRegressionTests ×6 → S-09 + S-04; Kalabalik40 GC → S-06.
  - GameFeel ve DEC082 bu koşuda geçti.
- Codex yan ürünleri (`K:wp-078/tools/r1-accept2.sh`, `player-run.sh`, `wp-079/driver`, `c-00/main-20260929`) repo dışındadır. Düzeltme satırları: M-12, M-16, M-17, K-05, K-06.
