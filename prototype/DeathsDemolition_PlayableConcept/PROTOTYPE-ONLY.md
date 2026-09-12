# PROTOTYPE-ONLY VALUES

**Status update — 2026-09-10:** The user has approved this project as the starting
codebase for the final game. Values in this document remain placeholders unless an
explicit later decision approves them. Approved exceptions, such as proximity-magnet
gold collection, are identified below and must be reclassified during WP-000.

This project began as an experimental visualisation prototype. Its original numbers,
names, weapons, enemy abilities and economy values were created so the prototype could
run. The original statement that `planning/` and `game/` were untouched is historical;
the current production conversion is governed by the planning documents.

Every tunable value carries a `[PrototypeOnly(...)]` attribute
(`Assets/_Prototype/Core/Config/PrototypeOnlyAttribute.cs`) recording which still-open
planning decision the value temporarily occupies, and whether the number has any source
in the request at all. The tag is visible in the Inspector
(`Core/Editor/PrototypeOnlyDrawer.cs`) and in the F1 debug panel, so nobody can read a
number off the screen without also reading where it came from.

**All line references below point at the attribute line, not the field line** — the
field is the line immediately after, or two lines after where a `[Range]` intervenes.

---

## 0 · The count, and where the values actually live

Counted by static scan of every `.cs` file under `Assets/_Prototype` on 2026-08-18 (**historical**).
Re-count as of WP-000 Phase 2 (2026-09-10): **491** `[PrototypeOnly(` declarations
(`grep -rc "\[PrototypeOnly(" --include=*.cs`); **485 as of WP-002 (2026-09-11)** — six
`ZombieEnemy` `*EffectScale` constants (class 2, enemy-side burst scales) were removed
when `HitFeedbackDirector` became the single owner of hit dressing
(`find Assets/_Prototype -name '*.cs' -exec grep -o "\[PrototypeOnly(" {} + | wc -l`; a scan
without the `.cs` filter also counts two mentions in `Tests/OTOMATIK-KAPI.md`). The invented split and the per-section
tables below are re-issued after the WP-000 Phase 4 classification (see the WP-000 report).
Method: parse every `[PrototypeOnly(...)]` declaration including multi-line ones, read
its first argument (the pending decision) and its third argument (`invented`).

| Metric | Count |
|---|---|
| `[PrototypeOnly(...)]` declarations in code | **485** (2026-09-11, WP-002; 491 at 2026-09-10; 337 at 2026-08-18) |
| Carrying an origin tag | 485 — the attribute *is* the tag |
| Declarations with **no source in the request at all** (`invented: true`) | **237 as of 2026-08-18; re-count pending WP-000 Phase 4** |
| Literal `PROTOTYPE-ONLY` string occurrences under `Assets/` | 163 (156 in `.cs`, 6 in `.md`, 1 line carries two) |

### The split that matters

| Where | Declarations | Invented |
|---|---|---|
| Inside `PrototypeConfig` (`Core/Config/*.cs`, the ten sections) | 178 | 78 |
| **Outside `PrototypeConfig`, in per-system tuning structs** | **159** | **159** |

> **Every value that lives outside `PrototypeConfig` is invented, and none of them
> appear in the F1 Config tab.** `ConfigFieldIndex.Build()`
> (`Core/Debug/ConfigFieldIndex.cs`) reflects `PrototypeConfig` and nothing else, and
> the headless validator (`Tools > Deaths Demolition > Validate Prototype Core`,
> `Core/Editor/PrototypeCoreValidation.cs:109-129`) counts the same set.
>
> That is why the earlier revision of this document said **78**: on 2026-08-17 04:56
> only the central config asset existed. **78 was never wrong — it was 33% of the
> eventual total.** The remaining 159 arrived with the eight later system tasks, each
> carrying its own `*Tuning` struct, and no automated surface has ever counted them.

### Distribution by module

