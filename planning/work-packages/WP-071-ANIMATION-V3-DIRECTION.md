# WP-071 — Animasyon v3 (AAA) · Yön belgesi (Faz A)

Sahip S-06 · 2026-09-24 · Tasarım ekibi: `character-animator` (Bölüm II — kusur analizi, 5 saldırı, tepkiler, 4 ölüm onset'i, silah performansı, **klip listesi v3 = P-06 AP-022 üretim listesi**), `animation-specialist` (Bölüm III — locomotion sistemi, ölüm→ragdoll boru hattı, hit katmanları, silah katman mimarisi, rig, **olay sözleşmesi v3**, kalite kademeleri, riskler). Bölüm I = S-06 motor kararları; iki konsey metni arasındaki farklarda **Bölüm I bağlayıcıdır**. Kullanıcı kararları: DEC-092 60 fps (Yüksek ≤ 16,6 / p99 ≤ 20 ms), DEC-093 5 GB, DEC-094 özgün Special'lar. Girdiler: S-02 WP-069 (anim kimlikleri, mesaj 24.09), S-01 WP-070 `f23f38e7` (CameraMode, FP göz = Head, `AimPitch`), P-07 AP-021 blockout v001 (`part_*`, `grip_r/grip_l/sight_ads`).

## Bölüm I — S-06 kararları (konsey farklarının çözümü)

| # | Konu | Karar |
|---|---|---|
| K1 | Klip adlandırma | Bölüm II biçimi: `AP022_tp_<name>`, `AP022_fp_<name>`, `AP022_zombie_<name>`; silah aksiyonu `AP022_<tp|fp>_<weaponId|class>_<action>` (S-02'ye verilen kalıp); silah parça klibi `AP022_prop_<weaponId>_<action>`. Bölüm III A8.5'teki `p/z/w` kısaltmaları bu kalıba çevrilir. |
| K2 | Olay adları | Bölüm II adları geçerli: `ragdoll_handoff` (= III'teki `ragdoll_on`), `ground_impact` (= `impact_land`). Diğer tüm yeni adlar III A8.2 + P-07 `cover_open/cover_close` (Special kapağı) + II'deki kozmetik `twitch`, `spasm`. Bölüm II'deki eş anlamlılar kanonik ada çevrilir: `knock_end`→`knockback_end`, `ads_in_done`→`ads_ready`, `interrupt_ok`/`reload_interrupt_ok`→`reload_cancel_point`, `recover`→`hurt_end`. Mevcut 28 ad değişmez; doğrulayıcı kanonik olmayan adı RET eder (öneriyle). |
| K3 | Kadans bandı (N5) | Oynatma hızı **0,75**–1,20 (oyuncu + zombi; B2: alt sınır 0,85 → 0,75 — Runner koşu klibi ×0,75'te dürüst, 0,85 ayakları gövdenin önüne geçirir); aşan hız kayarak alınır (TP ≤ %20, tepeden ≤ %35 görünür kayma kabul); ayak kilidi yalnız rate ≤ 1,05'te, bacak gerilme tavanı %96, çözülme 60 ms. Yeni halkalar: `walk_slow`, `shamble_fast`, `heavy_walk` (III A2.1). |
| K4 | Ragdoll tavanı | Sinematik **10** / Yüksek **6** / Performans **2** eşzamanlı; tavan doluysa ilgili ölüm ailesinin klibi sonuna kadar oynar — P-06 ile (24.09): `AP022_zombie_death_<family>` tek klip = yere kadar tam düşüş + `ragdoll_handoff` işaretli (hem onset hem fallback; ayrı `_full` yok); `fall_start` ≤ handoff. Performans dahil hiçbir kademede "yerinde çökme" yok. Ceset tavanı/ömür III §T. |
| K5 | Ragdoll uygulaması | **Proxy havuzu**: kademe tavanı kadar önceden kurulmuş ragdoll iskeleti (11 beden / 10 ConfigurableJoint, Sinematik 13), pasifken fizikte yok. Ölümde proxy zombinin kemik pozlarına oturur, gövde ölçüleri fit'e göre ayarlanır, `ragdoll_handoff`'ta SLERP-drive (powered) → tam ragdoll → settle → freeze; proxy kemik dönüşleri zombi kemiklerine LateUpdate'te yazılır; freeze'de proxy havuza döner, zombi son pozda kalır. Zombi prefab'ında kalıcı Rigidbody yok (DEC-032 korunur; 40 × 11 kinematik gövde senkron maliyeti yok), 0 B/kare. |
| K6 | Kök hareketi | (P-06 24.09: yol Hips'te, Root keylenmez → Humanoid gövde root motion; XZ bake OFF, Y bake ON, dönüş bake ON — dönen klipler hariç; manifest `root_motion {xz,yaw}` + `root_delta [x,z,yaw]`.) Kök keyli klipler (start/stop/lunge/knockback/ölüm onset) canlıyken `OnAnimatorMove` → `CharacterController.Move` (DEC-032 uyumlu); ölü gövdede (controller kapalı) kök delta `ragdoll_handoff`'a kadar transform'a. |
| K7 | Rig v002 (P-06) | Twist: ön kol ×2 + üst kol ×2 **P0**, uyluk ×2 **P1**; parmak 15/el oyuncu + FP kol **P0**, zombi parmak P2; jaw; `fp_camera`; oyuncu başı ayrı SkinnedMeshRenderer (FP `ShadowsOnly`, S-01); `ik_*` hedef kemikleri. Ekstra transform'lar Humanoid dışı, "Extra Transforms to Expose" listesi `ArtAnimatorBuilder` doğrular. |
| K8 | Silah katmanı | Tek üretilmiş taban controller + silah başına AnimatorOverrideController (S-02 ile anlaşıldı); FP ayrı viewmodel Animator (`FirstPersonView` katmanı, overlay kamera); sol el IK `grip_l`'e (order 910), ADS `sight_ads` hizası; parça klipleri (`AP022_prop_*`) karakter klibiyle aynı zaman çizgisinde senkron oynatılır, olaylar ses/VFX/sayım içindir. |
| K9 | Tepkiler | Pencere içinde (DEC-029) yalnız additive tepki; LMG akümülatörü; shotgun/launcher knockback: itki verisi + governor + tier direnci + CharacterController hareketi **S-03**, poz/animasyon yanıtı S-06 (S-03 ile 24.09); stagger yalnız REQ-124 kapısıyla; oyuncu hurt nişanı ≤ 120 ms bloke eder (III A5). |
| K10 | Bütçe | DEC-092 Yüksek kademede animasyon+IK+ragdoll payı **≤ 2,4 ms**; aşımda kısma sırası: ragdoll 6→4, foot IK 12→8, look-at 12→8, twist yalnız oyuncu. §4.8 ölçümü dilim sonlarında satır başına. |
| K11 | Açık kararlar (II sonu) | C03 hit toleransı 0,60 (C-00 17.09 kararı, sürer); dış kaynak 10 kr ölüm/shotgun video referansı P-06'nın AP-022 tavanı içinde isteğe bağlı. |

### Faz B dilimleri (her biri ayrı commit)
1. **B1** Olay sözleşmesi v3 + manifest doğrulayıcı (`ArtAnimationClipContract` v3: ad listesi, A8.4 zamanlama kuralları, TP/FP olay kare eşitliği) + ragdoll proxy havuzu + 4 ölüm ailesi seçimi (yön/kuvvet/silah sınıfı → aile) + tam-klip fallback — mevcut klipler üzerinde çalışır, AP-022 geldikçe klip değişir.
2. **B2** Locomotion v3: kadans bandı, ayak kilidi, lean additive, start/stop/pivot, zombi GaitProfile.
3. **B3** Hit/hurt katmanları + LMG akümülatörü + knockback + Special tepkileri (elektrik/jel) kancaları.
4. **B4** Silah taban controller + AOC üretimi (WeaponDefinition v3) + sol el IK + TwistCorrector + FP viewmodel (S-01 dilim 3 ile).
5. **B5** 5 saldırı + seçim kancaları (S-03 WP-076), kademe bağlama + ms raporu; AP-022 dilim 1–4 içe alımları.

### Bildirim
P-06 (AP-022 üretim listesi = Bölüm II "KLİP LİSTESİ v3", dilim sırası II sonu; rig v002 = K7), S-03 (saldırı/ölüm/hit seçim kancaları, `attack_recover`, `grab_*`), S-07 (`ground_impact`, `blood_burst`, `pivot_plant`, `sw_charge_peak`, `shell_eject`), S-08 (`mechanism_start/end`, `spin_up/down`, `weight_shift`, `roar`), S-02 (`reload_commit`, `reload_cancel_point`, `ads_ready`), S-01 (FP viewmodel + `AimPitch`).

---

# Bölüm II — character-animator konsey girdisi


Tarih 2026-09-24 · Rol: karakter animatörü · Tüketici: S-06 (motor sözleşmesi), P-06 (AP-022 Blender üretimi), S-03 (saldırı/ölüm kancaları), S-07 (darbe VFX zamanı), S-08 (temas sesleri)
Kaynak: M4-R1 §0 (N5 · N6 · N7 · N11 · N12 · N16), FACTS.md, clip-inventory.txt (86 klip, AP-009 v007), canlı kareler v006b-r3 (17), sheets-v006 (5), AP-009 kontak tabloları + AD kararı v001, AP-009 README (üretim notları).
Kapsam: A1 kusur analizi · A4 saldırılar · A5 tepkiler · A3 ölüm (animasyon yarısı) · A6 silah performansı · KLİP LİSTESİ v3. Kamera/IK/ragdoll motor tarafı S-06'nındır; burada yalnız animasyonun motora ne verdiği yazılır.

Kısaltmalar: TD = tepeden (19,9 m, −49°), TP = üçüncü şahıs, FP = birinci şahıs. Kareler 30 fps. "f" = kare. Yön = darbenin geldiği yön (front = önden vuruldu).

Bağlayıcı kararlar (C-00, 24.09): **DEC-092** perf hedefi 60 fps (Yüksek kademe ort ≤ 16,6 ms, p99 ≤ 20 ms) → ragdoll/IK/twist kademeleri bu bütçeye göre (§A3); **DEC-093** build tavanı 5 GB → klip sayısı/sıkıştırma kısıt değil (188 klip × ~40 KB ≈ 8 MB, Optimal sıkıştırma, FP klipleri sıkıştırılmaz); **DEC-094** Special'lar özgün ad + kendi modelimiz (Sliquifier / Wunderwaffe DG-2'ye mekanik/his/sanat yönü çok yakın) → burada `sw_gel` / `sw_arc` (AP-021 anahtarları).
Silah kimlikleri (AP-021 blockout manifesti, P-07): `ar_1, ar_2, smg_1, smg_2, sg_1 (pompalı, tüp şarjör: part_pump, part_shell), sg_2 (yarı-oto, kutu şarjör: part_bolt, part_mag), lmg_1 (kayış beslemeli: part_box_lid, part_belt, part_mag=kutu, part_bolt), lmg_2 (tepsi şarjör: part_mag, part_bolt), sr_1 (sürgülü: part_bolt, part_mag, part_round), rl_1 (part_rocket), sw_arc (part_cover, part_cell_1..3), sw_gel (part_canister, part_pump)`. Soketler: `grip_r` (= kök, sağ el), `grip_l` (sol el IK hedefi), `sight_ads` (ADS bakış noktası, kamera +Z arkasından), `Muzzle`, `Ejection`.

---

### Uygulama notu (B4b, 25.09 — S-06, C-00 isteği): Humanoid ek transformlar

- **Ölçüm:** Unity Humanoid klipleri avatar dışı (ek) transformların eğrilerini oynatmaz. `fp_camera`, `socket_weapon_r`, `ik_*`, twist ve `jaw` bunlara dahil. İçe alınan `AP022_fp_*` klibinde bu kemiklere 0 bağlama vardı; klip modelden maske alınarak (`CreateFromThisModel`) içe alındığında da sonuç aynıydı.
- **Kural:** ek kemiğe yazılmış anahtara güvenilmez. Aşağıdakiler çalışma zamanında hesaplanır ya da veriden gelir:
  - Twist: `TwistCorrector`.
  - Çene: prosedürel sürücü.
  - FP kamera ofseti: `FpCameraOffsets`, silah başına. Kaynak P-06 manifesti `weapon_layer.fp`.
  - FP silah çerçevesi: Humanoid `RightHand` + set başına kalibrasyon. Dinlenme pozunda silah fpView'dadır; ölçüm 0,1 cm / 0,0°.
- **P-06 için:** yeni bir ek kemik davranışı gerekiyorsa manifestte makinece okunur bir alan olarak da verilmelidir.
- **Mod ayrımı (C-00 25.09):**
  - FP v3 kolları açık.
  - TP v3 AR/SMG, P-06'nın silah başı gripOffset'li yeniden dışa aktarımına kadar kapalı (`PlayerSkinnedVisual.V3WeaponSets`). Eski TP kliplerinde kundak omuza 6,0 cm giriyordu.

## 0 · Teşhisin özü (neden mevcut set AAA okumuyor)

1. **Set prosedürel:** AP-009'un tamamı `AP009-build-clips-v001.py` ile parametrik (sinüs bob, sabit açı hedefleri, `lift_pow`, `drop_smooth`). Sonuç: her klipte **eşit aralıklama**, ease-in/out yok, çakışmalı hareket (overlap) yok, ikincil hareket yalnız `sec_*` verlet. TD'de 88 px'lik siluette kabul edilebilirdi; TP/FP'de (karakter 400–900 px) bu "robot" olarak okunur. Bu, tek tek klip kusurlarından önce gelen **set geneli MUST**tur: v3 elle keylenir (blokaj → spline → cila), betik yalnız dışa aktarım/doğrulama için kalır.
2. **Kök hiç keylenmemiş** (in-place, Hips bob taşır). Lunge, overhead, ölümler, knockdown'lar kökte yer değiştirmeden "yerinde" oynar; kullanıcının "oldukları yere çökmesinler" notunun kaynağı budur. v3'te seçili kliplerde **Root keylenir** (manifest `root_motion=1`, S-06 motor tarafında `OnAnimatorMove → CharacterController.Move`).
3. **Hız–adım eşleme (stride-matched) zombiyi çılgınlaştırıyor:** shamble A/B/C 1,29–1,55 m/s kaymasız; Standard 2,7 m/s → klip 1,7–2,1× hızlı oynuyor. Kullanıcı N5'te açıkça izin verdi: ayak/gövde hızdan yavaş, kalan mesafe kayarak. v3 kuralı: **kadans penceresi** (rate 0,85–1,20) + artık kayma kabul (§A1.4).
4. **El roll / twist / parmak yok.** TD'de görünmezdi; FP'de silah tutuşu, şarjör kavrama ve önkol "şeker kağıdı" burulması ilk saniyede görünür. Rig eki (§A1.0-R) olmadan FP kol seti üretilmez.
5. **Ölüm = tek dil.** 8 ölüm klibi var ama hepsi "yerinde katlanma"; darbe yönü, silah itkisi ve momentumla ilişkisi yok. v3: 4 yazılmış "onset" + ragdoll devri (§A3).

---

## A1 · AAA kusur analizi — klip ailesi bazında

Her satır: **kusur → görünürlük (TD/TP/FP) → düzeltme → önem**. "Kabul" = klip v3'te aynen kalabilir (yalnız cila).

### A1.0 Set geneli (tüm klipler)

| # | Kusur | TD | TP | FP | Düzeltme | Önem |
|---|---|---|---|---|---|---|
| G1 | Eşit aralıklama, ease yok; her uzuv aynı karede başlayıp bitiyor (overlap yok) | az | çok | çok | Elle keyleme; anticipation–aksiyon–settle üçlüsü her one-shot'ta; uzuvlar 2–4 f kaydırılmış (omuz→dirsek→bilek→parmak) | MUST |
| G2 | Kök yer değiştirmesi yok (lunge, overhead, ölümler, knockdown, stumble) | orta | çok | — | Root keyli klipler (§listede `root=1`); kayma/ilerleme manifestte `root_delta_xz` ile raporlanır | MUST |
| G3 | Ağırlık aktarımı yalnız Hips z bob'u; pelvis yanal ağırlık kayması (lateral shift) ve kalça yalpası yok | az | çok | — | Yürüyüşte pelvis yanal ±3–4 cm + yalpa (roll) ±4°, koşuda ±2 cm/±6°; one-shot'larda destek ayağı üstüne ağırlık kayması görünür olsun | MUST |
| G4 | Baş ve göğüs karşı-rotasyonu (counter) küçük; bakış hedefi `CharacterLookAt` ile geliyor ama omuz takibi yok | az | çok | — | Göğüs/baş counter ×1,5; LookAt'e omuz payı (S-06) | SHOULD |
| G5 | Eller sabit "kavrama" pozu; parmaklar hiç oynamıyor; el roll Humanoid'de kayboluyor (`WeaponMountAlignment` düzeltiyor) | — | çok | çok | Rig eki: parmak (her el 15) + önkol/üstkol twist ×2 → `rig_template_v002`; FP kol setinde parmak keylenir (kavrama/bırakma/tetik) | MUST (FP) |
| G6 | Yüz yok (çene, göz kapağı) | — | orta | — | Zombi çene açma/kapama 1 kemik + göz kapağı blendshape ×2; oyuncuda yalnız çene (ıkınma/ölüm) — düşük maliyet, yüksek okunabilirlik TP'de | SHOULD |
| G7 | Kontak: ayak "düz taban" iniyor, topuk–burun rulosu yok; toe kemiği pasif | az | çok | — | Topuk iniş → düz → burun kalkış; toe 15–25° | MUST |
| G8 | Settle yok: one-shot son karede hedef poza çakılıp `override_upper` rest'e geçiyor (geçiş sıçraması) | orta | çok | çok | Her one-shot'ın son 4–6 f'i rest pozuna yazılmış settle; motor geçişi 0,10–0,15 s kalır | MUST |
| G9 | Sec_* zincirleri verlet ile iyi; ama kaynak hareket sert olduğundan zincirler "titriyor" | az | orta | — | G1 çözülünce kendiliğinden düzelir; sönüm 0,85 → 0,80 önerisi S-06 | — |
| G10 | Loop 61/91 f idle'larda nefes sinüs; dönüş noktası fark ediliyor (TP'de yakın) | — | orta | çok | Idle 90–120 f, iki farklı periyot üst üste (nefes 3,0 s + ağırlık kayması 7,5 s), ilk/son kare eşit, hız eğrisi de eşit (C2 süreklilik) | SHOULD |

**Rig eki R (P-06 + 3d-modeller, AP-006 rig_template_v002):** parmak kemikleri (thumb 3 + 4×3 = 15/el, Humanoid haritasına bağlanır), `twist_forearm_L/R`, `twist_upperarm_L/R` (Humanoid dışı, deformasyon yardımcı; Blender'da copy-rotation %50), `jaw`, `fp_camera` (Head altı, S-02/S-01 sözleşmesi). Skin ağırlıkları yeniden bakılır (önkol/el). Bu ek FP kol seti için **ön koşul**.

### A1.1 Oyuncu hareket (idle, walk fwd/back, strafe L/R, run fwd/back/strafe L/R)

| # | Klip | Kusur | Düzeltme | Önem |
|---|---|---|---|---|
| L1 | `player_idle` 61 f | Nefes tek sinüs; silah tam sabit; ağırlık iki ayakta eşit | 120 f loop; nefes 3 s + ağırlık sağ→sol 7,5 s; namlu ±0,4° sürüklenme; parmak mikro-hareket (FP'de ayrı) | SHOULD |
| L2 | `walk_fwd` 13 f (3,01 m/s) | Adım çok kısa, bacak düz iniyor, kalça yalpası yok; gövde silah kilitli → "kayan büst" (TP'de belirgin) | 16 f (2 adım), 2,6 m/s kaymasız; topuk-burun; pelvis yanal ±3,5 cm; omuz counter 4°; silah kolları nefesle 1–2 cm süzülür | MUST |
| L3 | `walk_back` 13 f (2,49) | Geri yürüyüş öne yürüyüşün tersi gibi; geriye adımda burun önce iner (gerçekte) — yok | 16 f; burun iniş, gövde 3° öne (denge), bakış silahta | MUST |
| L4 | `strafe_L/R` 13 f (1,97) | Çapraz adım yok; ayaklar birbirine yapışıyor; 1,97 m/s çok yavaş → rate 1,5× | 16 f; çapraz adım (arka ayak öne geçer) yalnız TD/TP'de görünür iyi okunur; 2,4 m/s kaymasız | MUST |
| L5 | `run_fwd` 15 f (5,69) | Uçuş fazı var ama iniş "sert taban"; kollar silahla kilitli, gövde yalpa yok; TP'de dizler sert | 16 f; 5,8 m/s; lean 12°; pelvis roll ±6°; silah namlusu 4–5 cm bob (dikey) + 2 cm yanal; nefes/sarsıntı FP'de kamera kemiğine (§A6 FP) | MUST |
| L6 | `run_back` 15 f (4,60) | Geriye koşu fiziksel olarak yok — sahte okuyor | Geriye "hızlı back-pedal" 14 f (3,6 m/s kaymasız), kalan hız kayma (N5 izni); gövde öne 8°, adımlar kısa ve sık | SHOULD |
| L7 | `run_strafe_L/R` 15 f (4,41) | Yana koşu düz karşıya bakan gövdeyle "buz pateni" | 16 f; gövde yöne 20° açılır, iç ayak çapraz geçer; 4,6 m/s | MUST |
| L8 | (yok) `run_start` / `run_stop` | Durma anında ani kesilme; TP'de çok görünür | run_stop 12 f (iki fren adımı, gövde 6° geri), run_start 8 f (ilk itme) — P1; blend tree dışı tek atım | SHOULD (P1) |
| L9 | (yok) Yerinde dönüş | Nişan yönü değişince ayaklar kayarak döner | `turn_in_place_L/R` 90° 14 f additive alt gövde — P2 (TD'de gereksiz, TP'de görünür) | P2 |

Kalibrasyon kuralı v3 (S-06): `StrideCalibrator` kaymasız hızı ölçmeye devam eder; **rate = clamp(speed / noSlideSpeed, 0,85, 1,20)**; artık = kayma. Oyuncu koşu 6,2 / 5,8 = 1,07 → kayma yok. Strafe 6,2 / 4,6 = 1,35 → rate 1,20, kayma 0,68 m/s (kabul; kullanıcı notu). `FootIkAdapter` kayma karelerinde ayak kilidi **kapatır** (kilit yalnız rate ≤ 1,05 iken).

### A1.2 Oyuncu silah katmanı (A/B/C × idle_aim, fire, reload, switch, lower/raise; fire_recoil)

| # | Klip | Kusur | Düzeltme | Önem |
|---|---|---|---|---|
| W1 | `*_idle_aim` 61 f ×3 | Nişan tutuşu sabit; A "yanağı dipçikte" pozu TP'de iyi, B/C omuzsuz tutuş FP'de kol boşlukta durur | Sınıf başına yeniden (§A6); 90 f loop; el-silah ilişkisi soketle kilitli, sol el `grip_l` IK | MUST |
| W2 | `A_fire` 6 f / `B_fire` 5 f additive | Tepme yalnız namlu yukarı; omuz geri itiş, gövde 1–2° dönüş, kalça yok; 5–6 f çok kısa (settle yok) | Additive 8–10 f: f0 omuz geri 2–3 cm + namlu 1,5–3°, f1 tepe, f2–4 geri, f5–9 settle; sınıf başına (§A6) | MUST |
| W3 | `C_fire` 22 f (pompalı: eject@12, chamber@20) | Pompalama additive olarak override tutuşa binince sol el iki yerde birden (soket vs. kundak) | Pompalama **override_upper** one-shot olur (additive değil), fire additive ayrı 8 f; eject/chamber olayları override klipte | MUST |
| W4 | `A_reload` 45 f, `B_reload` 51 f | 1,5–1,7 s; şarjör hareketi iki anahtar poz arası doğrusal, sol el gövdeyi kesiyor (dō zırhı), şarjör "ışınlanıyor" (mag_out@12 → mag_in@30 arası el boş) | Sınıf başına yeniden: tac/empty ayrı, mekanizma olayları §A6; sol el yolu zırh dışından; şarjör el içinde görünür (S-02 mag prop soketi `socket_mag_l`) | MUST |
| W5 | `C_reload` 66 f (3 shell) | Sabit 16 f/shell; döngü değil → shell sayısı değişince klip uymaz | Shell reload = start (12 f) + **loop shell_in 14 f** + end (16 f); motor sayıya göre loop tekrarlar | MUST |
| W6 | `switch_in/out` 12 f | 0,4 s, düz interpolasyon; silah eldeki soketten yok olup beliriyor | in 14 f / out 10 f; out'ta silah aşağı-yana süpürülür, in'de yukarı-öne gelip "yerine oturma" 3 f settle; sınıf başına (ağır silahlar +4 f) | SHOULD |
| W7 | `weapon_lower/raise` 14 f | İşlevsel; TP'de kabul | Yalnız settle ekle | — |
| W8 | `fire_recoil` 10 f (genel) | Sınıf fire'larla çakışıyor | Kaldır; sınıf fire additive'leri devralır | — |

### A1.3 Oyuncu hurt / hit / death / pickup / sw_hold

| # | Klip | Kusur | Düzeltme | Önem |
|---|---|---|---|---|
| H1 | `hit_light` 12 f, `hurt_A` 12 f (üst) | Yön yok; AD MUST 3 sonrası fold var ama tek şekil; 12 f'de settle yok | 4 yön × hafif (üst gövde override 14 f) §A5.2 | MUST |
| H2 | `hurt_B` 14 f (tam gövde) | Tek yön, kökte yer değiştirme yok, ayaklar kayıyor | 4 yön × ağır (tam gövde, root 0,3–0,5 m geri adım) §A5.2 | MUST |
| H3 | `death_fwd/back/side` 36–48 f | Katlanma yerinde; AD MUST 2 sonrası kabul edilebilir ama TP'de sert | 2 onset + ragdoll (öne/arkaya), yan = arkaya + yan itki; `death_end` kalır (Performans kademesi için tam klip) | SHOULD |
| H4 | `pickup_upper/special` 24 f | İşlevsel; TD kabul | TP: eğilme 6 f daha uzun, bakış nesneye | — |
| H5 | `sw_hold_bell/loom/jewel` | Kalkıyor (N1: eski Special'lar gidiyor) | Yerine `sw_gel_*`, `sw_arc_*` (§A6.7–8) | — |

### A1.4 Zombi hareket (shamble A/B/C, run, idle sway/twitch/queue)

| # | Klip | Kusur | Düzeltme | Önem |
|---|---|---|---|---|
| Z1 | `shamble_A/B/C` 17 f (1,29–1,55 m/s) | Standard 2,7 m/s'te rate 1,7–2,1× → seğiren yürüyüş; kalça çekişi (drag), ölü bacak, asimetri yok; canlı karede (z_hit_front_std) zombi "kollarını açmış düz yürüyen adam" | Üç yeni yürüyüş 24 f (2 adım), kaymasız **2,2–2,4 m/s**: A "sürüklenen sağ bacak" (topuk yerde kayar, gövde sola yatık), B "ileri düşen" (her adım yakalanan düşüş, baş önde), C "geniş kalça yalpası" (kollar sarkık, salınım geç) — rate 2,7/2,3 = 1,17 → pencere içinde | MUST |
| Z2 | `run` 13 f (5,32) | Kollar önde "Frankenstein"; koşu insan koşusu gibi düzgün; Runner 4,6 → rate 0,87 (iyi) | 14 f; kollar gevşek geriye savrulur, gövde 18° öne, baş ileri uzanmış, ayak "çarpık" (dış kenar iniş); uçuş fazı kısa | SHOULD |
| Z3 | (yok) Guard/Elite yürüyüş | Guard 2,2 m/s Standard shamble ile aynı | `walk_heavy` 28 f, 2,1 m/s, omuzlar önde, ayak tabanıyla vuruş (foot event sesi ağır) | SHOULD |
| Z4 | (yok) Executioner yürüyüş | Ultra 1,7 m/s, shamble hızlandırılmış | `walk_stomp` 32 f, 1,7 m/s, her adımda gövde çöker-kalkar 6 cm, kollar hafif açık | SHOULD |
| Z5 | `idle_sway/twitch` 61 f, `idle_queue` 91 f | Kabul (TD); TP'de seğirme mekanik | 120 f; twitch'ler düzensiz aralıkla (f17, f52, f88), çene aç-kapa | SHOULD |
| Z6 | (yok) hız geçişleri | Shamble → run ani | `Speed01` blend yeter; run'a yalnız Runner geçer | — |

### A1.5 Zombi saldırılar (swipe, lunge, sweep, overhead)

| # | Klip | Kusur | Düzeltme | Önem |
|---|---|---|---|---|
| A1 | `swipe` 34 f (pencere 16–20) | AD SHOULD 5 sonrası coil var; vuruş anında gövde takip etmiyor; takip (follow-through) kolu gövdeyi kesiyor (f26); kökte adım yok | §A4.1 yeniden; adımlı swipe, root +0,35 m | MUST |
| A2 | `lunge` 30 f (10–13) | Kökte hareket yok → "yerinde zıplayan" lunge; Runner hız eşleşmesi yok | §A4.2; root 1,4 m, iniş adımı | MUST |
| A3 | `sweep` 51 f (24–30) | AD MUST 4 kısmen; f30'da gövde kameraya 60° dönmüş (siluet çöküyor); ağırlık aktarımı yok | §A4.4; çift kol, ayak pivotu, root +0,25 m | MUST |
| A4 | `overhead` 73 f (35–40, Ultra) | 2,4 s; kalkış çok uzun ve tek tempolu (anticipation'ın kendi içinde hız değişimi yok); iniş sonrası zemin darbesi tepkisi yok | §A4.5; 64 f, iki tempolu kalkış, yer darbesi settle | MUST |
| A5 | (yok) 5. tip | — | §A4.3 `bite_grab` | MUST |

### A1.6 Zombi darbe tepkileri (12 yön×tier, 2 light, 3 region additive, heavy_stagger, stumble, 3 armorbreak)

| # | Klip | Kusur | Düzeltme | Önem |
|---|---|---|---|---|
| R1 | `hit_<dir>_<std/elite/ultra>` 8–14 f | Yön yalnız rotasyonla (C03 kapsül sınırı 0,50); kökte yer değişimi yok; canlı karede front_std "iki kol açık" (sheets zombie_hits) → AD'nin "roar" itirazı hâlâ okunuyor; 8 f Ultra tepki çok kısa | §A5.1: yön × büyüklük (light/medium) tam gövde override; C03 kapsül **0,60**'a çıkar (S-06 kararı; 3–5 geçici kare); kollar asimetrik, bir kol darbe noktasına gider | MUST |
| R2 | `hit_light_L/R` 14 f | R1 ile çift | Kaldır; region additive'ler devralır | — |
| R3 | `hit_region_head/torso/leg` 10 f additive | İyi fikir; büyüklük tek | 6 region (head, torso, arm_L, arm_R, leg_L, leg_R) × 12 f; büyüklük motor `weight` ile (0,4 light / 0,7 med / 1,0 heavy) | SHOULD |
| R4 | `hit_heavy_stagger` 30 f | AD MUST 1 sonrası kabul; kökte 2 geri adım var ama in-place → ayaklar kayıyor | Root keyli (−0,6 m), settle 6 f | MUST |
| R5 | `hit_special_stumble` 16 f | Runner tökezleme iyi (kare: z_stumble); 16 f kısa, toparlama yok | 22 f; toparlama adımı (root +0,4 m ileri) | SHOULD |
| R6 | `armorbreak_head/torso/leg` 21 f | Kabul; `armor_break@0` olayı VFX'e yetiyor | Cila (G1) | — |
| R7 | (yok) silah sınıfı itkisi | Shotgun/sniper/LMG aynı flinch | §A5.1: shotgun_throw ×4 yön, sniper_stagger ×2, lmg_flinch_loop | MUST |
| R8 | (yok) knockdown / get-up | Launcher patlaması, Guard shove yok | §A5.1: knockdown_fwd/back + getup_front/back | MUST |

### A1.7 Zombi ölüm (8) + rise (3)

| # | Klip | Kusur | Düzeltme | Önem |
|---|---|---|---|---|
| D1 | `death_forward/backward/side/side_R/kneel` 36–44 f | Hepsi yerinde katlanma; canlı kare z_death_ultra_slam: gövde sıkışık kütle; diz çökme ölümü sürekli tekrar ediyor ("tek ölüş" algısı) | 4 onset + ragdoll (§A3); kneel kalkar | MUST |
| D2 | `death_special_A/B` | Eski Special'larla gidiyor | `death_gel_slip` (Sliquifier) + `death_arc_electro` (Wunderwaffe) (§A5.3) | MUST |
| D3 | `death_ultra` 60 f | İyi başlangıç (hold → slam), kökte yok | §A3.5 Ultra timber | SHOULD |
| D4 | `rise` 48 f, `rise_B` 36 f, `rise_C` 28 f | rise_B iyi (kare z_rise_B_land); rise (yerden kalkma) süpermen düzeltilmiş | rise_B/C kalır (cila); `rise` → `getup_front` ile birleşir | — |

---

## A4 · Zombi saldırıları — 5 tip

Ortak kurallar:
- Klip **Root keyli**; `root_delta_xz` manifestte; DEC-029 pencere olayları `hit_window_start/end` klipte. Tier telegraph süresi klip **rate**'ini sürer (FACTS): rate = telegraph_base / telegraph_tier; pencere kareleri rate'ten bağımsız (olay kareye bağlı).
- Anticipation üç tempolu: **yükleme (yavaş, 60 %) → tutma (8–10 %, "dondurulmuş" tehdit karesi, oyuncunun okuduğu an) → serbest bırakma (30 %, hızlı)**. Tutma karesi siluetin en geniş/en yüksek olduğu karedir (C11 ile ölç).
- Vuruş karesi: temas noktasında 1 f "smear" değil, 2 f tutuş (hit-stop hissi animasyonda; motor ayrıca 30–50 ms hit-stop uygulayabilir).
- Toparlama: settle + hazır poza dönüş; toparlamanın son 6 f'i `interruptible` (yeni saldırı/hit bu karelerden sonra kesebilir; motor `attack_recover@f` olayı).
- Tier varyantı = aynı klip + rate + additive `telegraph` katmanı vurgusu; Elite/Ultra için yalnız **overhead** ve **sweep** ayrı yazılır, geri kalan rate/genlik ile.
- Erişim: `AttackSocket` menzili sabit; erişim mesafeleri kök ilerlemesi + kol uzunluğu (0,62 m).

### A4.1 `attack_swipe_R` (tek kol pençe, temel) — tüm tier'lar
- Amaç: en sık, en okunur saldırı; Standard'ın ana silahı.
- Kareler: 36 f. Anticipation f0–14 (yükleme f0–9: sağ omuz geri-yukarı, gövde 16° geri + 40° burulma, ağırlık arka ayak; tutma f10–12; bırakma f13–14), temas f15–19, toparlama f20–35 (`attack_recover@30`).
- `hit_window_start@15`, `hit_window_end@19`.
- Telegraph: sağ el baş üstüne çıkar (siluet yüksekliği +12 %), sol kol kalçada; tutma karesinde çene açık.
- Erişim 1,1 m; root: f13–17 sol ayak öne adım +0,35 m (adımla pençe; ayak iniş f16 `foot_l`).
- Tier: Special (Runner) rate 1,25; Elite rate 0,9 genlik ×1,15 (additive telegraph); Ultra kullanmaz (overhead/sweep).
- Ayna: `attack_swipe_L` Blender'da mirror + 2 f kayma (aynı klip sayılmaz; olaylar aynı).

### A4.2 `attack_lunge` (dalış kapma) — Runner ana, Standard ikincil
- Amaç: mesafe kapatan tehdit; oyuncuyu geri adıma zorlar.
- Kareler: 32 f. Anticipation f0–9 (çömelme: kalça −22 cm, göğüs 24° öne, kollar kalça arkasında; tutma f7–8), sıçrama f9–12 (uçuş, kollar öne fırlar), temas f12–15, iniş+toparlama f16–31 (`attack_recover@26`; iniş f16 iki ayak `foot_l@16,foot_r@17`, gövde öne kayar 2 f sonra durur).
- `hit_window_start@12`, `hit_window_end@15`.
- Telegraph: çömelme siluet genişliği ×1,35 (C11 IoU ≤ 0,70); tutma karesi kısa (Runner'ın hızı).
- Erişim 1,9 m; root +1,4 m (f9–16), iniş sonrası +0,2 m kayma (Runner momentumu, N5 izni).
- Tier: Runner rate 1,0; Standard rate 0,85 + root ×0,8 (1,1 m); Elite/Ultra kullanmaz.
- Iskalarsa (`attack_miss` motor sinyali) f16'dan `stumble`e geçiş: Runner iskalayınca tökezler (oyun hissi ödülü).

### A4.3 `attack_bite_grab` (iki el kapma + ısırık) — Standard/Guard yakın menzil
- Amaç: 5. tip; yakın temasta "yakalanma" hissi; hasar tek darbe (grapple mekaniği yok, S-03).
- Kareler: 40 f. Anticipation f0–12 (iki kol dışa-öne açılır, baş geri, çene açılır; tutma f9–11), kapma f13–16 (kollar kapanır, baş öne dalar — temas), ısırık f17–20 (baş sarsılır 2 kez), toparlama f21–39 (kollar bırakır, baş yukarı; `attack_recover@32`).
- `hit_window_start@14`, `hit_window_end@18`.
- Telegraph: iki kolun açılması (siluet genişliği ×1,5, en okunur telegraph); TD'de "kucaklama" okunur.
- Erişim 0,8 m; root +0,25 m (f12–15 küçük adım).
- Tier: Standard rate 1,0; Guard (Elite) rate 0,85, genlik ×1,1; Runner kullanmaz; Ultra kullanmaz.

### A4.4 `attack_sweep` (çift kol yatay süpürme) — Guard/Elite ana, Standard nadir
- Amaç: geniş yay, kalabalıkta oyuncuya yanal baskı.
- Kareler: 48 f. Anticipation f0–19 (iki kol sağa savrulur, gövde 45° sağa burulur, ağırlık sağ ayak; tutma f15–17), süpürme f20–27 (kollar 160° yay, gövde sola döner, sağ ayak pivot, sol ayak adım), temas f22–27, toparlama f28–47 (kollar çapraz takip f29–34 — AD MUST 4'e uygun: çaprazlama pencere **sonrası**; `attack_recover@40`).
- `hit_window_start@22`, `hit_window_end@27`.
- Telegraph: kolların tek yana toplanması + gövde burulması (siluet asimetrik); ellerin eksenden erişimi ≥ 0,60 m f23–27 boyunca (C03 saldırı toleransı 0,66).
- Erişim 1,3 m yay 150°; root +0,25 m + 30° yaw dönüşü (kökte yaw keylenir; motor `root_yaw` uygular).
- Tier: Guard rate 1,0; Standard rate 1,1 genlik ×0,9; Ultra ayrı klip `attack_sweep_ultra` 56 f (aynı yapı, kollar daha yüksek, root +0,4 m, pencere 26–32, tutma 4 f).

### A4.5 `attack_overhead_slam` (çift yumruk çekiç) — Executioner/Ultra ana, Guard ikincil
- Amaç: en ağır darbe; kaçınılabilir uzun telegraph; yer darbesi VFX/ses (S-07/S-08 `ground_impact`).
- Kareler: 64 f. Anticipation f0–29 iki tempolu: f0–14 kollar yavaşça baş üstüne (gövde geri 12°), f15–24 daha yavaş son kaldırış + gövde yükselir (ayak parmak ucu), tutma f25–27 (siluet en yüksek), bırakma f28–29 hızlı. Darbe f30–35 (gövde 40° öne çöker, dizler bükülür, yumruklar yere 0,9 m önde; `ground_impact@33`), temas f30–35, toparlama f36–63 (yumruklar yerden 6 f "yapışıp" kalkar, gövde ağır doğrulur; `attack_recover@52`).
- `hit_window_start@30`, `hit_window_end@35`.
- Telegraph: yükseklik ×1,2 (kollar), tutma karesinde çene tam açık; TD'de dikey kollar kısalır → gövde geri yatışı (12°) tepeden okunurluğu taşır.
- Erişim 1,4 m; root +0,5 m (f28–33 öne düşüş adımı).
- Tier: Ultra rate 1,0; Guard rate 1,15, genlik ×0,85 (kollar daha alçak), root +0,35; Standard/Runner kullanmaz.

Saldırı dağılımı (S-03'e öneri): Standard {swipe_R/L 55 %, bite_grab 30 %, sweep 10 %, lunge 5 %}; Runner {lunge 60 %, swipe 40 %}; Guard {sweep 45 %, bite_grab 30 %, overhead 25 %}; Executioner {overhead 60 %, sweep_ultra 40 %}.

---

## A5 · Tepkiler

### A5.1 Zombi darbe tepkileri (yön × bölge × silah sınıfı itkisi)

Katman mimarisi (S-06): **Base flinch (override_upper veya base)** + **region additive** + **itki sınıfı seçimi**. Silah sınıfı `impulse_class` (S-02 WeaponDefinition v3): `light` (SMG, AR), `medium` (Sniper, Shotgun tek pelet), `throw` (Shotgun yakın, ≥ 5 pelet), `heavy` (Launcher, Ultra Special), `continuous` (LMG). Mesafe/hasar oranı motor tarafında `weight` 0–1'e çevrilir.

**a) Yönlü temel flinch — 8 klip** `hit_<front|back|left|right>_<light|medium>`
- light: 14 f override_upper (üst gövde; bacaklar yürüyüşe devam — zombi durmaz). Anlık geri savrulma f0–2 (darbe yönünün tersine), tutma f3, geri toparlama f4–13 ease-out. Genlik: baş 20°, göğüs 12°, omuzlar asimetrik (darbe tarafı kol geri savrulur, diğer kol darbe noktasına 4 f gecikmeyle gider).
- medium: 18 f **base** (tam gövde; zombi 0,25 s durur); root −0,15 m darbe yönünde; ayak yarım adım geri; C03 kapsül **0,60** (S-06 karar).
- Elite: aynı klipler `weight` 0,6; Ultra `weight` 0,35 + yalnız region additive (Ultra light'a hiç durmaz). Eski 12 tier klibi kalkar.

**b) Region additive — 6 klip** `hitadd_<head|torso|arm_l|arm_r|leg_l|leg_r>` 12 f
- head: baş darbe yönünün tersine 25° + boyun kısalması 1 cm, çene açılır; torso: göğüs içe çöker 8°; arm: kol geri savrulur + el gevşer; leg: diz bükülür 15°, kalça o tarafa 4 cm düşer, 1 tökezleme adımı (additive, base yürüyüşe biner).
- Motor: yön base flinch'i seçer, bölge additive'i `weight` ile ekler. Aynı bölgeye ard arda vuruşta additive her vuruşta f0'dan yeniden başlar (phase restart), base flinch yalnız f≥8'de yeniden tetiklenir (titreme önlemi).

**c) Shotgun throw — 4 klip** `hit_throw_<front|back|left|right>` 24 f base, root keyli
- Tam gövde savrulma: f0–5 gövde darbe yönünde 0,6 m fırlar (root), ayaklar yerden 1–2 f kesilir, f6–9 iniş (bir ayak, dengesiz), f10–23 toparlama 2 adım geri + settle. Ölümcülse motor f5'te `death_back/ side` onset'ine geçer (ragdoll bu klibin hızını devralır).
- Elite root ×0,6; Ultra kullanmaz (medium flinch).

**d) Sniper stagger — 2 klip** `hit_stagger_front`, `hit_stagger_side` 28 f base, root −0,5 m
- Tek büyük darbe: gövde 30° geri kırılır, 2 sendeleyen geri adım, baş savrulur, kollar asimetrik çırpınır (AD MUST 1 kuralı: simetrik yatay kol yok), settle. `stagger_end@27`. Mevcut `hit_heavy_stagger` bunun yerine geçer (yeniden yazılır). Side varyantı yandan gelen sniper/launcher kenar itkisi için.

**e) LMG sürekli flinch — 1 klip** `hit_flinch_loop` 16 f additive loop
- Sürekli isabet altında (≥ 6 vuruş/s): omuzlar titrer, baş küçük sarsıntılar 2 farklı frekans (f0–7 ve f8–15 farklı genlik), gövde 5° geri eğilir; base yürüyüş yavaşlar (motor `Speed01` ×0,6). Vuruş kesilince 0,25 s fade-out. Base flinch bu sırada bastırılır (yalnız region additive'ler).

**f) Knockdown / get-up — 4 klip**
- `knockdown_back` 32 f base root −1,0 m: launcher patlaması / Ultra Special; f0–6 uçuş (ayaklar yerden), f7–10 sırtla iniş (kalça önce, omuz 2 f sonra, baş 1 f sonra — kırbaç), f11–31 yerde yatış settle (kollar yere düşer). Sinematik kademede f6'da ragdoll devri (`ragdoll_handoff@6`).
- `knockdown_fwd` 30 f root +0,8 m: arkadan patlama; yüzüstü, eller önce.
- `getup_back` 40 f (sırtüstü → yan dönüş f0–12 → dizler f13–26 → ayakta f27–39; `rise_end@39`), `getup_front` 36 f (mevcut `rise` yeniden: burunlar yerde, kollarla itiş, tek diz, kalkış). Ragdoll'dan get-up'a geçiş: motor ragdoll pozunu 0,2 s blend ile get-up f0'a taşır (S-06).

### A5.2 Oyuncu hurt — yönlü

| Klip | Kareler | Katman | İçerik |
|---|---|---|---|
| `p_hurt_light_<front/back/left/right>` ×4 | 14 f | override_upper | Omuzlar kalkar, baş darbe yönünün tersine 15°, gövde katlanır 10° (AD MUST 3 şekli, yönlü); **namlu seviyede kalır** (ateş şekliyle karışmaz); silah eli 2 cm sarsılır; settle f8–13 |
| `p_hurt_heavy_<front/back/left/right>` ×4 | 20 f | base, root keyli | Tam gövde; 1 geri/yan adım (root 0,35–0,5 m darbe yönünde), diz çöküşü 8 cm, silah hattı 6° düşer ve f12'de geri gelir; ayak iniş olayı `foot_*`; ateş edilebilirlik f10'dan itibaren (`recover@10`) |
| `p_knockdown_back` | 36 f | base, root −0,9 m | Launcher self-damage / Ultra overhead; uçuş f0–7, iniş f8–11, yerde f12–35; `ragdoll_handoff@7` (Sinematik) |
| `p_getup_back` | 44 f | base | Dirsek → oturma → diz → ayakta; silah yeniden omuza f34; `rise_end@43` |
| FP kamera-additive `fp_hurt_<dir>` ×4 | 10 f | fp_camera | Yalnız kamera kemiği: yönlü 3–5° kick + 1 cm ötelenme + roll 2°; kollar 2 cm sarsılır (S-07 ekran sarsıntısıyla toplanır, N18) |

### A5.3 Special tepkiler (N1 yeni Special'lar)

- `hit_electro_loop` 24 f loop, base (tam gövde): Wunderwaffe benzeri zincir yıldırım. Gövde kasılır (sırt 10° geri, kollar 40° dışa, parmaklar gergin), 3 farklı frekansta seğirme (baş 4 Hz, kollar 6 Hz, bacaklar 2 Hz düzensiz), ayak ucunda (topuk 3 cm kalkık), çene tam açık. Loop içinde 2 büyük kasılma (f5, f16). Bitince `death_arc_electro` onset (§A3.6) veya `hit_stagger_front` (hayatta kaldıysa).
- `hit_gel_slip` 30 f base root keyli (+0,6 m hareket yönünde): Sliquifier benzeri jel. f0–4 ayak öne kayar (destek ayağı ileri fırlar, root ilerlemesi hızlı), f5–8 kollar dengeye çırpınır (asimetrik), f9–13 kalça yere (arkaya) — `ground_impact@12`, f14–29 yerde çırpınma settle. Sinematik: `ragdoll_handoff@9` (jel sürtünmesiz zemin ragdoll'la kayar). Hayattaysa `getup_back`.
- `hit_gel_slide_loop` 16 f loop (P1): jel üstünde yerde kayma döngüsü (ragdoll yoksa Performans kademesi).

---

## A3 · Ölüm — 4 yazılmış onset + ragdoll devri

İlke: ölüm klibi **kısa yazılmış "onset"** (darbenin anı: ilk 0,3–0,5 s tamamen animasyon; siluet, kırbaç ve niyet burada) + `ragdoll_handoff@f` olayında fizik. Aynı klip ragdoll olmadan da sonuna kadar oynar (yazılmış iniş = **Performans kademesi** ve pool tükendiğinde yedek). Kademeler (S-06, DEC-092 16,6 ms bütçesi; ragdoll 12 gövde ≈ 0,15–0,25 ms/ceset PhysX, yerde `Sleep` sonrası 0): **Sinematik ≤ 10** eşzamanlı ragdoll (≈ 2,5 ms tepe), **Yüksek ≤ 6** (≈ 1,5 ms; kalanı yazılmış iniş), **Performans 0** (yalnız yazılmış). Ölüm anında ragdoll kotası doluysa en eski uyuyan ceset kotayı boşaltır (yazılmış pozda dondurulur), yeni ölüm ragdoll alır — yakın plandaki (TP/FP kameraya ≤ 8 m) ölüm her zaman önceliklidir. Devir kuralı: motor devirden önceki 2 karenin kemik hızlarını ragdoll'a başlangıç hızı olarak verir (animatör bu 2 kareyi "hız taşıyan" keyler; f_handoff−2..f_handoff arası düz hız).

Seçim (S-03 `DeathSelector`): itki yönü (öne/arkaya/yan), itki sınıfı, hız (koşuyor mu), bölge (bacak/baş).

### A3.1 `death_fall_back` — önden isabet (AR/SMG son mermi, shotgun, sniper) · 42 f · root −0,7 m
- f0–3: göğüs geri kırılır, baş 1 f gecikmeli geri kırbaç, kollar öne "asılı" kalır (atalet), kalça henüz yerinde. f4–7: ağırlık topuklara, bir ayak yarım adım geri kayar (denge arayışı, ölmüş ama refleks), dizler bükülür. f8–10: kalça geri düşmeye başlar, kollar yukarı-yana açılır (asimetrik: darbe tarafı kol yüksek).
- `ragdoll_handoff@8`. Yazılmış devam: f11–18 kalça yere (`ground_impact@16`), f19–24 omuz/baş yere (baş 2 f sonra sekme 3 cm), f25–41 settle, `death_end@41`.
- Uyar: itki önden, orta/yüksek; shotgun yakın mesafede root −1,1 m varyantı = `hit_throw_front` üstünden devir (ayrı klip değil).

### A3.2 `death_fall_fwd` — arkadan isabet veya koşarken ölüm · 40 f · root +0,9 m
- f0–3: sırt öne kamburlaşır, baş öne düşer, kollar sarkar (gevşer). f4–9: bacaklar bir-iki adım daha "koşmaya çalışır" ama dizler tutmaz (momentum; koşan Runner'da root +1,3 m), gövde öne devrilir. f10–12: eller yere uzanmaz (ölü; refleks yok) — yüz/omuz önce.
- `ragdoll_handoff@9`. Yazılmış devam: f13–17 diz-göğüs-yüz sırayla yere (`ground_impact@15`), f18–24 kalça yere kayar, f25–39 settle, `death_end@39`.
- Uyar: arkadan itki; hareket hızı > 2 m/s her yön (momentum ölümü).

### A3.3 `death_spin_side` — yandan isabet veya baş isabeti · 44 f · root 0,5 m yan + 90° yaw
- f0–2: baş darbe yönünün tersine sert savrulur (baş isabeti), omuzlar takip eder 2 f gecikmeli. f3–8: gövde darbe ekseninde döner (yaw 60°), darbe tarafı kol gevşek savrulur, karşı bacak ayak dış kenarına yüklenir. f9–11: dış bacak kayar, gövde yan düşüşe geçer; kollar aksi yöne kırbaç.
- `ragdoll_handoff@10`. Yazılmış devam: f12–19 yan yatış (kalça, omuz, baş; `ground_impact@17`), f20–30 dönüşün kalanı (yüzüstüne yarım tur), f31–43 settle, `death_end@43`.
- Uyar: yan itki; baş isabeti (dönüş baştan başlar); sniper yan.

### A3.4 `death_legs_out` — bacak isabeti / patlama alt itkisi · 46 f · root +0,4 m
- Yerinde çökme DEĞİL: f0–4 darbe alan bacak katlanır, kalça o tarafa 12 cm düşer, gövde denge için karşı yöne savrulur (kollar dışa). f5–9: diğer bacak bir adım ileri "yakalamaya" çalışır, root +0,3 m, ama gövde öne-yana devrilir. f10–12: dizler yere doğru, gövde 45° öne + 30° yana eğik, kollar önde.
- `ragdoll_handoff@11`. Yazılmış devam: f13–18 diz yere (`ground_impact@14`), f19–26 gövde yana devrilir, omuz yere, f27–45 settle, `death_end@45`.
- Uyar: bacak bölgesi, launcher yakın, jel üstünde öldürme (kayma bileşeni ragdoll'da).

### A3.5 `death_ultra_timber` — Executioner, tek özel · 72 f · root +0,6 m (ÖNERİLİR)
- Ultra'nın gövde kütlesi ragdoll'da "çuval" gibi düşer; yazılmış inat gerekir. f0–12: darbe, gövde kırılır ama ayakta kalır (hold — mevcut `death_ultra` hold fikri korunur), baş öne düşer, kollar sarkar. f13–24: bir adım öne (root +0,4), dizler titrer, çene açık, `roar` yok (sessiz ölüm daha ağır). f25–30: tek diz yere (`ground_impact@28`), gövde dik. f31–36: öne "ağaç gibi" devrilme başlar.
- `ragdoll_handoff@33` (geç devir: inat animasyonla, düşüş fizikle). Yazılmış devam: f37–48 yüzüstü iniş (`ground_impact@44`, kamera sarsıntısı S-07), f49–71 settle, `death_end@71`.
- Ultra'ya yalnız bu; yön seçimi yok (arkadan itkide motor 180° yaw ile aynı klip + yüzüstü yerine sırtüstü ragdoll).

### A3.6 Special ölümleri (A5.3 ile paylaşılır)
- `death_arc_electro` 36 f: `hit_electro_loop`'tan devam; f0–6 kasılma tepe (parmak ucu), f7–10 tüm kaslar aynı anda gevşer, `ragdoll_handoff@8` (dik düşüş; ragdoll gevşek). Yazılmış devam f11–35 çökme + 2 seğirme (f20, f29), `death_end@35`.
- `death_gel_slip`: `hit_gel_slip` aynı klip; ölümcülse `death_end@29` ile biter (ayrı klip yok).

Ragdoll ile ilgili animatör talepleri (S-06): (1) devir anında 0,12 s pose-to-physics blend (animasyon hedef → fizik), (2) yere temas sonrası 0,4 s'de `Sleep`, (3) ceset yerde yatarken `SecondaryChainDriver` kapalı (mino zincirleri ragdoll'da), (4) pool'a dönüşte Root/Hips sıfırlanır.

---

## A6 · Silah performansı (per sınıf) — TP tam gövde + FP kollar

Ortak:
- TP: tam gövde, 90 f `idle_aim` loop (nefes 3 s, ağırlık 7,5 s), ayaklar yere sabit (FootIK), göğüs `PlayerAimLayer` ±35° yaw (mevcut). Reload'da ayak değişmez; gövde 6–10° silaha döner, baş şarjöre bakar (f_mag_out−4'ten f_mag_in+2'ye), sonra hedefe.
- FP: yalnız kollar + silah + `fp_camera`; kamera kemiği her klipte keylenir (nefes 0,3°, reload'da hafif aşağı bakış 4–6°, ateşte kick). FP klipleri **kamera uzayında** yazılır (silah kadrajı sabit, kollar ona göre), 1,3× daha uzun settle (yakın plan). Parmaklar keylenir (tetik parmağı, şarjör kavrama, palm-slap). Sol el silah `grip_l`'a IK ile; klipte de FK olarak yazılır (IK fallback).
- Olay adları S-02 ile uzlaşılan ve AP-021 hareketli parçalarına eşlenen (P-07 manifesti): `fire, eject` · `mag_out / mag_in / mag_in_2..` ↔ `part_mag` · `charge, chamber` ↔ `part_bolt` (sr_1'de `bolt_up/back/fwd/down` ↔ `part_bolt`, `part_round` eject ile) · `chamber` ↔ `part_pump` (sg_1 pompa yolu 0,11 m; sw_gel pompa) · `shell_in(+index)` ↔ `part_shell` · `box_open / box_close` ↔ `part_box_lid` · `belt_seat` ↔ `part_belt` · `rocket_in / rocket_seat` ↔ `part_rocket` · `cell_out / cell_in (+index 1..3)` ↔ `part_cell_1..3` · `canister_out / canister_in` ↔ `part_canister` · `cover_open / cover_close` ↔ `part_cover` (sw_arc). Parça animasyonu **silah prop'unda** keylenir (Blender'da silah armature'ı klibe dahil; FBX'te parça kemikleri `part_*` isimli), olay karesi = parçanın hareketinin başladığı kare; motor olayda parçayı prop animasyonuyla oynatır (S-06 `WeaponPartDriver`). Ek animasyon olayları: `reload_commit` (bu kareden sonra iptal edilirse mermi sayısı yine güncellenir; = mag_in / belt_seat / rocket_seat / son shell_in / son cell_in / canister_in), `reload_interrupt_ok` (kesilebilir son bölüm başlangıcı), `switch_ready`, `switch_away`, `ads_in_done`.
- Paylaşım kuralı (AP-021 mekanizmalarına göre): **ar_1/ar_2** aynı klipler (part_bolt + part_mag; uzunluk farkı 1,20/1,02 m → `grip_l` ofseti), **smg_1/smg_2** aynı (smg_2 tambur şarjör: `mag_in` yolu 4 cm alçak, aynı klip + prop ofseti), **sg_1 ≠ sg_2** (pompalı tüp/shell döngüsü vs yarı-oto kutu şarjör → sg_2 reload = AR yapısının 0,92 m kısa varyantı, ayrı klip ama AR zaman çizgisi kopyası), **lmg_1 ≠ lmg_2** (kayış kutusu vs tepsi şarjör → iki ayrı reload), sr_1 / rl_1 / sw_arc / sw_gel tekil. idle_aim / fire / switch / ADS **sınıf başına** paylaşılır (sg, lmg dahil; yalnız reload ayrılır). Klip kimliği: paylaşılanlarda `<class>` (ar, smg, sg, lmg, snp, lch, gel, arc), ayrılanlarda `<weaponId>` (sg_1, sg_2, lmg_1, lmg_2).
- ADS (TP): `ads_in` 8 f / `ads_out` 6 f (omuza çekme; `sight_ads` soketi göz hizasına gelir — kamera `sight_ads` +Z arkasından bakar); FP: `fp_ads_in` 8 f, kollar `sight_ads`'ı `fp_camera` önüne getirir (S-01/S-07 FOV geçişi 0,25 s ile eşzamanlı). ADS idle ayrı loop (60 f, nefes 0,5×).
- Hedef süreler (s): AAA referans aralıklarına göre; S-02 sayıları farklıysa klip rate'i ±15 % içinde uyarlanır, dışında yeniden keylenir.

| Sınıf | idle/aim | fire additive | tactical reload | empty reload | switch in / out | ADS in/out | Notlar |
|---|---|---|---|---|---|---|---|
| AR | 90 f loop; A pozu (omuzda, yanak dipçikte) | 8 f; namlu 2°, omuz 2,5 cm geri, `fire@0, eject@1` | **1,9 s** 57 f: `mag_out@14` (sol el şarjör mandalı f9), `mag_in@36` (`reload_commit`), `reload_interrupt_ok@42`; şarjör değiştirme yolu bel hizasından | **2,6 s** 78 f: +`charge@62` (kolçak çekme, sağ el/sol el S-02 silaha göre; kamera hafif sarsıntı) | 14 f / 10 f | 8 f / 6 f | FP: boş şarjör atılırken sol elde 6 f görünür kalır, yeni şarjör bel soketinden |
| SMG | 90 f; B pozu (kompakt, dirsekler içeride) | 6 f; namlu 1,2°, 1,5 cm geri; `fire@0, eject@1` | **1,6 s** 48 f: `mag_out@11`, `mag_in@30`, `interrupt_ok@36`; hızlı, el hareketleri kısa yaylı | **2,2 s** 66 f: +`charge@52` (yan kolçak) | 12 f / 8 f | 6 f / 5 f | FP: hızlı bilek dönüşü; parmak "slap" f31 |
| Shotgun (sınıf: idle/fire/switch/ADS ortak) | 90 f; C pozu (kalçada değil, omuz altı — FP için yükseltilir) | 10 f; namlu 4°, omuz 5 cm geri, gövde 1,5° dönüş; `fire@0` (sg_2: + `eject@1`) | sg_1 / sg_2 ayrı ↓ | ↓ | 16 f / 10 f | 8 f / 6 f | — |
| sg_1 pompalı, tüp şarjör | (sınıf) | (sınıf) | **shell loop**: `reload_start` 12 f (silah yana yatar, sol el kemer/torba, `part_shell` elde belirir) + `shell_in` 14 f loop (`shell_in@9` +index, `reload_commit@9` her turda; shell port = `marker_mag`) + `reload_end` 16 f (silah hedefe; boşsa `chamber@10` = pompa) | aynı loop; boşsa `reload_end` içinde `chamber` zorunlu | (sınıf) | (sınıf) | Pompalama `sg_1_cycle` 14 f override_upper her atışta: `eject@5, chamber@11` (`part_pump` 0,11 m geri-ileri) |
| sg_2 yarı-oto, kutu şarjör | (sınıf) | (sınıf) | **2,0 s** 60 f: `mag_out@14`, `mag_in@38` (`reload_commit`), `interrupt_ok@44` — AR zaman çizgisinin 0,92 m kısa varyantı, sol el yolu daha kısa | **2,7 s** 81 f: + `charge@66` (`part_bolt`) | (sınıf) | (sınıf) | Pompalama yok; FP'de şarjör mandalı sağ başparmak |
| LMG (sınıf: idle/fire/switch/ADS ortak) | 90 f; ağır poz (dirsek yüksek, gövde 4° geri — ağırlık) | 7 f; namlu 1,8°, 3 cm geri; sürekli ateşte omuz titreşimi additive loop `fire_loop` 12 f | lmg_1 / lmg_2 ayrı ↓ | ↓ | 20 f / 14 f | 10 f / 8 f | TP: gövde bir diz üstüne yarı çöker (ağırlık, 8 cm) |
| lmg_1 kayış beslemeli, kutu | (sınıf) | (sınıf) | **4,6 s** 138 f: `box_open@22` (`part_box_lid` kalkar), `mag_out@40` (kutu düşer), `mag_in@78` (yeni kutu), `belt_seat@104` (`part_belt` yerleşir, `reload_commit`), `box_close@116`, `interrupt_ok@124` | **5,2 s** 156 f: +`charge@142` (`part_bolt`) | (sınıf) | (sınıf) | FP: kutu kadraja girer-çıkar, kayış elle yerleştirme parmak keyi (en uzun FP performansı) |
| lmg_2 tepsi şarjör (üstten) | (sınıf) | (sınıf) | **3,8 s** 114 f: `mag_out@22` (tepsi yukarı kaldırılır, `part_mag` +Y 12 cm sonra yana), `mag_in@72` (yeni tepsi üstten oturur, `reload_commit`), `interrupt_ok@84` | **4,4 s** 132 f: + `charge@118` | (sınıf) | (sınıf) | Ofset nişangâh: ADS pozu baş 3° sola (sight_ads ofseti); TP'de tepsi silahın üstünde görünür kalır — silhouette + |
| Sniper | 90 f; A pozu, uzun namlu; `bolt_cycle` ayrı | 12 f; namlu 5°, omuz 6 cm geri, gövde 3° | **3,0 s** 90 f: `mag_out@18`, `mag_in@54` (`reload_commit`), `interrupt_ok@62` (bolt gerekmez) | **3,6 s** 108 f: + bolt: `bolt_up@80, bolt_back@86, bolt_fwd@94, bolt_down@100` | 18 f / 12 f | 10 f / 8 f (ADS = dürbün; FP'de ekran S-09/S-07) | `bolt_cycle` 30 f override_upper her atıştan sonra: `eject@8` (bolt_back ile), `bolt_up@4, bolt_back@8, bolt_fwd@18, bolt_down@24`; ADS içinde kısaltılmış `bolt_cycle_ads` 24 f (dürbün açık) |
| Launcher | 90 f; omuzda, sol el ön kabza, gövde 6° geri | 14 f; geri tepme az, gövde 2° geri, baş egzoz için sağa 4° | **3,4 s** 102 f: `rocket_in@58` (sol el yeni roket bel/sırt soketinden namluya), `rocket_seat@80` (`reload_commit`, tık), `interrupt_ok@88` | tek mermi → tac = empty (aynı klip) | 20 f / 14 f | 10 f / 8 f | TP: silah omuzdan iner (f10), namlu yukarı döner, roket önden girer, omuza geri; FP: roket kadrajı doldurur (en sinematik reload) |
| sw_gel (özgün Special, Sliquifier-benzeri his; DEC-094) | 90 f; B pozu benzeri; üstteki cam hazne nefesle 2 mm çalkalanır (prop) | `sw_fire` additive loop 12 f (püskürtme sürekli: titreşim + hafif geri itiş, geniş nozül sol elle sabit) | **2,8 s** 84 f: `canister_out@24` (sol el `part_canister`'ı üstten çeker, jel damlar — VFX), `canister_in@56` (`reload_commit`), `chamber@70` (`part_pump` ön kabza pompası ile basınç, 1 vuruş), `interrupt_ok@76` | aynı | 16 f / 10 f | — (ADS yok; `sw_hold` idle) | FP'de hazne değiştirme kadraj içinde, pompa vuruşu kamera 1° aşağı |
| sw_arc (özgün Special, Wunderwaffe DG-2-benzeri his; DEC-094) | 90 f; iki elle göğüs önü, gövde geri 3° (elektrik korkusu); bobinler (`coil_1/2`) nefesle döner (prop) | 16 f tek atış: namlu 3° + gövde 2° geri, kollar titrer f0–6, çatal uçlar (`prong_l/r`) 1 cm açılır; `fire@0` | **2,6 s** 78 f: `cover_open@14` (`part_cover` menteşe açılır), `cell_out@22,26,30` (+index 1..3; sağ el üç cam hücreyi sırayla çekip atar, ark kıvılcımı VFX), `cell_in@46,50,54` (+index; `reload_commit@54`), `cover_close@64`, `interrupt_ok@70` | aynı | 16 f / 10 f | — | `sw_charge` 10 f additive (tetik basılı: titreşim artar, bobin hızlanır) → `sw_fire`; FP'de hücreler kadrajın üst üçte birinde |

TP ile FP arasındaki farklar (P-06'ya kural):
1. **Kadraj:** FP'de silah sabit kadrajda; hareket kollarda ve kamerada. TP'de silah gövdeyi takip eder; ayaklar sabit, ağırlık kayması görünür.
2. **Süre:** aynı olay kareleri (S-02 mermi sayacı için ortak zaman çizgisi); FP yalnızca settle'ı 2–4 f uzatabilir → FP klibi TP'den ≤ 4 f uzun, mekanizma olayları **aynı karede**.
3. **Detay:** FP parmak/bilek keyli; TP parmak yalnız kavrama pozu (2 poz: grip/relaxed).
4. **Kamera kemiği:** FP'de `fp_camera` keyli (nefes, reload aşağı bakış, ateş kick); TP'de yok.
5. **Zırh:** TP'de sol el yolu dō/kote dışından (AP-008 zırh); FP'de kol modeli FP kolu (AP-021/AP-006 FP arms) — zırh kesişmesi yalnız TP'de kontrol edilir.
6. **Ateş additive:** FP'de kamera + kollar; TP'de omuz + gövde + kalça (0,3°). Hızlı ateşte additive f0'dan yeniden başlar (phase restart) — ikisi de.

---

## KLİP LİSTESİ v3 (AP-022)

Kimlik `AP022_<tp|fp|zombie>_<name>`. Sütunlar: kareler · loop · katman/maske · olaylar(@f) · root · öncelik · S-02 beklentisi (silah kimliği/soket/sayı gerekiyorsa **E**vet; AP-021 blockout + soketler geldi → geriye WeaponDefinition v3 sayıları: şarjör kapasitesi, ateş hızı, reload süresi hedefi). Paylaşım: `<class>` = sınıf başına 1 klip, sınıfın iki silahı paylaşır (ar, smg, sg, lmg, snp, lch, gel, arc); reload'da mekanizma ayrışan silahlar `<weaponId>` (sg_1, sg_2, lmg_1, lmg_2).

### 1 · Oyuncu TP hareket (base/full)

| # | Klip | f | loop | katman | olaylar | root | P | S-02 |
|---|---|---|---|---|---|---|---|---|
| 1 | AP022_tp_idle | 120 | 1 | base | breath@0 | 0 | P0 | — |
| 2 | AP022_tp_walk_fwd | 16 | 1 | base | foot_l@0, foot_r@8 | 0 | P0 | — |
| 3 | AP022_tp_walk_back | 16 | 1 | base | foot_l@0, foot_r@8 | 0 | P0 | — |
| 4 | AP022_tp_walk_strafe_l | 16 | 1 | base | foot_l@1, foot_r@9 | 0 | P0 | — |
| 5 | AP022_tp_walk_strafe_r | 16 | 1 | base | (ayna) | 0 | P0 | — |
| 6 | AP022_tp_run_fwd | 16 | 1 | base | foot_l@0, foot_r@8 | 0 | P0 | — |
| 7 | AP022_tp_run_back | 14 | 1 | base | foot_l@0, foot_r@7 | 0 | P0 | — |
| 8 | AP022_tp_run_strafe_l | 16 | 1 | base | foot_l@1, foot_r@9 | 0 | P0 | — |
| 9 | AP022_tp_run_strafe_r | 16 | 1 | base | (ayna) | 0 | P0 | — |
| 10 | AP022_tp_run_stop | 12 | 0 | base | foot_r@2, foot_l@6 | 1 (+0,5 m) | P1 | — |
| 11 | AP022_tp_run_start | 8 | 0 | base | foot_l@4 | 1 (+0,3 m) | P1 | — |
| 12 | AP022_tp_turn_l90 / r90 | 14 | 0 | additive lower | foot_l@6, foot_r@12 | yaw 90° | P2 | — |

### 2 · Oyuncu TP silah katmanı (override_upper / additive) — sınıf başına

| # | Klip (×8 sınıf: ar smg sg lmg snp lch gel arc) | f | loop | katman | olaylar | P | S-02 |
|---|---|---|---|---|---|---|---|
| 13 | AP022_tp_<class>_idle_aim | 90 | 1 | override_upper | breath@0 | P0 | E (poz/soket) |
| 14 | AP022_tp_<class>_fire | 6–16 (tablo A6) | 0 | additive | fire@0, eject@1 (snp: eject bolt'ta) | P0 | E |
| 15 | AP022_tp_<class>_reload_tac (ar, smg, snp, lch) + AP022_tp_<weaponId>_reload_tac (sg_2, lmg_1, lmg_2, sw_gel, sw_arc) | A6 | 0 | override_upper | mag_out/mag_in/… + reload_commit, reload_interrupt_ok; sw_arc: cover_open/cell_out×3/cell_in×3/cover_close; sw_gel: canister_out/in + chamber | P0 | E |
| 16 | AP022_tp_<class>_reload_empty (ar, smg, snp) + AP022_tp_<weaponId>_reload_empty (sg_2, lmg_1, lmg_2) | A6 | 0 | override_upper | + charge / bolt_* (lch, sw_gel, sw_arc: tac = empty, tek klip) | P0 | E |
| 17 | AP022_tp_<class>_switch_in | 12–20 | 0 | override_upper | switch_ready@son−1 | P0 | E |
| 18 | AP022_tp_<class>_switch_out | 8–14 | 0 | override_upper | switch_away@son−1 | P0 | E |
| 19 | AP022_tp_<class>_ads_in / ads_out | 8 / 6 | 0 | override_upper | ads_in_done@7 | P1 | E |
| 20 | AP022_tp_<class>_ads_idle | 60 | 1 | override_upper | breath@0 | P1 | E |
| 21 | AP022_tp_sg_1_reload_start / shell_in / reload_end | 12 / 14 / 16 | 0/1/0 | override_upper | shell_in@9(+idx), reload_commit@9; end: chamber@10 (boşsa) | P0 | E |
| 22 | AP022_tp_sg_1_cycle (pompa, her atış) | 14 | 0 | override_upper | eject@5, chamber@11 | P0 | E |
| 23 | AP022_tp_lmg_fire_loop | 12 | 1 | additive | fire@0, fire@6 | P1 | E |
| 24 | AP022_tp_snp_bolt_cycle / bolt_cycle_ads | 30 / 24 | 0 | override_upper | bolt_up@4, bolt_back@8, eject@8, bolt_fwd@18, bolt_down@24 | P0 | E |
| 25 | AP022_tp_gel_fire_loop | 12 | 1 | additive | fire@0 | P0 | E |
| 26 | AP022_tp_arc_charge | 10 | 0 | additive | charge@0 | P1 | E |
| 27 | AP022_tp_weapon_lower / raise | 14 | 0 | override_upper | lowered@13 / raised@13 | P1 | — |
| 28 | AP022_tp_pickup_upper | 28 | 0 | override_upper | pickup_grab@11, pickup@11, pickup_done@26 | P1 | — |

Sayım: sınıf ortak (idle_aim, fire, switch_in, switch_out) 8 × 4 = 32; ADS in/out/idle 6 sınıf × 3 = 18 (gel/arc yok); reload: ar 2 + smg 2 + sg_1 (3 + cycle) 4 + sg_2 2 + lmg_1 2 + lmg_2 2 + snp 2 + lch 1 + sw_gel 1 + sw_arc 1 = 19; ek: snp bolt ×2, lmg fire_loop, gel fire_loop, arc charge, lower/raise 2, pickup 1 = 8. **TP silah toplamı = 77 klip** (P0 ≈ 57, P1 ≈ 19, P2 1).

### 3 · Oyuncu FP kollar (fp_arms mesh, fp_camera keyli)

| # | Klip | f | loop | katman | olaylar | P | S-02 |
|---|---|---|---|---|---|---|---|
| 29 | AP022_fp_<class>_idle_aim | 120 | 1 | fp base | breath@0 | P0 | E |
| 30 | AP022_fp_<class>_fire | TP+0–2 | 0 | fp additive (kollar + kamera) | fire@0, eject@1 | P0 | E |
| 31 | AP022_fp_<class|weaponId>_reload_tac / reload_empty (TP satır 15–16 ile aynı ayrım) | TP+≤4 | 0 | fp base | TP ile **aynı karelerde**; parça (`part_*`) animasyonu prop'ta ortak | P0 | E |
| 32 | AP022_fp_<class>_switch_in / out | TP ile aynı | 0 | fp base | switch_ready / switch_away | P0 | E |
| 33 | AP022_fp_<class>_ads_in / out / ads_idle (6 sınıf) | 8 / 6 / 60 | 0/0/1 | fp base | ads_in_done@7; kollar `sight_ads`'ı `fp_camera` hattına getirir | P0 (FP'de ADS temel) | E |
| 34 | AP022_fp_sg_1_reload_start / shell_in / reload_end / cycle | TP ile aynı | | fp base | aynı | P0 | E |
| 35 | AP022_fp_snp_bolt_cycle / bolt_cycle_ads | 30 / 24 | 0 | fp base | aynı | P0 | E |
| 36 | AP022_fp_lmg_fire_loop, fp_gel_fire_loop, fp_arc_charge | 12 / 12 / 10 | 1/1/0 | fp additive | aynı | P1 | E |
| 37 | AP022_fp_walk_sway / run_sway | 16 / 16 | 1 | fp additive | foot_l@0, foot_r@8 | P0 | — |
| 38 | AP022_fp_hurt_<front/back/left/right> | 10 | 0 | fp_camera + kollar additive | hit@0 | P1 | — |
| 39 | AP022_fp_pickup | 24 | 0 | fp base | pickup@11 | P2 | — |

**FP toplamı = 82 klip** (TP silah 77 − lower/raise/pickup 3 + walk/run sway 2 + hurt 4 + pickup 1 + FP cycle/bolt kopyaları dahil). Kural: FP klipleri TP klibi **onaylandıktan sonra** üretilir (aynı zaman çizgisi kopyalanır, kamera uzayına taşınır; `part_*` prop animasyonu ortak) → üretim maliyeti TP'nin ~%60'ı.

### 4 · Oyuncu TP tepki / ölüm

| # | Klip | f | loop | katman | olaylar | root | P | S-02 |
|---|---|---|---|---|---|---|---|---|
| 40 | AP022_tp_hurt_light_<front/back/left/right> ×4 | 14 | 0 | override_upper | hit@0 | 0 | P0 | — |
| 41 | AP022_tp_hurt_heavy_<front/back/left/right> ×4 | 20 | 0 | base | hit@0, foot_*@4, recover@10 | 1 (0,35–0,5 m) | P0 | — |
| 42 | AP022_tp_knockdown_back | 36 | 0 | base | hit@0, ground_impact@9, ragdoll_handoff@7, knock_end@35 | 1 (−0,9 m) | P1 | — |
| 43 | AP022_tp_getup_back | 44 | 0 | base | rise_end@43 | 0 | P1 | — |
| 44 | AP022_tp_death_fall_back | 42 | 0 | base | ragdoll_handoff@8, ground_impact@16, death_end@41 | 1 (−0,7 m) | P0 | — |
| 45 | AP022_tp_death_fall_fwd | 40 | 0 | base | ragdoll_handoff@9, ground_impact@15, death_end@39 | 1 (+0,9 m) | P0 | — |
| 46 | AP022_tp_death_spin_side | 44 | 0 | base | ragdoll_handoff@10, ground_impact@17, death_end@43 | 1 | P1 | — |

### 5 · Zombi hareket / idle

| # | Klip | f | loop | katman | olaylar | root | P |
|---|---|---|---|---|---|---|---|
| 47 | AP022_zombie_shamble_A (drag) | 24 | 1 | base | foot_l@0, foot_r@12 | 0 | P0 |
| 48 | AP022_zombie_shamble_B (fall-catch) | 24 | 1 | base | foot_l@0, foot_r@11 | 0 | P0 |
| 49 | AP022_zombie_shamble_C (wide sway) | 24 | 1 | base | foot_l@0, foot_r@12 | 0 | P0 |
| 50 | AP022_zombie_run | 14 | 1 | base | foot_l@0, foot_r@7 | 0 | P0 |
| 51 | AP022_zombie_walk_heavy (Guard) | 28 | 1 | base | foot_l@0, foot_r@14 | 0 | P1 |
| 52 | AP022_zombie_walk_stomp (Executioner) | 32 | 1 | base | foot_l@0, foot_r@16 | 0 | P1 |
| 53 | AP022_zombie_idle_sway / twitch | 120 | 1 | base | twitch@17,52,88 | 0 | P1 |
| 54 | AP022_zombie_idle_queue | 120 | 1 | base | shift_l@30, shift_r@84 | 0 | P2 |
| 55 | AP022_zombie_rise_B / rise_C (spawn; cila) | 36 / 28 | 0 | base | spawn_rise@0, spawn_land@25/14, rise_end@35/27 | 0 | P1 |

### 6 · Zombi saldırılar (A4)

| # | Klip | f | loop | katman | olaylar | root | P |
|---|---|---|---|---|---|---|---|
| 56 | AP022_zombie_attack_swipe_R | 36 | 0 | base | hit_window_start@15, hit_window_end@19, foot_l@16, attack_recover@30 | 1 (+0,35) | P0 |
| 57 | AP022_zombie_attack_swipe_L (ayna +2 f) | 36 | 0 | base | aynı | 1 | P0 |
| 58 | AP022_zombie_attack_lunge | 32 | 0 | base | hit_window_start@12, hit_window_end@15, foot_l@16, foot_r@17, attack_recover@26 | 1 (+1,4) | P0 |
| 59 | AP022_zombie_attack_bite_grab | 40 | 0 | base | hit_window_start@14, hit_window_end@18, attack_recover@32 | 1 (+0,25) | P0 |
| 60 | AP022_zombie_attack_sweep | 48 | 0 | base | hit_window_start@22, hit_window_end@27, attack_recover@40 | 1 (+0,25, yaw 30°) | P0 |
| 61 | AP022_zombie_attack_sweep_ultra | 56 | 0 | base | hit_window_start@26, hit_window_end@32, attack_recover@46 | 1 (+0,4) | P1 |
| 62 | AP022_zombie_attack_overhead_slam | 64 | 0 | base | hit_window_start@30, hit_window_end@35, ground_impact@33, attack_recover@52 | 1 (+0,5) | P0 |

### 7 · Zombi tepkiler (A5.1, A5.3)

| # | Klip | f | loop | katman/maske | olaylar | root | P |
|---|---|---|---|---|---|---|---|
| 63 | AP022_zombie_hit_<front/back/left/right>_light ×4 | 14 | 0 | override_upper | hit@0 | 0 | P0 |
| 64 | AP022_zombie_hit_<front/back/left/right>_medium ×4 | 18 | 0 | base | hit@0, foot_*@5 | 1 (−0,15) | P0 |
| 65 | AP022_zombie_hitadd_<head/torso/arm_l/arm_r/leg_l/leg_r> ×6 | 12 | 0 | additive (bölge maskesi) | hit@0 | 0 | P0 |
| 66 | AP022_zombie_hit_throw_<front/back/left/right> ×4 | 24 | 0 | base | hit@0, ground_impact@7, ragdoll_handoff@5 (ölümcülse) | 1 (0,6) | P0 |
| 67 | AP022_zombie_hit_stagger_front / side ×2 | 28 | 0 | base | hit@0, foot_*@8,16, stagger_end@27 | 1 (−0,5) | P0 |
| 68 | AP022_zombie_hit_flinch_loop | 16 | 1 | additive upper | — | 0 | P1 |
| 69 | AP022_zombie_hit_stumble (Runner) | 22 | 0 | base | hit@0, stagger_end@21 | 1 (+0,4) | P1 |
| 70 | AP022_zombie_knockdown_back / fwd ×2 | 32 / 30 | 0 | base | hit@0, ragdoll_handoff@6, ground_impact@9, knock_end@son | 1 (−1,0 / +0,8) | P1 |
| 71 | AP022_zombie_getup_back / front ×2 | 40 / 36 | 0 | base | rise_end@son | 0 | P1 |
| 72 | AP022_zombie_armorbreak_head / torso / leg ×3 (cila) | 21 | 0 | base | hit@0, armor_break@0 | 0 | P2 |
| 73 | AP022_zombie_hit_electro_loop | 24 | 1 | base | spasm@5, spasm@16 | 0 | P0 |
| 74 | AP022_zombie_hit_gel_slip (ölüm = aynı klip) | 30 | 0 | base | hit@0, ragdoll_handoff@9, ground_impact@12, death_end@29 | 1 (+0,6) | P0 |
| 75 | AP022_zombie_hit_gel_slide_loop | 16 | 1 | base | — | 0 | P2 |

### 8 · Zombi ölüm (A3)

| # | Klip | f | loop | katman | olaylar | root | P |
|---|---|---|---|---|---|---|---|
| 76 | AP022_zombie_death_fall_back | 42 | 0 | base | ragdoll_handoff@8, ground_impact@16, death_end@41 | 1 (−0,7) | P0 |
| 77 | AP022_zombie_death_fall_fwd | 40 | 0 | base | ragdoll_handoff@9, ground_impact@15, death_end@39 | 1 (+0,9) | P0 |
| 78 | AP022_zombie_death_spin_side | 44 | 0 | base | ragdoll_handoff@10, ground_impact@17, death_end@43 | 1 (0,5 yan + yaw 90°) | P0 |
| 79 | AP022_zombie_death_legs_out | 46 | 0 | base | ragdoll_handoff@11, ground_impact@14, death_end@45 | 1 (+0,4) | P0 |
| 80 | AP022_zombie_death_ultra_timber | 72 | 0 | base | ground_impact@28, ragdoll_handoff@33, ground_impact@44, death_end@71 | 1 (+0,6) | P1 |
| 81 | AP022_zombie_death_arc_electro | 36 | 0 | base | ragdoll_handoff@8, spasm@20,29, death_end@35 | 0 | P0 |

### Toplam ve kapsam

| Grup | Klip | P0 | P1 | P2 |
|---|---|---|---|---|
| TP hareket | 12 | 9 | 2 | 1 |
| TP silah (8 sınıf, 12 silah) | 77 | 57 | 19 | 1 |
| FP kollar | 82 | 64 | 16 | 2 |
| TP tepki/ölüm | 13 | 10 | 3 | 0 |
| Zombi hareket/idle | 10 | 4 | 5 | 1 |
| Zombi saldırı | 7 | 6 | 1 | 0 |
| Zombi tepki | 30 | 21 | 6 | 3 |
| Zombi ölüm | 6 | 5 | 1 | 0 |
| **Toplam** | **237** | **176** | **53** | **8** |

Bellek (DEC-093 5 GB): 237 klip × ort. 45 f × 63–73 kemik, Optimal sıkıştırma ≈ 10–12 MB; FP kol klipleri (82) sıkıştırmasız ≈ +6 MB (yakın plan kalite kaybı sıfır) → toplam ≤ 20 MB, kısıt değil. Perf (DEC-092): animasyon CPU bütçesi 16,6 ms içinde **≤ 2,4 ms** hedef (40 zombi Animator + FootIK 12 gövde + verlet 40 gövde + ragdoll ≤ 6 Yüksek); FP kol Animator'ı ayrı katman, ≤ 0,15 ms.

Gerçekçilik değerlendirmesi (tek Blender animatörü): 237 klip mevcut 86'nın ~2,8 katı; ancak (a) FP seti (82) TP zaman çizgisinin kamera uzayına taşınmasıdır, (b) sınıf ortak switch/ads/idle klipleri (50) şablon üstünden türev, (c) sg_2 reload = AR kopyası, lmg_2 = kısa AR/LMG melezi, (d) aynalar ve loop bölünmeleri sıfıra yakın maliyet. Özgün el emeği ≈ 120 klip. Üretim sırası önerisi (AP-022 dilimleri):
1. **Dilim 1 (P0 çekirdek, S-02 sayılarını beklemez):** rig eki R → zombi hareket 47–50 → saldırı 56–60, 62 → ölüm 76–79, 81 → tepki 63–67, 73–74 → oyuncu hareket 1–9 → oyuncu hurt 40–41, ölüm 44–45. (~55 klip)
2. **Dilim 2 (AP-021 blockout üstünde, WeaponDefinition v3 sayıları geldikçe rate uyarlanır):** TP silah ar/smg (13–18) → sg_1/sg_2 (21–22, 15–16) → lmg_1/lmg_2 → sr_1 (24) → rl_1 → sw_gel/sw_arc (25–26). (~57 P0, sonra 19 P1)
3. **Dilim 3:** FP kollar (29–37) TP onayı üzerinden. (~64 P0)
4. **Dilim 4 (P1/P2):** Ultra timber, knockdown/get-up, Guard/Executioner yürüyüşleri, run start/stop, idle'lar, flinch loop, ADS TP.

Kabul (her klip, P-06 → S-06): AD karar kuralları (simetrik yatay kol yok, siluet C11), C03 kapsül (hit 0,60, saldırı 0,66), ayak kayması ≤ 2 cm (rate ≤ 1,05'te), loop ilk/son kare + hız sürekliliği, root_delta manifestte, olay adları tam liste, TP/FP olay kareleri eşit, ragdoll_handoff öncesi 2 kare hız taşıyor (kemik hız vektörü ≥ eşik, sıfır hız devri yok).

Açık kararlar (C-00/S-06): (1) C03 hit toleransı 0,50 → 0,60; (2) Root keyli klipler için `OnAnimatorMove` → CharacterController yolu (DEC-032 ile uyum); (3) ragdoll kademe sayıları 10/6/0 onayı (DEC-092 bütçesinde ölçülecek); (4) rig_template_v002 (parmak/twist/jaw/fp_camera) sahibi ve sırası (AP-022 dilim 0); (5) `part_*` prop animasyonunun taşınma yolu: silah FBX'inde parça kemikleri + karakter klibiyle aynı zaman çizgisinde ayrı prop klibi (`AP022_prop_<weaponId>_<action>`) → S-06 `WeaponPartDriver` olayda değil, klip senkron oynatır (önerim: senkron; olay tabanlı sürüş kesintide parça yarım kalır); (6) dış kaynak 10 kr referans (A3 ölüm düşüşleri + shotgun throw için 2–3 video referansı; isteğe bağlı).

---

# Bölüm III — animation-specialist konsey girdisi


Yazar: animation-specialist · Tarih: 2026-09-24 · Paket lideri: S-06 · Tüketiciler: S-06 (motor), P-06 (AP-022 klipler), S-03 (AI), S-07 (VFX/feel), S-08 (ses), S-02 (silah), S-01 (kamera).
Kapsam: A2 locomotion v3 · A3 ölüm→ragdoll sistemi · A5 hit reaction katmanlaması · A6/A7 silah katmanı mimarisi + rig gereksinimleri · A8 olay sözleşmesi v3 · kalite kademeleri · risk listesi. Karakter performansı (klip içeriği, pozlama, timing çizelgesi) character-animator'ın belgesindedir; burada yalnız sistem tarafı vardır.
Bağlayıcı kararlar (C-00, 24.09): **DEC-092** perf hedefi 60 fps — Yüksek kademe ort ≤ 16,6 ms, p99 ≤ 20 ms (ragdoll tavanı, IK bütçesi, Animator LOD bu hedefe göre boyutlandırıldı; §T); **DEC-093** build boyutu tavanı 5 GB (220 silah klibi + ölüm/locomotion seti sığar, klip sıkıştırma "Optimal", ölçüm S-06 kapıda); **DEC-094** 2 Special özgün ad + kendi modelimiz (bu belgede `Special gel` / `Special yıldırım` olarak anılır; mekanik referans yalnız davranış için). Her ağır özellik kademeli, ms maliyeti tablo §T'de. 0 B/kare kuralı bütün yollarda geçerlidir.
FP mimarisi (S-01 ile anlaşıldı): FP = ayrı **viewmodel** (kollar + silah) `FirstPersonView` katmanında, overlay kamera ile; dünya bedeni FP'de `ShadowsOnly`; S-01 Animator float `AimPitch` (derece, + yukarı, −60…+60) ve muhtemelen `AimYaw` sağlar; P-06 oyuncu başını ayrı SkinnedMeshRenderer'a böler (FP'de ShadowsOnly ile gizlenir).

Okunan kanıt: M4-R1 §0 notları (ayak/gövde hızdan yavaş kalabilir, kayarak mesafe alınır; 4 ölüm, gerçekçi düşüş; 5 saldırı; vuruş/darbe/reload profesyonel), FACTS.md, clip-inventory (86 klip), `CharacterAnimationEvents` (28 ad), `FootIkAdapter` (12 beden/kare), `SecondaryChainDriver` (40 beden/kare), `CharacterAnimatorLod` (Far = LOD≥2, 15 Hz), `FeedbackConfigSection` (StaggerCooldown 1,1 s, MaxStaggerDuration 0,45 s), canlı kareler v006b-r3 (tepeden karakter ≈ 90 px boy: ayak kayması okunmaz, siluet ve gövde eğimi okunur).

---

## A2 · Locomotion v3 sistemi

### A2.1 Politika değişikliği: stride-matched → "kadans bandı + ayak kilidi"
1. Bugün: klip hızı = hız ÷ no-slide hızı (StrideCalibrator). Standard zombi 2,7 m/s ÷ shamble 1,29–1,55 → ×1,74–2,09 oynatma: çırpınan bacaklar (AD ve kullanıcı şikâyetinin kaynağı). Oyuncu 6,2 ÷ 5,687 → ×1,09 kabul edilebilir.
2. v3 kuralı: **oynatma hızı bandı** `rate = clamp(speed / noSlide(blend), RateFloor, RateCeil)`; bandın dışında kalan fark **kayma** olarak kabul edilir ve **ayak kilidi + adım germe** ile gizlenir (A2.4). Kullanıcı notu birebir: "ayaklar ve gövde daha yavaş hareket edip kayarak mesafe alınabilir".
3. Band sabitleri (parametre, kademeden bağımsız):

| Aktör | RateFloor | RateCeil | İzinli kayma oranı (speed/(rate·noSlide) − 1) |
|---|---|---|---|
| Oyuncu TP/tepeden | 0,85 | 1,20 | ≤ %15 TP, ≤ %30 tepeden |
| Oyuncu FP | 0,85 | 1,10 | ayak görünmez; kadans yalnız bob/foley'i sürer |
| Zombi Standard | 0,80 | 1,25 | ≤ %20 TP, ≤ %40 tepeden |
| Zombi Runner | 0,85 | 1,15 | ≤ %12 |
| Zombi Guard / Executioner | 0,75 | 1,15 | ≤ %25 (ağırlık hissi; kısa adım, uzun süre) |

4. Görünüme göre eşik: `LocomotionPolicy.SlideTolerance` kamera modundan (S-01 CameraMode API) okunur; tepeden modda RateCeil zombi için 1,10'a **düşer** (uzaktan yavaş kadans daha "ağır" okunur, kayma görünmez), TP'de tabloya döner. FP'de yalnız oyuncu için geçerli.
5. Klip değiştir / hızlandır kararı: hedef `noSlide` bandındaki en yakın klip log-uzayda seçilir: `|ln(speed/noSlide_i)| ≤ ln(RateCeil)` sağlayan klip yoksa **komşu iki klip 1-D blend** (Speed01 eşikleri = no-slide hızları); blend içinde `rate` iki klibin harmanlanmış no-slide değerine göre hesaplanır. Bu, bugünkü halka (ring) yapısını korur; yalnız eşikler ve clamp değişir.
6. Klip ihtiyacı (P-06'ya, band matematiğinden çıkan): oyuncu `walk_slow` (no-slide ≈ 1,3 m/s; 0,35–1,8 bandı), `walk` (≈ 3,0), `run` (≈ 5,9 — 6,2 için rate 1,05), `sprint_lean` (isteğe bağlı, FP hissi). Zombi: `shamble_slow` (≈ 1,3, Executioner 1,7 → ×1,15 clamp'te %14 kayma), `shamble` (≈ 1,6; A/B/C varyantları korunur), `shamble_fast` / "hurry lurch" (≈ 2,4; Standard 2,7 → ×1,12), `heavy_walk` (≈ 2,0; Guard 2,2 → ×1,10), `run` (5,3 mevcut; Runner 4,6 → ×0,87). Her klip için `spd` (no-slide) manifestte kalır; StrideCalibrator ölçümü doğrulama olarak sürer (klipteki `spd` ile ölçüm farkı > %8 → uyarı).
7. Kadans tavanı hız artınca değil adım uzunluğu artınca aşılır: oyuncu koşuda 6,2 m/s = 15 kare döngü ×1,05 → ≈ 2 adım/s·... = 3,4 Hz ayak sesi (S-08 için: foley ritmi rate ile ölçeklenir, ceil'den dolayı ≤ 4 Hz).

### A2.2 Başla / dur / pivot / yerinde dönüş
8. Kök hareketi **yok** (DEC-032 zombi CharacterController; oyuncu da kod hızıyla). Başlat/dur klipleri **kod ivmesinin üstüne binen kısa override'lar**: kontrolcü hızlanırken (0→6,2 m/s 0,18 s, ease-out) `run_start_L/R` (10 kare, hangi ayak öndeyse) Base'de crossfade 0,08 s; dururken hız > 4 m/s iken giriş bırakılırsa `run_stop_L/R` (12 kare, planted ayak) + kontrolcü yavaşlama 0,14 s; ayak kilidi (A2.4) kayan ayağı sabitler. Hız < 4 m/s'de dur klibi yok (blend'den idle'a 0,12 s).
9. Pivot: koşarken giriş yönü mevcut yönle > 135° ise `run_pivot_180` (14 kare; plant karesinde `pivot_plant` olayı), 60–135° ise `run_pivot_90_L/R` (10 kare). Pivot sırasında kontrolcü hız vektörü 0,1 s'de eski yönün %30'una iner, sonra yeni yöne ivmelenir (S-01/S-02 ile hız modeli). Tepeden modda pivot klipleri **kapalı** (okunmaz, hissi geciktirir); TP/FP'de açık.
10. Yerinde dönüş (idle): gövde yaw'ı ile bakış/hedef yaw'ı farkı > 45° → `turn_in_place_90_L/R` (16 kare), > 135° → `turn_in_place_180_L/R` (22 kare); 45°'ye kadar üst gövde/aim katmanı (±35° + kalça 10°) karşılar. Oyuncu için tetik: nişan yaw'ı (fare/sağ çubuk) alt gövdeye 0,25 s boyunca > 45° kaldıysa. Zombi için tetik: NavMesh yön değişimi > 60° ve hız < 0,4 m/s (kuyrukta / boğazda). Kademe Performans: yerinde dönüş klipleri kapalı, yaw slerp 540°/s.
11. Zombi yön değiştirme koşarken: `yawRate` > 240°/s ise Runner'da `run_turn_lean` additive (A2.3) yeter; pivot klibi yalnız oyuncuda.

### A2.3 Eğim (lean) katmanı
12. Yeni additive katman `lean` (kalça + spine ×3 + baş; ağırlık 1, her zaman açık): iki 1-D additive poz kümesi `lean_accel_fwd/back` (±6° pitch) ve `lean_turn_L/R` (±8° roll, baş karşı yöne 3°). Parametreler: `LeanAccel = clamp(a_fwd / 12 m/s², −1, 1)`, `LeanTurn = clamp(yawRate / 360°/s, −1, 1) · smoothstep(speed / 3 m/s)`. Yumuşatma 8 Hz kritik sönümlü; tepeden modda kazanç ×0,6 (siluet şişmesin), TP ×1,0, FP ×0 (kamera roll'u S-07'de).
13. Zombi lean: aynı katman, kazanç Runner ×1,3, Standard ×0,8, Guard/Executioner ×0,5; "momentum taşıması" hissi buradan gelir (klipte değil).

### A2.4 Ayak IK, ayak kilidi, kayma gizleme, moonwalk önleme
14. `FootIkAdapter` v3: 2 ışın (topuk + ayak ucu değil; **ayak merkezi + kalça altı**), zemin yüksekliği + normal; ayak pitch/roll normale ≤ 25°; kalça ofseti = min(footL_Δ, footR_Δ), aralık [−0,25, +0,05] m, 10 Hz yumuşatma. Yalnız LOD0 ve kademe bütçesi içinde (tablo §T).
15. **Ayak kilidi (foot lock)**: `foot_l` (plant) olayından `foot_l_off` (toe-off, yeni olay) olayına kadar ayağın dünya pozisyonu kilitlenir (IK hedefi = plant anındaki dünya nokta); kilit süresi boyunca gövde ilerler → bacak gerilir. Gerilme sınırı: diz uzunluğu toplamının %96'sı; aşılınca kilit 60 ms'de çözülür (ayak kısa bir kayma yapar; kısa ve nadir olduğu için okunmaz). Kayma oranı ≤ %20 iken kilit hiç kopmaz (ölçüm: 15 kare döngüde adım 1,4 m, %20 kayma = 0,28 m ek gerilme < %4 bacak uzunluğu).
16. Kilit yalnız `speed > 0,35` ve `|slideRatio| > %5` iken açılır (gereksiz IK yok); durakta (idle) kapalı; saldırı/hit/ölüm durumlarında kapalı; pivotta yalnız `pivot_plant` ayağı kilitli.
17. Moonwalk önleme: (a) hız < 0,35 m/s → idle (yerinde dönüş kliple), (b) 0,35–1,8 bandında `walk_slow` klibi (RateFloor 0,85 → 1,1 m/s no-slide alt sınır; hız bunun altındaysa ayak kilidi kayan ayağı **geri** çeker → "yerinde adım", asla ters yön), (c) `slideRatio < −0,15` (ayaklar gövdeden hızlı) hiçbir zaman kabul edilmez: bu durumda önce rate düşer, sonra bir alt klibe geçilir. Doğrulama testi: her klip/hız çiftinde `sign(footVelWorld · travelDir)` kilitli ayak için ≤ 0 (geri kaymıyor, ileri kaymıyor).
18. Eğimli zemin (Dünya v5 koridor/merdiven): kalça ofseti + ayak normali yeterli; merdiven için `step_up` klibi yok (NavMesh eğim ≤ 30°, basamak yüksekliği ≤ 0,3 m kabulü S-05'e bildirilir).

### A2.5 Zombi yürüyüş çeşitliliği (spawn başına)
19. Spawn'da deterministik tohum (LifeId) → `GaitProfile` seçilir: `gaitClip` (A/B/C + `limp_L`, `limp_R`, `drag_foot` = 6 Standard varyant), `phaseOffset` ∈ [0,1) (kalabalıkta senkron adım yok), `rateJitter` ±%6, `idleVariant` (sway/twitch/queue), `postureAdd` (additive `hunch` 0–1, `head_tilt_L/R` 0–0,6, `arm_hang_L/R`), `scale` ±%4 (mevcut varyasyonla çakışmasın; AP-007C boy çeşitliliği varsa devre dışı).
20. Tier başına küme:

| Tier | Yürüyüş klipleri | Kadans/posture | Tempo hissi |
|---|---|---|---|
| Standard (2,7) | shamble_fast A/B + limp_L/R + drag_foot (5) | hunch 0,3–0,8, rate ±%6 | düzensiz kalabalık |
| Runner/Special (4,6) | run A/B + sprint_lean; başlangıç `run_burst` (8 kare) | hunch 0–0,3, lean ×1,3 | sürekli hamle |
| Guard/Elite (2,2) | heavy_walk A/B (omuz öne, zırh ağırlığı) | hunch 0,2, rate ±%3, lean ×0,5 | ezici, düz |
| Executioner/Ultra (1,7) | dread_walk (tek; ×1,15 tavan) + `idle_loom` | hunch 0, rate 0 | kaçınılmaz |

21. Kalabalıkta okunabilirlik (tepeden): tier siluet farkı yürüyüşte değil **omuz/baş çizgisinde** (posture additive) ve hız farkında; tepeden karede 90 px boyla yalnız kadans + gövde salınımı okunur; bu yüzden posture additive her kademede açık, ayak IK kalabalıkta kapalı olabilir.
22. Mevcut Animator (ArtAnimatorBuilder üretir) değişiklikleri: zombi Base 1-D Gait blend'inde eşikler no-slide'a bağlı; yeni parametreler `RateClamp`, `LeanAccel`, `LeanTurn`, `Hunch`, `HeadTilt`; oyuncu 2-D freeform halkaları korunur, `MaxRunRate` 1,6 → 1,20 (RateCeil), yeni `walk_slow` iç halka.

---

## A3 · Ölüm → ragdoll sistemi (sistem yarısı)

### A3.1 Aşamalar ve zamanlama
23. Boru hattı, her ölüm için (kademeye göre 5 veya 3 aşama):
   1. **Onset (0–0,20 s)**: ölüm ailesi klibi başlar (A3.5); Animator tam yetkili; hitbox/damage alıcı **onset karesinde** kapanır; ThreatDirector/altın/drop/occupancy **onset'te** çözülür.
   2. **Powered ragdoll (0,20–0,65 s)**: klipteki `ragdoll_on` olayında rigidbody'ler dinamik olur; her ConfigurableJoint SLERP drive `targetRotation` = o karede Animator'ın verdiği yerel rotasyon (Animator `ragdoll_on`'dan sonra 0,45 s daha örneklenir, yazmaz — "pose kaynağı"). Drive spring 900 → 0 (exponential, τ 0,18 s), damper 40 → 8. Darbe itkisi (A3.2) `ragdoll_on` karesinde uygulanır. Bu aşama "klipteki niyet + fizik gerçeklik" karışımıdır; kullanıcının istediği "gerçekçi düşüş" burada oluşur.
   3. **Full ragdoll (0,65 s → settle)**: drive 0; yalnız eklem limitleri + yerçekimi + sürtünme (dinamik 0,7).
   4. **Settle tespiti**: tüm bedenlerde |v| < 0,15 m/s ve |ω| < 0,5 rad/s **0,35 s** boyunca, ya da `ragdoll_on` + 2,5 s zaman aşımı, ya da kalça y < zemin −0,3 (dünya dışına düşme) → dondur.
   5. **Freeze**: tüm rigidbody `isKinematic`, collider kapalı (Yüksek'te kalça+göğüs collider 1,0 s daha **statik engel** olarak açık kalır: sonraki ragdoll üstüne düşebilsin), Animator kapalı, kemikler son pozda kalır (SkinnedMeshRenderer çalışmaya devam eder: kemik güncellemesi yok → skinning ≈ 0,02 ms/beden). Performans kademesinde `BakeMesh` (önceden ayrılmış Mesh havuzu, yönetilen ayırma yok) → MeshRenderer'a geçiş, SkinnedMesh kapatılır.
   6. **Ceset ömrü & havuz**: ömür kademeye göre (§T; Yüksek 4 s, tier çarpanı Ultra ×1,5), ceset tavanı aşılırsa en eski 0,6 s'de "gömülme" (y −0,4 m + fade) ile havuza döner; havuza dönüşte `RagdollReset`: rigidbody kinematik, collider kapalı, kemik yerel pozları bind pose'a, Animator rebind, LifeId artar (state sızıntısı testi: 200 ölüm/dönüş döngüsü, alloc 0).

### A3.2 Darbe itkisi
24. `DeathImpulse = dir · I_weapon · m_tier + up · 0,15 · I_weapon`, uygulanma noktası vuruş kemiği (hitbox), `ForceMode.Impulse`, `ragdoll_on` karesinde tek sefer; onset klibi seçimi de aynı vektörden (A3.5).

| Silah sınıfı | I_weapon (N·s) | Not |
|---|---|---|
| SMG | 25 | kalça hız tavanı 2,0 m/s |
| AR | 40 | |
| LMG | 35 | son 3 vuruşun yön ortalaması |
| Shotgun | 140 (saçma toplamı, tavan) | yalnız ≤ 6 m'de tam; 12 m'de ×0,4 |
| Sniper | 180 | baş vuruşunda baş kemiğine ×0,5, gövdeye ×1 (kafa "kopma" değil, çökme) |
| Launcher | radyal 260 merkezde → 60 @ 3 m; yön = patlama merkezinden | havaya kalkış izinli, kalça hız tavanı 6,0 m/s |
| Special gel (DEC-094 özgün ad) | 0 + sürtünme 0,05 (kayarak düşer) | onset ailesi "slip" |
| Special yıldırım (DEC-094 özgün ad) | 30 rastgele yön + up 60 (kasılma) | onset ailesi "convulse" |

25. Tier kütle çarpanı `m_tier`: Standard 1,0 · Runner 1,1 · Guard 0,55 · Executioner 0,35. Kalça hız tavanı silaha göre (tablo); tüm bedenlerin toplam kinetik enerjisi `E_max = 350 J` ile kırpılır (fırlatılan ceset değil, düşen ceset).
26. Yön okunabilirliği (tepeden): itkinin yatay bileşeni ≥ %70 korunur; düşüş yönü = mermi yönü olduğu için tepeden bakışta "nereden vuruldu" okunur.

### A3.3 Çarpışma katmanları
27. Katman `Ragdoll`: ✔ `World` (statik), ✔ `Prop_Static`, ✔ `Corpse` (Sinematik + Yüksek; Performans ✘), ✘ `Player`, ✘ `Zombie` (canlı, CharacterController), ✘ `Ragdoll` (kendi kendine: aynı bedenin komşu olmayan kapsülleri de ✘ — 11 kapsülde self-collision 55 çift, gereksiz), ✘ `Projectile` (mermi cesetten geçer; S-02: cesete "kozmetik darbe" VFX'i ray ile ayrı, hasar olayı yok), ✘ `Prop_Dynamic` (kırılabilir AP-017 propları itilmez).
28. Canlı zombi cesedin **üzerinden yürür** (NavMesh oyulmaz, ceset itilmez); kabul edilen sanat kararı: ceset kapsülleri kalça/göğüs 1 s statik engel (Yüksek+) yalnız *sonraki* ragdollar için (üst üste yığılma), yürüyenler için değil.

### A3.4 Kemik seti, eklem limitleri, kütle
29. **11 rigidbody / 10 eklem** (Yüksek, Performans): Hips, Chest(Spine2), Head, UpperArm L/R, LowerArm L/R (el forearm'a kaynaklı), Thigh L/R, Shin L/R (ayak shin'e kaynaklı). Sinematik: +Foot L/R = **13/12** (ayak dönmesi TP yakın çekimde okunur).
30. Kapsül yarıçapları tier ölçeğine göre otomatik (rig template'den; Guard zırh +%10). Eklem limitleri (ConfigurableJoint, derece):

| Eklem | twist | swing1 | swing2 | Not |
|---|---|---|---|---|
| Hips–Chest | ±18 | 30 | 20 | |
| Chest–Head | ±40 | 35 | 30 | |
| Chest–UpperArm | ±55 | 85 | 60 | omuz |
| UpperArm–LowerArm | ±8 | 0…135 (hinge) | 5 | dirsek |
| Hips–Thigh | ±25 | 70 | 40 | |
| Thigh–Shin | ±5 | 0…130 (hinge) | 5 | diz |
| Shin–Foot (Sinematik) | ±10 | 25 | 15 | |

31. Kütle (Standard, toplam 68 kg): Hips 12 · Chest 16 · Head 5 · UpperArm 2,5×2 · LowerArm 1,8×2 · Thigh 8×2 · Shin 5×2; Guard ×1,6, Executioner ×2,2 (Ultra 5'e kadar çıkmaz: yığında ezilme yok). Damping: linear 0,15, angular 0,8; `solverIterations` 8 / `solverVelocityIterations` 4; `maxDepenetrationVelocity` 2; `Physics.defaultSolverIterations` değişmez. Fixed step mevcut (50 Hz varsayımı); 60 fps'te kare başına ≤ 2 adım.

### A3.5 Onset aileleri = kullanıcının "4 farklı ölüm"ü
32. Ragdoll gerçek düşüşü üretir; **aile** = ilk 6–14 karelik yazılı niyet + ragdoll ön hız profili. Seçim: `(bölge, silah sınıfı, yatay vuruş açısı, tier)` → aile:

| # | Aile | Tetik | Onset klibi (P-06) | Ragdoll profili |
|---|---|---|---|---|
| D1 | **Drop** (bacaklar boşalır) | baş bölgesi (Sniper/AR/SMG baş), ya da HP'nin > %60'ı tek vuruşta | `z_death_drop` 8 kare (dizler bükülür, kollar düşer) | itki ×0,4, drive τ 0,10 (hızlı gevşeme) |
| D2 | **Knockback** (geriye savrulur) | Shotgun ≤ 6 m, Launcher, ön açı ±60° | `z_death_thrown` 10 kare (göğüs geri, kollar öne) | itki ×1,0, drive τ 0,25 |
| D3 | **Stumble-fall** (ileri momentumla kapaklanır) | gövde/bacak vuruşu, zombi hız > 1,5 m/s, arka açı ±70° ya da bacak bölgesi | `z_death_stumble` 12 kare (bir adım daha, diz kırılır) | kalçaya ileri +1,5 m/s ek hız, drive τ 0,22 |
| D4 | **Spin-fall** (yan darbe, dönerek) | yan açı 60–120°, LMG akışı (accumulator > 0,6) | `z_death_spin` 12 kare (omuz döner, yan çöker) | itkiye yaw torku 40 N·m, drive τ 0,20 |
| D5 | Ultra 2 aşamalı (diz + devrilme) | Executioner | `z_death_ultra_kneel` 30 kare (mevcut ultra'dan) sonra `ragdoll_on` | kütle ×2,2, drive τ 0,35 (ağır) |
| S1 | Slip (gel) | Special gel | `z_death_slip` 8 kare (ayaklar kayar, sırt üstü) | sürtünme 0,05, itki 0 |
| S2 | Convulse (yıldırım) | Special yıldırım | `z_death_convulse` 14 kare (kasılma) | rastgele + up, drive τ 0,30 |

33. Kap dolunca (A3.6) **aynı aileler tam yazılı düşüş klibi** olarak oynar: `z_death_drop_full` 36 k · `z_death_thrown_full` 40 k · `z_death_stumble_full` 44 k · `z_death_spin_full` 40 k (mevcut 8 klip yeniden isimlendirilerek 4'e indirgenir; ultra/special mevcut). `death_end`'de 3-nokta ışın ile zemine eğim uydurma (≤ 15°, kalça pitch/roll) — yerinde çökmeyi bile "yerle temaslı" gösterir. Kullanıcı isteğinin garantisi: hangi kademe olursa olsun **4 farklı ölüm görünür**.

### A3.6 Eş zamanlı ragdoll tavanı ve havuz
34. Aktif (donmamış) ragdoll tavanı: **Sinematik 12 · Yüksek 6 · Performans 2**; donmuş cesetler sayılmaz (ceset tavanı ayrı: 16 / 10 / 6). Tavan doluysa: (a) en eski aktif ragdoll settle zorlaması (τ 0 → hemen dondur) *yalnız* `ragdoll_on` + 1,2 s'yi geçmişse, (b) aksi hâlde yeni ölüm A3.5 tam klip yoluna düşer. Öncelik: oyuncuya yakınlık (< 8 m) ve TP/FP görüş konisi içindekiler ragdoll alır; tepeden modda hepsi eşit.
35. Havuz: **önceden kurulu, beden içi** (her pooled zombide rigidbody + eklem + kapsül; canlıyken kinematik ve collider kapalı, mevcut WP-016 kemik hitbox kapsülleri **çift kullanımlı** — ayrı kapsül seti eklenmez). Alternatif "ayrı ceset rig havuzu" reddedildi: AP-007C/014 görsel varyantlarını (baş, şapka, zırh parçaları) kopyalamak gerekir. Canlı kinematik rigidbody'nin transform senkron maliyeti ölçülür: 40 beden × 11 = 440 kinematik gövde; `Physics.autoSyncTransforms=false` ile beklenen < 0,15 ms; aşılırsa rigidbody bileşenleri yalnız `Ragdoll` tavanı kadar bedende (eşleşen kemik hiyerarşisine ölümde `Rigidbody.isKinematic` yerine bileşen **enable** — bileşen ekleme/çıkarma yok).
36. Maliyet tahmini (referans Mac, 11 beden/10 eklem, kapsül–dünya mesh): fizik ≈ 0,09–0,13 ms/aktif ragdoll + poz yazımı 0,02 ms; Sinematik 12 → ≈ 1,6 ms; Yüksek 6 → ≈ 0,8 ms; Performans 2 → ≈ 0,25 ms. Ceset (donmuş) ≈ 0,02 ms (skinning). Powered aşaması (drive) +%20. S-06 kapıda ölçer, tabloya yazar.

### A3.7 Oynanışın ölümden sonra asla bağlı olmayacağı şeyler
37. Hitbox/hasar alıcı: onset karesinde kapalı; cesede mermi hasar olayı yok (S-02: `Corpse` katmanı, kozmetik darbe). NavMesh: oyulmaz, ceset engel değil, `NavMeshAgent`/NavPathFollower onset'te bırakılır. AttackSocket / hit window: onset'te iptal (`hit_window_end` sentetik olarak yükseltilir, DEC-029 tüketicisi çift açık pencere görmez). Kalabalık occupancy, ThreatDirector sayımı, spawn kotası: onset. Altın/drop/başarım: onset. Ses/VFX "yere çarpma": ragdoll'da kalça/göğüs `OnCollisionEnter` (hız > 1,5 m/s) → `impact_land` sentetik olay (S-08 gövde düşme, S-07 toz); yazılı düşüşte klip olayı. CameraShake, screen effect: yalnız onset. Ragdoll kapatma (kademe/kap) oynanış determinizmini değiştirmez (test: aynı tohum, ragdoll on/off → aynı altın/dalga sonucu).

---

## A5 · Hit reaction katmanlaması (sistem yarısı)

### A5.1 Katman tablosu
38. Zombi: `Base` (locomotion/attack/stagger/death) · `override_upper` (yönlü hit, ağırlık = darbe) · `additive_region` (baş/gövde/bacak flinch; mevcut) · **yeni** `additive_impulse` (kod sürücülü yay: spine ×3 + baş rotasyon ofseti; kritik sönümlü, f 6 Hz, ζ 0,55). Oyuncu: `override_upper` (hurt_A, arms maskeden **çıkarılmış**: spine+baş) · `additive_flinch` (kollar/silah 80 ms sarsıntı) · `Base` (hurt_B tam beden, yalnız knockback/HP<%30) · FP: kollar additive + kamera (S-07).
39. Override vs additive kuralı: yönlü/ağır reaksiyon **override** (üst gövde, okunur siluet), hafif ve sık reaksiyon **additive** (locomotion bozulmaz, üst üste binebilir). Karar eşiği `impulse01` (A5.2): < 0,35 yalnız additive_region + yay; 0,35–0,75 override_upper ağırlık = impulse01; ≥ 0,75 stagger adayı (A5.4).

### A5.2 Yönlü blend uzayı ve darbe ölçekli ağırlık
40. `override_upper` içinde tier başına 2-D freeform directional blend: `HitX, HitY` = vuruş yönünün beden yerelinde yatay birim vektörü; klipler mevcut `hit_front/back/left/right_<tier>` (12) → çapraz vuruşlar harmanlanır, 8 yön klibi gerekmez. Bölge yüksekliği `additive_region` ile ayrı (baş/gövde/bacak).
41. `impulse01 = clamp( damage / (tierHP · 0,12) · w_weapon , 0, 1 )`; `w_weapon`: SMG 0,6 · AR 0,9 · LMG 0,7 · Shotgun 1,6 · Sniper 1,8 · Launcher 2,0 · gel 0,3 · yıldırım 1,2. Tier poise böleni: Standard 1,0 · Runner 0,8 · Guard 1,8 · Executioner 3,0 (`impulse01 /= poise`). Override katman ağırlığı = impulse01, 40 ms'de biner, klip boyunca 1→0 (klibin son %30'unda lineer).
42. Yay katmanı: her vuruşta `additive_impulse` yayına hız darbesi `k · impulse01 · dir` (spine pitch/roll ±9° tavan, baş ±14°); tepeden bakışta "titreme" okunur, TP'de override ile birleşir.

### A5.3 LMG sürekli flinch akümülatörü (stun-lock'suz sarsılma)
43. `A ← A·exp(−dt/0,35) + impulse01`; override klibi yalnız (a) A'nın 0,4 üstüne **ilk** çıkışında ya da (b) mevcut klip normalizedTime > 0,4 iken ve yeni vuruş impulse01 ≥ 0,25 ise **yeniden başlar**; aksi hâlde vuruş yalnız yaya (A5.2) gider. Override ağırlığı `min(1, A)`; A > 1,5'te klip oynatma hızı ×1,25 (daha sinirli). LMG 12 Hz'de: A ≈ 0,7·(1/(1−exp(−1/4,2))) ≈ 3,3 doygun → sürekli sarsılan, ama yürümeye devam eden zombi; stagger yalnız A5.4 kapısıyla.

### A5.4 Knockback ve stagger eşikleri ↔ REQ-124
44. Shotgun/Launcher knockback: **kod yer değiştirmesi** (root motion değil; DEC-032): CharacterController `Move` ile 0,25 s'de ease-out 0,6 m (Shotgun ≤ 4 m) / 1,2 m (Launcher ≤ 2 m), yön = yatay mermi yönü, NavMesh kenarına kırpılır (`SamplePosition` 0,5 m); animasyon `z_hit_knockback_<tier>` (Base override, 18 kare, `knockback_end` olayı); Guard ×0,5 mesafe, Executioner 0 (yalnız üst gövde). Tepeden okunabilirlik: yer değiştirme siluet kaymasıyla görünür.
45. Stagger kapısı: mevcut `StaggerCooldown` 1,1 s, `MaxStaggerDuration` 0,45 s korunur (REQ-124). Eşik: `impulse01 ≥ 0,75` ve kapı açık → stagger (locomotion kod tarafından durur, `z_hit_stagger_<dir>` 14 kare Base); kapı kapalıysa aynı vuruş override_upper ağırlık 1,0 ile oynar (görsel kaybolmaz, oynanış kilitlenmez). Ağır stagger (`hit_heavy_stagger`, 1,4 s tavan) yalnız zırh kırılması (mevcut). Flinch (override/additive) hiçbir kapıya bağlı değil; yalnız 0,12 s mikro-cooldown akümülatör tarafından örtük.
46. Saldırı sırasında hit: `hit_window` içindeyse reaksiyon **yalnız additive** (DEC-029 pencere klipten kesilmez); telegraph sırasında override_upper izinli (S-03 iptal kararı `attack_recover`/stagger ile).

### A5.5 Oyuncu hurt katmanı — nişan bloke sınırı
47. Kural: **hiçbir hurt reaksiyonu nişanı 120 ms'den uzun bloke etmez.** Uygulama: `override_upper` hurt_A maskesi spine1..head (kollar + silah **dışarıda**); `aim_layer` (IK) her zaman en son ve tam yetkili; kollar `additive_flinch` ile 80 ms sarsılır (fire/reload durumları bozulmaz, reload klibi kesilmez). hurt_B (tam beden) yalnız Launcher öz-hasar/Executioner vuruşu/HP<%30'da, 14 kare; ilk 120 ms sonra maske kollara yetkiyi geri verir (katman ağırlığı 0,12 s'de 1 → 0,35). FP: kollar additive 90 ms + kamera darbesi (S-07); ateş/ADS asla kesilmez. Test: hurt sırasında `fire` girişi → mermi 1 kare içinde çıkar; nişan yaw hatası ≤ 2°.

---

## A6/A7 · Silah katmanı mimarisi (10 silah + 2 Special; TP tam beden + FP kollar)

### A6.1 Üretilen taban controller (ArtAnimatorBuilder) — katmanlar
48. Oyuncu TP controller katmanları (sıra = yetki sırası):
   0 `Base` (full): idle, walk_slow/walk/run halkaları, start/stop/pivot/turn_in_place, hurt_B, knockback, pickup_special, deaths (3), `jump`/`vault` yok (kapsam dışı).
   1 `override_upper` (UpperBody mask: spine1→head, omuz→parmaklar, `socket_weapon_r`): **generic durumlar** `Aim`, `ADS` (1-D blend Aim↔ADS, param `ADS01`), `Fire_Override` (yalnız pompalı/sürgülü mekanizma: Shotgun pump, Sniper bolt — tam üst gövde), `ReloadTac`, `ReloadEmpty`, `SwitchIn`, `SwitchOut`, `Lower`, `Raise`, `Inspect` (isteğe bağlı), `Pickup_Upper`, `SwHold`, `SwFire_Override` (Special'lar tam üst gövde), `SwReload`, `Hurt_A`. Dinlenme = `Aim`.
   2 `additive_fire`: `Fire` (mermi başına, 4–8 kare kick), `Recoil` (S-07 yayı üstüne yazılı mikro), `SwFire_Add`.
   3 `additive_flinch`: kollar/silah 80 ms.
   4 `lean`: A2.3.
   5 `aim_layer` (IK pass): spine aim yaw ±35° pitch −40…+30, `CharacterLookAt` (TP'de baş+göğüs hedefe; tepeden yalnız göğüs), sol el IK (A6.3), `WeaponMountAlignment` (order 900) sonrası `TwistCorrector` order 850'de (A6.5).
49. FP viewmodel controller'ı (ayrı Animator; viewmodel = kollar + silah, `FirstPersonView` katmanı, overlay kamera — S-01 anlaşması): 0 `FP_Base` (`fp_idle`, `fp_walk`, `fp_run`, `fp_sprint` 1-D blend Speed01 — **bob için değil**, el/silah pozu için; bob S-07), 1 `fp_override` (`Aim`, `ADS`, `Fire_Override`, `ReloadTac`, `ReloadEmpty`, `SwitchIn/Out`, `Lower/Raise`, `Inspect`, `SwHold/SwFire/SwReload`), 2 `fp_additive` (`Fire`, `Flinch`). FP'de aim IK yok (viewmodel kökü kameraya bağlı; S-01'in `AimPitch` parametresi viewmodel'e **girmez**); sol el IK var. Overlay kamera near/FOV S-01'de; viewmodel dünya ışığını alır (S-05 ile: aynı ışık katmanı, gölge almaz).
50. Kamera modu geçişinde (S-01 Pause'dan): TP/tepeden ↔ FP geçişinde her iki Animator **aynı durum + normalizedTime** ile senkronlanır (`AnimatorStateInfo` kopyası; reload ortasında geçiş cephane sayacını bozmaz çünkü mekanizma olayları her iki klipte de aynı karede yazılır — A8.4 kuralı). FP'de dünya bedeni **ShadowsOnly** (S-01) — gölge locomotion/reload'u göstermeye devam eder (FP'de yerdeki gölge "kendi gövdesi" hissini verir; bu yüzden TP Animator FP'de **çalışmayı sürdürür**, LOD Far'a düşmez). P-06 oyuncu **başını ayrı SkinnedMeshRenderer**'a böler: FP'de baş ShadowsOnly (kamera baş içinde kalmaz), TP'de normal; baş kemik hiyerarşisi aynı, yalnız renderer ayrı (A6.5 export kuralına eklendi).
50a. `AimPitch` (S-01, derece, + yukarı, −60…+60) TP `aim_layer` girdisidir: spine dağılımı A6.3'teki gibi, klip aralığı −40…+30'un dışındaki ±20° kalan pay omuz/boyun additive'ine (`aim_pitch_up/down` additive pozları, 1-D blend). `AimYaw` gelirse ±35° spine, üstü alt gövde dönüşü (A2.2 madde 10); gelmezse yaw bakış-gövde farkından türetilir. Tepeden modda `AimPitch` ≈ 0 sabit.

### A6.2 Per-weapon AnimatorOverrideController ve klip sayımı
51. **Kavrama sınıfı (grip class)** paylaşımı: `rifle` (AR×2, LMG×2 aim pozu farkı LMG'ye kayar → `heavy` ayrı), `smg`, `shotgun_pump`, `heavy` (LMG), `bolt` (Sniper), `launcher`, `special_spray` (gel), `special_cannon` (yıldırım) = 8 sınıf. Sınıf-paylaşımlı slotlar: Aim, ADS, Lower, Raise, SwitchIn, SwitchOut, Inspect (7). Silah-özel slotlar: Fire (add), Fire_Override (varsa), ReloadTac, ReloadEmpty (4). Sayım TP: 8×7 + 12×4 = 56 + 48 = **104**; FP aynı → **208** klip + Special hold/fire/reload (2×3×2 = 12) = **220** silah klibi. (Karakter-animator belgesi klip listesini bu tavana göre budar; ReloadTac/ReloadEmpty ayrımı SMG/AR'de zorunlu, Launcher/Sniper'da tek reload izinli → −8.)
52. AOC her silahta yalnız dolu slotları override eder; boş slot taban (sınıf) klibine düşer. WeaponDefinition v3 (S-02) alanları: `gripClass`, `animSet` (AOC referansı), `reloadTacSeconds/reloadEmptySeconds` (klip süresinden türetilir, veri ile çakışırsa klip kazanır, rate ile eşitlenir ±%10 tavan), `adsSeconds`, `mechanismEvents[]`.

### A6.3 IK: sol el, aim/look, hedef kemikler
53. Sol el IK: `Animator.SetIKPosition/Rotation(LeftHand)` hedef = silah `grip_l` (her silahta zorunlu; Launcher'da omuz kavraması `grip_l` ön tutamaç, Special gel'de hortum kavraması). Ağırlık 1; klip olayları `lh_release` → 80 ms'de 0, `lh_grab` → 80 ms'de 1 (reload/pompa/bolt sırasında el serbest). `WeaponMountAlignment` (900) sağ eli düzelttikten **sonra** sol el IK (OnAnimatorIK Animator içinde koşar → sıralama: IK pass'te `grip_l` konumu bir kare gecikmeli olurdu; çözüm: sol el IK **LateUpdate 910**'da two-bone IK çözücüsüyle (Animation Rigging paketi kurulu değilse elle 2-kemik çözüm, 0 B) — S-06 kararı; Animator IK pass kullanılırsa 1 kare gecikme kabul edilmez (yakın FP'de titreme).
54. Aim IK (TP): spine, spine1, chest dağılımı %25/%35/%40; yaw ±35° (üstü alt gövde döner A2.2), pitch −40…+30; hız 720°/s; ateş sırasında yay S-07'de. Look-at (`CharacterLookAt`): TP'de baş+göğüs hedefe (ağırlık 0,6/0,3), tepeden yalnız göğüs 0,3; FP kapalı; zombide tehdide (oyuncu) baş 0,7 — 12 beden/kare bütçesi (§T).
55. Rig hedef kemikleri (P-06 rig template v2): `ik_hand_l`, `ik_hand_r`, `ik_foot_l`, `ik_foot_r`, `ik_weapon` (kök altında, Generic ekstra; Blender'da IK/constraint hedefi; Unity'de export'ta kalır, Humanoid dışı transform olarak taşınır) — çalışma zamanı IK bunları **okumaz**, yalnız yazarlık için. Silah soketleri (AP-021 P-07): `grip_r` (= `socket_weapon_r` hizası), `grip_l`, `sight_socket`, `muzzle`, `eject`, `mag`, `ads_socket` (kamera hizası), `fp_socket_weapon_r`.

### A6.4 FP kol rig + kamera soketi + prosedürel katman sahipliği
56. FP viewmodel mesh'i: aynı PlayerRig (56 kemik + parmaklar), yalnız üst gövde ağırlıklı; kök `fp_arms_root` → `weapon_spring` (S-07 yay: sway/bob/recoil) → overlay kamera rig'i altında, katman `FirstPersonView` (dünya kamerası bu katmanı çizmez, overlay kamera yalnız bunu çizer — S-01). `fp_camera` kemiği (baş çocuğu) TP bedeninde **konum referansı** olarak durur (kamera modu geçişinde göz hizası); FP'de kamera kemiği izlemez (bob prosedürel; klipteki baş hareketi kamerayı sarsmaz). Silah mesh'i viewmodel'de `fp_socket_weapon_r`'a bağlıdır; aynı silah prefab'ı TP'de `socket_weapon_r`'a (tek varlık, iki soket — AP-021 LOD0 FP kalitesinde).
57. Prosedürel/yazılı ayrımı (S-07 ile anlaşma): **S-07 sahibi**: sway (bakış hızına göre 2 eksen, yay f 4 Hz ζ 0,7), bob (ayak olaylarına kilitli sinüs, `foot_l/r` fazından), recoil kamera+kol yayı (silah verisinden), ADS FOV/pozisyon lerp'i. **Yazılı (P-06)**: mekanizma (sürgü/bolt/pompa/kayış), el kick'i (additive `Fire`, kickin %40'ı), reload, switch, idle nefes. Kural: aynı kanal iki kaynaktan gelmez — kol kökü yalnız yay, kemikler yalnız klip. Recoil tepe: FP AR 1,2° pitch/0,3° yaw kamera + kol kökü 18 mm geri (S-07 kalibre).
58. ADS blend: `ADS01` 0→1 0,18 s (ease-in-out), 1→0 0,14 s; override katmanında `Aim↔ADS` 1-D blend (sınıf klibi); FP'de kol kökü `ads_socket`'i kamera eksenine 0,22 m'de hizalayacak şekilde translate (sight hizası matematiksel, klipten bağımsız → her silahta doğru); TP'de ADS = omuzlama pozu + kamera S-01. Sniper ADS'de kol/silah gizlenir (scope overlay S-09) — klip yine oynar (ses/mekanizma olayları için).

### A6.5 Rig gereksinimleri (P-06 / AP-006 rig template v2) — Humanoid uyumlu
59. **Twist kemikleri**: `upperarm_twist_l/r` (üst kolda omuz→dirsek %50), `lowerarm_twist_l/r` (dirsek→bilek, 2 adet: %40/%80; ön kol candy-wrapper FP'de en görünür yer), `thigh_twist_l/r` (%50). Humanoid avatar bunları eşlemez; **Generic ekstra transform** olarak tutulur ve çalışma zamanında `TwistCorrector` (LateUpdate 850, Animator sonrası, WeaponMountAlignment öncesi) çözer: `twist = swing-twist ayrışımı(child.localRotation)`, twist kemiği `slerp(identity, twist, oran)`. Maliyet ≈ 8 kemik × 0,4 µs = 3 µs/beden; oyuncu + LOD0 zombiler (§T). Skin ağırlıkları twist kemiklerine Blender'da otomatik (P-06 script: dirsek/bilek arası lineer). Blender rig'de `Copy Rotation` twist constraintleri export öncesi **bake** edilir; klip verisi twist kemiklerine de yazılır (Humanoid onları atlar; Generic ekstra eğrileri Unity'de kalır → TwistCorrector klip eğrisi varsa **klip kazanır**, yoksa çözer).
60. **Parmak kemikleri**: tam Humanoid parmak seti (5 parmak × 3 = 15/el); kavrama pozları per grip class (8 poz) kliplerde yazılıdır (Humanoid parmak retarget'i yeterli); tetik parmağı `Fire` additive'te; FP kol mesh'inde parmak ağırlıkları zorunlu (yakın çekim). Parmak kemikleri zombide Humanoid'e eşlenir ama zombi kliplerinde animasyonsuz (Performans kademesinde Animator'da parmak eğrileri kırpılır: import "Optimize Game Objects" zombide açık, oyuncuda kapalı).
61. **İkincil zincirler**: `sec_*` korunur (oyuncu 7, zombi 17), `SecondaryChainDriver` verlet; ragdoll'da zincirler ölümde 0,4 s daha verlet ile sürer, sonra donar (Yüksek+), Performans'ta ragdoll_on'da donar. FP kol mesh'inde `sec_*` yok.
62. **Sol/sağ el roll**: Humanoid roll kaybı → `grip_r` hizası `WeaponMountAlignment` ile sürüyor; v3'te P-06 kliplerde elin roll'unu `ik_hand_r` kemiğine de yazar (Generic ekstra) → `WeaponMountAlignment` klip eğrisi mevcutsa **onu** okur (kick delta eski yolla). Kademe ayrımı yok.
63. Export kuralı: 30 fps, Humanoid + "ekstra transformlar" listesi (`sec_*`, `*_twist_*`, `ik_*`, `socket_*`, `fp_camera`) avatar maskesinde **Transform** olarak işaretli; kök hareketi kliplerde **yok** (hips XZ yerinde, yalnız Y); ragdoll onset klipleri `ragdoll_on` karesinden sonra 0,45 s daha poz taşır (powered hedef). Oyuncu mesh'i P-06'da **iki SkinnedMeshRenderer**: `body` ve `head` (aynı kemik dizisi, aynı rootBone; FP'de `head` ShadowsOnly — S-01/C-00 kararı). FP viewmodel üçüncü renderer (`fp_arms`), `FirstPersonView` katmanı; DEC-093 5 GB tavanında klip verisi: ≈ 260 klip × ort 60 kare × Humanoid ≈ 25–35 MB (sıkıştırma Optimal) — boyut riski yok.

---

## A8 · Olay sözleşmesi v3

### A8.1 Mevcut 28 ad (değişmez, anlamı korunur)
64. `fire, hit_window_start, hit_window_end, foot_l, foot_r, death_end, rise_end, pickup, breath, hit, stagger_end, shift_l, shift_r, eject, chamber, mag_out, mag_in, mag_in_2, mag_in_3, switch_ready, switch_away, lowered, raised, pickup_grab, pickup_done, armor_break, spawn_rise, spawn_land` (CharacterAnimationEvents). Mekanizma (WeaponEvents.Mechanism, S-02 ile anlaşılan): `charge, box_open, belt_seat, box_close, bolt_up, bolt_back, bolt_fwd, bolt_down, shell_in(+index), rocket_in, rocket_seat, cell_out, cell_in, canister_out, canister_in`.

### A8.2 Yeni adlar (v3) — ad · payload · anlam · tüketici
65. Locomotion:
   - `foot_l_off` / `foot_r_off` · — · ayak ucu zeminden kalkar (ayak kilidi kapanır) · S-06 (FootLock), S-08 (yok).
   - `pivot_plant` · — · pivot ayağı basar (kilit + toz + ses) · S-06, S-07 (toz), S-08 (kaydırma).
   - `stop_plant` · — · dur klibinde son plant · S-06, S-08.
   - `weight_shift` · `side:l|r` · yerinde dönüş ağırlık aktarımı · S-08 (kumaş/zırh).
66. Saldırı (5 saldırı; S-03 WP-076):
   - `attack_recover` · — · aktif pencere bitti, kurtarma başladı; S-03 bu kareden itibaren iptal/yeniden hedef alabilir · S-03.
   - `grab_start` / `grab_end` · — · kavrama saldırısında oyuncuyu tutma aralığı (oyuncu hareketi kısıtlı; S-03 kural) · S-03, S-07.
   - `attack_step` · — · saldırı içindeki adım (lunge ilerlemesi kodla; bu olay adım anını verir) · S-06 (kod yer değiştirme başlangıcı), S-08.
   - `roar` · — · Ultra/Elite bağırma anı · S-08, S-07 (nefes VFX).
67. Hit/ölüm:
   - `ragdoll_on` · — · yazılı → powered ragdoll devri karesi · S-06.
   - `knockback_end` · — · knockback klibi bitti, locomotion serbest · S-06/S-03.
   - `hurt_end` · — · oyuncu hurt katmanı serbest · S-06.
   - `impact_land` · `part:hips|chest|head` · beden yere çarptı (yazılı düşüşte klipten, ragdoll'da sentetik) · S-07 (toz), S-08 (gövde düşüşü).
   - `blood_burst` · `region` · yönlü hit klibinde kan/etkinin tepe karesi (isteğe bağlı; yoksa `hit` kullanılır) · S-07.
68. Silah/FP:
   - `lh_release` / `lh_grab` · — · sol el IK kapat/aç · S-06.
   - `ads_ready` · — · ADS klibi hizada (nişan dağılımı daralır) · S-02.
   - `reload_commit` · `count?` · cephane sayacının güncellendiği kare (mag_in ile aynı olabilir; shell_in'de her biri) · S-02.
   - `reload_cancel_point` · — · bu kareden sonra iptal edilirse mermi sayılır (öncesinde sayılmaz) · S-02.
   - `mechanism_start` / `mechanism_end` · `kind` · sürgü/bolt/pompa hareket aralığı (ses "yol" katmanı) · S-08.
   - `spin_up` / `spin_down` · — · LMG/Special kanon dönüşü (varsa) · S-08, S-07.
   - `sw_charge_peak` · — · Special şarj tepe karesi · S-07, S-08.
   - `shell_eject` · `index?` · pompalı/tek mermi ejeksiyonu (eject'ten farkı: kabuk çıkışı ile mekanizma ayrı olabilir) · S-07.
   - `inspect_peak` · — · inspect klibinde en görünür kare (isteğe bağlı) · —.
69. Sentetik (klipten değil koddan, aynı sözleşmeyle yükseltilir; tüketici ayırt etmez, `Synthetic=true` bayrağı): `impact_land` (ragdoll çarpması), `hit_window_end` (onset'te kapama), `foot_l/r` (Performans kademesinde Far LOD 15 Hz'de kaçan olaylar kadans fazından üretilir — foley kesilmez).

### A8.3 Payload ve semantik kuralları
70. Payload `AnimationEvent.stringParameter` = ad, `intParameter` = index/side (0 = yok), `floatParameter` = ağırlık/oran (isteğe bağlı); yönetilen ayırma yok (adlar tablo indeksine `CharacterAnimationEvents` gibi çözülür). Aynı ad aynı klipte tekrar edebilir yalnız `index`/`side` ile (`shell_in` 1…8, `mag_in_2/3` mevcut istisna korunur).
71. Tüketici sıralaması aynı karede: S-06 durum (`hit_window`, `ragdoll_on`, `lh_*`) → S-02 sayım (`reload_commit`, `ads_ready`) → S-03 karar (`attack_recover`, `grab_*`) → S-07/S-08 kozmetik. Kozmetik tüketici bir olayı düşürebilir (LOD/uzaklık), oynanış tüketicisi düşüremez (`EnemyCombatEvents` `AnimationDriven` zamanlayıcı yedeği sürer).

### A8.4 Zamanlama kuralları (P-06 için doğrulanabilir)
72. `hit_window_start→end` **≥ 3 kare (100 ms) ve ≤ 8 kare**; pencere klibin ilk 6 karesinde açılamaz (telegraph okunur); `attack_recover` pencere kapanışından ≥ 2 kare sonra; 5 saldırının her birinde tam olarak bir pencere (grab'da `grab_start` pencere yerine geçmez; pencere yine var).
73. `foot_l/r` **plant karesinde** (ayak dünya hızı minimumda, yükseklik ≤ 2 cm); `foot_*_off` ayak ucu 2 cm'yi aşınca; döngüde her ayak için tam 1 plant + 1 off; plant→off ≥ 4 kare (kilit için). Doğrulama: manifest oluşturucu ayak hız eğrisinden otomatik kontrol (fark > 1 kare → RET).
74. `ragdoll_on` onset kliplerinde 4–14 kare arasında; sonrasında klip ≥ 14 kare daha sürer (powered hedef). `death_end` yalnız tam düşüş kliplerinde (son kare). `impact_land` yazılı düşüşte kalça yere değince.
75. Mekanizma/reload: `mag_out` → `mag_in`/`reload_commit` → `chamber` sırası bozulmaz; `reload_cancel_point` `reload_commit`'ten ≥ 1 kare sonra; `lh_release`, `mag_out`'tan ≥ 2 kare önce; `lh_grab`, `chamber`'dan sonra ≤ 4 kare. FP ve TP eşleniği aynı `action`'da mekanizma olayları **aynı karede** (A6.1 senkron gereksinimi; tolerans 0).
76. 0. karede yalnız `fire`, `hit`, `breath`, `spawn_rise`, `foot_*` (döngü fazı) olabilir; aynı ad 2 kare içinde tekrar edemez (`shell_in` hariç). `hit` her hit/hurt/ölüm klibinde 0. karede zorunlu (mevcut).
77. Ses/VFX önerimi zamanlaması: `fire` klip 0. karesi = mermi çıkışı (S-02 kod tetikler; klip olay değil senkron kanıtıdır); `eject` ≥ 1 kare sonra; kamera darbesi S-07 aynı karede.

### A8.5 Klip adlandırma
78. `AP022_<target>_<name>`; `target` ∈ `p` (oyuncu TP tam beden), `fp` (FP kollar), `z` (zombi), `w` (silah mesh'inin kendi mekanizma klibi, varsa: `AP022_w_<weaponId>_<action>`). Silah aksiyonları: `AP022_p_<weaponId|gripClass>_<action>` ve `AP022_fp_<weaponId|gripClass>_<action>`; `action` ∈ `aim, ads, fire, fire_override, reload_tac, reload_empty, switch_in, switch_out, lower, raise, inspect, sw_hold, sw_fire, sw_reload`. Zombi: `AP022_z_<name>[_<tier>][_<var>]`, tier ∈ `std, elite, ultra, special`, var ∈ `A, B, C`; ölüm: `AP022_z_death_<family>[_full]`. Locomotion: `AP022_p_walk_slow`, `AP022_z_shamble_fast_B`… Küçük harf snake_case, ASCII, ≤ 48 karakter; manifest `events` alanı klip başına, `spd` locomotion'da zorunlu, `layer`/`mask` alanları mevcut şema.

---

## §T · Kalite kademeleri (S-05 WP-075 kademe sözleşmesine bağlanır)

| Özellik | Sinematik | Yüksek | Performans | Tahmini ms (Sin/Yük/Perf) |
|---|---|---|---|---|
| Foot IK beden/kare (2 ışın + kilit) | 24 | 12 (mevcut) | 4 (yalnız oyuncu + 3) | 0,45 / 0,22 / 0,08 |
| Ayak kilidi | oyuncu + LOD0 zombi | oyuncu + ≤ 8 zombi | yalnız oyuncu | IK'ye dâhil |
| İkincil zincir beden/kare | 60 | 40 (mevcut) | 16 | 0,50 / 0,34 / 0,14 |
| Aktif ragdoll tavanı | 12 (13 beden) | 6 (11 beden) | 2 (11 beden) | 1,6 / 0,8 / 0,25 |
| Ceset tavanı / ömür (s) | 16 / 6 | 10 / 4 | 6 / 2,5 | 0,3 / 0,2 / 0,1 |
| Ceset–ceset çarpışması | ✔ | ✔ (1 s statik) | ✘ | dâhil |
| Twist çözümü (8 kemik) | oyuncu + LOD0+LOD1 | oyuncu + LOD0 | yalnız oyuncu | 0,12 / 0,06 / 0,01 |
| Look-at beden/kare | 20 | 12 | 4 | 0,10 / 0,06 / 0,02 |
| Lean additive | herkes | herkes | oyuncu + LOD0 | ≤ 0,05 |
| Posture additive (zombi çeşitlilik) | herkes | herkes | herkes | Animator içinde |
| Yerinde dönüş / pivot klipleri | ✔ | ✔ | ✘ (slerp) | 0 |
| Animator LOD: tam kare mesafesi (TP/FP) | ≤ 30 m | ≤ 22 m | ≤ 14 m | — |
| Animator Far (15 Hz) mesafesi | > 30 m | > 22 m | > 14 m | — |
| Animator Far (tepeden, LOD kuralı) | LOD ≥ 3 | LOD ≥ 2 (mevcut) | LOD ≥ 1 | — |
| Parmak eğrileri zombide | ✔ | ✘ | ✘ | ≈ 0,15 / 0 / 0 |
| Toplam animasyon+ragdoll (40 zombi, tahmin) | ≈ 3,4 | ≈ 1,9 | ≈ 0,7 | S-06 ölçer |

79. Kademe değişimi çalışma sırasında (Pause menüsü) anında; aktif ragdollar bitene kadar eski tavan geçerli. Her satır `QualityTierSettings` (S-05 sözleşmesi) alanına bağlanır; S-06 kapı raporunda satır başına ölçülen ms. **DEC-092 bütçe payı:** Yüksek 16,6 ms içinde animasyon+ragdoll+IK için ayrılan pay **≤ 2,4 ms** (tablo Yüksek sütunu ≈ 1,9 + FP viewmodel Animator ≈ 0,15 + ShadowsOnly beden 0), p99 kare 20 ms'de ragdoll fizik adımı ≤ 2 adım; Sinematik sütunu 60 fps'i hedeflemez (kullanıcı tercihi, ≈ 3,4 ms). Performans sütunu eski DEC-088 8,0 ms çerçevesine de sığar.

## §R · Risk listesi

80. R1 **Perf (DEC-092 60 fps)**: Yüksek sütunu ≈ 1,9 ms tahmindir; 40 zombi + 6 ragdoll + Dünya v5 + atmosfer v3 aynı karede ölçülmedi. Azaltım: kademe anahtarları gün 1'den; S-06 kapıda satır başına ms; pay 2,4 ms aşılırsa sıra: ragdoll 6→4, foot IK 12→8, look-at 12→8, twist LOD0→yalnız oyuncu. Performans kademesi bile 4 ölüm ailesini (tam klip) taşır.
80a. R1b **FP ShadowsOnly beden + viewmodel çifte Animator**: FP'de iki Animator (beden + viewmodel) koşar; beden Far LOD'a düşmez (gölge). Maliyet ≈ +0,15 ms; kabul. Baş ayrı renderer → draw call +1, gölge geçişinde +1; kabul (DEC-092 içinde).
81. R2 **Powered ragdoll kararlılığı**: SLERP drive + limitler PhysX'te titreşebilir (özellikle Ultra kütlede). Azaltım: drive tavanı 900, τ ≥ 0,10, damper ≥ 8; ilk hafta 3 tier'de 200 ölüm soak (NaN/uçma/yerden geçme sayacı = 0).
82. R3 **Havuz sızıntısı**: rigidbody/eklem durumu yeniden kullanımda temizlenmezse ikinci ölümde fırlama. Azaltım: `RagdollReset` + 200 döngü testi + 0 B ölçümü.
83. R4 **Ayak kilidi + Humanoid retarget**: kilit IK bacak boyunu aşarsa "kopma"; %96 gerilme tavanı ve 60 ms çözülme; tepeden modda tolerans yüksek olduğu için kilit uzak zombide kapalı.
84. R5 **Twist kemikleri Humanoid ile çakışma**: Unity Humanoid ekstra transformları bazen avatar maskesinde kaybeder; export sonrası import ayarı "Extra Transforms to Expose" listesi P-06 kontrol listesine girer; ArtAnimatorBuilder doğrular (yoksa RET).
85. R6 **Klip hacmi (220 silah klibi + ~40 locomotion/ölüm/saldırı)**: P-06 kapasitesi. Azaltım: grip class paylaşımı (A6.2), TP↔FP aynı sahneden çift kamera render/export (tek yazarlık), ReloadTac/Empty ayrımı yalnız 6 silahta.
86. R7 **Kayma politikası okunabilirliği**: kullanıcı kaymayı kabul etti ama TP yakın çekimde %20 üstü fark edilir; band + kilit + "shamble_fast" klibi üçlüsü; AD/kullanıcı turunda TP 3 m mesafede kayıt zorunlu kanıt.
87. R8 **Ceset–NavMesh**: cesetler engel değil; kalabalık ceset üstünden yürür (Yüksek'te 1 s statik kapsül yalnız ragdoll için). Kabul edilen görsel uzlaşı; R2'de "ceset üzerinden adım" IK isteği gelebilir.
88. R9 **FP/TP Animator senkronu**: mekanizma olayları farklı karede olursa cephane sayımı kamera geçişinde ikilenir/kaybolur; A8.4 tolerans 0 + manifest testi (fp/tp çiftlerinde olay kare eşitliği).
89. R10 **DEC-029 penceresi ve reaksiyon**: pencere içinde override reaksiyon açılırsa saldırı görsel olarak kesilir ama hitbox açık kalır → "havadan vuruş". A5.4 kuralı: pencere içinde yalnız additive; test: pencere içinde 10 hit → `hit_window_end` klipten gelir, override ağırlığı 0.
90. R11 **Fixed timestep / 60 fps**: 50 Hz fizik + 60 fps → 1–2 adım/kare dalgalanması ragdoll akıcılığını bozabilir; `Rigidbody.interpolation = Interpolate` yalnız aktif ragdollarda (donunca None).
91. R12 **Tepeden okunabilirlik**: 90 px boyda ayak/parmak/twist görünmez; bütçe orada gövde eğimi, posture, düşüş yönü ve knockback yer değiştirmesine gider — kademe tablosu tepeden modda IK/twist'i otomatik kısar (A2.1 madde 4).

## Önerilen paket sırası (S-06 için)
92. (1) Olay sözleşmesi v3 + manifest doğrulayıcı (A8) → P-06 üretime başlar. (2) Locomotion band + walk_slow/shamble_fast halkaları + ayak kilidi (A2). (3) Ragdoll boru hattı + 4 aile + kap/fallback (A3) — S-03 ile onset kancaları. (4) Hit reaction katmanları + akümülatör + knockback (A5). (5) Silah taban controller + AOC + sol el IK + TwistCorrector (A6/A7) — S-02 WeaponDefinition v3 ve S-01 CameraMode API geldikçe. (6) Kademe bağlama + ms raporu (§T).
