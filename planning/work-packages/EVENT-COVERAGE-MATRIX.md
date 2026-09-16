# EVENT COVERAGE MATRIX — ses (S-08 / WP-021) + VFX (S-07 / WP-019)

> Kullanıcı direktifi (2026-09-16, §12): "Her şeyin kendine ait sesi ve efekti olacak; hiçbir detay atlanmasın." Ortak dosya: S-08 ses sütununu, S-07 VFX sütununu doldurur (satır çakışmasında ikinci biten rebase'de birleştirir). **Boş ses satırı = WP-021 DoD FAIL.**
>
> Ses sütunu okunuşu: `SFX dosya` = AP-011 klip kökü (`Assets/_Art/Audio/AP011/<kök>_nn.wav`) ya da `proc:<AudioSoundId>` (WP-002 prosedürel recipe, katalogda üretilmiş klip yok) ya da `paylaşımlı:<kök>` (başka satırın klibini mix-plan farkıyla kullanır). `Kaynak` = olayın koddaki tetiği. Durum: **Ü** üretildi (AP-011 v003), **P** prosedürel, **R** rezerve (tetik henüz yok). WP-021B: WP-018 (ADR-018-1) main'e geldi → ayak/nefes/ölüm-sonu satırları `CharacterAnimationEvents.Any` ile bağlandı (R kalktı). Kredi kalanı yetmeyen satırlar **P** ile sevk edilir ve "AP-011 v4 kuyruğu" bölümünde listelenir (kredi kararı kullanıcıda). **WP-021C:** **T** = türetilmiş (AP-011 v005/v006: mevcut 48k/24-bit kaynaklardan ofline katman/pitch/stretch, kredi 0; `EVENT-COVERAGE-MATRIX-UI.md` satırları §8'e taşındı, olay adları sabit); zemin: AP-005 placement aileleri sahne yüklenince `SurfaceTag` alır (`DD.Art.Ap005SurfaceTags`, ADR-021-1 v2), avlu zemini taş rotalarda taşa iyileşir.

## 1. Oyuncu

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Ayak sesi — taş | `anim.foot_l/foot_r` + aşağı ışın → `AudioSurfaceHints` → `SurfaceResolver` (WP-021C: quiet_paving/ring/gate/shore_path/kaya/fener + taş rota şeridi; etiketsiz = taş) | FootstepStone (×4) | 3D | SFX-Player | Ü | |
| Ayak sesi — ahşap (veranda/köprü) | `anim.foot_l/foot_r` + `SurfaceKind.Wood` | FootstepWood (×3) | 3D | SFX-Player | Ü | |
| Ayak sesi — toprak/çakıl | `anim.foot_l/foot_r` + `SurfaceKind.Dirt` | FootstepDirt (×4) | 3D | SFX-Player | Ü | |
| Ayak sesi — çimen/bitki | `anim.foot_l/foot_r` + `SurfaceKind.Foliage` | FootstepFoliage (×4) | 3D | SFX-Player | Ü | |
| Ayak sesi — su kıyısı/sığ su | `anim.foot_l/foot_r` + `SurfaceKind.Water` | FootstepWater (×4) | 3D | SFX-Player | Ü | |
| Koşu / strafe (kumaş + teçhizat hışırtısı) | `anim.foot_l/foot_r` + `Motor.Speed01 > 0,7` → +2,4 dB | aynı yüzey klibi (GearRustle katmanı v4 kuyruğu) | 3D | SFX-Player | Ü | |
| Ateş — SMG (Light) | `CombatAudioEvent.ClassicWeaponFire` intensity ≤ 0,87 | SmgFire (×5, v006 B Carbine) | 3D | SFX-Player | Ü/T | |
| Ateş — tüfek (Medium) | `ClassicWeaponFire` 0,87–1,25 | RifleFire (mekanik + gövde + avlu kuyruğu) | 3D | SFX-Player | Ü | |
| Ateş — pompalı (Heavy) | `ClassicWeaponFire` ≥ 1,25 | ShotgunFire | 3D | SFX-Player | Ü | |
| Ateş — ağır döner top (Special) | `HeavyWeaponFire` | HeavyFire (×4) | 3D | SFX-Player | Ü | |
| Boş tetik | `ClassicWeaponDryFire` | DryFire | 2D | SFX-Player | Ü | |
| Şarjör çıkar / tak / kurma | `ReloadStart` / `ReloadInsert` / `ReloadEnd` | ReloadStart / ReloadInsert / ReloadEnd | 2D | SFX-Player | Ü | |
| İsabet onayı / öldürme onayı | `HitMarker` / `KillMarker` | HitMarker (×6, v006) / KillMarker (×3, v006 T) | 2D | UI | Ü/T | |
| Hasar alma (hafif/ağır) | `PlayerHealth.AnyDamaged` (şiddet → gain) | PlayerDamage (×3) | 2D | UI | Ü | |
| Ölüm — çökme | `PlayerHealth.AnyPlayerDied` | PlayerDeath | 2D | UI | Ü | |
| Ölüm — koşu sonu vurgusu | `RunAudioEvent.RunEnded` | paylaşımlı:PlayerDeath (stinger) | 2D | UI | Ü | |
| Special alma | `SpecialWeaponPickup` | SwPickup | 2D | UI | Ü | |
| Special kullanma — döner top motoru | `HeavyWeaponSpinUp` (loop) / `HeavyWeaponSpinDown` | HeavyMotorLoop (v006 loop) / HeavySpinDown (v006) | 2D | SFX-Player | Ü/T | |
| Special kullanma — silah başına (Kagura Bell / Ofuda Loom / Tide Jewel) | WP-037 `SpecialWeaponUsage` tablosu: FireCue = `HeavyWeaponFire`, ActiveLoop = `HeavyWeaponSpinUp` (üçü bugün ortak) | HeavyFire (×4) / HeavyMotorLoop (v006 loop) — silah başına ayrı ses AP-011 v4 kuyruğu | 3D/2D | SFX-Player | Ü | |
| Special bitiş uyarısı / bitiş | `SpecialWeaponFinalSeconds` / `SpecialWeaponExpired` | SwFinalSeconds / SwExpired (×3, v006) | 2D | UI | Ü | |
| Gold toplama (tekli) / mıknatıs serisi | `RunAudioEvent.GoldCollected` streak 0–1 / ≥ 2 | GoldPickup (×3, sentez) / GoldMagnet | 2D | UI | Ü | |
| Upgrade satın alma / red / atlama | `UpgradePurchased` / `UpgradeRefused` / `UpgradeSkipped` | UpgradePurchase / UiDenied / UiBack | 2D | UI | Ü | |