| Module | Declarations | Invented | Where the invented ones live |
|---|---|---|---|
| `Core` | 178 | 78 | `EnemyConfigSection` 17 · `FeedbackConfigSection` 17 · `WeaponConfigSection` 13 · `RoundConfigSection` 12 · `EconomyConfigSection` 9 · `PlayerConfigSection` 7 · `ProgressionConfigSection` 3 |
| `Player` | 41 | 41 | `PlayerProceduralAnimator` 17 · `PlayerUpgradeTuning` 8 · `PlayerMotor` 5 · `PlayerRigFactory` 5 · `PlayerDamageFeedback` 4 · `PlayerAim` 2 |
| `Weapons` | 35 | 35 | `WeaponUpgradeTuning` 21 · `WeaponVisualRig` 7 · `WeaponController` 6 · `SpecialWeaponDirector` 1 |
| `Feedback` | 18 | 18 | `HitFeedbackDirector` 18 |
| `Economy` | 17 | 17 | `GoldOrbTuning` 17 |
| `Spawn` | 16 | 16 | `SpawnWeightTuning` 13 · `SpawnPointDefinition` 2 · `EnemySpawnDirector` 1 |
| `Navigation` | 15 | 15 | `CrowdTuning` 15 |
| `Camera` | 9 | 9 | `PrototypeCameraRig` 5 · `CameraOcclusionFader` 4 |
| `World` | 4 | 4 | `WorldBootstrap` 4 |
| `Input` | 2 | 2 | `PrototypeInputSource` 2 |
| `Rounds` | 2 | 2 | `RunDirector` 2 |
| **Total** | **337** | **237** | |

`Damage`, `Profile`, `UI`, `Audio` and `Tests` declare no `[PrototypeOnly]` fields of
their own; they read `PrototypeConfig`.

### Distribution by occupied planning decision

| Pending decision | Declarations | of which invented |
|---|---|---|
| `UpgradesAndGold` | 66 | 55 |
| `CombatScaling` | 30 | 8 |
| `SpecialWeapon` | 12 | 2 |
| `ProfileXp` | 11 | 0 |
| `ExplosionShares` | 3 | 0 |
| `HealthRegen` | 3 | 0 |
| `None` — occupies no pending planning line | 212 | 172 |

**Read the `UpgradesAndGold` row carefully: 55 of its 66 values were invented.** It is
by a wide margin the most heavily pre-empted planning line in this prototype — and gold
is the next decision topic on the roadmap.

---

## 1 · Which pending planning decision each value occupies

Source: `planning/00-PROJECT-PLAN.md`, section
*"Kararlaştırılması sonraya bırakılan alanlar"*. The Turkish text is quoted verbatim
because it is the traceability key; it must not be translated in code
(`Core/Config/PendingDecisions.cs`).

**Six of the seven pending lines are currently occupied by this prototype's numbers.**
This mapping is the point of this document.

---

### 1.1 · `Kesin can, hasar, zırh ve round ölçeklendirme değerleri`
key `PendingDecisions.CombatScaling` · **30 values · 8 invented**

| Occupied by | File · line | Source |
|---|---|---|
| Player max health `100` | `PlayerConfigSection.cs:10` | request |
| Classic weapon damage (all three) | `WeaponConfigSection.cs:20` | request |
| Special Weapon damage `16` | `WeaponConfigSection.cs:73` | request |
| Standard base health `34` | `EnemyConfigSection.cs:216` | request |
| Per-tier health multiplier `0.85` / `2.2` / `5` | `EnemyConfigSection.cs:55` | request |
| Per-tier move speed | `EnemyConfigSection.cs:58` | request (Standard `2.7` only; the other three are prototype picks) |
| Per-tier attack damage | `EnemyConfigSection.cs:61` | request (Standard `10` only) |
| Armour durability `22` / `38` / `28` | `EnemyConfigSection.cs:124,127,130` | request |
| Exposed-region multipliers: head `2×`, upper `1×`, leg `0.8×` | `EnemyConfigSection.cs:133,136,139` | request |
| **Armour spawn chance per slot, growth per round, ceiling** | `EnemyConfigSection.cs:14,17,20,23,26` | **invented ×5** |
| **Tier spawn weight, growth per round, maximum** | `EnemyConfigSection.cs:88,91,94` | **invented ×3** |
| Pool formula coefficients `12` / `6` / `2.5` / `1.15` | `RoundConfigSection.cs:15,18,21,24` | request |
| Active-target coefficients `7` / `2`, cap `32` | `RoundConfigSection.cs:28,31,34` | request |
| Health `+0.12` and damage `+0.08` per round | `RoundConfigSection.cs:41,44` | request |

