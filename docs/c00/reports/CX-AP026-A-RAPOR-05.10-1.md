# CX-AP026-A RAPOR 1 — 05.10.2026
## ÖZET (≤10 satır)
Durum: **TESLİM — Faz A belge/karar paketi**.
Ne üretildi: Üç bağlayıcı belge; kemik, klip, olay ve Mixamo listeleri; **553 plan satırı**, yeni FBX **0**.
Teslim yolu: [/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/exports/v001/](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/exports/v001/) — yedi dondurulmuş belge/veri dosyası.
Manifest: [AP026-manifest-v001.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/manifest/AP026-manifest-v001.json) · SHA256 `0b55c72ab40afbcb1b63b5c5821d90e197c019ff86c8d0242c42f1772b2d360d`.
Bütünlük: [SHA256SUMS](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/SHA256SUMS) · tek mekanik dd_validator **21/21 PASS**.
Kalite kontrol: Lider statik kontrol **12/12 PASS**; runtime/deformasyon/görsel kalite **ölçülmedi**.
Açık: İki C-00 karar sorusu; Mixamo katalog doğrulaması ve B fazı teknik ölçümleri aşağıda.
Sonraki adım: C-00/S-06 belge değerlendirmesi, ardından ayrı B görevi; bu oturum **DURDU**.

## 1. Yapılanlar ve karar gerekçeleri
CODEX-SISTEM-R2, CX-AP026-A, KADRO-CODEX-AJANLARI HIZ MODU, WP-071 yön/iş paketi, AP009/AP022 durumu ve repo karakter/animasyon kaynakları okundu. Kullanıcının görev değişikliği sonrası AP024'e yazılmadı, mevcut dosyalar silinmedi. Ana repo/Unity projesi değiştirilmedi.

Araştırma beş somut AAA referansı (TLOU Part II, RE4 Remake, Days Gone, Ghost of Tsushima, Dead Island2) kaynak beyanı / proje çıkarımı / öneri olarak ayırır. Locomotion, tüm beden ağırlık/karşı rotasyon, baş/kol/parmak, secondary, zombi kütlesi, start/stop/pivot, temas ve12silah reload anatomisi için üretim karşılığı yazıldı. Video oynatılmış veya kadans/açı ölçülmüş gibi sunulmadı.

Rig standardı eski **thumb REST21°→72,8°**, **yanlış TP socketclass/omuz tutuşu** ve **elde kalmayan şarjör** kusurlarını isimleriyle ele alır. Bozuk action/rest snapshot yerine yeni nötr bind/Avatar; el altı canonical soket; silaha özel palm/mount kalibrasyonu ve final solved pose'dan prop transferi önerildi. Bunların runtime'da giderildiği iddia edilmez.

K1 Mixamo+elle cila+prosedürel ve K2 Animation Rigging1.2.x kararları korundu. Pinned kaynak `e0ec412060b3eb1d2c0ba3944a9cb24703552e9c` manifest/lock içinde AR1.2.1 ve Burst1.8.12 beyan eder; Editor resolve doğrulanmadı. Eşzamanlı ilerleyen main yerine [input-snapshot-v1.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/validation/input-snapshot-v1.json) statik kanıtı sabitler.

Güncel AP021-classic-sheets/WP069 kimliği eski arsenal-direction'a üstün tutuldu: SG2 **Nito break-action çift namlu/iki fişek**, LMG1 **alttan kutu/soldan metal link**, LMG2 **üst Ø22×5cm pan**, RL1 **demir tüp/önden rocket**. Research/plan bu kimliklere göre tek lider düzeltmesiyle birleştirildi; Special tasarımları korundu.

HIZ MODU kalan fazda uygulandı: ayrı AD/oyun/Unity incelemesi sürdürülmedi; lider kalite kontrolü yaptı. Teslim öncesi yalnız **bir mekanik dd_validator turu** çalıştı. Ek keşif/animasyon üretimi başlatılmadı.