## 2. Zombi

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Ayak — fit A (hafif) / B / C (ağır) | `anim.foot_l/foot_r` zombi (tier: Standard/Special → Light, Elite/Ultra → Heavy −1 st) | ZombieStepLight (×5) / ZombieStepHeavy (×4) | 3D | SFX-World | Ü | |
| Nefes (animasyon olayı) | `anim.breath` → ZombieIdle (yoklama sayacını yeniden kurar) | ZombieIdle (×4) | 3D | SFX-World | Ü | |
| Ölüm sonu — beden yere oturur | `anim.death_end` | paylaşımlı:ZombieStepHeavy (pitch 0,8) | 3D | SFX-World | Ü | |
| Idle / nefes (yakın mesafe hırıltı) — Standard | `AudioEventRouter` idle yoklaması (≤ 9 m, 6–14 s aralık, düşman başına, saldırı fazı dışında) | ZombieIdle (×4) | 3D | SFX-World | Ü | |
| Idle — Special / Elite / Ultra | idle yoklaması, `AudioEventMap.ZombieIdle(tier)` (WP-021C) | ZombieIdleSpecial / ZombieIdleElite / ZombieIdleUltra (×3) | 3D | SFX-World | T | |
| Aggro — ayağa kalktı, seni gördü ×4 tier | `anim.rise_end` → `AudioEventMap.ZombieAggro(tier)` (WP-021C; ZombieRise toprak, bu vokal) | ZombieAggro{Standard,Special,Elite,Ultra} (×3) | 3D | SFX-World | T | |
| Saldırı hazırlığı — Standard | `ZombieEnemy.AttackPhase → Windup` | ZombieWindupStandard (×3) | 3D | SFX-World | Ü | |
| Saldırı hazırlığı — Special (Lunge, kısa hazırlık) | Windup, tier Special (WP-021C) | ZombieWindupSpecial (×3) | 3D | SFX-World | T | |
| Saldırı hazırlığı — Elite | Windup, tier Elite | ZombieWindupElite (×3) | 3D | SFX-World | Ü | |
| Saldırı hazırlığı — Ultra | Windup, tier Ultra | ZombieWindupUltra (×3) | 3D | SFX-World | Ü | |
| Saldırı (savurma) ×4 tier | `AttackPhase → Active` | ZombieAttack{Standard,Elite,Ultra} (Ü) · ZombieAttackSpecial (T, ×3) | 3D | SFX-World | Ü/T | |
| Saldırı isabeti (oyuncuya) | `DamageEvents.PlayerAttacked` | AttackImpactBody (et/deri darbe) | 3D | SFX-World | Ü | |
| Hit — kafa (açık) | `EnemyDamaged` bölge Head | paylaşımlı:ZombieHit* + CriticalHit (×3, v006 T) | 3D | SFX-World | Ü | |
| Hit — gövde | `EnemyDamaged` bölge UpperBody | ZombieHit{Standard,Elite,Ultra} (Ü) · ZombieHitSpecial (T, ×3) | 3D | SFX-World | Ü/T | |
| Hit — bacak | `EnemyDamaged` bölge LowerBody | paylaşımlı:ZombieHit* (pitch −2 st) | 3D | SFX-World | Ü | |
| Ağır sendeleme (stagger) | `DamageEvents.EnemyDamaged.CausedStagger` (`AudioEventRouter`) | ZombieStagger (×3) | 3D | SFX-World | Ü | |
| Ölüm — öne çökme / geriye / diz / patlama | `DamageEvents.EnemyDied` (+ `CombatAudioEvent.EnemyDeath` beden) | ZombieDeath{Standard,Elite,Ultra} (×2–3) · ZombieDeathSpecial (T, ×3) + EnemyDeath (×3, v006 T) / HeavyEnemyDeath | 3D | SFX-World | Ü | |
| Spawn / yerden doğrulma | `ZombieEnemy.Live` giriş kenarı (`AudioEventRouter`) | ZombieRise (×3, toprak katmanı) | 3D | SFX-World | Ü | |
| Special / Elite / Ultra farkı | tier → vokal seti + `HeavyEnemyDeath`; WP-033 `ThreatMusicFeed.Accent` → müzik vurgu katmanı | tier'lı kökler (yukarıda) + MusicAccent | — | — | Ü | |

