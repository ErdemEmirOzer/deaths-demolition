# EVENT COVERAGE MATRIX — ses (S-08 / WP-021) + VFX (S-07 / WP-019)

> VFX sütunu (S-07, WP-019/WP-019b, 2026-09-17): `AP-010 v003` HQ geçişi + `AP-010B v003` kan seti/wave-2 Unity'de (`DD_VfxCatalog`); "katalogda … WP-047" = varlık içeride, olay kancası WP-047'de (A kancalar, B silah, C kırılabilir, D dünya); `n/a — gerekçe` yalnız gerekçeyle. Boş VFX hücresi = DoD FAIL (`ArtBloodTests.OlayKapsamMatrisi_BosSatirYok`). Kan sistemi ve wave-2 durumu: bkz. planning/04 WP-019 (I)/(K).

> Kullanıcı direktifi (2026-09-16, §12): "Her şeyin kendine ait sesi ve efekti olacak; hiçbir detay atlanmasın." Ortak dosya: S-08 ses sütununu, S-07 VFX sütununu doldurur (satır çakışmasında ikinci biten rebase'de birleştirir). **Boş ses satırı = WP-021 DoD FAIL.**
>
> Ses sütunu okunuşu: `SFX dosya` = AP-011 klip kökü (`Assets/_Art/Audio/AP011/<kök>_nn.wav`) ya da `proc:<AudioSoundId>` (WP-002 prosedürel recipe, katalogda üretilmiş klip yok) ya da `paylaşımlı:<kök>` (başka satırın klibini mix-plan farkıyla kullanır). `Kaynak` = olayın koddaki tetiği. Durum: **Ü** üretildi (AP-011 v003), **P** prosedürel, **R** rezerve (tetik henüz yok). WP-021B: WP-018 (ADR-018-1) main'e geldi → ayak/nefes/ölüm-sonu satırları `CharacterAnimationEvents.Any` ile bağlandı (R kalktı). Kredi kalanı yetmeyen satırlar **P** ile sevk edilir ve "AP-011 v4 kuyruğu" bölümünde listelenir (kredi kararı kullanıcıda). **WP-021C:** **T** = türetilmiş (AP-011 v005/v006/v007: mevcut 48k/24-bit kaynaklardan ofline katman/pitch/stretch, kredi 0; `EVENT-COVERAGE-MATRIX-UI.md` satırları §8'e taşındı, olay adları sabit); zemin: AP-005 placement aileleri sahne yüklenince `SurfaceTag` alır (`DD.Art.Ap005SurfaceTags`, ADR-021-1 v2), avlu zemini taş rotalarda taşa iyileşir.

