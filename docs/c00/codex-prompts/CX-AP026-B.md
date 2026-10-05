# CX-AP026-B — Rig şablonu + AAA hareket klipleri (kahraman, zombi, reload) · Faz B

> Önce `K/c-00/codex/CODEX-SISTEM-R2.md` ve `K/c-00/codex/KADRO-CODEX-AJANLARI.md`'yi oku (K = `/Users/Erdem/Desktop/KADRO/context/projects/deaths-demolition`). Sondaki **HIZ MODU 1–7 bağlayıcı**: ayrı inceleme alt ajanı yok, kaliteyi lider kontrol eder, teslimden önce tek mekanik `dd_validator`.
> Paket: **AP-026** · Alan: `K/asset-production/AP-026/` · Faz A teslimin: `exports/v001/` · Faz A raporun: `K/c-00/codex/reports/CX-AP026-A-RAPOR-05.10-1.md`
> Blender `tools/slot.sh` ile; disk ≥ 15 GB.

## Kararlar (C-00, 05.10)
- **Kullanıcı seçimleri:** G1 kahraman **O1 Kırık Mühür** (AP-024-B üretiyor), G3 zombi **O1 Boğulmuş Muhafızlar** (AP-025-B üretiyor). Rig standardın v1 ikisi için de bağlayıcı.
- **S1 (SG1/SG2):** önerin kabul.
  - `mag_body` ortak mantıksal taşıyıcı adı kalır.
  - Gerçek `shell_body` ve menteşe parçaları ayrı kalır.
  - Olmayan tüp/tambur için sahte "detach" animasyonu üretilmez.
  - sg_2 (kırma, iki fişek) iki kabuk olayı taşır.
- **S2 (run_back):** motor hızı korunur (6,2 × 0,65 = 4,03 m/s). Klip oynatma hızı gövde adımıyla ölçülerek uydurulur (≈ × 1,025). Denge değişmez.
- **Runtime iki el tutuşu: Unity yerleşik IK** (S-06, WP-083 B2c, 05.10 son karar).
  - Animation Rigging bu gövdelerde yalnız ağırlığı kurulumda sabit 1 olan kısıtlarla çalışıyor (el IK 0,47 cm). Ağırlık sonradan 0→1 değişince etkisiz; şarjör değiştirme ve silah değişiminde tutuşun yumuşak bırakılması mümkün olmuyor.
  - Bu yüzden destek eli ve tutuş pivotu OnAnimatorIK (SetIKPosition/Rotation + ağırlık) + mevcut WeaponGripIk ile; ikincil hareket SetBoneLocalRotation ile.
  - Rigging paketi kurulu kalır; Hero v2 rig'iyle yeniden denenir.
  - Senin işin: bu runtime çözümle uyumlu temiz kaynak klip ve rig.
  - Klipleri, sol el IK ağırlığının reload penceresinde 1→0→1 eğrisiyle çalışacağını varsayarak yaz: `lh_release` / `lh_grab` olay kareleri; o pencerede elin klipteki pozu doğru olmalı.

## Main'de hazır sözleşmeler (uy, değiştirme)
- **Yürüyüş v4 kimlikleri:** `git show main:prototype/DeathsDemolition_PlayableConcept/Assets/_Prototype/Characters/LocomotionV4.cs` (S-06, 7a72b7c2).
  - Kapsam: 4 yürüyüş türü × 8 yön start/stop × ayak, pivot 90/135/180, turn 45; toplam 142 id.
  - Klip adları bu kimliklerle bire bir eşleşir. Eşleşmeyenleri raporda listele.
- **Reload olayları:**
  - `MagHandAttach = 79`.
  - `mag_detach` / `mag_insert` / `bolt_release` eş adlar → MagOut / MagIn / Chamber.
  - rl_1: `mag_hand_attach` / `rocket_in` / `rocket_seat`.
- **Şarjör:** `Weapons/WeaponMagazineOwnership.cs` + `WeaponMagazineVisual.cs` (S-02). Şarjör `socket_mag_l`'e bağlanır; havada 0 kare.

## Adımlar
1. **Rig şablonu (ÖNCE, hızlı).**
   - bone map v1'den Blender armature şablonu, FBX iskelet ve Avatar eşleme tablosu → `exports/v002/rig-template/`.
   - AP-024-B (kahraman) ve AP-025-B (zombi) bu şablonu kullanır.
   - Hazır olunca `exports/v002/rig-template/HAZIR.md` bırak ve devam et.
