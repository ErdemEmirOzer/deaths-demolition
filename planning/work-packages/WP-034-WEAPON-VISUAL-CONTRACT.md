# WP-034 — Klasik Silah Görsel / Soket / Efekt / Ses Sözleşmesi (AP-013 için)

**Durum:** Teknik sözleşme (S-02, 2026-09-16, hızlı hat). Silah listesi kararı kullanıcıda (`WP-024-WEAPON-LIST-PROPOSAL.md`); bu belge **veri-güdümlü** roster'ın (`Assets/_Prototype/Weapons/Roster/Resources/WeaponRoster/*.asset`) AP-013 tarafındaki karşılığıdır. Karar gelince yalnız roster verisi ve bu tablonun satırları değişir; kod değişmez. Sayısal denge değerleri (hasar/tempo/menzil) burada yok — hepsi `PrototypeConfig.Weapons` (PrototypeOnly).

## 1. Roster (varsayılan = öneri (a)/(a)/3 silah)

| Key | Tür sınıfı | Runtime id | Etkin | Siluet sınıfı | Atış modeli | Çekirdek dallar (eklenti soketi sırası) | Açılış | Ses (AudioSoundId) | HUD ikon |
|---|---|---|---|---|---|---|---|---|---|
| A | Tüfek tipi | `AssaultRifle` | evet | **Long** | hitscan | Piercing · FireRate · ReloadSpeed | varsayılan (DEC-037/039) | `RifleFire` | `weapon_rifle` |
| B | Karabina tipi | `AutoSmg` | evet | **Medium** | hitscan | Chain · Ricochet · FireRate | profil seviyesi (config `UnlockProfileLevel`) | `SmgFire` | `weapon_carbine` |
| C | Pompalı / geniş ağızlı | `CombatShotgun` | evet | **Short** (kalın) | pellet | Explosion · Piercing · ReloadSpeed | profil seviyesi | `ShotgunFire` | `weapon_shotgun` |
| D | Ağır tek atış tabanca | — (yok) | **hayır** (veri) | Short (küçük) | hitscan | Explosion · Ricochet · Piercing | profil seviyesi | `HeavyFire` | `weapon_heavypistol` |