## 2b. Düşman olay yüzeyi — `EnemyCombatEvents` (WP-040, ADR-040-1)

> Tek kaynak: `DD.Prototype.Enemies.EnemyCombatEvents.Raised` (`EnemyCombatEventInfo`: kind, tier, beden, konum, yön, bölge, `AnimationDriven`, süre, `LifeId`). Satır adı = `info.RowName`. Ses sütunu bugün `AudioEventRouter`'ın yoklama/olay kaynaklarıyla aynı anlara düşer (WP-021); S-08 isterse bu yüzeye taşır (WP-021C). **VFX sütunu S-07 devri (WP-019 → WP-019C): boş satır = FAIL — her satır ya bir efekt kimliği ya da adlı devir taşır.** Vuruş anları DEC-029 pencerelerinden gelir (WP-018 klip olayları; yer tutucu/blok gövdede zamanlayıcı yedeği, `AnimationDriven=false`).

| Olay (satır) | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| `telegraph_start` | `EnemyCombatEvents` · saldırı Windup başlangıcı (Seconds = telegraph) | paylaşımlı:ZombieWindup{Standard,Elite,Ultra} (tier) | 3D | SFX-World | Ü | S-07 devir: `telegraph_glint` (göz/el parıltısı, tier rengi; Ultra 1,15 s uzun) — WP-019C |
| `attack_commit` | `EnemyCombatEvents` · Windup→Active (klip `hit_window_start`; yer tutucuda zamanlayıcı) | paylaşımlı:ZombieAttack{Standard,Elite,Ultra} | 3D | SFX-World | Ü | S-07 devir: `claw_swipe` (pençe izi yayı; Lunge/Sweep/Overhead şekli `Style`'a göre) — WP-019C |
| `attack_hit` | `EnemyCombatEvents` · bağlantı karesi (= `DamageEvents.PlayerAttacked`) | paylaşımlı:AttackImpactBody | 3D | SFX-World | Ü | WP-019 mevcut: oyuncu hasar vinyeti + kan sıçraması (`PlayerHitFeedback`) |
| `attack_miss` | `EnemyCombatEvents` · Active→Recovery, bağlantı yok (ceza penceresi) | paylaşımlı:ZombieAttack* kuyruğu (whoosh katmanı; WP-021C aday) | 3D | SFX-World | Ü | S-07 devir: `claw_swipe_miss` (aynı yay, toz/ iz sonu) — WP-019C |
| `stagger` | `EnemyCombatEvents` · REQ-124 sendeleme (`Seconds` = süre) / zırh kırılması ağır sendeleme klibi (`Seconds` = 1,4 tavan, `stagger_end`'e kadar; kırılma vuruşunda ikisi birden gelebilir) | ZombieStagger (×3) | 3D | SFX-World | Ü | WP-019 mevcut: hit reaction kan/toz (`HitFeedbackDirector`); ağır sendelemede S-07 devir: `stagger_dust` — WP-019C |
| `armor_break_head` / `_torso` / `_leg` | `EnemyCombatEvents` · `Region` (= `DamageEvents.ArmorBroken` anı) | ArmorBreak (×2) + ArmorPieceDrop | 3D | SFX-World | Ü | WP-019 mevcut: zırh kıymığı patlaması; S-07 devir: `armor_crack_dust` (bölge ankrajında lake tozu) — WP-019C |
| `death_standard` / `_special` / `_elite` / `_ultra` | `EnemyCombatEvents` · `Tier` (= `DamageEvents.EnemyDied` anı) | ZombieDeath{Standard,Elite,Ultra} (Special → Standard seti, WP-021C ayrı vokal) | 3D | SFX-World | Ü | WP-019 mevcut: ölüm kanı; S-07 devir: `death_special` (hızlı çökme tozu), `death_ultra` (ağır çarpma halkası) — WP-019C |
| `spawn_rise` | `EnemyCombatEvents` · DEC-081 kamera dışı doğuş, klip başlangıcı (`Seconds` 1,57) | ZombieRise (×3, toprak katmanı) | 3D | SFX-World | Ü | S-07 devir: `spawn_dust` (yerden kalkış toprak/toz) — WP-019C |
| `spawn_land` | `EnemyCombatEvents` · `rise_end` / kalkışsız doğuşta hemen | paylaşımlı:ZombieStepHeavy (pitch 0,8; yalnız kalkış sonrası) | 3D | SFX-World | Ü | S-07 devir: `spawn_dust` kuyruğu (ayak toz pufu) — WP-019C |

## 3. Zırh (bölge başına: kask / gövde / bacak)

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Vuruş — plaka dayanıyor | `CombatAudioEvent.ArmorHit` (rung 1) | ArmorImpact (×3, v006) | 3D | SFX-World | Ü/T | |
| Hasar — plaka çatlıyor | `ArmorHit` rung ≥ 3 (fatigue) | ArmorCrack (×3, v006 T) | 3D | SFX-World | Ü/T | |
| Kırılma — kask / gövde / bacak | `CombatAudioEvent.ArmorBreak` (slot `ArmorBreakInfo`) | ArmorBreak (×2, metal transient + lake kırılması) | 3D | SFX-World | Ü | |
| Parça düşme (yere) | `DamageEvents.ArmorBroken` + 0,42 s (`AudioEventRouter`; `ArmorVisualBinder.Broken` DD.Art'ta, Prototype erişemez) | ArmorPieceDrop (×2) | 3D | SFX-World | Ü | |

## 4. Kırılabilirler

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| AP-003B small_breakable (seramik/çömlek) | WP-013 kırılma bağlaması → `SurfaceImpact*` + `BreakCeramic` | BreakCeramic (×2) | 3D | SFX-World | Ü | |
| AP-003B stone_lantern (isabet) | `SurfaceImpactStone` | ImpactStone | 3D | SFX-World | Ü | |
| AP-003B bamboo_fence / bamboo_stalk (isabet) | `SurfaceImpactWood` (yüzey tablosunda bambu ayrı değil) | ImpactWood (bambu varyantı v4 kuyruğu) | 3D | SFX-World | Ü | |
| AP-005B barrels / handcart / shed (ahşap isabet) | `SurfaceImpactWood` | ImpactWood | 3D | SFX-World | Ü | |
| AP-005B strawbales (isabet) | `SurfaceImpactFoliage` | ImpactFoliage (×3, v006 T) | 3D | SFX-World | Ü/T | |
| AP-005B tsukubai / mossrock (taş isabet) | `SurfaceImpactStone` | ImpactStone | 3D | SFX-World | Ü | |
| Kağıt paravan / shoji (isabet) | `SurfaceImpactPaper` | ImpactPaper (×3, v006) | 3D | SFX-World | Ü/T | |

## 5. Çevre

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Ortak atmosfer (yağmur sonrası damla, rüzgâr) | `AmbienceDirector` ortak bed | AmbCommon (loop) | 2D | Ambience | Ü | |
| Merkez avlu (taş, damla, yankı) | bölge `center` | AmbRegionCenter (loop) | 2D | Ambience | Ü | |
| Kuzey — koridor/ahşap rüzgârı | bölge `north` | AmbRegionNorth (loop) | 2D | Ambience | Ü | |
| Doğu — bambu korusu | bölge `east` | AmbRegionEast (loop) | 2D | Ambience | Ü | |
| Güney — gölet / su kıyısı | bölge `south` | AmbRegionSouth (loop) | 2D | Ambience | Ü | |
| Batı — teras / uçurum rüzgârı | bölge `west` | AmbRegionWest (loop) | 2D | Ambience | Ü | |
| Bambu tıkırtısı / ahşap gıcırtı / metal çınlama / taş oturması (scatter) | `AmbienceDirector` scatter (9–24 s) | AmbBambooKnock (×3, T) / AmbWoodCreak (×4) / AmbMetalRing (×3, T) / AmbStoneSettle (×3, T) — v006 | 3D | Ambience | Ü/T | |
| Sakura / polen (rüzgâr sürüklenmesi) | ortak bed içinde (rüzgâr katmanı) | paylaşımlı:AmbCommon | 2D | Ambience | Ü | |
| Fener / kapı / koridor yankısı | bölge reverb zone'ları (ADR-021-2: avlu taş / bambu kapalı / gölet açık) | reverb (klip yok) | 3D | — | Ü | |
| Damla (tek, yakın) | scatter listesine eklendi | AmbDrip (×3) | 3D | Ambience | Ü | |

## 6. Silah / Special Weapon

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Yüzey isabeti ×8 (default/stone/wood/foliage/water/metal/dirt/paper) | `SurfaceImpact*` | ImpactStone/Wood/Metal/Dirt/Water (Ü) · ImpactDefault/Foliage (v006 T) · ImpactPaper (v006) | 3D | SFX-World | Ü | |
| Sekme / zincir arkı | `Ricochet` / `ChainArc` | Ricochet (×5, v006) / ChainArc (×4, v006) | 3D | SFX-World | Ü/T | |
| Patlama (Ofuda) | `Explosion` | Explosion (×2) | 3D | SFX-World | Ü | |
| Kagura dalgası / Tide kuyusu döngüsü | `SpecialWeaponBeaconLoop` (Tide, KS-12) | SwBeaconLoop (v006 loop) | 3D | UI | Ü/T | |
| Special spawn (gong, rezerv) | `SpecialWeaponSpawn` | SwSpawnGong | 3D | UI | Ü | |
| Special beacon (yerde) | `SpecialWeaponBeaconLoop` | SwBeaconLoop (v006 loop) | 3D | UI | Ü/T | |
| Yerde süresi doldu | `SpecialWeaponGroundExpired` | SwGroundExpired (×2, v006 T) | 3D | UI | Ü/T | |

## 7. Ekonomi / round

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Gold düşme | `RunAudioEvent.GoldSpawned` | GoldSpawn (×2) | 3D | UI | Ü | |
| Gold çekilme (mıknatıs) / toplama | `GoldCollected` | GoldMagnet / GoldPickup | 2D | UI | Ü | |
| Round başı / geri sayım | `RoundStart` / `CountdownTick` | RoundStart / RoundCountdownTick | 2D | UI | Ü | |
| Round sonu / havuz tükendi | `RoundComplete` / `RoundPoolExhausted` | RoundComplete / AmbMetalRing (×3, v006 T) (uzak çan) | 2D | UI | Ü | |
| Round arası geri çekilme | `RunPhase` RoundComplete/Upgrading → `MusicDirector` + snapshot RoundBreak | (mix; klip yok) | — | Music | Ü | |
| Tanıtım vurgusu (Elite/Ultra/Special varlığı) | `MusicDirector` accent katmanı | MusicAccent (loop) | 2D | Music | Ü | |
| Müzik katmanları | `MusicDirector` | MusicBase / MusicPercussion / MusicTension (loop) | 2D | Music | Ü | |

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