---

### 1.2 · `Patlama hasarının bölgelere dağılım oranları`
key `PendingDecisions.ExplosionShares` · **3 values · 0 invented**

| Occupied by | File · line |
|---|---|
| Head share `25%` | `EnemyConfigSection.cs:185` |
| Upper body share `45%` | `EnemyConfigSection.cs:188` |
| Lower body share `30%` | `EnemyConfigSection.cs:191` |

The blast radius `3.2 m` and falloff exponent `1` sitting immediately below them
(`EnemyConfigSection.cs:194,197`) are tagged `None` and **invented** — the request never
states them, so they do not pre-empt this line.

---

### 1.3 · `Profil XP değerleri, formülü ve seviye eğrisi`
key `PendingDecisions.ProfileXp` · **11 values · 0 invented — every one is a request value**

| Occupied by | File · line |
|---|---|
| `1` XP per kill | `ProgressionConfigSection.cs:55` |
| Round XP `15 + 5 × round` | `ProgressionConfigSection.cs:58,61` |
| Level requirement `100 + 75 × (Level-1)` | `ProgressionConfigSection.cs:64,67` |
| Gold grants no XP | `ProgressionConfigSection.cs:70` |
| Playtime grants no XP | `ProgressionConfigSection.cs:73` |
| Incomplete round grants no round XP | `ProgressionConfigSection.cs:76` |
| Tier kill bonuses `+1` / `+3` / `+8` | `EnemyConfigSection.cs:82` |
| Classic weapon unlock levels `2`, `3` | `WeaponConfigSection.cs:59` |
| Branch unlock levels — Explosion `2`, Chain `3` | `EconomyConfigSection.cs:35` |

This is the one pending line the prototype occupies entirely with numbers the request
supplied. **It is also the line whose consequence is broken** — the unlock ladder is
exhausted in a single run. See §4.

---

### 1.4 · `Geliştirme seçenekleri, paternleri ve gold ekonomisinin sayısal ayrıntıları`
key `PendingDecisions.UpgradesAndGold` · **66 values · 55 invented**

| Occupied by | File · line | Source |
|---|---|---|
| Gold rewards `2` / `5` / `10` / `25` | `EnemyConfigSection.cs:79` | request |
| Gold collection mode `ProximityMagnet` | `EconomyConfigSection.cs:53` | **approved product decision on 2026-09-10**; the other comparison modes are development-only |
| Magnet radius `3.5 m` | `EconomyConfigSection.cs:57` | request |
| `3` cards per round, unspent gold carries over | `EconomyConfigSection.cs:76,82` | request |
| **No reroll** (`AllowReroll = false`) | `EconomyConfigSection.cs:79` | **contradicts DEC-050 / REQ-079** (one paid reroll per round); not implemented — tracked as an implementation gap for a later package, not a product decision |
| Cost band `15`-`45` sanity clamp | `EconomyConfigSection.cs:85,88` | request |
| Ten branch identities, display names, base costs | `EconomyConfigSection.cs:14,17,20` (values at `:91-152`) | request names the branches; the individual costs are prototype picks inside the stated band |
| **Cost growth per tier, max tier, effect per tier, offer weight** — for every branch | `EconomyConfigSection.cs:23,26,29,32` | **invented ×4** |
| **Magnet speed and acceleration, contact pickup radius, orb ceiling** | `EconomyConfigSection.cs:60,63,69,72` | **invented ×4** |
| **Orb lifetime** (`OrbLifetime = 22f`) | `EconomyConfigSection.cs:66` | **contradicts DEC-045 / REQ-076** (gold does not disappear within a round; auto-collected at round end) — product decision already made → implementation gap, not a tunable |
| **Gold orb presentation and motion** — hop duration/height, scatter min/max, rest height, bob amplitude/frequency, spin speed, flash duration/scale, streak window, diameter, colour, light budget/range/intensity, high-value scale | `Economy/GoldOrbTuning.cs:25-81` | **invented ×17** |
| **Piercing / Ricochet / Explosion / Chain effect magnitudes** — target counts, damage falloff, base and per-tier chances, radii, damage scales | `Weapons/WeaponUpgradeTuning.cs:53-119` | **invented ×17** |
| **Fire Rate and Reload Speed bonuses and their clamps** | `Weapons/WeaponUpgradeTuning.cs:126-139` | **invented ×4** |
| **Movement Speed, Maximum Health, Regen Delay and Regen Rate bonuses and their clamps** | `Player/PlayerUpgradeTuning.cs:74-122` | **invented ×8** |