## 1. Oyuncu

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Idle ağırlık kayması (shift_l/shift_r, WP-032B) | `anim.shift_l/shift_r` → yüzeye göre ayak sesi yarım kazanç (WP-048B) | paylaşımlı:Footstep* / ZombieStep* | 3D | SFX-Player/World | Ü | |
| Ayak sesi — sığ su / derin su / çakıl / kaldırım (AP-015P plakaları) | `anim.foot_l/foot_r` + `Ap005SurfaceTags` aile (WP-038 plakaları gelince; şimdi R) | FootstepWaterShallow / FootstepWaterDeep / FootstepGravel / FootstepPavement (×3, v007 T) | 3D | SFX-Player | T/R | |
| Silah foley A/B/C — eject / chamber / mag_out / mag_in(+2/3) / switch_ready+away / lowered / raised | WP-032C klip olayları oyuncu rig'inde → `AudioEventMap.WeaponFoley(WeaponRegistry.SelectedWeapon, olay)` (WP-048B) | Weapon{A,B,C}{Eject,Chamber,MagOut,MagIn,Switch,Lowered,Raised} (×2, v007 T; A tüfek, B carbine, C pompalı ayrışması) | 2D | SFX-Player | T | |
| Silah/Special alma — kavrama / yerine oturma | `anim.pickup_grab` / `anim.pickup_done` (WP-032C; WP-048B bağlı) | PickupGrab / PickupDone (×2, v007 T) | 2D | SFX-Player | T | |
| Ayak sesi — taş | `anim.foot_l/foot_r` + aşağı ışın → `AudioSurfaceHints` → `SurfaceResolver` (WP-021C: quiet_paving/ring/gate/shore_path/kaya/fener + taş rota şeridi; etiketsiz = taş) | FootstepStone (×4) | 3D | SFX-Player | Ü | `dust_step` (katalogda, stone tint) — bağlama `anim.foot_l/foot_r` WP-047 A (S-06 ayak olayı) |
| Ayak sesi — ahşap (veranda/köprü) | `anim.foot_l/foot_r` + `SurfaceKind.Wood` | FootstepWood (×3) | 3D | SFX-Player | Ü | `dust_step` wood_tatami tint — WP-047 A |
| Ayak sesi — toprak/çakıl | `anim.foot_l/foot_r` + `SurfaceKind.Dirt` | FootstepDirt (×4) | 3D | SFX-Player | Ü | `dust_step` earth tint — WP-047 A |
| Ayak sesi — çimen/bitki | `anim.foot_l/foot_r` + `SurfaceKind.Foliage` | FootstepFoliage (×4) | 3D | SFX-Player | Ü | n/a — yaprak/çimen adım tozu okunmaz (20 m kamera); `dust_step` earth tint düşük yoğunluk WP-047 A |
| Ayak sesi — su kıyısı/sığ su | `anim.foot_l/foot_r` + `SurfaceKind.Water` | FootstepWater (×4) | 3D | SFX-Player | Ü | `water_splash_ring` küçük (0.4 m) — WP-047 A/D |
| Koşu / strafe (kumaş + teçhizat hışırtısı) | `anim.foot_l/foot_r` + `Motor.Speed01 > 0,7` → +2,4 dB | aynı yüzey klibi (GearRustle katmanı v4 kuyruğu) | 3D | SFX-Player | Ü | n/a — görsel yok (kumaş hışırtısı ses); koşu tozu = adım satırları |
| Ateş — SMG (Light) | `CombatAudioEvent.ClassicWeaponFire` intensity ≤ 0,87 | SmgFire (×5, v006 B Carbine) | 3D | SFX-Player | Ü/T | `fb_muzzle` v003 × PowerScale(Light) + tracer (`LinkEffect`) — AP-010 v003 / WP-002; A/B/C varyantı WP-047 B |
| Ateş — tüfek (Medium) | `ClassicWeaponFire` 0,87–1,25 | RifleFire (mekanik + gövde + avlu kuyruğu) | 3D | SFX-Player | Ü | `fb_muzzle` v003 × PowerScale(Medium) + tracer — AP-010 v003 / WP-002 |
| Ateş — pompalı (Heavy) | `ClassicWeaponFire` ≥ 1,25 | ShotgunFire | 3D | SFX-Player | Ü | `fb_muzzle` v003 × PowerScale(Heavy) + saçma başına tracer — AP-010 v003; pump dumanı WP-047 B |
| Ateş — ağır döner top (Special) | `HeavyWeaponFire` | HeavyFire (×4) | 3D | SFX-Player | Ü | `fb_muzzle` v003 × `_specialFlashMultiplier` (Kagura hariç) — AP-010 v003; `barrel_heat` katalogda (ısı sürücüsü Weapons, WP-047 B) |
| Boş tetik | `ClassicWeaponDryFire` | DryFire | 2D | SFX-Player | Ü | n/a — görsel yok; HUD şarjör çipi (S-09) |
| Şarjör çıkar / tak / kurma | `ReloadStart` / `ReloadInsert` / `ReloadEnd` | ReloadStart / ReloadInsert / ReloadEnd | 2D | SFX-Player | Ü | `mag_drop` + `mag_drop_dust` katalogda — bağlama `ReloadStarted` + el noktası WP-047 B |
| İsabet onayı / öldürme onayı | `HitMarker` / `KillMarker` | HitMarker (×6, v006) / KillMarker (×3, v006 T) | 2D | UI | Ü/T | `HitMarkerLayer` (tier renkli) / kill varyantı — WP-002 |
| Hasar alma (hafif/ağır) | `PlayerHealth.AnyDamaged` (şiddet → gain) | PlayerDamage (×3) | 2D | UI | Ü | `vignette_damage` v003 parlama 0.35 (4 f / 12 f) × Vermilion600 (`DamageVignetteLayer`) — AP-010 v003; düşük can nabız 0.35 |
| Ölüm — çökme | `PlayerHealth.AnyPlayerDied` | PlayerDeath | 2D | UI | Ü | `player_death_overlay` maskesi 2 s kararma 0.6 (`DamageVignetteLayer`, `AnyPlayerDied`) — AP-010B v003; doygunluk düşürme post-fx devri |
| Ölüm — koşu sonu vurgusu | `RunAudioEvent.RunEnded` | paylaşımlı:PlayerDeath (stinger) | 2D | UI | Ü | n/a — sonuç ekranı geçişi (S-09) |
| Special alma | `SpecialWeaponPickup` | SwPickup | 2D | UI | Ü | `weapon_pickup_flash` 1.2 m oyuncu ayağında (`SpecialWeaponPickedUp`) + beacon söner — AP-010B v003 |
| Special kullanma — döner top motoru | `HeavyWeaponSpinUp` (loop) / `HeavyWeaponSpinDown` | HeavyMotorLoop (v006 loop) / HeavySpinDown (v006) | 2D | SFX-Player | Ü/T | `barrel_heat` (+distort) katalogda — ısı 0→1 sürücüsü Weapons, WP-047 B |
| Special kullanma — silah başına (Kagura Bell / Ofuda Loom / Tide Jewel) | WP-037 `SpecialWeaponUsage` tablosu: FireCue = `HeavyWeaponFire`, ActiveLoop = `HeavyWeaponSpinUp` (üçü bugün ortak) | HeavyFire (×4) / HeavyMotorLoop (v006 loop) — silah başına ayrı ses AP-011 v4 kuyruğu | 3D/2D | SFX-Player | Ü | `SpecialWeaponUsage` sw_kagura_wave/field, sw_ofuda_dart/web, sw_tide_pull/well → AP-014 sheet'leri (`SpecialWeaponVisualLibrary.FindSheet`) + `chain_arc_sheet`/`ofuda_seal` — WP-047 B (bugün `LinkEffect`/`ReportShockRing` WP-002) |
| Special bitiş uyarısı / bitiş | `SpecialWeaponFinalSeconds` / `SpecialWeaponExpired` | SwFinalSeconds / SwExpired (×3, v006) | 2D | UI | Ü | beacon nabız (`SetPulse`) + `weapon_expire` 0.8 m (elde: oyuncuda 1 m; yerde: beacon noktası) — AP-010B v003 |
| Gold toplama (tekli) / mıknatıs serisi | `RunAudioEvent.GoldCollected` streak 0–1 / ≥ 2 | GoldPickup (×3, sentez) / GoldMagnet | 2D | UI | Ü | `card_gold_trail` çekilirken (`GoldPackVisual.SetPulling`) — AP-010 v003; toplama HUD (S-09) |
| Upgrade satın alma / red / atlama | `UpgradePurchased` / `UpgradeRefused` / `UpgradeSkipped` | UpgradePurchase / UiDenied / UiBack | 2D | UI | Ü | n/a — UI (S-09) |

