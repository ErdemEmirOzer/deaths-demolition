# EVENT COVERAGE MATRIX — ses (S-08 / WP-021) + VFX (S-07 / WP-019)

> VFX sütunu (S-07, WP-019/WP-019b, 2026-09-17): `AP-010 v003` HQ geçişi + `AP-010B v003` kan seti/wave-2 Unity'de (`DD_VfxCatalog`); "katalogda … WP-047" = varlık içeride, olay kancası WP-047'de (A kancalar, B silah, C kırılabilir, D dünya); `n/a — gerekçe` yalnız gerekçeyle. Boş VFX hücresi = DoD FAIL (`ArtBloodTests.OlayKapsamMatrisi_BosSatirYok`). Kan sistemi ve wave-2 durumu: bkz. planning/04 WP-019 (I)/(K).

> Kullanıcı direktifi (2026-09-16, §12): "Her şeyin kendine ait sesi ve efekti olacak; hiçbir detay atlanmasın." Ortak dosya: S-08 ses sütununu, S-07 VFX sütununu doldurur (satır çakışmasında ikinci biten rebase'de birleştirir). **Boş ses satırı = WP-021 DoD FAIL.**
>
> Ses sütunu okunuşu: `SFX dosya` = AP-011 klip kökü (`Assets/_Art/Audio/AP011/<kök>_nn.wav`) ya da `proc:<AudioSoundId>` (WP-002 prosedürel recipe, katalogda üretilmiş klip yok) ya da `paylaşımlı:<kök>` (başka satırın klibini mix-plan farkıyla kullanır). `Kaynak` = olayın koddaki tetiği. Durum: **Ü** üretildi (AP-011 v003), **P** prosedürel, **R** rezerve (tetik henüz yok). WP-021B: WP-018 (ADR-018-1) main'e geldi → ayak/nefes/ölüm-sonu satırları `CharacterAnimationEvents.Any` ile bağlandı (R kalktı). Kredi kalanı yetmeyen satırlar **P** ile sevk edilir ve "AP-011 v4 kuyruğu" bölümünde listelenir (kredi kararı kullanıcıda). **WP-021C:** **T** = türetilmiş (AP-011 v005/v006/v007/v008/v009: mevcut 48k/24-bit kaynaklardan ofline katman/pitch/stretch, kredi 0; `EVENT-COVERAGE-MATRIX-UI.md` satırları §8'e taşındı, olay adları sabit); zemin: AP-005 placement aileleri sahne yüklenince `SurfaceTag` alır (`DD.Art.Ap005SurfaceTags`, ADR-021-1 v2), avlu zemini taş rotalarda taşa iyileşir.