This is the deepest pre-emption in the prototype. **Every number that determines what an
upgrade is actually worth was invented**, and 25 of those live outside `PrototypeConfig`
in `WeaponUpgradeTuning` and `PlayerUpgradeTuning`, where neither the F1 Config tab nor
the validator can see them.

---

### 1.5 · `Special Weapon türleri, çıkacağı roundlar ve süre değerleri`
key `PendingDecisions.SpecialWeapon` · **12 values · 2 invented**

| Occupied by | File · line | Source |
|---|---|---|
| Name `Heavy Rotary Cannon` | `WeaponConfigSection.cs:70` | request |
| Active duration `11 s`, ground lifetime `12 s` | `WeaponConfigSection.cs:79,82` | request |
| First eligible round (after round 3) | `WeaponConfigSection.cs:91` | request |
| Base chance `35%`, cap `65%` | `WeaponConfigSection.cs:94,102` | request |
| Spawn window `30%`-`70%` of round progress | `WeaponConfigSection.cs:106,110` | request |
| Second spawn from round `8` at `10%` | `WeaponConfigSection.cs:114,117` | request |
| **Chance growth per round** | `WeaponConfigSection.cs:98` | **invented** |
| **Final-seconds warning length** | `WeaponConfigSection.cs:121` | **invented** |

The request says the chance "may rise slowly with rounds but must not exceed 65%". It
never says how fast. The growth rate is ours.

---

### 1.6 · `Otomatik can yenilenmesinin hasarsız bekleme süresi, yenilenme hızı ve sınırı`
key `PendingDecisions.HealthRegen` · **3 values · 0 invented**

| Occupied by | File · line |
|---|---|
| Regen delay `4.5 s` | `PlayerConfigSection.cs:22` |
| Regen rate `12 HP/s` | `PlayerConfigSection.cs:25` |
| Regen ceiling = full health (`1.0`) | `PlayerConfigSection.cs:28` |

The narrowest occupation in the prototype: three values, all supplied by the request,
and the pending line asks for exactly those three things.

---

### 1.7 · `Hedef işletim sistemi, mağaza ve dağıtım modeli`

**Not touched.** This project occupies nothing on this line. A macOS Player build is
produced for verification only (WP-000, gate G6); it is not a platform, store or
distribution decision — those remain deferred (`planning/03-ROADMAP.md`).

---

### 1.8 · Occupying no pending planning line
key `PendingDecisions.None` · **212 values · 172 invented**

Still PROTOTYPE-ONLY, but they do not pre-empt an open product decision.