## 2. Üretilen dosyalar
Aşağıdaki dosyalar `01-design/` kaynaklarıyla byte-eş ve `exports/v001/` altında teslimdir. CSV/JSON listeleri üç Markdown belgenin ayrılmaz ekleridir.

| Dosya | Tür / özellik | SHA256 kısa |
|---|---|---|
| [AP026-A-arastirma.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/exports/v001/AP026-A-arastirma.md) | Araştırma;5 AAA referans, Unity/Mixamo, reload anatomisi | `cd6c9822682cbcc6` |
| [AP026-A-rig-standardi-v1.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/exports/v001/AP026-A-rig-standardi-v1.md) | Rig standardı; Humanoid/twist/parmak/REST/socket/retarget | `463d3575313ba789` |
| [AP026-A-klip-plani-v1.md](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/exports/v001/AP026-A-klip-plani-v1.md) | Klip planı; hız-kadans ve Q01–Q08 kabul sözleşmesi | `898f41f96a62ab23` |
| [AP026-bone-map-v1.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/exports/v001/AP026-bone-map-v1.json) | 84 olası/77 default kemik;30 parmak,8 twist | `3c52db1a3998f96f` |
| [AP026-clips-v1.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/exports/v001/AP026-clips-v1.csv) | 553 plan satırı; süre/hız/root/loop/olay/öncelik | `eb42124de0f9cde8` |
| [AP026-events-v1.json](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/exports/v001/AP026-events-v1.json) | 25 reload profili +korunan AP022 event/root verisi | `ed4a1b3650a774bb` |
| [AP026-mixamo-downloads-v1.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/exports/v001/AP026-mixamo-downloads-v1.csv) | 18 aday;30fps/None/WithoutSkin/in-place-root ayarları | `95c530d9a9db85c9` |

Destek kayıtları: [README](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/README.md), [araştırma kaynakları](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/03-sources/research-sources.json), [LICENSES.csv](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/03-sources/LICENSES.csv), [lider kontrolü](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/validation/leader-check-v1.json), [plan sayımı](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/validation/plan-summary-v1.json), [kaynak snapshot](/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition/asset-production/AP-026/validation/input-snapshot-v1.json).
SHA256SUMS dosyasının SHA256'sı: `d4e9507002d63e65a33cd46f30e6b60bde51552f37489f97a8428fe41cb8ba7c`. Checksum dosyası kendi kendisini içermez; paket içindeki diğer21 dosyayı kapsar.

## 3. Kalite ve doğrulama
**Lider belge kontrolü PASS:** kapsam/tutarlılık, bağlayıcı sözleşmenin rig ve klip planına aynen girmesi,4gait×8yön,12silah,4tier, mevcut4ölüm+5saldırı ailelerinin korunması; herCSV kalemi Q01–Q08 kabul kimliğine bağlı. SG1 empty end'de canonical bolt_release ile chamber alias çift tetiklemesi kaldırıldı.

**Tek mekanik dd_validator sonucu PASS:**

| Kontrol | Sonuç |
|---|---|
| Manifest7öğe SHA/byte/source parity |7/7 PASS|
| SHA256SUMS, shasum kontrolü/kapsam |21/21 OK|
| JSON parse |9/9 PASS|
| Kemik adları/parent/cycle |84 benzersiz; default77; eksikparent0/döngü0|
| Klip CSV |553 benzersiz;30fps sample/süre eşliği PASS|
| CSV olay adları/frame sınırları |1.311 olay PASS|
| Reload event profilleri |25 PASS|
| Mixamo indirme ayarları |18 aday PASS; katalogdoğrulanmış0/indirme0|
| Lisans metadata |25 kayıt;21 araştırma kaynağı URL eşliği PASS|

Doğrulayıcı salt-okuma çalıştı, dosya yazmadı; estetik veya Unity runtime incelemesi yapmadı. Mekanik engel yok.