## 2. Zombi

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Ayak — fit A (hafif) / B / C (ağır) | `anim.foot_l/foot_r` zombi (tier: Standard/Special → Light, Elite/Ultra → Heavy −1 st) | ZombieStepLight (×5) / ZombieStepHeavy (×4) | 3D | SFX-World | Ü | `dust_step` (yüzey tint'i, tier ölçeği) katalogda — WP-047 A (S-06 zombi ayak olayı) |
| Nefes (animasyon olayı) | `anim.breath` → ZombieIdle (yoklama sayacını yeniden kurar) | ZombieIdle (×4) | 3D | SFX-World | Ü | n/a — görsel yok |
| Ölüm sonu — beden yere oturur | `anim.death_end` | paylaşımlı:ZombieStepHeavy (pitch 0,8) | 3D | SFX-World | Ü | kan gölü büyümesi bu anda tamamlanır (0.4→1.0 m 3 s) — AP-010B v003 `decal_blood_pool_albedo` |
| Idle / nefes (yakın mesafe hırıltı) — Standard | `AudioEventRouter` idle yoklaması (≤ 9 m, 6–14 s aralık, düşman başına, saldırı fazı dışında) | ZombieIdle (×4) | 3D | SFX-World | Ü | n/a — görsel yok |
| Idle — Special / Elite / Ultra | idle yoklaması, `AudioEventMap.ZombieIdle(tier)` (WP-021C) | ZombieIdleSpecial / ZombieIdleElite / ZombieIdleUltra (×3) | 3D | SFX-World | T | n/a — görsel yok (tier siluet WP-036) |
| Aggro — ayağa kalktı, seni gördü ×4 tier | `EnemyCombatEvents.spawn_land` → `AudioEventMap.ZombieAggro(tier)` (WP-048; anim.rise_end ile aynı an, doğrulmasız spawn'ı da kapsar) | ZombieAggro{Standard,Special,Elite,Ultra} (×3) | 3D | SFX-World | T | `spawn_dust` bölge tint'i (yalnız kamera dışı rise, DEC-081) — katalogda, WP-047 A `spawn_rise` |
| Saldırı hazırlığı — Standard | `EnemyCombatEvents.telegraph_start` (WP-040; WP-048'de AttackPhase yoklaması kaldırıldı) | ZombieWindupStandard (×3) | 3D | SFX-World | Ü | n/a — telegraph animasyon (DEC-029) + HUD çip; `claw_swipe` vuruşta |
| Saldırı hazırlığı — Special (Lunge, kısa hazırlık) | Windup, tier Special (WP-021C) | ZombieWindupSpecial (×3) | 3D | SFX-World | T | n/a — telegraph animasyon; tier cue WP-036 |
| Saldırı hazırlığı — Elite | Windup, tier Elite | ZombieWindupElite (×3) | 3D | SFX-World | Ü | n/a — telegraph animasyon |
| Saldırı hazırlığı — Ultra | Windup, tier Ultra | ZombieWindupUltra (×3) | 3D | SFX-World | Ü | n/a — telegraph animasyon (Ultra sweep) |
| Saldırı (savurma) ×4 tier | `EnemyCombatEvents.attack_commit` (WP-040) | ZombieAttack{Standard,Elite,Ultra} (Ü) · ZombieAttackSpecial (T, ×3) | 3D | SFX-World | Ü/T | `claw_swipe` 4×4 8 f (1.1 × 0.7 m, koyu tint) katalogda — WP-047 A `attack_commit` |
| Saldırı isabeti (oyuncuya) | `EnemyCombatEvents.attack_hit` (WP-040; WP-048'de DamageEvents.PlayerAttacked'ten taşındı) | AttackImpactBody (et/deri darbe) | 3D | SFX-World | Ü | `vignette_damage` parlama + `claw_swipe` — AP-010 v003 / WP-047 A `attack_hit` |
| Saldırı ıskası | `EnemyCombatEvents.attack_miss` | n/a — savurma commit'te çaldı, ıska sonraki sessizliktir | — | — | n/a | |
| Hit — kafa (açık) | `EnemyDamaged` bölge Head | paylaşımlı:ZombieHit* + CriticalHit (×3, v006 T) | 3D | SFX-World | Ü | kan A7 `fb_blood_heavy_01/02` (kritik) + damlacık + 2 iz — AP-010B v003 (`BloodSystem.HitClass.Critical`) |
| Hit — gövde | `EnemyDamaged` bölge UpperBody | ZombieHit{Standard,Elite,Ultra} (Ü) · ZombieHitSpecial (T, ×3) | 3D | SFX-World | Ü/T | kan A1 `fb_blood_splash_01..06` + 12 damlacık + yön izi (+%40 damla) — AP-010B v003 |
| Hit — bacak | `EnemyDamaged` bölge LowerBody | paylaşımlı:ZombieHit* (pitch −2 st) | 3D | SFX-World | Ü | kan A1 sıçrama + damlacık + yön izi — AP-010B v003 |
| Ağır sendeleme (stagger) | `EnemyCombatEvents.stagger` (WP-040: REQ-124 penceresi + zırh kırılma sendelemesi; WP-048) | ZombieStagger (×3) | 3D | SFX-World | Ü | n/a — animasyon; zırh kırılma satırındaki efekt |
| Ölüm — öne çökme / geriye / diz / patlama | `DamageEvents.EnemyDied` (+ `CombatAudioEvent.EnemyDeath` beden) | ZombieDeath{Standard,Elite,Ultra} (×2–3) · ZombieDeathSpecial (T, ×3) + EnemyDeath (×3, v006 T) / HeavyEnemyDeath | 3D | SFX-World | Ü | A6 `fb_blood_death_01..03` (3.0 m) + 2 A1 kartı + 36 damlacık + göl — AP-010B v003; hit-stop 40 ms |
| Spawn / yerden doğrulma | `EnemyCombatEvents.spawn_rise` (WP-040 DEC-081: yalnız doğrulan gövdeler; WP-048) | ZombieRise (×3, toprak katmanı) | 3D | SFX-World | Ü | `spawn_dust` (yalnız kamera dışı rise) — katalogda, WP-047 A |
| Ayağa kalktı / doğrudan indi | `EnemyCombatEvents.spawn_land` (rise_end ya da doğrulmasız spawn) | ZombieAggro{tier} (×3) | 3D | SFX-World | T | |
| Special / Elite / Ultra farkı | tier → vokal seti + `HeavyEnemyDeath`; WP-033 `ThreatMusicFeed.Accent` → müzik vurgu katmanı | tier'lı kökler (yukarıda) + MusicAccent | — | — | Ü | `death_special` (2.64 m) / `death_ultra` (3.96 m) A6'nın yerine; Elite ağır A6 ×1.15 + göl 1.2 m — AP-010B v003 |

## 2b. Düşman olay yüzeyi — `EnemyCombatEvents` (WP-040, ADR-040-1)

> Tek kaynak: `DD.Prototype.Enemies.EnemyCombatEvents.Raised` (`EnemyCombatEventInfo`: kind, tier, beden, konum, yön, bölge, `AnimationDriven`, süre, `LifeId`). Satır adı = `info.RowName`. Ses sütunu bugün `AudioEventRouter`'ın yoklama/olay kaynaklarıyla aynı anlara düşer (WP-021); S-08 isterse bu yüzeye taşır (WP-021C). **VFX sütunu S-07 devri (WP-019 → WP-019C): boş satır = FAIL — her satır ya bir efekt kimliği ya da adlı devir taşır.** Vuruş anları DEC-029 pencerelerinden gelir (WP-018 klip olayları; yer tutucu/blok gövdede zamanlayıcı yedeği, `AnimationDriven=false`).

| Olay (satır) | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| `telegraph_start` | `EnemyCombatEvents` · saldırı Windup başlangıcı (Seconds = telegraph) | paylaşımlı:ZombieWindup{Standard,Elite,Ultra} (tier) | 3D | SFX-World | Ü | n/a — animasyon telegraph; HUD çip (S-09) |
| `attack_commit` | `EnemyCombatEvents` · Windup→Active (klip `hit_window_start`; yer tutucuda zamanlayıcı) | paylaşımlı:ZombieAttack{Standard,Elite,Ultra} | 3D | SFX-World | Ü | `claw_swipe` — S-07 bağlar (WP-047 A; WP-052 vuruşa `Style` ekledi: Lunge/Swipe/Sweep/Overhead, Ultra overhead = zemin darbesi anı) |
| `attack_hit` | `EnemyCombatEvents` · bağlantı karesi (= `DamageEvents.PlayerAttacked`) | paylaşımlı:AttackImpactBody | 3D | SFX-World | Ü | `vignette_damage` + `claw_swipe` (oyuncu satırı) — WP-047 A |
| `attack_miss` | `EnemyCombatEvents` · Active→Recovery, bağlantı yok (ceza penceresi) | paylaşımlı:ZombieAttack* kuyruğu (whoosh katmanı; WP-021C aday) | 3D | SFX-World | Ü | n/a — görsel yok (ceza penceresi HUD) |
| `stagger` | `EnemyCombatEvents` · REQ-124 sendeleme (`Seconds` = süre) / zırh kırılması ağır sendeleme klibi (`Seconds` = 1,4 tavan, `stagger_end`'e kadar; kırılma vuruşunda ikisi birden gelebilir) | ZombieStagger (×3) | 3D | SFX-World | Ü | n/a — animasyon; zırh kırılmada parça patlaması |
| `armor_break_head` / `_torso` / `_leg` | `EnemyCombatEvents` · `Region` (= `DamageEvents.ArmorBroken` anı) | ArmorBreak (×2) + ArmorPieceDrop | 3D | SFX-World | Ü | `fb_impact_armor` v003 ×1.3 + `atlas_armorbreak` + `decal_lacquer_splinter` + kan A7 — AP-010 v003 (`ArmorVisualBinder.AnyBroken`); WP-047 A: bölge → plaka ankrajı |
| `death_standard` / `_special` / `_elite` / `_ultra` | `EnemyCombatEvents` · `Tier` (= `DamageEvents.EnemyDied` anı) | ZombieDeath{Standard,Elite,Ultra} (Special → Standard seti, WP-021C ayrı vokal) | 3D | SFX-World | Ü | A6 / `death_special` / `death_ultra` + kan katmanı + göl — AP-010B v003 (bugün `ReportEnemyHit` Death; WP-047 A: `EnemyCombatEvents`) |
| `spawn_rise` | `EnemyCombatEvents` · DEC-081 kamera dışı doğuş, klip başlangıcı (`Seconds` 1,57) | ZombieRise (×3, toprak katmanı) | 3D | SFX-World | Ü | `spawn_dust` bölge tint'i — S-07 bağlar (WP-047 A) |
| `spawn_land` | `EnemyCombatEvents` · `rise_end` / kalkışsız doğuşta hemen | paylaşımlı:ZombieStepHeavy (pitch 0,8; yalnız kalkış sonrası) | 3D | SFX-World | Ü | n/a — görsel yok (adım tozu adım satırı) |

## 3. Zırh (bölge başına: kask / gövde / bacak)

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Kırılma — bölge sesi (kask / gövde / bacak) | `EnemyCombatEvents.armor_break_{head,torso,leg}` (WP-040; WP-048) — tier ArmorBreak cue'sunun üstüne bölge vokali + 0,42 s plaka düşüşü | ArmorBreakHelmet / ArmorBreakBody / ArmorBreakLeg (×3, v007 T) + ArmorPieceDrop | 3D | SFX-World | T | |
| Vuruş — plaka dayanıyor | `CombatAudioEvent.ArmorHit` (rung 1) | ArmorImpact (×3, v006) | 3D | SFX-World | Ü/T | `fb_impact_armor` v003 (+ emissive) + `armor_crack_dust` — AP-010 v003 / AP-010B v003 |
| Hasar — plaka çatlıyor | `ArmorHit` rung ≥ 3 (fatigue) | ArmorCrack (×3, v006 T) | 3D | SFX-World | Ü/T | AP-008 durum mesh'i (K→D) + `armor_crack_dust` — AP-008 / AP-010B v003 |
| Kırılma — kask / gövde / bacak | `CombatAudioEvent.ArmorBreak` (slot `ArmorBreakInfo`) | ArmorBreak (×2, metal transient + lake kırılması) | 3D | SFX-World | Ü | `fb_impact_armor` v003 ×1.3 + `atlas_armorbreak` v003 parça patlaması + `decal_lacquer_splinter` v003 + kan A7 + 14 damlacık + hit-stop 35 ms + sarsıntı 0.075 — AP-010 v003 / AP-010B v003 |
| Parça düşme (yere) | `DamageEvents.ArmorBroken` + 0,42 s (`AudioEventRouter`; `ArmorVisualBinder.Broken` DD.Art'ta, Prototype erişemez) | ArmorPieceDrop (×2) | 3D | SFX-World | Ü | `atlas_armorbreak` 10 parça, gravite, 0.9 s (plaka ankrajı) — AP-010 v003; yerde parça mesh'i AP-008B Broken |

## 4. Kırılabilirler

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| AP-017 aileleri (fener taş / fener kâğıt / saksı / çit / balya / bank / tabela / shoji) | `BreakableProp.Broke` aile → ses (AP-017 main'e gelince aile anahtarı bağlanır; şimdi R) | Break{LanternStone,LanternPaper,Planter,Fence,Bale,Bench,Sign,Shoji} (×3, v007 T) | 3D | SFX-World | T/R | |
| AP-003B small_breakable (seramik/çömlek) | WP-013 kırılma bağlaması → `SurfaceImpact*` + `BreakCeramic` | BreakCeramic (×2) | 3D | SFX-World | Ü | `BreakableProp` çip + toz + `fb_impact_wood` + `breakable_residue` 2×2 atlas dekalı (seramik hücresi) — WP-013 / AP-010B v003 |
| AP-003B stone_lantern (isabet) | `SurfaceImpactStone` | ImpactStone | 3D | SFX-World | Ü | `fb_impact_stone` v003 + `decal_impact_stone` — AP-010 v003; `lantern_flame` katalogda (WP-029 ankrajı) |
| AP-003B bamboo_fence / bamboo_stalk (isabet) | `SurfaceImpactWood` (yüzey tablosunda bambu ayrı değil) | ImpactWood (bambu varyantı v4 kuyruğu) | 3D | SFX-World | Ü | `fb_impact_wood` v003 + dekal — AP-010 v003 |
| AP-005B barrels / handcart / shed (ahşap isabet) | `SurfaceImpactWood` | ImpactWood | 3D | SFX-World | Ü | `fb_impact_wood` v003 + `decal_impact_wood` (SurfaceResolver → Wood) — AP-010 v003 |
| AP-005B strawbales (isabet) | `SurfaceImpactFoliage` | ImpactFoliage (×3, v006 T) | 3D | SFX-World | Ü/T | `fb_impact_wood` (Wood fallback); `straw_puff` + `straw_puff_cloud` katalogda — yüzey sınıfı Straw eklenince WP-047 C |
| AP-005B tsukubai / mossrock (taş isabet) | `SurfaceImpactStone` | ImpactStone | 3D | SFX-World | Ü | `fb_impact_stone` v003 + `decal_impact_stone` — AP-010 v003 |
| Kağıt paravan / shoji (isabet) | `SurfaceImpactPaper` | ImpactPaper (×3, v006) | 3D | SFX-World | Ü/T | `paper_tear` 4×4 sprite patlaması (8 parça) — AP-010B v003 |

## 5. Çevre

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Look B — uzak gök gürültüsü | `AmbienceDirector.TickWetLook` (WetLook, 40–90 s, 52 m uzakta, yüksek) | AmbThunderDistant (×3, v007 T) | 3D | Ambience | T | |
| Look B — ıslak arduvaz damlası | scatter halkası (WetLook seti) | AmbDripSlate (×3, v007 T) | 3D | Ambience | T | |
| Look B — koi havuzu su bed'i | `Ap005SurfaceTags.RegisterPond` → `AudioSurfaceHints.PondCenters` → havuz merkezinde 3D loop (6–30 m) | AmbPondBed (loop, v007 T) | 3D | Ambience | T | |
| Ortak atmosfer (yağmur sonrası damla, rüzgâr) | `AmbienceDirector` ortak bed | AmbCommon (loop) | 2D | Ambience | Ü | `AtmosphereDrift` polen/sakura + `WindDriver` gust — WP-013 |
| Merkez avlu (taş, damla, yankı) | bölge `center` | AmbRegionCenter (loop) | 2D | Ambience | Ü | n/a — bölge sesi; görsel ortak atmosfer |
| Kuzey — koridor/ahşap rüzgârı | bölge `north` | AmbRegionNorth (loop) | 2D | Ambience | Ü | n/a — bölge sesi |
| Doğu — bambu korusu | bölge `east` | AmbRegionEast (loop) | 2D | Ambience | Ü | `DD/Foliage Wind` bambu salınımı — WP-013 |
| Güney — gölet / su kıyısı | bölge `south` | AmbRegionSouth (loop) | 2D | Ambience | Ü | `DD/Water v1` + vuruşta `water_splash` + halka — WP-013 / AP-010B v003; koi havuzu v2 WP-047 D |
| Batı — teras / uçurum rüzgârı | bölge `west` | AmbRegionWest (loop) | 2D | Ambience | Ü | n/a — bölge sesi |
| Bambu tıkırtısı / ahşap gıcırtı / metal çınlama / taş oturması (scatter) | `AmbienceDirector` scatter (9–24 s) | AmbBambooKnock (×3, T) / AmbWoodCreak (×4) / AmbMetalRing (×3, T) / AmbStoneSettle (×3, T) — v006 | 3D | Ambience | Ü/T | n/a — scatter sesi; görsel karşılığı yok |
| Sakura / polen (rüzgâr sürüklenmesi) | ortak bed içinde (rüzgâr katmanı) | paylaşımlı:AmbCommon | 2D | Ambience | Ü | `AtmosphereDrift` polen + sakura — WP-013 |
| Fener / kapı / koridor yankısı | bölge reverb zone'ları (ADR-021-2: avlu taş / bambu kapalı / gölet açık) | reverb (klip yok) | 3D | — | Ü | `lantern_flame` 4×4 24 fps döngü katalogda — WP-029 look B ankrajı, WP-047 D; kapı statik |
| Damla (tek, yakın) | scatter listesine eklendi | AmbDrip (×3) | 3D | Ambience | Ü | n/a — görsel yok |

## 6. Silah / Special Weapon

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Yüzey isabeti ×8 (default/stone/wood/foliage/water/metal/dirt/paper) | `SurfaceImpact*` | ImpactStone/Wood/Metal/Dirt/Water (Ü) · ImpactDefault/Foliage (v006 T) · ImpactPaper (v006) | 3D | SFX-World | Ü | `fb_impact_{stone,wood,earth,armor}` v003 + dekaller; foliage `leaf_burst`, water `water_splash` + halka, paper `paper_tear` — AP-010 v003 / AP-010B v003 |
| Sekme / zincir arkı | `Ricochet` / `ChainArc` | Ricochet (×5, v006) / ChainArc (×4, v006) | 3D | SFX-World | Ü/T | `LinkEffect` + `spark` + `spark_streak` (sekme) — WP-002 / AP-010B v003; `chain_arc_sheet` WP-047 B |
| Patlama (Ofuda) | `Explosion` | Explosion (×2) | 3D | SFX-World | Ü | `fb_explosion` v003 (48 f) + `fb_shockwave` 5.0 m + `card_smoke` ×3 + `decal_scorch` v003 + sarsıntı — AP-010 v003 |
| Kagura dalgası / Tide kuyusu döngüsü | `SpecialWeaponBeaconLoop` (Tide, KS-12) | SwBeaconLoop (v006 loop) | 3D | UI | Ü/T | `ReportShockRing` + `fb_shockwave` (Tide); AP-014 sw_* sheet'leri WP-047 B — WP-002 / AP-010 v003 |
| Special spawn (gong, rezerv) | `SpecialWeaponSpawn` | SwSpawnGong | 3D | UI | Ü | `beacon_column` v003 ×2 + `beacon_ring` + `beacon_ring_anim` — AP-010 v003 |
| Special beacon (yerde) | `SpecialWeaponBeaconLoop` | SwBeaconLoop (v006 loop) | 3D | UI | Ü/T | beacon kartları + `SetPulse` nabız + `hud_marker` (S-09) — AP-010 v003 |
| Yerde süresi doldu | `SpecialWeaponGroundExpired` | SwGroundExpired (×2, v006 T) | 3D | UI | Ü/T | `weapon_expire` beacon noktasında + beacon solar — AP-010B v003 |

## 7. Ekonomi / round

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Gold düşme | `RunAudioEvent.GoldSpawned` | GoldSpawn (×2) | 3D | UI | Ü | `goldpack_V01` mesh + `card_gold_sparkle` v003 (0.45 m, alfa 0.5–0.7) — AP-010 v003 |
| Gold çekilme (mıknatıs) / toplama | `GoldCollected` | GoldMagnet / GoldPickup | 2D | UI | Ü | `card_gold_trail` v003 (`SetPulling`) — AP-010 v003; toplama HUD (S-09) |
| Round başı / geri sayım | `RoundStart` / `CountdownTick` | RoundStart / RoundCountdownTick | 2D | UI | Ü | kan izleri 2 s solar + birleşik mesh temizlenir (`RoundStarted`) — WP-019b; HUD (S-09) |
| Round sonu / havuz tükendi | `RoundComplete` / `RoundPoolExhausted` | RoundComplete / AmbMetalRing (×3, v006 T) (uzak çan) | 2D | UI | Ü | n/a — HUD (S-09) |
| Round arası geri çekilme | `RunPhase` RoundComplete/Upgrading → `MusicDirector` + snapshot RoundBreak | (mix; klip yok) | — | Music | Ü | n/a — UI (S-09) |
| Tanıtım vurgusu (Elite/Ultra/Special varlığı) | `MusicDirector` accent katmanı | MusicAccent (loop) | 2D | Music | Ü | n/a — müzik; tier siluet WP-036 |
| Müzik katmanları | `MusicDirector` | MusicBase / MusicPercussion / MusicTension (loop) | 2D | Music | Ü | n/a — ses katmanı; görsel karşılığı yok |

## 8. UI (WP-022B `UiEvents` — 39 olay, olay adları sabit; sahip = tek satır, çift ses yok)

Yayın: `UiEvents.Raise(UiEvent, detail)`; S-08 `AudioEventRouter.OnUiEvent` yalnız **bağlı** satırları çalar; **sahibi başka** satırlar aynı karede sahibinden çalar (UiSound → `PrototypeAudio.*`, Run/Combat/Player kaynakları). `AudioEventMap` Ui bölümü = bu tablo (EditMode `UiOlaylari_HerUiEventUyesininSatiriVar_RouterHaritayaEsit`).

| UiEvent | Ne zaman | SFX dosya (ses sahibi) | 2D/3D | Bus | Durum | VFX / mikro-animasyon (S-09, mevcut) |
|---|---|---|---|---|---|---|
| FocusMoved | Odak başka kontrole geçti | UiNavigate — sahibi `UiSound.Move → PrototypeAudio.Navigate` | 2D | UI | Ü | Odak halkası anında; hover yıkaması `MotionFast` |
| ButtonHover | İşaretçi kontrol üstüne geldi | **UiHover** (×3, T) | 2D | UI | T | Krem yıkama %8 |
| ButtonPress | Etkin kontrol basıldı | UiConfirm — sahibi Confirm (aynı kare) | 2D | UI | Ü | Basılı dolgu `MotionInstant` |
| Confirm | Eylem kabul edildi | UiConfirm — sahibi `UiSound.Confirm` | 2D | UI | Ü | — |
| Back | Geri / iptal | UiBack (×2) — sahibi `UiSound.Back` | 2D | UI | Ü | Ekran fade |
| Denied | Kilitli / erişilemez | UiDenied (×2) — sahibi `UiSound.Denied` | 2D | UI | Ü | Durum çipi LOCKED / TOO EXPENSIVE |
| ScreenEntered | Ekran kuruldu (detail = ekran) | **UiTransition** (×2) — `RunStarted`/`Phase→None` aynı kimlik, MinRetrigger 0,3 s aynı-kare ikilemeyi katlar | 2D | UI | Ü | Canvas fade 0,28 s |
| ScreenLeft | Ekran yok ediliyor | n/a — sonraki ScreenEntered taşır | — | — | n/a | — |
| PaneOpened | Ana menü paneli | **UiPaneOpen** (×2, T: kâğıt kayması) | 2D | UI | T | Sekme raised + vermilion tik |
| ChoiceSelected | Skin / silah kartı | **UiSelect** (×3, T: ahşap tok) | 2D | UI | T | Kart raised + SELECTED |
| SliderChanged | Kaydırıcı adımı | **UiSliderTick** (×3, T) | 2D | UI | T | Okuma metni |
| ToggleChanged | Reduced motion ON/OFF | **UiToggle** (×2, T) | 2D | UI | T | Etiket ON/OFF |
| DialogOpened | Koşuyu bırak onayı | **UiDialogOpen** (×2, T) | 2D | UI | T | Scrim + odak |
| DialogClosed | Onay kapandı | UiBack — sahibi `UiSound.Back` | 2D | UI | Ü | Odak iadesi |
| HudRoundStarted | Round başladı | RoundStart — sahibi `RunAudioEvent.RoundStart` | 2D | UI | Ü | ROUND N banner |
| HudRoundCompleted | Round temizlendi | RoundComplete — sahibi `RunAudioEvent.RoundComplete` | 2D | UI | Ü | Banner |
| HudPoolExhausted | Havuz bitti | RoundComplete (soft) — sahibi `RunAudioEvent.RoundPoolExhausted` | 2D | UI | Ü | Banner |
| HudLowHealthEntered | Can eşiğin altına indi | **HudLowHealth** (×2, T: boğuk çift kalp vuruşu) | 2D | UI | T | Can çubuğu `StateDanger` |
| HudLowHealthLeft | Can eşiğin üstüne çıktı | n/a — sessiz (görsel yeterli, S-09 satırı) | — | — | n/a | Renk geri döner |
| HudMagazineEmpty | Şarjör boş (HUD çipi) | **HudMagazineEmpty** (×2, T; DryFire tetik çekişinde kalır) | 2D | UI | T | MAGAZINE EMPTY · RELOAD |
| HudSpecialSpawned | Special yere düştü | SwSpawnGong — sahibi `SpecialWeaponSpawn` (REQ-053 rezerv) | 3D | UI | Ü | Dünya işareti + pip |
| HudSpecialPickedUp | Special alındı | SwPickup — sahibi `SpecialWeaponPickup` | 2D | UI | Ü | Radyal halka |
| HudSpecialFinalSeconds | Son saniyeler (REQ-058) | SwFinalSeconds — sahibi `SpecialWeaponFinalSeconds` | 2D | UI | Ü | Vinyet pulse + banner |
| HudSpecialEnded | Special bitti | SwExpired (×3, v006) — sahibi `SpecialWeaponExpired` | 2D | UI | Ü/T | CLASSIC WEAPON RESTORED |
| HudSpecialExpired | Yerde süresi doldu | SwGroundExpired (×2, v006 T) — sahibi `SpecialWeaponGroundExpired` | 3D | UI | Ü/T | İşaret kalkar |
| HudThreatEncircled | SURROUNDED (WP-033) | **HudThreatEncircled** (×2, T: düşük gerilim vuruşu, ambiyans duck 0,7/0,6 s) | 2D | UI | T | Kafatası çipi |
| HudThreatRearAttack | BEHIND YOU | **HudThreatRear** (×2, T: keskin ters whoosh) | 2D | UI | T | Chevron çipi |
| HudThreatStrongVersion | ELITE/ULTRA NEARBY | **HudThreatStrong** (×2, T: kasvetli kısa çan −9 st, gong'dan ayrık) | 2D | UI | T | `StateWarning` çipi |
| HudThreatWindup | Hazırlık sayısı arttı | n/a — 3D windup vokali (DEC-067) sahibi; 2D ikileme yönü bulanıklaştırır | — | — | n/a | — |
| HudDamageEdge | Kenar yandı | PlayerDamage — sahibi `PlayerHealth.AnyDamaged` | 2D | UI | Ü | Yönlü kenar flaşı |
| UpgradePurchased | Satın alındı | UpgradePurchase — sahibi `RunAudioEvent.UpgradePurchased` | 2D | UI | Ü | PURCHASED çipi |
| UpgradeRerolled | Teklif yenilendi | **UiReroll** (×2, T: 3 kart hışırtısı) | 2D | UI | T | Kartlar yeniden |
| UpgradeOfferLocked | Teklif kilitlendi | **UiLock** (×2, T: metal kilit) | 2D | UI | T | OFFER LOCKED |
| ResultsRevealed | NEW rozeti | **UiReveal** (×2, T) — sahibi `UiSound.Reveal → PrototypeAudio.Reveal` (WP-021C: yerel sentez yerine kütüphane) | 2D | UI | T | NEW pulse |
| LevelUp | Profil seviyesi | **UiLevelUp** (×2, T) | 2D | UI | T | LEVEL UP çipi |
| UnlockRevealed | Yeni içerik | **UiUnlock** (×2, T) | 2D | UI | T | Madalyon + NEW |
| AchievementRevealed | Başarım | **UiAchievement** (×2, T) | 2D | UI | T | Nadirlik madalyonu |
| NewBestRound | Rekor | **UiNewBest** (×2, T) | 2D | UI | T | NEW BEST ROUND pulse |
| SettingsChanged | Ayar kaydedildi | n/a — Slider/Toggle olayı taşır | — | — | n/a | — |
| (mixer) Ana ses | `UiSettings.MasterVolume` → `PrototypeAudio.MasterVolume` + `MasterVolumeChanged` → mixer "MasterVolume" (60 ms rampa) | — | — | Master | Ü | — |
| Pause aç / kapat | `RunFlowClock` pause hold → snapshot Pause (+ `PrototypeAudio.Back`) | UiBack + snapshot | 2D | UI | Ü | |

## AP-011 v4 kuyruğu → **v006 ile kapandı** (kullanıcı kararı 2026-09-17: taban 200→100, S-08'e ≤ 26 kr; 17 render = 40,8 kr gerçek (izin 26 → 14,8 aşım, bildirildi) + 11 kredisiz türetme; prosedürel canlı SFX **0**)

WP-021 kapanışı (v003, 90,2 kredi harcandı) ve WP-021B (Higgsfield tabanı: bakiye < 200 → 0 render). **P** ile sevk edilen satırlar ve üretilmişe çevirme tahmini (1,1 kr/render, 1–2 aday) — **kredi kararı kullanıcıda**:

| Satır | Bugün | v4 üretimi | Tahmin |
|---|---|---|---|
| İsabet / öldürme onayı (HitMarker/KillMarker) | proc | 2 render | 2,2 kr |
| Special döner top motor loop + spin-down | proc (WP-002) | 2 render | 2,2 kr |
| Special bitiş haberi (SwExpired) / yerde doldu (SwGroundExpired) | proc | 2 render | 2,2 kr |
| Zırh vuruş / çatlama (ArmorImpact / ArmorCrack) | proc | 2 render | 2,2 kr |
| Yüzey isabeti default / foliage / paper | proc | 3 render | 3,3 kr |
| Sekme / zincir arkı (Ricochet / ChainArc) | proc | 2 render | 2,2 kr |
| SMG ateşi (Light) | proc | 1–2 render | 2,2 kr |
| Special beacon loop (yerde) + Tide kuyusu döngüsü | proc (paylaşımlı KS-12) | 2 render | 2,2 kr |
| Scatter one-shot'lar (bambu/ahşap/metal/taş) | proc | 4 render | 4,4 kr |
| Koşu kumaş/teçhizat katmanı (GearRustle) | yok (koşu = yürüme klibi) | 1 render | 1,1 kr |
| ZombieRise toprak take'i (ayrı), MusicBase 2. aday (NSFW reddi sonrası yeniden prompt), 2 dk müzik düzenlemesi | v003 devri | 3 render + stitching | 3,3 kr |
| **Toplam** | | ≈ 24 render | **≈ 26 kr** (karar kullanıcıda) |