| Area | What | Where |
|---|---|---|
| Player movement | move speed `6.2`, acceleration `28`, rotation `720°/s` (request) plus collision capsule, aim plane height, motor smoothing (invented) | `PlayerConfigSection.cs`, `Player/PlayerMotor.cs`, `Player/PlayerAim.cs` |
| Player pseudo-animation | every idle / run / aim-rotation / fire-recoil / reload / death parameter | `Player/PlayerProceduralAnimator.cs` — 17, all invented |
| Player rig and damage feedback | primitive proportions, skin hex colours, damage vignette timing | `Player/PlayerRigFactory.cs`, `Player/PlayerDamageFeedback.cs` |
| Camera | height `15 m`, back distance `13 m`, FOV `42`, follow `0.12 s`, look-ahead `2.5 m` (request) plus look-ahead smoothing, occlusion fade time and target alpha (invented) | `Camera/PrototypeCameraRig.cs`, `Camera/CameraOcclusionFader.cs` |
| Weapons | fire rates, magazines, reload times (request) plus spread cone angles, bloom per shot / max / decay, recoil kick, per-weapon camera shake, stagger impulse, effective range | `WeaponConfigSection.cs`, `Weapons/WeaponController.cs`, `Weapons/WeaponVisualRig.cs` |
| Special Weapon | pierce target count, spread, minimum drop distance from the player | `Weapons/SpecialWeaponDirector.cs` |
| Enemies | attack active / recovery / range / cooldown for every tier, body radius, separation mass, stagger susceptibility | `EnemyConfigSection.cs` |
| Explosion geometry | blast radius `3.2 m`, falloff exponent `1` | `EnemyConfigSection.cs:194,197` |
| Rounds | refill interval `0.45 s`, round-start countdown `3 s`, absolute safety ceiling `40` (request) plus max spawns per refill tick, the auto-start delay and the frozen "ROUND COMPLETE" beat (invented) | `RoundConfigSection.cs`, `Rounds/RunDirector.cs` |
| Spawn | min distance `12 m`, preferred `18-35 m`, same-point cooldown `4 s` (request) plus every weighting coefficient, max useful distance, local density radius, on-screen reject distance | `SpawnConfigSection`, `Spawn/SpawnWeightTuning.cs`, `Spawn/SpawnPointDefinition.cs` |
| Crowd solver | separation strength and radius, neighbour count, iteration count, grid cell size, push clamps, mass response | `Navigation/CrowdTuning.cs` — 15, all invented |
| Hit feel | all hit-stop durations, hit-stop minimum interval, every simultaneous-effect ceiling, shake amplitude cap and decay, stagger cooldown and maximum duration, audio voice budget | `FeedbackConfigSection.cs`, `Feedback/HitFeedbackDirector.cs` |
| World build order | build on awake, bake NavMesh after building, print the self-check report, install run systems after the map is built — switches, not visuals | `World/WorldBootstrap.cs` |
| Progression flavour | achievement display text and thresholds (tagged); skin colour values (documented in `PlayerSkins.cs:17` but not attribute-tagged — see §3.3) | `ProgressionConfigSection.cs`, `Player/PlayerSkins.cs` |
| Input | device-switch deadzone, pointer-versus-stick arbitration threshold | `Input/PrototypeInputSource.cs` |
| System | `60` FPS target, vSync policy | `SystemConfigSection` |

---

## 2 · DEC-021 is only half implemented — WP-000: known gap, next-package input

DEC-021 (approved) requires round difficulty to scale through a **hybrid** system: a
formula baseline **plus** hand-authored armour ratios and Special/Elite/Ultra
distributions on specific rounds.

This project implements the **formula half only**. The authored half is not tested.
This was a user decision (`t-0002`) at prototype time, not an oversight. **WP-000 status:**
known gap (REQ-013), tracked in the traceability matrix; the empty override table is
guarded by `ProjectIntegrityTests`.

The structure for it exists and is deliberately empty:

```
Core/Config/PrototypeConfig.cs:52          List<RoundDifficultyOverride> _roundDifficultyOverrides
Core/Resources/PrototypeConfig.asset:417   _roundDifficultyOverrides: []
```

`PrototypeConfig.ArmorChance(...)` (`:193`) and `PrototypeConfig.TierSpawnWeight(...)`
(`:208`) already call `TryGetRoundOverride(round, out _)` first, so the authored half
can be switched on later without touching call sites. **Do not populate this list
without an explicit instruction**: a non-empty list means the prototype has started
making authored difficulty decisions on its own.

Note the knock-on for §1.1: because the authored half is absent, the eight invented
`CombatScaling` values — the five armour chances and the three tier weights — are the
*only* thing deciding armour and tier distribution per round. They are carrying a load
DEC-021 intended to be hand-authored.

---

## 3 · Values with no source in the request at all

**237 of 337.** The request is silent on these; they were invented so a system could
function. They are flagged `invented: true` in code and rendered in a different colour
in the Inspector (`Core/Editor/PrototypeOnlyDrawer.cs:46`) and in the F1 panel.
**These need sign-off before anyone treats them as anything.**