**Henüz ölçülmedi / açık:** Avatar valid/isHuman, yeni bind/thumb deformasyonu, gerçek ayak kayması, grip/palm hatası, loop dikişi, magazine takibi, stream job gecikmesi, extra curve binding, üç kamera görüntüsü, ms/GC. Yeni rig/klip FBX0; Blender/Unity/Player çalıştırılmadı. İsteğe bağlı prosedürel önce/sonra Blender kanıtı yapılmadı.

B/C hedefleri: herplant ve8yön/ara blend'de kayma≤%5; sıfıra yakın hızda mutlak drift≤5mm; final görünür palm-tutamak≤1cm; transfer sürekliliği≤1cm; loop pose≤1mm/0,5°+velocity sınırı; şarjör elde rigid offset; tek owner/duplicate0/latency0. Klip planı bu metriklerin marker, denominator, sample ve kamera matrisini verir. Sayılar **hedef**, ölçülmüş başarı değil.

553 sayı dağılımı: TP294, FP117, prop27, yeni zombi104, korunanAP02211. Bunlar üretim işleri/plan satırlarıdır. Dört death/beş attack ailesi yanında swipe_L ve arc_electro varyantları da korunur;11 mevcut kalemin olayları source v008 manifestinden taşındı.

## 4. Kaynaklar ve lisanslar
21 araştırma kaynağı JSON'da, bunlar ve4ek teknik dokümantasyon bağlantısı LICENSES.csv'de (toplam25) kayıtlıdır. Başlıca somut referanslar: [TLOU II GDC Motion Matching](https://gdcvault.com/play/1027118/Motion-Matching-in-The-Last), [Capcom RE4 kılavuzu](https://game.capcom.com/manual/re4/en/ps5/page/2/2), [Bend Days Gone sürü açıklaması](https://blog.playstation.com/archive/2019/04/25/how-to-fight-off-days-gones-overwhelming-freaker-hordes), [Sucker Punch Ghost katana/mocap açıklaması](https://blog.playstation.com/2020/06/23/ghost-of-tsushima-mastering-the-katana/), [Dead Island2 geliştirici röportajı](https://80.lv/articles/dead-island-2-explained-gameplay-combat-mechanics).

Araştırmada geliştirici beyanı kaynakla, bizim biomekanik/solver önerimiz ayrı işaretle bulunur. Bu kaynakların telifli görüntü/metinleri oyun varlığı olarak alınmadı. Lisans kayıtları **reference metadata only**; ticari oyun varlığı yeniden dağıtım izni varsayılmaz. Dış asset/motion/media indirilmedi; Mixamo gerçek dosya/receipt lisansı indirme yapıldığında klip bazında tamamlanacak. Hesap açılmadı/giriş yapılmadı, satın alma yapılmadı.

## 5. Entegrasyon notları — Claude S-06 / S-02
Bu teslim otomatik Unity import asset'i değildir; B/C üretimi ve WP083 runtime için teknik sözleşmedir. Import işinde yeni sürüm exports kullanılır, AP009/AP022 önceki exports değiştirilmez.

- Locomotion default **in-place**, world translasyon motor/navigation tek sahibi. Gait hedefleri authoring referansı; yeni sprint/walk gameplay mekaniği/balance kararı değildir. Root yaw/pivot/lunge için tek sahip seçilir.
- Humanoid ana mapping,8sidebranch twist,30finger; kontrol domain skeleton'a kardeş. Meter scale/30fps/FBX7.4binary, leafbone kapalı,4vertex influence ve gerçek reimport eksen/deform testi B'de.
- Aynıframe sıra: pelvis/torso→bağımsız sağtarget→RightTwoBoneIK→**custom stream bridge**→LeftTwoBoneIK→yalnız bacak finalcontact→temassız secondary. Bridge solvedRightHand stream ve sabit mount/grip/palm ofseti kullanır; scene grip cache tekbaşına uygun değildir. Kod/handle spike henüz uygulanmadı.
- WeaponMountAlignment(order900), WeaponGripIk(order910), WeaponVisualRig.UpdateKick(recoil/28°reload tilt) final temas sonrası ortak writer olmamalı. Yeni modda kapat/ön-solve input'a taşı; automatic graph üzerine ikinci Evaluate koyma. Optimize/expose tekbaşına extra clip curve playback kanıtı değildir.
- TP/FP socket_mag_l LeftHand altında; socket_weapon_r sağ el; grip_l silah support frame. Her12varlıkta mag_body/gerçek mekanizma node denetimi ve ayrıştırma C-00 ayrı üretim işi. SG1 sabit tüp, SG2 iki-shell adapter kararı bekler.
- mag_detach/mag_hand_attach/mag_insert/bolt_release ve korunmuş lh/reload eventleri; rl1 mag_hand_attach/rocket_in/rocket_seat. Current event allowlist'te mag_detach/mag_insert/bolt_release eksik; AP026prefix/alias adapter S-06/S-02 işidir. Canonical ve legacy aynı işlemi iki kez yapmaz.
- Mevcut **ammo authority timer**; magazine state presentation. reload_commit ismi tekbaşına ammo authority değişikliği değildir. TP/FP tek transaction/gameplay tüketimi; propklip gameplay tüketmez. İptal/death/switch ve loaded-state cleanup aynı sahipte.
- Events intent queue; bound control hierarchy sabit, finalpose propflush. lhrelease/grab weight same-sample authoredcurve. El çözümünden sonra pelvis/hand ancestor secondary writer yok.

## 6. C-00'A SORULAR / AÇIK KARARLAR
**S1 — SG1/SG2 generic transfer sözleşmesi fiziksel mekanizmaya nasıl uyarlanacak?** Seçenekler: sabit tüp/break-action'a mantıksal carrier +gerçek shell event adapter; veya class-specific event sözleşmesini ayrıca genişletme. **Öneri:** mag_body ortak mantıksal carrier adını koru, gerçek shell_body/hinge parçalarını ayrı tut; olmayan tüp/tambur detach animasyonu üretme. C-00/S-02 kararı açık; ilgili B export/event adapter bu kararı bekler. Ayrıştırma AP021 yeni göreviyle yapılır.

**S2 — Run_back3,93 değeri authoring referansı mı runtime hedefi mi?** Mevcut pinned motor6,2×0,65=4,03m/s. Seçenekler: motoru koruyup3,93source'a1,02545playback fit; veya C-00/S-06 ayrıca gameplay hedefini değiştirsin. **Öneri:** mevcut motoru koru, gerçek body stride/speed ölçümüyle fit yap. Bu faz balance değiştirmedi; soru açık.

K1/K2 yeniden onay istemez. Mixamo aday katalog isim/ID doğrulaması, mevcut kullanıcı hesabıyla izinli indirme adımıdır. Yeni oturum açma/satın alma isteği yok. AR resolve, custom job, REST ve bütün runtime metrikler B spike riskidir; Faz A'ya sonuç gibi yazılmadı.

## 7. Araç / Higgsfield istekleri
Yok. Görev belge/araştırmadır; Higgsfield/Adobe görsel üretimi, Unity/Blender slotu, yeni hesap, ücretli kaynak veya repo yazımı gerekmedi. Mixamo indirmesi yapılmadı; eksiksiz ayar/adayeşleme listesi teslim edildi, exact katalog doğrulaması açık.

## 8. Sonraki önerilen çalışma ve durma
C-00/S-06 belgeleri değerlendirip ayrı B görevi yazsın. İlk spike: anatomik REST+rightgrip+sameframe leftbridge+magazine finalflush; ardından run8yön/back ve Executioner actual body cadence kanıtı. İlk kabulden sonra toplu hero,12silah HAND+cila ve zombi tier retarget. Runtime kanıtı olmadan “AAA animasyon hazır” denmez.

Bu raporla Faz A tamamlandı. Ek inceleme/revizyon/B üretimi başlatılmadı. AP024'e dokunulmadı; korunmuş dosyalar silinmedi.

Kayıt zamanı: 2026-10-05T12:48:12 Europe/Istanbul.