İş sırası (AP-013): **A → C → B (→ D yalnız kullanıcı D'yi etkinleştirirse)**. D için runtime id / config girdisi yoktur; etkinleştirme = kullanıcı kararı + `ClassicWeaponId` ekleme + `PrototypeConfig.Weapons` girdisi (ayrı paket).

## 2. Model paketi (silah başına)

- **1 taban model + 3 dal eklentisi** (soket tabanlı; eklenti = çekirdek dal görseli, kademe farkı **malzeme/emissive** ile — kademe başına ayrı mesh YOK).
- Tri tavanı (taban model): **LOD0 ≤ 3 000 · LOD1 ≤ 1 200 · LOD2 ≤ 400**; eklenti başına LOD0 ≤ 400. Doku: **1K** taban (BaseColor + Normal + MRA), eklentiler tabanın atlasını paylaşır; emissive maskesi kademe için ayrı 512.
- Siluet sınıfı, kamera mesafesinde (yükseltilmiş kamera, REQ-105) ayrışmanın ölçüsüdür: Long (A) referans yatay çizgi; Medium (B) belirgin kısa kundak/kabza; Short-kalın (C) genişlik/uzunluk oranıyla ayrık; Short-küçük (D) tek elle tutuş + belirgin namlu alevi/tracer ile telafi (küçük siluet riski kullanıcı sorusu).
- **Altın bandı yasak** (gold pickup rengi rezervi, AP-010 `colour_reserve_checks`); DEC-013 erken dönem ateşli silah dili (ahşap kundak, demir/çelik namlu, fitil/kibrit kilit hissi), marka/lore yok.

## 3. Soketler (isimler `ClassicWeaponDefinition` alanlarıyla birebir)

| Soket | Nerede | Anlam |
|---|---|---|
| `socket_weapon_r` | AP-006 `PlayerRig` (sağ el, 1,37 m) | Silah kökü buraya parent edilir (`PlayerRigFactory` mevcut davranış). Silah kökü: +Z namlu yönü, +Y yukarı, orijin kabza-tetik noktası. |
| `Muzzle` | silah modeli çocuğu | Namlu ucu: muzzle flash + tracer başlangıcı (`WeaponVisualRig.Muzzle` işaretçisi bu adı arar). |
| `Ejection` | silah modeli çocuğu | Kovan atma noktası (+X sağa doğru yön). |
| `attach_barrel` / `attach_top` / `attach_stock` | silah modeli çocukları | Üç çekirdek dal eklentisinin bağlanma noktaları, **roster'daki dal sırasıyla** (A: Piercing→barrel, FireRate→top, ReloadSpeed→stock; B: Chain→barrel, Ricochet→top, FireRate→stock; C: Explosion→barrel, Piercing→top, ReloadSpeed→stock). |

Eksen sözleşmesi Import Contract v1.1 ile aynı (FBX embed kapalı, ayrı PNG, +Z ileri; `bakeAxisConversion` dersi WP-011).

## 4. Efekt ve ses bağlama

- VFX: muzzle flash = AP-010 `muzzle` ailesi (`MuzzleVfxFamily`), vuruş = `impact` ailesi (`ImpactVfxFamily`; yüzey türü `SurfaceTag` ile seçilir, silah seçmez); patlama dalı `explosion` ailesi. Silah başına farklı flash ölçeği `WeaponPower` ile (mevcut `HitFeedbackDirector.ReportMuzzleFlash`), yeni aile yok.
- SFX: `FireSfx` (`RifleFire`/`SmgFire`/`ShotgunFire`/`HeavyFire`), `ReloadSfx` = `ReloadStart` seti (S-08 matrisi `AudioEventMap` satırları; `CombatAudioEvent.ClassicWeaponFire` olayı silah kimliğine göre ses seçer — S-08 tarafında `AudioSoundId` eşlemesi).
- HUD: `HudIconId` S-09 sprite anahtarı (WP-022); ikon siluet sınıfını taşımalı.

## 5. Dal / pellet sözleşmesi (ADR-034-1, testle sabit)

| Dal | Çok saçmalı silahta çözümleme |
|---|---|
| Piercing | **pellet başına** (istenen çarpan) |
| Explosion | **pellet-normalize** (şans / pellet sayısı; WP-004'ten beri) |
| Chain, Ricochet | **atış başına** (yalnız 0. pellet taşır) — pompalıya çekirdek dal olarak verilmez (`Validate` reddeder) |
| FireRate, ReloadSpeed, oyuncu dalları | pellet'e dokunmaz |

Dükkân kapısı: silah takılıyken yalnız o silahın 3 çekirdek dalı + 4 ortak oyuncu dalı teklif edilir (`WeaponRoster.EnforceCoreBranchSets`, veri anahtarı; kapalıyken WP-034 öncesi davranış).

## 6. Teslim listesi (AP-013 → WP-035 import)

Silah başına: `AP013_<key>_<tür>_base.fbx/.glb` + 3 eklenti `AP013_<key>_attach_<barrel|top|stock>.fbx/.glb` + dokular (1K BaseColor/Normal/MRA + 512 emissive maske) + `AP013-manifest.json` (soket adları, tri sayıları, doku listesi, HF kredi satırı). Roundtrip: soketler Unity'de `Muzzle`/`Ejection`/`attach_*` çocuk transformları olarak gelmeli (isim birebir).

## 7. ADR-042-1 · WeaponSocket sözleşmesi (WP-042, 2026-09-17) — §3'ün ölçülü hali

`Core/Weapons/WeaponSocketContract.cs` tek kaynak; testle sabit (`WeaponFeelTests.SoketSozlesmesi_*`, `WeaponBlockoutGateTests`). Kaynak ölçüler AP-009C v003 README "AP-013 blockout contract" (P-06 elleri bu işaretlere çözdü):

| Silah | Uzunluk | `Muzzle` (x, y, z) | `marker_foregrip` | `marker_mag` | Not |
|---|---|---|---|---|---|
| A tüfek (`AssaultRifle`, Long) | 1,20 m | (0, 0,065, 0,86) | (0, −0,01, 0,27) | (0, −0,11, 0,10) | stok omuzda, gövde −24° |
| B karabina (`AutoSmg`, Medium) | 0,85 m | (0, 0,06, 0,56) | (0, −0,01, 0,22) | (0, −0,13, 0,06) | göğse yakın |
| C pompalı (`CombatShotgun`, Short) | 0,78 m | (0, 0,07, 0,52) | (0, 0, 0,20) (pump; geri 0,11) | (0, −0,05, 0,05) = fişek portu | kol altı, göğüs hizası |

Değerler blockout FBX'lerinden okundu (README yalnız z'yi verir; namlu kabzanın 6–7 cm üstündedir).

Kurallar: kök = kabza/tetik (`marker_grip` = orijin), +Z namlu, +Y yukarı, +X sağ; kök `socket_weapon_r` altında identity. Zorunlu çocuklar `Muzzle`, `Ejection` (+X), `attach_barrel/top/stock`; ölçü işaretleri `marker_grip/foregrip/mag`. **Tolerans ±3 cm** (`MarkerToleranceMetres`): aşımda klipler yeniden çözülür (P-06, tek build koşusu). LOD0–2 (tri tavanı §2: 3 000 / 1 200 / 400; eklenti LOD0 ≤ 400), el modelinde collider yok (karakter collider'ı vuruş hacmi), rig yok (rijit). Yer tutucu: `Weapons/Roster/Resources/WeaponBlockout/AP013_<A|B|C>_*_blockout.fbx` (AP-009C props, 104–148 tri) `ClassicWeaponDefinition.ModelResource` ile `socket_weapon_r`'a takılır; AP-013 nihai modeli aynı alanı değiştirerek gelir (soket adları/ölçüler değişmez → kod değişmez).

Mekanizma olay slotları (WP-042 C): `WeaponEvents.Mechanism` (`WeaponMechanismEvent`: kovan `eject`, şarjör `mag_out`, fişek `mag_in`, sürgü/pump `chamber`; konum/yön = soket dünya çerçevesi; `FromClipEvent` klip olayından mı zamanlayıcı yedeğinden mi) — S-07 `AP010B_mag_drop`/`spark`, S-08 SFX bu slotlardan okur; klip olayları (AP-009C `new_events`) `CharacterAnimationEvents` enum'una S-06 (WP-032C) ekleyince ada göre çözülür (`SpecialWeaponUsage.ClipEventNames`), o güne kadar denetleyici zamanlayıcı yedeği (A/B: kovan atışta, şarjör reload başında +0,4 s; C: kovan atış +0,4 s, fişek 14/30/46 f, pump 60 f).