Listed per file, because the file is what a reviewer has to open. Counts are exact.

### 3.1 · Inside `PrototypeConfig` — 78, visible in the F1 Config tab

| File | Count | What was invented |
|---|---|---|
| `EnemyConfigSection.cs` | 17 | five armour spawn probabilities (per-slot chance, growth per round, ceiling); three tier spawn weight fields (weight, growth, maximum); Runner / Guard / Executioner move speeds and attack damage; attack active, recovery, range and cooldown for every tier; body radius; separation mass; stagger susceptibility; explosion blast radius and falloff exponent |
| `FeedbackConfigSection.cs` | 17 | all hit-stop durations and the minimum interval between them, every simultaneous-effect ceiling, shake amplitude cap and decay, damage vignette time, stagger cooldown and maximum duration, audio voice budget |
| `WeaponConfigSection.cs` | 13 | spread cone angles, spread bloom per shot / maximum / decay, recoil kick, per-weapon camera shake, stagger impulse, effective range, Special Weapon pierce count and spread, Special Weapon chance growth per round, final-seconds warning length |
| `RoundConfigSection.cs` | 12 | max spawns per refill tick, max useful spawn distance, spawn scatter, and the remaining spawn safety geometry the request does not fix |
| `EconomyConfigSection.cs` | 9 | magnet speed and acceleration, orb lifetime, contact pickup radius, orb ceiling, and — per branch — cost growth per tier, max tier, effect per tier, offer weight |
| `PlayerConfigSection.cs` | 7 | collision capsule radius and height, aim plane height, motor smoothing and the remaining rig geometry |
| `ProgressionConfigSection.cs` | 3 | achievement display text and thresholds the request does not state |

### 3.2 · Outside `PrototypeConfig` — 159, invisible to the Config tab and the validator

| File | Count | What was invented |
|---|---|---|
| `Weapons/WeaponUpgradeTuning.cs` | 21 | Piercing target count and damage falloff · Ricochet base chance, per-tier chance, tiers per extra bounce, radius, damage scale · Explosion base and per-tier chance, base and per-tier radius, base and per-tier damage scale · Chain target count, radius, damage scale, falloff · Fire Rate and Reload Speed bonuses plus their clamps |
| `Feedback/HitFeedbackDirector.cs` | 18 | eight effect palette colours (blood, armour spark, armour-break shard, impact dust, explosion core, chain/ricochet link, tracer, muzzle flash) · four alive-at-once ceilings (tracers 48, muzzle flashes 6, links 20, explosions 8) · effect merge distance and window · damage-number merge window · three camera-shake amplitudes (armour break, heavy death, explosion) |
| `Economy/GoldOrbTuning.cs` | 17 | hop duration and height, scatter min/max, rest height, bob amplitude and frequency, spin speed, flash duration and scale, streak window, diameter, colour, light budget/range/intensity, high-value scale |
| `Player/PlayerProceduralAnimator.cs` | 17 | leg and arm swing degrees, step frequency, hip bob, lean degrees and response, breath amplitude and frequency, recoil kick distance / pitch / stiffness / damping, reload arm drop, flinch decay and displacement, damage flash colour, death duration |
| `Navigation/CrowdTuning.cs` | 15 | spacing scale, separation stiffness, max separation speed, approach damping, tangential bias, max neighbours per member, player contact padding, player brace strength, player depenetration stiffness and max speed, max correction speed, correction deadzone, grid cell size, solve interval, velocity smoothing |
| `Spawn/SpawnWeightTuning.cs` | 13 | distance factor at max useful distance, off-region factor, behind-heading factor, heading idle speed, recent-use memory, local density floor, behind-camera reject distance, spawn scatter radius, path cache interval, and the round-progress fraction that must be spent first |
| `Player/PlayerUpgradeTuning.cs` | 8 | Movement Speed, Maximum Health, Regen Delay and Regen Rate bonuses per tier plus each one's clamp |
| `Weapons/WeaponVisualRig.cs` | 7 | return speed, damping, pitch per kick, action travel and cycle time, rotary spin max speed and ramp time |
| `Weapons/WeaponController.cs` | 6 | shake metres at scale one, shake base duration, shake duration per scale, muzzle flash size and light intensity, Special Weapon flash multiplier |
| `Camera/PrototypeCameraRig.cs` | 5 | yaw degrees, focus height offset, near clip, far clip, pointer look-ahead factor |
| `Player/PlayerMotor.cs` | 5 | gravity, grounded stick speed, slope limit, step offset, skin width |
| `Player/PlayerRigFactory.cs` | 5 | hips height, torso height, head radius, leg length and arm length — all as fractions of the capsule |
| `Camera/CameraOcclusionFader.cs` | 4 | probe height above the follow target, probe interval, faded target alpha, fade time |
| `World/WorldBootstrap.cs` | 4 | build-on-awake, bake-NavMesh-after-build, print-self-check-report, install-run-systems-after-build — build-order switches, not visuals |
| `Player/PlayerDamageFeedback.cs` | 4 | bump amplitude and duration, the damage fraction that triggers a full reaction, death shake amplitude |
| `Input/PrototypeInputSource.cs` | 2 | pointer intent threshold in pixels, stick intent threshold — the two numbers that arbitrate which device owns aim |
| `Rounds/RunDirector.cs` | 2 | auto-start delay after scene load (`0.35 s`), the frozen "ROUND COMPLETE" beat before the upgrade screen opens (`1.1 s` — the request asks for the beat and states no duration) |
| `Spawn/SpawnPointDefinition.cs` | 2 | the authored position of each spawn point, and the designer base weight — the first of the nine weighting inputs |
| `Player/PlayerAim.cs` | 2 | stick aim distance, minimum aim distance |
| `Weapons/SpecialWeaponDirector.cs` | 1 | fallback drop distance when no valid Special Weapon point qualifies |
| `Spawn/EnemySpawnDirector.cs` | 1 | auto-start delay |