2. **Zombi klipleri.**
   - Mevcut 73 joint iskelete AP-022 v008 temelinden tier yürüyüşleri.
   - Executioner kadansı gerçek gövde ölçüsüyle (1,48).
   - Kalabalık faz çeşitliliği; senkron çift payı ≤ 0,10 (R2 L5).
3. **Kahraman klipleri.**
   - Kaynak Mixamo; dosyaları kullanıcı indirir (aşağıda).
   - Mixamo gelene kadar Mixamo'ya bağlı olmayan işleri yap: FP reload elle animasyonu, start/stop/pivot geçişleri, ikincil hareket.
   - FP + TP reload, 12 silah. Olay kareleri yukarıdaki sözleşmeyle. Şarjör elde rijit ofset.
4. **Kalite ölçümleri (R2 kapıları; Blender'da ölç, kesin ölçümü Unity'de S-06 yapar).**
   - L1 temas kayması avg ≤ 0,05, p95 ≤ 0,10.
   - L1t 0,3 s geçiş avg ≤ 0,10.
   - L2 kadans × adım sapması ≤ 0,08.
   - L3 start/stop/pivot uzuv ≤ 20°/kare, gövde ≤ 10°/kare.
   - Loop pozu ≤ 1 mm / 0,5°.

## Kullanıcı adımı: Mixamo (Codex giriş yapamaz)
- Liste: `exports/v001/AP026-mixamo-downloads-v1.csv` (M01–M23). Ayarlar CSV'de: FBX Binary, 30 fps, Without Skin, keyframe reduction None, in-place.
- C-00 listeyi kullanıcıya sunar. Dosyalar `/Users/Erdem/DD-Sources/mixamo/` altına gelir.
- Gelince `catalog_status` / `downloaded` sütunlarını güncelle, lisans satırlarını yaz.

## Teslimatlar
- `exports/v002/`: rig şablonu, zombi klipleri, kahraman klipleri (gelenler), olay JSON'u, kalite ölçüm tablosu.
- `manifest/AP026-manifest-v002.json` + `SHA256SUMS`.
- Unity'ye alma yok; entegrasyonu S-06 yapar (WP-083/082/084).

## Durma ve rapor
- Mixamo'ya bağlı olmayan kapsam bitince raporu yaz ve DUR. Mixamo bekleyen satırları raporda ayrı listele.
- Rapor: `K/c-00/codex/reports/CX-AP026-B-RAPOR-<gg.aa>-1.md`. Son mesaj tek satır: rapor yolu.

## 05.10 ek — Mixamo dosyaları GELDİ
- Yer: `/Users/Erdem/DD-Sources/mixamo/`. Eşleme tablosu ve ayarlar `INDIRME-NOTU.md`'de; bütünlük `SHA256SUMS`'ta.
- M01–M14 ve M20–M23 mevcut. FBX 7.4, Without Skin, 30 fps, in-place.
- **EK:** Rifle 8-Way Locomotion Pack (49 animasyon). 8 yönün birincil kaynağı olarak değerlendir.
- Açık: M02 kök hareketli ikinci sürüm. Gerekirse raporda iste; C-00 indirir.
- CSV'deki `catalog_status` / `downloaded` sütunlarını güncelle, LICENSES.csv satırlarını yaz. Kahraman kliplerine bu dosyalarla devam et.

## 05.10 ek 2 — S-06 ön kontrol geri bildirimi (C-00 kararları, bağlayıcı)
- **Twist kuralı:** oyundaki TwistCorrector esas (−0,5 × kendi kemiğinin twist'i). Şablondaki "çocuk kemikten +0,5 (S-06 AnimationJob)" notunu ve klip twist'lerini buna göre düzelt.
- **sw_gel / sw_arc reload:**
  - mag_detach → mag_hand_attach arası en az 6 kare (≥ 200 ms; el şarjöre gerçekten gidiyor).
  - sw_arc'ta bolt_release reload_cancel_point'ten ÖNCE.
  - sw_gel'e bolt_release ekle (boş reload'da).
- **_tac klipleri:** bolt_release YOK. Doğru, böyle kalsın; boş (empty) reload'larda var.
- **SG:**
  - sg_1 reload_shell'de aynı karede mag_insert + shell_in olmasın; fişek başına yalnız `shell_in`.
  - sg_1 start / shell / end bölünmüş klipleri döngü için kalsın: shell klibi kendi içinde loop pozu ≤ 1 mm / 0,5°.
- **İçe alma belgesi:** FBX eksen/ölçek bayraklarını (Z-up / −Y ileri, metre) ve Hips 0,93 m / baş 1,80 m ölçüsünü `rig-template/README`'ye yaz. Geçiş olaylarını JSON'da zamana göre sırala.
