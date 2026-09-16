# EVENT COVERAGE MATRIX — ses (S-08 / WP-021) + VFX (S-07 / WP-019)

> Kullanıcı direktifi (2026-09-16, §12): "Her şeyin kendine ait sesi ve efekti olacak; hiçbir detay atlanmasın." Ortak dosya: S-08 ses sütununu, S-07 VFX sütununu doldurur (satır çakışmasında ikinci biten rebase'de birleştirir). **Boş ses satırı = WP-021 DoD FAIL.**
>
> Ses sütunu okunuşu: `SFX dosya` = AP-011 klip kökü (`Assets/_Art/Audio/AP011/<kök>_nn.wav`) ya da `proc:<AudioSoundId>` (WP-002 prosedürel recipe, katalogda üretilmiş klip yok) ya da `paylaşımlı:<kök>` (başka satırın klibini mix-plan farkıyla kullanır). `Kaynak` = olayın koddaki tetiği. Durum: **Ü** üretildi (AP-011 v003), **P** prosedürel, **R** rezerve (tetik henüz yok). WP-021B: WP-018 (ADR-018-1) main'e geldi → ayak/nefes/ölüm-sonu satırları `CharacterAnimationEvents.Any` ile bağlandı (R kalktı). Kredi kalanı yetmeyen satırlar **P** ile sevk edilir ve "AP-011 v4 kuyruğu" bölümünde listelenir (kredi kararı kullanıcıda).

## 1. Oyuncu

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Ayak sesi — taş | `anim.foot_l/foot_r` + aşağı ışın → `SurfaceResolver` (etiketsiz = taş) | FootstepStone (×4) | 3D | SFX-Player | Ü | |
| Ayak sesi — ahşap (veranda/köprü) | `anim.foot_l/foot_r` + `SurfaceKind.Wood` | FootstepWood (×3) | 3D | SFX-Player | Ü | |
| Ayak sesi — toprak/çakıl | `anim.foot_l/foot_r` + `SurfaceKind.Dirt` | FootstepDirt (×4) | 3D | SFX-Player | Ü | |
| Ayak sesi — çimen/bitki | `anim.foot_l/foot_r` + `SurfaceKind.Foliage` | FootstepFoliage (×4) | 3D | SFX-Player | Ü | |
| Ayak sesi — su kıyısı/sığ su | `anim.foot_l/foot_r` + `SurfaceKind.Water` | FootstepWater (×4) | 3D | SFX-Player | Ü | |
| Koşu / strafe (kumaş + teçhizat hışırtısı) | `anim.foot_l/foot_r` + `Motor.Speed01 > 0,7` → +2,4 dB | aynı yüzey klibi (GearRustle katmanı v4 kuyruğu) | 3D | SFX-Player | Ü | |
| Ateş — SMG (Light) | `CombatAudioEvent.ClassicWeaponFire` intensity ≤ 0,87 | proc:SmgFire | 3D | SFX-Player | P | |
| Ateş — tüfek (Medium) | `ClassicWeaponFire` 0,87–1,25 | RifleFire (mekanik + gövde + avlu kuyruğu) | 3D | SFX-Player | Ü | |
| Ateş — pompalı (Heavy) | `ClassicWeaponFire` ≥ 1,25 | ShotgunFire | 3D | SFX-Player | Ü | |
| Ateş — ağır döner top (Special) | `HeavyWeaponFire` | HeavyFire (×4) | 3D | SFX-Player | Ü | |
| Boş tetik | `ClassicWeaponDryFire` | DryFire | 2D | SFX-Player | Ü | |
| Şarjör çıkar / tak / kurma | `ReloadStart` / `ReloadInsert` / `ReloadEnd` | ReloadStart / ReloadInsert / ReloadEnd | 2D | SFX-Player | Ü | |
| İsabet onayı / öldürme onayı | `HitMarker` / `KillMarker` | proc:HitMarker / proc:KillMarker | 2D | UI | P | |
| Hasar alma (hafif/ağır) | `PlayerHealth.AnyDamaged` (şiddet → gain) | PlayerDamage (×3) | 2D | UI | Ü | |
| Ölüm — çökme | `PlayerHealth.AnyPlayerDied` | PlayerDeath | 2D | UI | Ü | |
| Ölüm — koşu sonu vurgusu | `RunAudioEvent.RunEnded` | paylaşımlı:PlayerDeath (stinger) | 2D | UI | Ü | |
| Special alma | `SpecialWeaponPickup` | SwPickup | 2D | UI | Ü | |
| Special kullanma — döner top motoru | `HeavyWeaponSpinUp` (loop) / `HeavyWeaponSpinDown` | proc:HeavyMotorLoop / proc:HeavySpinDown | 2D | SFX-Player | P | |
| Special bitiş uyarısı / bitiş | `SpecialWeaponFinalSeconds` / `SpecialWeaponExpired` | SwFinalSeconds / proc:SwExpired | 2D | UI | Ü/P | |
| Gold toplama (tekli) / mıknatıs serisi | `RunAudioEvent.GoldCollected` streak 0–1 / ≥ 2 | GoldPickup (×3, sentez) / GoldMagnet | 2D | UI | Ü | |
| Upgrade satın alma / red / atlama | `UpgradePurchased` / `UpgradeRefused` / `UpgradeSkipped` | UpgradePurchase / UiDenied / UiBack | 2D | UI | Ü | |

## 2. Zombi

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Ayak — fit A (hafif) / B / C (ağır) | `anim.foot_l/foot_r` zombi (tier: Standard/Special → Light, Elite/Ultra → Heavy −1 st) | ZombieStepLight (×5) / ZombieStepHeavy (×4) | 3D | SFX-World | Ü | |
| Nefes (animasyon olayı) | `anim.breath` → ZombieIdle (yoklama sayacını yeniden kurar) | ZombieIdle (×4) | 3D | SFX-World | Ü | |
| Ölüm sonu — beden yere oturur | `anim.death_end` | paylaşımlı:ZombieStepHeavy (pitch 0,8) | 3D | SFX-World | Ü | |
| Idle / nefes (yakın mesafe hırıltı) | `AudioEventRouter` idle yoklaması (≤ 9 m, 6–14 s aralık, düşman başına, saldırı fazı dışında) | ZombieIdle (×4) | 3D | SFX-World | Ü | |
| Saldırı hazırlığı — Standard/Special | `ZombieEnemy.AttackPhase → Windup` | ZombieWindupStandard (×3) | 3D | SFX-World | Ü | |
| Saldırı hazırlığı — Elite | Windup, tier Elite | ZombieWindupElite (×3) | 3D | SFX-World | Ü | |
| Saldırı hazırlığı — Ultra | Windup, tier Ultra | ZombieWindupUltra (×3) | 3D | SFX-World | Ü | |
| Saldırı (savurma) ×3 tier | `AttackPhase → Active` | ZombieAttack{Standard,Elite,Ultra} | 3D | SFX-World | Ü | |
| Saldırı isabeti (oyuncuya) | `DamageEvents.PlayerAttacked` | AttackImpactBody (et/deri darbe) | 3D | SFX-World | Ü | |
| Hit — kafa (açık) | `EnemyDamaged` bölge Head | paylaşımlı:ZombieHit* + proc:CriticalHit | 3D | SFX-World | Ü/P | |
| Hit — gövde | `EnemyDamaged` bölge UpperBody | ZombieHit{Standard,Elite,Ultra} | 3D | SFX-World | Ü | |
| Hit — bacak | `EnemyDamaged` bölge LowerBody | paylaşımlı:ZombieHit* (pitch −2 st) | 3D | SFX-World | Ü | |
| Ağır sendeleme (stagger) | `DamageEvents.EnemyDamaged.CausedStagger` (`AudioEventRouter`) | ZombieStagger (×3) | 3D | SFX-World | Ü | |
| Ölüm — öne çökme / geriye / diz / patlama | `DamageEvents.EnemyDied` (+ `CombatAudioEvent.EnemyDeath` beden) | ZombieDeath{Standard,Elite,Ultra} (×2–3) + proc:EnemyDeath / HeavyEnemyDeath | 3D | SFX-World | Ü | |
| Spawn / yerden doğrulma | `ZombieEnemy.Live` giriş kenarı (`AudioEventRouter`) | ZombieRise (×3, toprak katmanı) | 3D | SFX-World | Ü | |
| Special / Elite / Ultra farkı | tier → vokal seti + `HeavyEnemyDeath`; WP-033 `ThreatMusicFeed.Accent` → müzik vurgu katmanı | tier'lı kökler (yukarıda) + MusicAccent | — | — | Ü | |

## 3. Zırh (bölge başına: kask / gövde / bacak)

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Vuruş — plaka dayanıyor | `CombatAudioEvent.ArmorHit` (rung 1) | proc:ArmorImpact | 3D | SFX-World | P | |
| Hasar — plaka çatlıyor | `ArmorHit` rung ≥ 3 (fatigue) | proc:ArmorCrack | 3D | SFX-World | P | |
| Kırılma — kask / gövde / bacak | `CombatAudioEvent.ArmorBreak` (slot `ArmorBreakInfo`) | ArmorBreak (×2, metal transient + lake kırılması) | 3D | SFX-World | Ü | |
| Parça düşme (yere) | `DamageEvents.ArmorBroken` + 0,42 s (`AudioEventRouter`; `ArmorVisualBinder.Broken` DD.Art'ta, Prototype erişemez) | ArmorPieceDrop (×2) | 3D | SFX-World | Ü | |

## 4. Kırılabilirler

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| AP-003B small_breakable (seramik/çömlek) | WP-013 kırılma bağlaması → `SurfaceImpact*` + `BreakCeramic` | BreakCeramic (×2) | 3D | SFX-World | Ü | |
| AP-003B stone_lantern (isabet) | `SurfaceImpactStone` | ImpactStone | 3D | SFX-World | Ü | |
| AP-003B bamboo_fence / bamboo_stalk (isabet) | `SurfaceImpactWood` (yüzey tablosunda bambu ayrı değil) | ImpactWood (bambu varyantı v4 kuyruğu) | 3D | SFX-World | Ü | |
| AP-005B barrels / handcart / shed (ahşap isabet) | `SurfaceImpactWood` | ImpactWood | 3D | SFX-World | Ü | |
| AP-005B strawbales (isabet) | `SurfaceImpactFoliage` | proc:ImpactFoliage | 3D | SFX-World | P | |
| AP-005B tsukubai / mossrock (taş isabet) | `SurfaceImpactStone` | ImpactStone | 3D | SFX-World | Ü | |
| Kağıt paravan / shoji (isabet) | `SurfaceImpactPaper` | proc:ImpactPaper | 3D | SFX-World | P | |

## 5. Çevre

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Ortak atmosfer (yağmur sonrası damla, rüzgâr) | `AmbienceDirector` ortak bed | AmbCommon (loop) | 2D | Ambience | Ü | |
| Merkez avlu (taş, damla, yankı) | bölge `center` | AmbRegionCenter (loop) | 2D | Ambience | Ü | |
| Kuzey — koridor/ahşap rüzgârı | bölge `north` | AmbRegionNorth (loop) | 2D | Ambience | Ü | |
| Doğu — bambu korusu | bölge `east` | AmbRegionEast (loop) | 2D | Ambience | Ü | |
| Güney — gölet / su kıyısı | bölge `south` | AmbRegionSouth (loop) | 2D | Ambience | Ü | |
| Batı — teras / uçurum rüzgârı | bölge `west` | AmbRegionWest (loop) | 2D | Ambience | Ü | |
| Bambu tıkırtısı / ahşap gıcırtı / metal çınlama / taş oturması (scatter) | `AmbienceDirector` scatter (9–24 s) | proc:AmbBambooKnock / AmbWoodCreak / AmbMetalRing / AmbStoneSettle | 3D | Ambience | P | |
| Sakura / polen (rüzgâr sürüklenmesi) | ortak bed içinde (rüzgâr katmanı) | paylaşımlı:AmbCommon | 2D | Ambience | Ü | |
| Fener / kapı / koridor yankısı | bölge reverb zone'ları (ADR-021-2: avlu taş / bambu kapalı / gölet açık) | reverb (klip yok) | 3D | — | Ü | |
| Damla (tek, yakın) | scatter listesine eklendi | AmbDrip (×3) | 3D | Ambience | Ü | |

## 6. Silah / Special Weapon

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Yüzey isabeti ×8 (default/stone/wood/foliage/water/metal/dirt/paper) | `SurfaceImpact*` | ImpactStone/Wood/Metal/Dirt/Water (Ü) · proc:ImpactDefault/Foliage/Paper (P) | 3D | SFX-World | Ü/P | |
| Sekme / zincir arkı | `Ricochet` / `ChainArc` | proc:Ricochet / proc:ChainArc | 3D | SFX-World | P | |
| Patlama (Ofuda) | `Explosion` | Explosion (×2) | 3D | SFX-World | Ü | |
| Kagura dalgası / Tide kuyusu döngüsü | `SpecialWeaponBeaconLoop` (Tide, KS-12) | proc:SwBeaconLoop | 3D | UI | P | |
| Special spawn (gong, rezerv) | `SpecialWeaponSpawn` | SwSpawnGong | 3D | UI | Ü | |
| Special beacon (yerde) | `SpecialWeaponBeaconLoop` | proc:SwBeaconLoop | 3D | UI | P | |
| Yerde süresi doldu | `SpecialWeaponGroundExpired` | proc:SwGroundExpired | 3D | UI | P | |

## 7. Ekonomi / round

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Gold düşme | `RunAudioEvent.GoldSpawned` | GoldSpawn (×2) | 3D | UI | Ü | |
| Gold çekilme (mıknatıs) / toplama | `GoldCollected` | GoldMagnet / GoldPickup | 2D | UI | Ü | |
| Round başı / geri sayım | `RoundStart` / `CountdownTick` | RoundStart / RoundCountdownTick | 2D | UI | Ü | |
| Round sonu / havuz tükendi | `RoundComplete` / `RoundPoolExhausted` | RoundComplete / proc:AmbMetalRing (uzak çan) | 2D | UI | Ü/P | |
| Round arası geri çekilme | `RunPhase` RoundComplete/Upgrading → `MusicDirector` + snapshot RoundBreak | (mix; klip yok) | — | Music | Ü | |
| Tanıtım vurgusu (Elite/Ultra/Special varlığı) | `MusicDirector` accent katmanı | MusicAccent (loop) | 2D | Music | Ü | |
| Müzik katmanları | `MusicDirector` | MusicBase / MusicPercussion / MusicTension (loop) | 2D | Music | Ü | |

## 8. UI

| Olay | Kaynak (tetik) | SFX dosya | 2D/3D | Bus | Durum | VFX (S-07) |
|---|---|---|---|---|---|---|
| Seçim hareketi (her buton/liste) | `PrototypeAudio.Navigate` | UiNavigate | 2D | UI | Ü | |
| Onay (her buton) | `PrototypeAudio.Confirm` | UiConfirm | 2D | UI | Ü | |
| Geri / kapat | `PrototypeAudio.Back` | UiBack (×2) | 2D | UI | Ü | |
| Hata / reddedildi | `PrototypeAudio.Denied` | UiDenied (×2) | 2D | UI | Ü | |
| Ekran geçişi (Menu ↔ Run ↔ Results) | `RunPhase` → snapshot (Menu/Run/RoundBreak) + `UiTransition` | UiTransition | 2D | UI | Ü | |
| HUD uyarısı — Special son saniyeler / düşük can | `SpecialWeaponFinalSeconds` / `PlayerHealth.AnyDamaged` düşük can gain | SwFinalSeconds / PlayerDamage | 2D | UI | Ü | |
| Pause aç / kapat | `RunFlowClock` pause hold → snapshot Pause (+ `PrototypeAudio.Back`) | UiBack + snapshot | 2D | UI | Ü | |

## AP-011 v4 kuyruğu (kredi yetmezse; karar kullanıcıda)

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