### 3.3 · Placeholder content documented but not attribute-tagged

Not counted in the 337 because they are static data, not serialized fields. They are
still placeholder content:

* **Skin colours** — `Player/PlayerSkins.cs:17-60`. The request names the pairs
  (Ivory/Crimson, Indigo/Black, Moss/Cream) but no colour values; **every RGB value is
  ours.** The file says so in its own header comment.
* **Achievement identifiers and trigger thresholds** —
  `ProgressionConfigSection.cs:109-130`. The request names four achievements; the
  triggers and thresholds behind them are ours.

---

## 4 · Explicit request-level flags

* **Gold collection mode `ProximityMagnet` is approved.** The comparison modes
  (`DirectCredit`, `ContactPickup`) remain development-only until WP-000 determines
  whether their debug paths should be retained. The live switches are in the F1
  **Economy** tab (`Economy/GoldService.cs:411-421`).
* **Exposed head `2×` and leg `0.8×` multipliers** are marked in code as not being
  product decisions, as the request requires
  (`EnemyConfigSection.cs:133,139`).
* **Weapon names, enemy names (`Runner`, `Guard`, `Executioner`), the ten upgrade
  branches and the four achievements** are placeholder content, not an approved roster.
* **The unlock ladder is exhausted in a single run.** Reaching profile level 4 costs a
  cumulative 525 XP (`100 + 175 + 250`, from `LevelRequirement = 100 + 75 × (Level-1)`);
  a run reaching round 8 earns roughly 545-600 XP. From the second run onward, "Newly
  Unlocked Content" on Results is usually empty. Every XP number involved is a request
  value (§1.3); they were frozen by user decision and not rebalanced.
* **The performance target does not occur in natural play.** `min(7 + 2R, 32)` caps at
  32 (`RoundConfigSection.cs:34`) while the stated target is "around 40". A 40-enemy
  load exists only under F1 → Spawn → `Force Spawn to 40`
  (`Spawn/EnemySpawnDirector.cs:813`).
* **The crowd solver's frame cost at 40 agents is not measured.** It is an estimate. The
  measuring instrument is F1 → Perf → `Crowd`
  (`Navigation/NavigationSystemBehaviour.cs:93`).