## 1. Oyuncu

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Idle ağırlık kayması (shift_l/shift_r, WP-032B) | `anim.shift_l/shift_r` → yüzeye göre ayak sesi yarım kazanç (WP-048B) | paylaşımlı:Footstep* / ZombieStep* | 3D | SFX-Player/World | Ü | n/a — görsel yok |
| Ayak sesi — çakıl / kaldırım / sığ su (AP-015P plakaları, WP-038) | `anim.foot_l/foot_r` + `Ap005SurfaceTags.FootstepVariantFor` (plaka adı: AP015_paving_* → kaldırım, AP015_ground_center → çakıl avlusu, su yüzeyi → sığ) (WP-058C) | FootstepGravel / FootstepPavement / FootstepWaterShallow (×3, v007 T) | 3D | SFX-Player | T | `dust_step` yüzey tint'i (`SurfaceResolver` → Dirt/Stone) ve sığ suda küçük `water_splash_ring` (`VfxEventBinder`; AP-015P plaka sınıfı gelince aynı kanca) — WP-047 A |
| Ayak sesi — derin su | derinlik sinyali yok (havuz havzası yürünmez; yüzey sığ okunur) — R | FootstepWaterDeep (×3, v007 T) | 3D | SFX-Player | T/R | |
| Silah foley A/B/C — eject / chamber / mag_out / mag_in(+2/3) / switch_ready+away / lowered / raised | WP-032C klip olayları oyuncu rig'inde → `AudioEventMap.WeaponFoley(WeaponRegistry.SelectedWeapon, olay)` (WP-048B) | Weapon{A,B,C}{Eject,Chamber,MagOut,MagIn,Switch,Lowered,Raised} (×2, v007 T; A tüfek, B carbine, C pompalı ayrışması) | 2D | SFX-Player | T | n/a — görsel yok |
| Silah/Special alma — kavrama / yerine oturma | `anim.pickup_grab` / `anim.pickup_done` (WP-032C; WP-048B bağlı) | PickupGrab / PickupDone (×2, v007 T) | 2D | SFX-Player | T | `weapon_pickup_flash` (alma) — AP-010B v003; kavrama animasyonu S-06 |
| Ayak sesi — taş | `anim.foot_l/foot_r` + aşağı ışın → `AudioSurfaceHints` → `SurfaceResolver` (WP-021C: quiet_paving/ring/gate/shore_path/kaya/fener + taş rota şeridi; etiketsiz = taş) | FootstepStone (×4) | 3D | SFX-Player | Ü | `dust_step` (katalogda, stone tint) — bağlama `anim.foot_l/foot_r` WP-047 A (S-06 ayak olayı) |
| Ayak sesi — ahşap (veranda/köprü) | `anim.foot_l/foot_r` + `SurfaceKind.Wood` | FootstepWood (×3) | 3D | SFX-Player | Ü | `dust_step` wood_tatami tint — WP-047 A |
| Ayak sesi — toprak/çakıl | `anim.foot_l/foot_r` + `SurfaceKind.Dirt` | FootstepDirt (×4) | 3D | SFX-Player | Ü | `dust_step` earth tint — WP-047 A |
| Ayak sesi — çimen/bitki | `anim.foot_l/foot_r` + `SurfaceKind.Foliage` | FootstepFoliage (×4) | 3D | SFX-Player | Ü | n/a — yaprak/çimen adım tozu okunmaz (20 m kamera); `dust_step` earth tint düşük yoğunluk WP-047 A |
| Ayak sesi — su kıyısı/sığ su | `anim.foot_l/foot_r` + `SurfaceKind.Water` | FootstepWater (×4) | 3D | SFX-Player | Ü | `water_splash_ring` küçük (0.4 m) — WP-047 A/D |
| Koşu / strafe (kumaş + teçhizat hışırtısı) | `anim.foot_l/foot_r` + `Motor.Speed01 > 0,7` → +2,4 dB | aynı yüzey klibi (GearRustle katmanı v4 kuyruğu) | 3D | SFX-Player | Ü | n/a — görsel yok (kumaş hışırtısı ses); koşu tozu = adım satırları |
| Ateş — SMG (Light) | `CombatAudioEvent.ClassicWeaponFire` intensity ≤ 0,87 | SmgFire (×5, v006 B Carbine) | 3D | SFX-Player | Ü/T | `fb_muzzle` v003 **B varyantı** (0.75 boy, %60 süre) + tracer — AP-010 v003 / WP-047 B (DEC-083) |
| Ateş — tüfek (Medium) | `ClassicWeaponFire` 0,87–1,25 | RifleFire (mekanik + gövde + avlu kuyruğu) | 3D | SFX-Player | Ü | `fb_muzzle` v003 **A varyantı** (uzun koni, ×1.35 namlu boyunca) + tracer — AP-010 v003 / WP-047 B |
| Ateş — pompalı (Heavy) | `ClassicWeaponFire` ≥ 1,25 | ShotgunFire | 3D | SFX-Player | Ü | `fb_muzzle` v003 **C varyantı** (geniş ×1.25, ×1.3 en) + saçma tracer + pump dumanı 0.35 s sonra (`card_smoke` 2 puf) — AP-010 v003 / WP-047 B |
| Ateş — ağır döner top (Special) | `HeavyWeaponFire` | HeavyFire (×4) | 3D | SFX-Player | Ü | `fb_muzzle` v003 × `_specialFlashMultiplier` (Kagura hariç) — AP-010 v003; `barrel_heat` katalogda (ısı sürücüsü Weapons, WP-047 B) |
| Boş tetik | `ClassicWeaponDryFire` | DryFire | 2D | SFX-Player | Ü | n/a — görsel yok; HUD şarjör çipi (S-09) |
| Şarjör çıkar / tak / kurma | `ReloadStart` / `ReloadInsert` / `ReloadEnd` | ReloadStart / ReloadInsert / ReloadEnd | 2D | SFX-Player | Ü | `mag_drop` kartı oyuncunun sağ önünde zeminde + `mag_drop_dust` (`WeaponEvents.ReloadStarted`, 0.45 s gecikme) — AP-010B v003 / WP-047 B |
| İsabet onayı / öldürme onayı | `HitMarker` / `KillMarker` | HitMarker (×6, v006) / KillMarker (×3, v006 T) | 2D | UI | Ü/T | `HitMarkerLayer` (tier renkli) / kill varyantı — WP-002 |
| Hasar alma (hafif/ağır) | `PlayerHealth.AnyDamaged` (şiddet → gain) | PlayerDamage (×3) | 2D | UI | Ü | `vignette_damage` v003 parlama 0.35 (4 f / 12 f) × Vermilion600 (`DamageVignetteLayer`) — AP-010 v003; düşük can nabız 0.35 |
| Ölüm — çökme | `PlayerHealth.AnyPlayerDied` | PlayerDeath | 2D | UI | Ü | `player_death_overlay` maskesi 2 s kararma 0.6 (`DamageVignetteLayer`, `AnyPlayerDied`) — AP-010B v003; doygunluk düşürme post-fx devri |
| Ölüm — koşu sonu vurgusu | `RunAudioEvent.RunEnded` | paylaşımlı:PlayerDeath (stinger) | 2D | UI | Ü | n/a — sonuç ekranı geçişi (S-09) |
| Special alma | `SpecialWeaponPickup` | SwPickup | 2D | UI | Ü | `weapon_pickup_flash` 1.2 m oyuncu ayağında (`SpecialWeaponPickedUp`) + beacon söner — AP-010B v003 |
| Special kullanma — döner top motoru | `HeavyWeaponSpinUp` (loop) / `HeavyWeaponSpinDown` | HeavyMotorLoop (v006 loop) / HeavySpinDown (v006) | 2D | SFX-Player | Ü/T | `barrel_heat` (+distort) katalogda — ısı 0→1 sürücüsü Weapons'ta yok (devir) |
| Special kullanma — silah başına (Kagura Bell / Ofuda Loom / Tide Jewel) | WP-037 `SpecialWeaponUsage` tablosu: FireCue = `HeavyWeaponFire`, ActiveLoop = `HeavyWeaponSpinUp` (üçü bugün ortak) | HeavyFire (×4) / HeavyMotorLoop (v006 loop) — silah başına ayrı ses AP-011 v4 kuyruğu | 3D/2D | SFX-Player | Ü | Kagura: AP-014 `sw_kagura_wave` halkası zeminde dalga cephesiyle büyür (`ReportShockRing`, `SpecialWeaponSheets` runtime materyali); Ofuda: `sw_ofuda_collapse` her mühürlü bedende (`ReportSealLink`, kare içi tekilleştirme); Tide: `ReportShockRing` + `fb_shockwave` (AP-014 tide sheet'i yok) — WP-047 B; `sw_ofuda_dart/web` (uçan kart / iplik) ve `chain_arc_sheet` devir |
| Special bitiş uyarısı / bitiş | `SpecialWeaponFinalSeconds` / `SpecialWeaponExpired` | SwFinalSeconds / SwExpired (×3, v006) | 2D | UI | Ü | beacon nabız (`SetPulse`) + `weapon_expire` 0.8 m (elde: oyuncuda 1 m; yerde: beacon noktası) — AP-010B v003 |
| Gold toplama (tekli) / mıknatıs serisi | `RunAudioEvent.GoldCollected` streak 0–1 / ≥ 2 | GoldPickup (×3, sentez) / GoldMagnet | 2D | UI | Ü | `card_gold_trail` çekilirken (`GoldPackVisual.SetPulling`) — AP-010 v003; toplama HUD (S-09) |
| Upgrade satın alma / red / atlama | `UpgradePurchased` / `UpgradeRefused` / `UpgradeSkipped` | UpgradePurchase / UiDenied / UiBack | 2D | UI | Ü | n/a — UI (S-09) |

## 2. Zombi

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Ayak — fit A (hafif) / B / C (ağır) | `anim.foot_l/foot_r` zombi (tier: Standard/Special → Light, Elite/Ultra → Heavy −1 st) | ZombieStepLight (×5) / ZombieStepHeavy (×4) | 3D | SFX-World | Ü | `dust_step` yüzey tint'i (`CharacterAnimationEvents.Any`, zombi ayak olayları; kısma/mesafe kapısı) — WP-047 A |
| Nefes (animasyon olayı) | `anim.breath` → ZombieIdle (yoklama sayacını yeniden kurar) | ZombieIdle (×4) | 3D | SFX-World | Ü | n/a — görsel yok |
| Ölüm sonu — beden yere oturur | `anim.death_end` | paylaşımlı:ZombieStepHeavy (pitch 0,8) | 3D | SFX-World | Ü | kan gölü büyümesi bu anda tamamlanır (0.4→1.0 m 3 s) — AP-010B v003 `decal_blood_pool_albedo` |
| Idle / nefes (yakın mesafe hırıltı) — Standard | `AudioEventRouter` idle yoklaması (≤ 9 m, 6–14 s aralık, düşman başına, saldırı fazı dışında) | ZombieIdle (×4) | 3D | SFX-World | Ü | n/a — görsel yok |
| Idle — Special / Elite / Ultra | idle yoklaması, `AudioEventMap.ZombieIdle(tier)` (WP-021C) | ZombieIdleSpecial / ZombieIdleElite / ZombieIdleUltra (×3) | 3D | SFX-World | T | n/a — görsel yok (tier siluet WP-036) |
| Aggro — ayağa kalktı, seni gördü ×4 tier | `EnemyCombatEvents.spawn_land` → `AudioEventMap.ZombieAggro(tier)` (WP-048; anim.rise_end ile aynı an, doğrulmasız spawn'ı da kapsar) | ZombieAggro{Standard,Special,Elite,Ultra} (×3) | 3D | SFX-World | T | `spawn_dust` (1.76 × 1.1 m) `EnemyCombatEvents.SpawnRise` — yalnız kamera dışı rise (DEC-081) — WP-047 A |
| Special tepkisi — itilme+sersemleme / ağa yakalanma / çekim tiki | WP-055 `EnemyCombatEvents.sw_push` / `sw_web` / `sw_pull` (bedenin tarafı; alan dokunuşu SpecialUsage.Hit) (WP-058C) | ZombieReactPush / ZombieReactWeb / ZombieReactPull (×3, v010 T; ağır tier −3 st) | 3D | SFX-World | T | |
| Saldırı hazırlığı — Standard | `EnemyCombatEvents.telegraph_start` (WP-040; WP-048'de AttackPhase yoklaması kaldırıldı) | ZombieWindupStandard (×3) | 3D | SFX-World | Ü | n/a — telegraph animasyon (DEC-029) + HUD çip; `claw_swipe` vuruşta |
| Saldırı hazırlığı — Special (Lunge, kısa hazırlık) | Windup, tier Special (WP-021C) | ZombieWindupSpecial (×3) | 3D | SFX-World | T | n/a — telegraph animasyon; tier cue WP-036 |
| Saldırı hazırlığı — Elite | Windup, tier Elite | ZombieWindupElite (×3) | 3D | SFX-World | Ü | n/a — telegraph animasyon |
| Saldırı hazırlığı — Ultra | Windup, tier Ultra | ZombieWindupUltra (×3) | 3D | SFX-World | Ü | n/a — telegraph animasyon (Ultra sweep) |
| Saldırı (savurma) ×4 tier | `EnemyCombatEvents.attack_commit` (WP-040) | ZombieAttack{Standard,Elite,Ultra} (Ü) · ZombieAttackSpecial (T, ×3) | 3D | SFX-World | Ü/T | `claw_swipe` (1.1 × 0.7 m, tier ölçeği 1/1.1/1.3) `EnemyCombatEvents.AttackCommit`, +u vuruş yönü — WP-047 A |
| Saldırı isabeti (oyuncuya) | `EnemyCombatEvents.attack_hit` (WP-040; WP-048'de DamageEvents.PlayerAttacked'ten taşındı) | AttackImpactBody (et/deri darbe) | 3D | SFX-World | Ü | `vignette_damage` parlama (oyuncu) + `claw_swipe` (commit'te) — AP-010 v003 / WP-047 A |
| Saldırı ıskası | `EnemyCombatEvents.attack_miss` | n/a — savurma commit'te çaldı, ıska sonraki sessizliktir | — | — | n/a | n/a — görsel yok; swipe commit'te oynadı (`claw_swipe`) |
| Hit — kafa (açık) | `EnemyDamaged` bölge Head | paylaşımlı:ZombieHit* + CriticalHit (×3, v006 T) | 3D | SFX-World | Ü | kan A7 `fb_blood_heavy_01/02` (kritik) + damlacık + 2 iz — AP-010B v003 (`BloodSystem.HitClass.Critical`) |
| Hit — gövde | `EnemyDamaged` bölge UpperBody | ZombieHit{Standard,Elite,Ultra} (Ü) · ZombieHitSpecial (T, ×3) | 3D | SFX-World | Ü/T | kan A1 `fb_blood_splash_01..06` + 12 damlacık + yön izi (+%40 damla) — AP-010B v003 |
| Hit — bacak | `EnemyDamaged` bölge LowerBody | paylaşımlı:ZombieHit* (pitch −2 st) | 3D | SFX-World | Ü | kan A1 sıçrama + damlacık + yön izi — AP-010B v003 |
| Ağır sendeleme (stagger) | `EnemyCombatEvents.stagger` (WP-040: REQ-124 penceresi + zırh kırılma sendelemesi; WP-048) | ZombieStagger (×3) | 3D | SFX-World | Ü | n/a — animasyon; zırh kırılma satırındaki efekt |
| Ölüm — öne çökme / geriye / diz / patlama | `DamageEvents.EnemyDied` (+ `CombatAudioEvent.EnemyDeath` beden) | ZombieDeath{Standard,Elite,Ultra} (×2–3) · ZombieDeathSpecial (T, ×3) + EnemyDeath (×3, v006 T) / HeavyEnemyDeath | 3D | SFX-World | Ü | A6 `fb_blood_death_01..03` (3.0 m) + 2 A1 kartı + 36 damlacık + göl — AP-010B v003; hit-stop 40 ms |
| Spawn / yerden doğrulma | `EnemyCombatEvents.spawn_rise` (WP-040 DEC-081: yalnız doğrulan gövdeler; WP-048) | ZombieRise (×3, toprak katmanı) | 3D | SFX-World | Ü | `spawn_dust` (yalnız kamera dışı rise) — WP-047 A |
| Ayağa kalktı / doğrudan indi | `EnemyCombatEvents.spawn_land` (rise_end ya da doğrulmasız spawn) | ZombieAggro{tier} (×3) | 3D | SFX-World | T | n/a — toz `spawn_rise`'da; kalkışsız doğuşta efekt yok (kamera dışı) |
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
| `sw_push` | `EnemyCombatEvents` · WP-055 Kagura Bell dalgası gövdeye çarptı (`Special` = KaguraBell; `Direction` = itiş yönü; `Seconds` = sersemleme, tier direnci Elite ×0,5 / Ultra ×0,2 → 0,45 / 0,225 / 0,09 s) | S-08 bağlar (aday: paylaşımlı:ZombieStagger + sw_kagura katmanı) | 3D | SFX-World | Ü | AP-014 `sw_kagura_*` sheet — S-07 bağlar (WP-047 B; itiş yönünde, tier direnciyle ölçek) |
| `sw_web` | `EnemyCombatEvents` · WP-055 Ofuda Loom mührü gövdeyi ağa aldı (`Special` = OfudaLoom; `Seconds` = ağ süresi: Standard kök 2,1 s / Special 1,5 / Elite 1,2 / Ultra 0,9; hız çarpanı 0 / 0,35 / 0,55 / 0,75) | S-08 bağlar (aday: sw_ofuda seal tık) | 3D | SFX-World | Ü | AP-014 `sw_ofuda_web` sheet gövdede `Seconds` boyunca — S-07 bağlar (WP-047 B) |
| `sw_pull` | `EnemyCombatEvents` · WP-055 Tide Jewel kuyusu bir çekim tıkı (`Special` = TideJewel; `Direction` = merkeze doğru; `Seconds` = kuyunun kalan süresi; Ultra çekilmez, o süre boyunca ×0,6 — kuyu patlayınca/`FieldEnded` ile kalkar) | S-08 bağlar (aday: paylaşımlı:SwBeaconLoop içinde, ayrı ses yok) | 3D | SFX-World | Ü | AP-014 `sw_tide_pull` iz — S-07 bağlar (WP-047 B; Ultra'da yalnız ayak köpüğü) |

## 3. Zırh (bölge başına: kask / gövde / bacak)

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Kırılma — bölge sesi (kask / gövde / bacak) | `EnemyCombatEvents.armor_break_{head,torso,leg}` (WP-040; WP-048) — tier ArmorBreak cue'sunun üstüne bölge vokali + 0,42 s plaka düşüşü | ArmorBreakHelmet / ArmorBreakBody / ArmorBreakLeg (×3, v007 T) + ArmorPieceDrop | 3D | SFX-World | T | zırh kırılma efekti bölge başına aynı (`ReportEnemyHit` ArmorBreak: sheet ×1.3 + `atlas_armorbreak` plaka ankrajı + splinter + kan A7) — AP-010 v003 |
| Vuruş — plaka dayanıyor | `CombatAudioEvent.ArmorHit` (rung 1) | ArmorImpact (×3, v006) | 3D | SFX-World | Ü/T | `fb_impact_armor` v003 (+ emissive) + `armor_crack_dust` — AP-010 v003 / AP-010B v003 |
| Hasar — plaka çatlıyor | `ArmorHit` rung ≥ 3 (fatigue) | ArmorCrack (×3, v006 T) | 3D | SFX-World | Ü/T | AP-008 durum mesh'i (K→D) + `armor_crack_dust` — AP-008 / AP-010B v003 |
| Kırılma — kask / gövde / bacak | `CombatAudioEvent.ArmorBreak` (slot `ArmorBreakInfo`) | ArmorBreak (×2, metal transient + lake kırılması) | 3D | SFX-World | Ü | `fb_impact_armor` v003 ×1.3 + `atlas_armorbreak` v003 parça patlaması + `decal_lacquer_splinter` v003 + kan A7 + 14 damlacık + hit-stop 35 ms + sarsıntı 0.075 — AP-010 v003 / AP-010B v003 |
| Parça düşme (yere) | `DamageEvents.ArmorBroken` + 0,42 s (`AudioEventRouter`; `ArmorVisualBinder.Broken` DD.Art'ta, Prototype erişemez) | ArmorPieceDrop (×2) | 3D | SFX-World | Ü | `atlas_armorbreak` 10 parça, gravite, 0.9 s (plaka ankrajı) — AP-010 v003; yerde parça mesh'i AP-008B Broken |

## 4. Kırılabilirler

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| AP-017 aileleri (fener taş / fener kâğıt / saksı / çit / balya / bank / tabela / shoji) | `BreakableProp.Broke` aile → ses (AP-017 main'e gelince aile anahtarı bağlanır; şimdi R) | Break{LanternStone,LanternPaper,Planter,Fence,Bale,Bench,Sign,Shoji} (×3, v007 T) | 3D | SFX-World | T/R | `BreakableVfx` (AP-017 olay tablosu v001): BreakStone/Ceramic → TL_ceramic kalıntı + taş tozu; BreakWood → TR_wood + ahşap tozu; BreakBamboo → TR_wood + `leaf_burst`; BreakPaper → BR_cloth + `paper_tear`; BreakStraw → BL_straw + `straw_puff` + bulut; prefab adı → kimlik (`BreakableVfx.EventIdFor`), `BreakableProp.Broke` — WP-047 C (S-05 WP-038 prefabları gelince aynı satır) |
| AP-003B small_breakable (seramik/çömlek) | WP-013 kırılma bağlaması → `SurfaceImpact*` + `BreakCeramic` | BreakCeramic (×2) | 3D | SFX-World | Ü | `BreakableProp` çip + BreakCeramic profili: TL_ceramic kalıntı + `fb_impact_stone` toz — WP-013 / WP-047 C (`BreakableVfx`, AP-017 olay kimlikleri) |
| AP-003B stone_lantern (isabet) | `SurfaceImpactStone` | ImpactStone | 3D | SFX-World | Ü | `fb_impact_stone` v003 + `decal_impact_stone`; kırılmada BreakStone (TL_ceramic + taş tozu 0.7 m); `lantern_flame` döngüsü fener kafesinde (`LanternFlames`, ≤40) — AP-010 v003 / WP-047 C/D |
| AP-003B bamboo_fence / bamboo_stalk (isabet) | `SurfaceImpactWood` (yüzey tablosunda bambu ayrı değil) | ImpactWood (bambu varyantı v4 kuyruğu) | 3D | SFX-World | Ü | `fb_impact_wood` v003 + dekal; kırılmada BreakBamboo (TR_wood + `leaf_burst` 8 + ahşap tozu) — AP-010 v003 / WP-047 C |
| AP-005B barrels / handcart / shed (ahşap isabet) | `SurfaceImpactWood` | ImpactWood | 3D | SFX-World | Ü | `fb_impact_wood` v003 + `decal_impact_wood` (SurfaceResolver → Wood) — AP-010 v003 |
| AP-005B strawbales (isabet) | `SurfaceImpactFoliage` | ImpactFoliage (×3, v006 T) | 3D | SFX-World | Ü/T | `fb_impact_wood` (Wood fallback); kırılmada BreakStraw: `straw_puff` 12 sap + `straw_puff_cloud` + BL_straw kalıntı — WP-047 C (AP-017 strawbale) |
| AP-005B tsukubai / mossrock (taş isabet) | `SurfaceImpactStone` | ImpactStone | 3D | SFX-World | Ü | `fb_impact_stone` v003 + `decal_impact_stone` — AP-010 v003 |
| Kağıt paravan / shoji (isabet) | `SurfaceImpactPaper` | ImpactPaper (×3, v006) | 3D | SFX-World | Ü/T | `paper_tear` 4×4 sprite patlaması (8 parça); kırılmada BreakPaper (BR_cloth + 10 parça) — AP-010B v003 / WP-047 C |

## 5. Çevre

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Look B — uzak gök gürültüsü | `AmbienceDirector.TickWetLook` (WetLook, 40–90 s, 52 m uzakta, yüksek) | AmbThunderDistant (×3, v007 T) | 3D | Ambience | T | n/a — ses; gök/ışık dili WP-029 look B |
| Look B — ıslak arduvaz damlası | scatter halkası (WetLook seti) | AmbDripSlate (×3, v007 T) | 3D | Ambience | T | n/a — ses; ıslak yüzey parıltısı WP-029/S-05 (devir) |
| Look B — koi havuzu su bed'i | `Ap005SurfaceTags.RegisterPond` → `AudioSurfaceHints.PondCenters` → havuz merkezinde 3D loop (6–30 m) | AmbPondBed (loop, v007 T) | 3D | Ambience | T | `DD/Water v1` + vuruş/adım `water_splash` halkası — WP-013 / WP-047 D |
| Ortak atmosfer (yağmur sonrası damla, rüzgâr) | `AmbienceDirector` ortak bed | AmbCommon (loop) | 2D | Ambience | Ü | `AtmosphereDrift` polen/sakura + `WindDriver` gust — WP-013 |
| Merkez avlu (taş, damla, yankı) | bölge `center` | AmbRegionCenter (loop) | 2D | Ambience | Ü | n/a — bölge sesi; görsel ortak atmosfer |
| Kuzey — koridor/ahşap rüzgârı | bölge `north` | AmbRegionNorth (loop) | 2D | Ambience | Ü | n/a — bölge sesi |
| Doğu — bambu korusu | bölge `east` | AmbRegionEast (loop) | 2D | Ambience | Ü | `DD/Foliage Wind` bambu salınımı — WP-013 |
| Güney — gölet / su kıyısı | bölge `south` | AmbRegionSouth (loop) | 2D | Ambience | Ü | `DD/Water v1` + vuruşta `water_splash` + halka + su kıyısı adımında küçük halka — WP-013 / AP-010B v003 / WP-047 D (ıslak parıltı look B → WP-029/S-05 devir) |
| Batı — teras / uçurum rüzgârı | bölge `west` | AmbRegionWest (loop) | 2D | Ambience | Ü | n/a — bölge sesi |
| Bambu tıkırtısı / ahşap gıcırtı / metal çınlama / taş oturması (scatter) | `AmbienceDirector` scatter (9–24 s) | AmbBambooKnock (×3, T) / AmbWoodCreak (×4) / AmbMetalRing (×3, T) / AmbStoneSettle (×3, T) — v006 | 3D | Ambience | Ü/T | n/a — scatter sesi; görsel karşılığı yok |
| Sakura / polen (rüzgâr sürüklenmesi) | ortak bed içinde (rüzgâr katmanı) | paylaşımlı:AmbCommon | 2D | Ambience | Ü | `AtmosphereDrift` polen + sakura — WP-013 |
| Fener / kapı / koridor yankısı | bölge reverb zone'ları (ADR-021-2: avlu taş / bambu kapalı / gölet açık) | reverb (klip yok) | 3D | — | Ü | `lantern_flame` döngüsü fener kafesinde (`LanternFlames`: fener renderer'ları, ≤40, 5 s tarama) — WP-047 D; look B ankrajı WP-029; kapı statik |
| Damla (tek, yakın) | scatter listesine eklendi | AmbDrip (×3) | 3D | Ambience | Ü | n/a — görsel yok |

## 6. Silah / Special Weapon

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Special kullanım — ateş karakteri (Kagura çan+hava / Ofuda kâğıt+ip / Tide su+parıltı) | WP-056 `SpecialUsageEvent.Fired` (WP-058; HeavyWeaponFire gövdesi üstüne) | SwFireKagura / SwFireOfuda / SwFireTide (×2, v009 T) | 2D-yakın | SFX-Player | T | `fb_muzzle` sw varyantı yok — Kagura: `sw_kagura_wave` sheet (WP-047 B), Ofuda: `sw_ofuda_dart` kart-ipliği devir, Tide: sheet yok (AP-014) devir |
| Special alan — doğdu (Kagura cephe one-shot / Ofuda ağ uğultusu loop / Tide kuyu loop) | `SpecialUsageEvent.FieldSpawned` → `AudioEventRouter.OnSpecialUsage` (3D döngüler alan merkezinde, FieldEnded'a kadar) | SwFieldKagura (×2) / SwFieldOfuda (loop) / SwFieldTide (loop) (v009 T) | 3D | SFX-World | T | Kagura `sw_kagura_wave` halka büyümesi + yer tutucu cephe (WP-047 B); Ofuda `seal_glyph` (WP-002/007); Tide kuyu — sheet yok, devir |
| Special alan — bitti (dalga kıyı / ağ çöküşü / kuyu içe çöküş) | `SpecialUsageEvent.FieldEnded` (döngü durur) | SwFieldEndKagura / SwFieldEndOfuda / SwFieldEndTide (v009 T) | 3D | SFX-World | T | Ofuda `sw_ofuda_collapse` çöküş patlaması (WP-047 B, `ReportSealLink`); Kagura cephe solması (halka alfa); Tide — devir |
| Special vuruş — bedene temas (dalga tokat / mühür yapışma / çekim tiki) | `SpecialUsageEvent.Hit` (Falloff01 × kazanç; Special başına 60 ms kısma) | SwHitKagura / SwHitOfuda / SwHitTide (×2, v009 T) | 3D | SFX-World | T | `fb_blood_splash` / zırh `impact_armor` aynı `ReportEnemyHit` yolu (WP-019); Ofuda mühür `seal_glyph`; ek temas VFX'i yok (WP-056 `Hit` VFX bağlaması devir) |
| Special Activated / Deactivated | `SpecialUsageEvent.Activated/Deactivated` | n/a — HeavyWeaponSpinUp / SpecialWeaponExpired sahibi | — | — | n/a | n/a — HUD/ses sahibi; `weapon_pickup_flash` alma anında (WP-019 wave 2) |
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
| Müzik — round bitti stinger'ı | `RunEvents.RoundCompleted` → `MusicDirector.PlayStinger` (WP-057; katmanlar 0,55'e duck, 1,5 s bırakma) | StingerRoundBreak (×2, v008 T) | 2D | Music | T | n/a — görsel yok |
| Müzik — Special alındı stinger'ı | `CombatAudioEvent.SpecialWeaponPickup` (dövüşte) | StingerSpecialPickup (×2, v008 T) | 2D | Music | T | n/a — görsel yok |
| Müzik — ölüm stinger'ı + sonuç döngüsü | `PlayerHealth.AnyPlayerDied` → StingerDeath, ardından `Music.results` | StingerDeath (v008 T) → MusicResults (loop) | 2D | Music | T | n/a — görsel yok |
| Müzik — koşu sonu stinger'ı | `RunAudioEvent.RunEnded` (ölümsüz) → StingerRunEnd → MusicResults | StingerRunEnd (v008 T) | 2D | Music | T | n/a — görsel yok |
| Müzik — menü döngüsü | `RunPhase.None` (koşu sonrası değil) → `MusicDirector.TickPhaseLoop` (koşu katmanları 0'a çekilir) | MusicMenu (45 s loop, v008 T) | 2D | Music | T | n/a — görsel yok |
| Müzik — tehdit katmanı tabanı | `ThreatMusicFeed.Layer` 0–3 (WP-033 histerezis) → perc/tension/accent tabanı (WP-057) | MusicPercussion/Tension/Accent | 2D | Music | Ü | n/a — görsel yok |
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