* **Ultra may not appear through natural progression.** The rounds 1-7 pool is 271
  enemies (`TotalEnemyPool`, `RoundConfigSection.cs:58`) and `Executioner` debuts at
  round 8 (`EnemyConfigSection.cs:309`).
* **Gamepad is not verified.** No controller hardware on this machine. Bindings written,
  never exercised against real input.

---

## 5 · Candidate decision list

Values liked during play can be flagged with `+` in the F1 **Candidates** tab; they
collect in `Core/Debug/CandidateDecisionLog.cs`. Flagging changes nothing on its own.
**Nothing moves from this list into `planning/` without an explicit, separate
instruction.** Agents never write to `planning/`; changes reach it only through
user-approved decisions recorded by the main coordination session.

### 5.1 · Runtime-flagged values

| Field | Value | Pending decision it would settle | Noted on |
|---|---|---|---|
| *(empty)* | | | |

### 5.2 · Findings raised during the build — **NOT APPLIED**

Two economy findings were produced while building the prototype. **Neither was acted on.
Neither is a decision. Neither goes near `planning/` without your explicit approval.**

---

#### C-1 · The economy's well runs dry around rounds 10-13

* **Finding.** Over a 20-round × 20-seed simulation, the entire upgrade tree fills
  somewhere in rounds 10-13. After that, gold is a meaningless counter: nothing is left
  to buy and unspent gold simply accumulates.
* **Pending decision it touches.** `Geliştirme seçenekleri, paternleri ve gold
  ekonomisinin sayısal ayrıntıları`.
* **Why the ceiling exists.** The ten branches have `MaxTier` between 3 and 5
  (`Core/Config/EconomyConfigSection.cs:91-152`), so the tree is finite by construction.
  Costs grow geometrically (`CostGrowthPerTier` 1.5-1.6) but income grows with the round
  pool, which outpaces them past round 10.
* **Provenance.** Reported by the economy build task. **The simulation was not re-run
  for this document**; the `MaxTier` ceiling above is re-verified, the round 10-13
  crossover is taken on report.
* **Status. Not applied.** No config value was changed because of it.
* **What resolving it would take.** A decision on whether the prototype's economy is
  supposed to terminate. Options exist — deeper tiers, a gold sink, escalating repeat
  purchases — but choosing one is a product decision, not a prototype fix.

---

#### C-2 · The round 1 upgrade screen opens dead in 13.4% of runs

* **Finding.** In 13.4% of runs the player reaches the round 1 upgrade screen unable to
  afford any of the three offered cards.
* **Pending decision it touches.** `Geliştirme seçenekleri, paternleri ve gold
  ekonomisinin sayısal ayrıntıları`.
* **Arithmetic, re-derived here.** Round 1's pool is 12 enemies
  (`TotalEnemyPool(1)`, `RoundConfigSection.cs:58`), all Standard, since Runner debuts
  at round 3 (`EnemyConfigSection.cs:257`). Standard pays 2 gold
  (`EnemyConfigSection.cs:233`). **Round 1 gross income is therefore 24 gold.** Of the
  eight branches offerable at profile level 1, three cost more than 24 — Piercing 30,
  Ricochet 30, Maximum Health 25 (`EconomyConfigSection.cs:91-152`). When the three-card
  draw returns those three, nothing is affordable.
* **Provenance.** The arithmetic above is re-derived and holds. **The 13.4% frequency is
  the economy task's simulated figure and was not re-measured for this document.**
* **Severity.** Not a wall. Unspent gold carries over
  (`EconomyConfigSection.cs:82`), so the cost is one screen of tempo, not progression.
* **Status. Not applied.** No card cost, no draw weight and no round 1 income was
  changed because of it.

---

*Section tables last recompiled 2026-08-18 (historical); headline count refreshed
2026-09-10 by WP-000 (491) and 2026-09-11 by WP-002 (485). The runtime figure printed by
`Tools > Deaths Demolition > Validate Prototype Core` counts reflected leaf fields of
`PrototypeConfig` only — list elements expand, and per-system tuning structs are
invisible to it — so it will not match the counts above. **That validator was not
re-run for this document.***
