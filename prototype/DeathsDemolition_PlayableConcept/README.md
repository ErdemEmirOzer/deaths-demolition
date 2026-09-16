# Deaths Demolition — Production Starting Codebase

**Status update — 2026-09-10:** This project was originally created as an experimental
visualisation prototype. The user has now approved it as the starting codebase that will
be developed incrementally into the final game. This does not automatically approve its
placeholder numbers, names or content; those remain tracked in
[`PROTOTYPE-ONLY.md`](PROTOTYPE-ONLY.md) until audited or replaced by approved values.

## Production Baseline (WP-000, 2026-09-10)

* **Verified on a clean copy** (Assets + Packages + ProjectSettings, no `Library/`) with
  Unity 2022.3.22f1 (887be4894c44) / URP 14.0.10 / macOS, Metal, Apple M1 Pro:
  import + compile PASS (0 `error CS`, 0 `warning CS`), EditMode **175/175**, PlayMode
  **21/21** on a real graphics device, macOS Player build PASS (verification only — not a
  platform decision), MainMenu and Run captured with no shader fallback and no exceptions.
  Physical keyboard/mouse, gamepad and OS-level Retina capture: **not measured** in WP-000.
* Evidence, commands, exit codes and hashes live in the WP-000 report set outside the
  repository (`S-01-ASAMA-2-KANIT-PAKETI.md`, final WP-000 report) — not in the historical
  `Build/` folder or the 2026-08-20 Player logs, which are superseded.
* Invariants for every later package: Unity 2022.3.22f1 and URP 14.0.10 are fixed
  (DEC-058/059); no bulk rewrite (DEC-060); no Unity-generated files in version control;
  only Boot is a valid entry scene; `planning/` is never written by agents.
* Fixed in WP-000 under test protection: firing while the simulation is frozen
  (`PlayerWeaponBinding.CanFire`, `PauseFireTests`); PlayMode tests writing the real player
  profile (`TestProfileIsolation` set-up fixture, `ProfileIsolationTests`); second-run reset
  coverage (`SecondRunTests`).
* Known gaps carried forward (not fixed in WP-000): ~~paid reroll (DEC-050)~~, orb lifetime vs
  DEC-045, hand-authored half of DEC-021, ~~per-round purchase limit (DEC-048), locked-branch
  offers (DEC-049)~~, persistent `DD.Prototype.DirectToRun` flag and F1 panel in Player
  builds, aborted runs not reaching the profile, leftover `Assets/InitTestScene…unity`.
  **WP-008 (2026-09-13) closed DEC-048, DEC-049 and DEC-050** (one purchase per round-end
  screen, locked/maxed branches never offered, one paid reroll before the purchase) and
  implemented the DEC-043 reward formula (base + version + spawn-armour bonus, frozen at
  spawn); orb lifetime had already been closed by WP-004. **WP-009 (2026-09-14)** hardened
  the profile without a new product decision: durable exactly-once profile commits, a
  crash-safe save chain proven by fault injection, save schema v2 with a lossless v1
  migration, earned-list entitlements, an immutable Results receipt; "aborted runs not
  reaching the profile" is unchanged and now regression-locked (B5 stays an open decision).
  **WP-009H (2026-09-14)** is the technical closure of a WP-009 defect found after
  integration: the anti-replay ring of eight ids protected only the last eight runs, so a
  ninth run let the first run's summary apply again. It is replaced by a profile lineage +
  monotonic completion ordinal issued by `CompletionAuthority` (save schema v3, lossless
  v1/v2 migration, legacy GUIDs refused), and the profile write lock became a fail-closed
  OS lease.
* Historical documents at the repository root (`PROTOTIP-INCELEME-RAPORU-2026-08-20.md`,
  `PROTOTIP-ONARIM-PROMPTU-2026-08-20.md`) are frozen evidence; do not run their commands.

| | |
|---|---|
| Unity | `2022.3.22f1` (`ProjectSettings/ProjectVersion.txt`) |
| Render pipeline | URP `14.0.10` (`Packages/manifest.json`) |
| Input | Input System `1.7.0`, actions in `Assets/_Prototype/Input/Resources/DD_Controls.inputactions` |
| Navigation | `com.unity.ai.navigation 1.1.5` NavMesh + a custom crowd layer |
| Network | none — the prototype runs fully offline |
| C# files | 233 under `Assets/` (WP-000 count, 2026-09-10; regenerate with `find Assets -name '*.cs' | wc -l`) |

---

## 1 · How to run it

1. The project is already registered in Unity Hub as
   `~/Documents/Deaths Demolition/prototype/DeathsDemolition_PlayableConcept`.
   If it is not, use **Add → Add project from disk** and point at that folder.
2. Open it with **Unity 2022.3.22f1**. Any other editor version will trigger an
   upgrade prompt — decline it.
3. **The first open takes several minutes.** `Library/` has to be built, shaders have
   to be imported and the URP pipeline assets have to compile. This is normal and
   happens only once per machine. Do not interrupt it.
4. Open `Assets/_Prototype/Scenes/Boot.unity`.
5. Press **Play**.

`Boot` is the only correct entry scene. It loads
`Assets/_Prototype/Core/Resources/PrototypeConfig.asset`, applies the runtime settings
that asset dictates, brings up the persistent services (pools, config, debug panel) and
hands off to the first real scene (`Core/Boot/PrototypeBootstrapper.cs`). Starting from
any other scene skips that hand-off and the scene will be missing its services.

**Build scene order** (`ProjectSettings/EditorBuildSettings.asset`):

```
0  Assets/_Prototype/Scenes/Boot.unity
1  Assets/_Prototype/Scenes/MainMenu.unity
2  Assets/_Prototype/Scenes/Selection.unity
3  Assets/_Prototype/Scenes/Run.unity
4  Assets/_Prototype/Scenes/Results.unity
```

The meta flow is `Boot → MainMenu → Selection → Run → Results → MainMenu`.

Shortcut for iteration: the F1 panel has a **Misc → `PROTOTYPE_DIRECT_TO_RUN (next
boot)`** toggle that sends the next boot straight into `Run`
(`Core/Boot/PrototypeBootstrapper.cs:65`).

---

## 2 · Controls

Bindings are declared once, in `DD_Controls.inputactions`, and read through an
abstraction (`Core/Input/IInputSource.cs`, `Input/PrototypeInputSource.cs`). Movement
and aim are fully independent: you can run one way and fire the other. There is no dash
or dodge. Taking damage never interrupts movement, aim or fire.

### Keyboard and mouse — verified at prototype delivery (user statement, no artefact); WP-000 covers this path by simulated input only

| Action | Binding |
|---|---|
| Move (camera-relative) | `W` `A` `S` `D`, arrow keys also bound |
| Aim direction | mouse cursor position in the world |
| Fire (hold for automatic) | hold **Left Mouse Button** |
| Reload | `R` |
| Pause (freezes the whole simulation) | `Esc` |
| Prototype debug panel | `F1` |

### Gamepad — **NOT VERIFIED, no hardware on this machine**

The bindings below exist in `DD_Controls.inputactions` and the device-switch path in
`Input/PrototypeInputSource.cs:379` handles the right stick as an aim source. **None of
it has been exercised against a real controller.** Do not read this table as "working";
read it as "written and unsanctioned".

| Action | Binding | Status |
|---|---|---|
| Move | Left Stick | not verified |
| Aim + limited camera look-ahead | Right Stick | not verified |
| Fire | Right Trigger | not verified |
| Reload | West face button (`X` / `Square`) | not verified |
| Pause | `Start` / Menu button | not verified |

UI navigation is bound separately (`UI` action map): `WASD`/arrows/left stick/d-pad to
navigate, `Enter`/`Space`/South button to submit, `Esc`/East button to cancel.

---

## 3 · The F1 debug panel

**The panel is not a convenience. For several things in this prototype it is the only
way to see them at all.** Three of the request's own numbers make specific states
unreachable through natural play:

* **The performance target never occurs naturally.** Target active enemies is
  `min(7 + 2R, 32)` and the cap is `32`
  (`Core/Config/RoundConfigSection.cs:34`), while the stated prototype target is
  "around 40 concurrent enemies". A 40-enemy load can only be produced from
  **Spawn → `Force Spawn to 40 (perf measurement)`**
  (`Spawn/EnemySpawnDirector.cs:813`).
* **Ultra may never be seen.** `Executioner` debuts at round 8
  (`Core/Config/EnemyConfigSection.cs:309`, `IntroductionRound = 8`) and the rounds 1-7
  pool is 271 enemies. **Spawn → `Skip to Round 8 (Executioner debut)`**
  (`Spawn/EnemySpawnDirector.cs:846`) exists for exactly this reason.
* **The packet economy has no other witness.** **Economy → `Wallet` / `Packets` /
  `Reward ledger`** readouts show the run id, the per-source totals (pickup vs round-end
  sweep), applied transactions, packets on the floor, and every refusal counter that
  should read zero (`Economy/GoldService.cs`, WP-004). The three collection-mode buttons
  are gone: the approved contract is one visible, magnet-collected packet per kill.

`F1` toggles it. It never appears during normal play. Nine tabs
(`Core/Debug/DebugPanelRegistry.cs:9-18`):

| Tab | What lives there |
|---|---|
| **Config** | Every field of `PrototypeConfig`, live-editable mid-run, each row showing the pending planning decision it occupies. |
| **Rounds** | Run/round/clock/XP readouts, start-restart run, end run, skip to next round, force-complete round, skip to round N, force the upgrade screen open or closed, live upgrade offer, gold grants. |
| **Enemies** | Clear/kill all, spawn one of each tier, fill to safety ceiling, break all armour, `Verify Damage Model` (runs the regional-damage assertions and logs violations), crowd / tier-schedule / FX-budget / armour readouts. |
| **Weapons** | Equip any classic weapon, trigger or end the Special Weapon, infinite ammo, live fire rate and damage, camera-shake intensity, `+1` on each upgrade branch, clear build, effect-pool and feel-budget readouts. |
| **Economy** | Wallet, packet and reward-ledger readouts, gold grants, sweep (credit) / destroy (no credit) all packets, live magnet radius. |
| **Spawn** | Force spawn to 40, release forced target, clear enemies, spawning on/off, round-3 / round-5 / round-8 debut jumps, skip to round N, live spawn-point weight table. |
| **Perf** | `Pools`, `Crowd`, `NavMesh` readouts, crowd layer on/off, reset crowd peak. **The `Crowd` row is the only measuring instrument for the crowd solver's frame cost.** (`Navigation/NavigationSystemBehaviour.cs:93`) |
| **Candidates** | The candidate decision list. Flagging a value here changes nothing and writes nothing to `planning/`. |
| **Misc** | Player heal/damage/kill, player and camera readouts, input and aim-device readouts, occluder readout, UI contrast audit, reset local profile (**destructive** — resets the real player profile; never use in tests, tests use an isolated profile path), `PROTOTYPE_DIRECT_TO_RUN`. |

---

## 4 · Main systems and files

Runtime code lives under `Assets/_Prototype/`; URP pipeline, renderer and volume assets live in `Assets/Settings/` (plus `Assets/UniversalRenderPipelineGlobalSettings.asset`). Production ART content (environment families, textures, materials, the pilot scenes and the art pipeline that imports them) lives under `Assets/_Art/` since WP-011 (ADR-011-2); gameplay content is still planned for `Assets/_Game/` (asset contract, WP-000).

`Assets/_Art/` (WP-011 — M3 content pipeline pilot): `Environment/Families/<family>/` holds the AP-001 FBX exports (three LODs + collider parts, stored in Git LFS) and the generated `SM_<family>.prefab` (LODGroup, primitive colliders, layer); `Environment/GoldenSlice/` the AP-001 layout (`AP001_GoldenSliceLayout.json` provenance + `.asset`); `Textures/Source/` the byte-identical AP-001 PNGs and `Textures/Derived/` the URP metallic-smoothness masks the repack tool derives from them; `Materials/` the ten URP/Lit materials; `Scenes/Pilot/` the pilot scene `AP001_GoldenSlice` (sixth build scene; the slice, ground, markers, NavMesh and run systems are built at load by `PilotSceneBootstrap`, mirroring `WorldBootstrap`) and the editor-only `AP001_KitShelf`; `Editor/` the Import Contract v1 (`ArtImportContract` + `ArtAssetPostprocessor`, applied to every asset under `_Art`) and the pipeline (`Tools > Deaths Demolition > Art Pipeline > Run All`: repack → materials → prefabs → layout → scenes → build settings); `Tests/` the asset gate (EditMode) and pilot scene tests (PlayMode). `Assets/_Prototype/Diagnostics/PilotPerfProbe.cs` is the development-only 40-enemy measurement probe (`-ddPilotPerfProbe`).

WP-012 (M3 art library import, ADR-012-1/2) adds the production libraries under `Environment/Families/<AP>/<family>/` (AP-002: 12 architecture families; AP-003: 6 vegetation/rock families × 42 variations; AP-003B: 11 pond/bridge/corridor/bamboo/prop families × 53 variations; FBX with embedded textures stripped outside Unity, wind data kept as a vertex-colour layer), one generated manifest per library (`<AP>_Library.json`: model names, material slot order, proxy shells, bounds — the only thing that survives `materialImportMode None`), one prefab per variation (`SM_<family>_V<nn>`: LODGroup with thresholds authored for the shipped lodBias, one primitive collider per proxy shell, roof parts on their own fading LODGroup, broken states as inactive children) and a prefab index (`<AP>_Library.asset`) for placement. `Lighting/DD_Overcast_v1.asset` and `Resources/DD_Sky_Overcast_v1.mat` are the shared overcast base every scene (Run, pilot, showcase) uses; `Scenes/AP_LibraryShowcase.unity` (editor-only) shelves every prefab for review. `Tools > Deaths Demolition > Art Pipeline > Run Libraries (WP-012)` rebuilds all of it; `PilotProfilerAnalysisCli` reads a `-ddPilotProfilerLog` capture back in batchmode.

WP-014 (M3 world assembly, ADR-014-1/2) turns AP-005's `placement.json` into the authored world: `Environment/World/` (the byte-identical placement source, its Unity-space `AP005_Placement.asset`, the stand-in ground mesh), `Scenes/World_AP005.unity` (2354 prefab instances with `RegionTag`, layer by occluder flag, static flags, baked NavMesh) and `Ap005WorldBootstrap` (atmosphere, markers, run systems). `Resources/DD_WorldSource.asset` decides whether `Run` builds the procedural blockout or loads this scene additively (`-ddWorldSource` / `DD_WORLD_SOURCE` override); see `Assets/_Art/Scenes/README.md`.

WP-015 (M3 asset budget & build policy, ADR-015-1/2) puts the art under a texture policy — `ArtImportContract.TextureClass`: ground/paving 2048, structure and foliage 1024, props/breakables 512 (reserved), sky 256; mipmap streaming on with a 160 MB budget — and a build-list policy: enabled Build Settings entries are the release scenes, disabled ones (the AP-001 pilot, the library showcase) ship only in the development build. Result: shipped textures 337 → 113 MB, resident art textures 315 → 71–84 MB, release Player 655 → 297 MB. It also records the user's 2026-09-15 decisions (DEC-076..080, REQ-104/105 measurable criteria), finalises the style guide (`Assets/_Art/StyleGuide/style-guide-v1.md`), adds Unity-side placement overrides (`Environment/World/AP005_placement-overrides.json`) and the official readability thresholds (`ReadabilityThresholds`). See `Assets/_Art/README.md`.

WP-016 (M3 character integration, ADR-016-1/2) replaces the M1 capsule bodies with the AP-006 player and the AP-007 zombie: `Assets/_Art/Characters/` (Import Contract v1.1 — Humanoid avatars with one explicit 52-bone mapping, rig-test clips, LODGroups, `M_player` with albedo-swap skins, the shared zombie atlas with cloth-masked version tints, `ZombieModuleLibrary` = 3 fits × 19 modules × 4 LOD re-pointed by bone name at spawn from a deterministic, ratio-ruled look selector). The prototype binds them through `Assets/_Prototype/Characters/` (`CharacterAssetSet` in Resources, `PlayerSkinnedVisual`, `ZombieSkinnedVisual`, `ProceduralPoseBridge`): the M1 procedural animators keep driving lean / attacks / flinch / death onto the bones after a minimal idle-walk Animator, hitboxes and armour anchors move onto the bones, the weapon sits on `socket_weapon_r`. `-ddCharacters Procedural` keeps the primitive bodies for A/B. See `Assets/_Art/Characters/README.md`.

WP-017 (M3 world v2 & armour, ADR-017-1/2) brings the AP-005B delta into the world (`AP005_placement-delta-v002.json` applied on the untouched v001 source at import: 2354 − 169 + 202 = 2387 instances — palace roof and walls, gates, pavilions, colonnades, six Tripo prop families under `Assets/_Art/Environment/Families/AP-005B/`; full XYZ euler for the tilted gable panels; `material_override` mapped to library roles — plaster copies × 0.82, roof with a generated tile-course detail normal, stone kerbs; heavy roof meshes stay out of static batching) and binds the AP-008 armour to the WP-006 `ArmorSet` states: `Assets/_Art/Characters/Zombie/Armor/` (18 FBX on Contract v1.1, one atlas, `ZombieArmorLibrary`), `ArmorVisualBinder` (Absent / Present / Damaged / Broken = mesh swaps on six armour renderers per LOD, break event on the anchor for S-07), variants per spawn seed, and the garment draw re-run under the equipped pieces' AP-008 exclusions plus faded cloth beneath a plate. Texture budgets split (world ≤ 120 / character ≤ 80 / total ≤ 200 MB); `-ddArmor all` on the perf probe fits every piece on the crowd.

WP-021 (M3 audio, ADR-021-1/2) replaces the placeholder soundscape's plumbing without removing it: a Unity AudioMixer (`Assets/_Prototype/Audio/Resources/DD_AudioMixer.mixer`, Master → Music / Ambience / SFX-World / SFX-Player / UI, snapshots Run / RoundBreak / Pause / Menu) under the WP-002 voice pools, the AP-011 produced set (`Assets/_Art/Audio/AP011/`, 88 clips over 52 sound ids, Seed Audio 1.0 → BS.1770-normalised masters) overlaid on the procedural recipes through `DD_AudioCatalog`, the event map `AudioEventMap` (every combat/run/player/UI event plus the DEC-067 zombie vocal set — windup, swing, pain, death — routed by `AudioEventRouter` without touching `Enemies/`), five region beds cross-faded from the AP-005 polygons (`DD_AudioRegionMap`, 2.5 s) over a common after-rain layer, and a four-layer score (`MusicDirector`: base / percussion / tension / accent on a progress-and-pressure intensity, pulled back between rounds).

WP-018 (M3 animation integration, ADR-018-1/2) puts the AP-009 set on the characters: `Assets/_Art/Characters/Animations/` (30 animation-only FBX + manifest; loop / events / additive reference pose written at import from the manifest — importer event time is normalised), generated `DD_Player` / `DD_Zombie` controllers (player: 2-D directional locomotion + aim layer + additive recoil + upper-body hit + deaths + pickup; zombie: idle/gait blend by fit and version, attacks at the tier telegraph rate, upper-body hits, regional additive flinches, heavy stagger on armour break, four deaths, rise), `CharacterAnimationEvents` as the single event contract (the DEC-029 hit window is now the clip's `hit_window_start/end`, the timers are a safety net), `ProceduralPoseBridge` removed, `CharacterAnimatorLod` stepping far bodies at 15 Hz under `CullUpdateTransforms`; the 40-body animation path allocates nothing per frame.

| Folder | One-line summary |
|---|---|
| `Core/` | The spine: `PrototypeConfig` and its ten sections, the `PrototypeOnly` origin attribute, boot/scene hand-off, object pooling, the input abstraction, and the F1 panel plus its registry. |
| `Input/` | The single concrete `IInputSource` implementation over the Input System, plus the installer that decides which device currently owns aim. |
| `Player/` | Motor, aim, health and regeneration, damage feedback, procedural animation, the primitive-built rig and its three skins, and the binding that applies upgrade tiers to player stats. |
| `Camera/` | The fixed high-angle third-person rig with look-ahead and smoothing, the shake service, and the occlusion fader that dissolves anything between the camera and the player. |
| `Weapons/` | The three classic weapons and the Special Weapon: firing loop, hitscan engine, target resolution, modifier state for the upgrade branches, the stagger governor, the visual rig, and the Special Weapon spawn/pickup/round bridge. |
| `Feedback/` | The hit-feel chain — muzzle flash, tracer, impact, hit marker, damage number, burst, beacon and link effects — all pooled and metered by a shared budget, plus hit-stop and the camera-shake bridge. |
| `Enemies/` | The single zombie family: factory, targeting, the tier authority bridge (the scheduler was removed in WP-010; `TierSelectionPolicy` under `Spawn/` is the single authority), attack state machine, hit-reaction state, procedurally built body and appearance layers, and the pseudo-animator. |
| `Damage/` | The regional damage model: hitbox registry, the three-piece armour set, the damage contracts, and `RegionalDamageProof`, which asserts the approved armour and explosion rules against the live model. |
| `Navigation/` | NavMesh path following plus the custom crowd layer — solver, spatial grid and tuning — that gives soft separation without a solid traffic lock, and the ring-region model of the map. |
| `Spawn/` | The twelve hand-placed spawn points, the dynamic weight calculator that scores them, the pure `SpawnPointSelector` (weighted, deterministic with an injected RNG), the tier selector that owes each tier its guaranteed debut, the pure `RoundLedger` (round state machine, population counters, the REQ-012 completion rule: pool empty AND no living enemy, corpses excluded) with `RoundPlan` as the single place the round's pool/target/ceiling are resolved, the pause-safe `SpawnRefillClock`, and the director that owns the pool, the enemy lifecycle sink (`IEnemyLifecycleSink`, ticket per life) and keeps the target active count under the technical ceiling (WP-003). |
| `World/` | The 90×90 m blockout: procedural mesh building, ground, structures, foliage, atmosphere, palette, landmarks, and the NavMesh bake — assembled at load by `WorldBootstrap`/`WorldBuilder`; the editor tool `RunSceneBuilder` is a one-time setup utility and is not run in normal work. |
| `Rendering/` | The serialized shader library (`PrototypeShaderLibrary`, in `Resources/`), the eight variant-pin materials that keep URP keyword variants in the Player build, the `PrototypeShaders` accessor that replaced `Shader.Find`, and the build-time check that stops a build when the library is missing. |
| `Rounds/` | The run director: round pools, round-end flow, the round-end upgrade SESSION (`RoundUpgradeShop` + the pure `UpgradeOfferPolicy`: exactly three distinct eligible cards, one purchase per round that locks the offer, one paid reroll before it that swaps one or two cards, session/revision-quoted commands, a safe no-charge error state — WP-008), XP and stats tracking (gold spent = purchases + rerolls), the run flow clock that freezes everything on pause, and the debug registrations. |
| `Economy/` | The run wallet (transaction ledger with sources and ids, spend split by upgrades/rerolls), the reward-eligibility ledger, the value contract and the pure `GoldRewardCalculator` (WP-008: `base + version bonus + spawn-armour bonus`, frozen per enemy life in an `EnemyRewardSnapshot` at spawn), the packet lifecycle and its pooled body, its tuning, and the service that drops one packet per kill carrying exactly that snapshot, pulls it in with the magnet and sweeps the floor when a round completes. |
| `UI/` | Every screen (main menu, selection, HUD, upgrade, pause, results) built on a shared kit — tokens, fonts, sprites, buttons, bars, motion, sound and a contrast auditor — plus the flow object that drives scene transitions. WP-005 adds `Navigation/` (the `UiNavigation` state model: one base context, a layer, an overlay; conflicting states are refused, transitions are idempotent) and `Presentation/` (`RunHudViewModel`, `ResultsViewModel`, `ProfileViewModel`/`StatisticsViewModel`/`AchievementsViewModel`, `UpgradeIcons`): every screen draws from a view model built from an authoritative snapshot, never from a live gameplay object. |
| `Profile/` | The local, offline profile: persistent stats, level and unlocks (the EARNED lists are the access authority — WP-009), the pure progression arithmetic (`ProfileProgression`: two XP sources, saturating counters, bounded level loop), the transaction (`ProfileService.ApplyRun`: exactly-once by an issued completion capability - WP-009H: profile lineage + monotonic ordinal high-water mark from `CompletionAuthority`, O(1), no id ring - candidate → crash-safe save → publish → one `Changed`), the run-end commit authority (`ProfileRunCommitter`), the crash-safe file store (`ProfileStore`: fsync'd candidate, verified backup, single-rename replace, deterministic recovery, v1/v2→v3 `ProfileMigration`, fail-closed fcntl write lease) and the immutable per-run receipt that Results reads. |
| `Audio/` | Fully synthesised placeholder audio — no external assets. Recipes for weapons, impacts, ambience and meta, a clip library, a voice pool with a budget, buses, a mix plan and the ambience director. |
| `Tests/` | Edit-mode tests for the formulas (round, progression, special-weapon chance, regional damage, mesh winding), the combat contracts (WP-002) and the round ledger / spawn selection contracts (WP-003), and play-mode tests for the flow, scene integrity, the core combat slice (WP-002) and the round & dynamic spawning slice (WP-003), plus `OTOMATIK-KAPI.md` (what the automated gate verifies) and `MANUEL-TEST-KITI.md` (what only a human can verify). |

---

## 5 · PROTOTYPE-ONLY assumptions

The full list is in **[`PROTOTYPE-ONLY.md`](PROTOTYPE-ONLY.md)**. Summary:

* Every tunable value carries a `[PrototypeOnly(...)]` attribute naming the still-open
  planning decision it temporarily occupies. **492 such declarations exist in the
  code** (WP-024 re-count 2026-09-16: +9 plain-form tags for the gamepad slice — `InputPreferences` ×4 (deadzone / rumble / aim-assist preferences behind the request's fixed ceilings), `PrototypeInputSource.StickSaturation`, `GamepadRumble` pulse table ×4 — no tunable value changed; plain `grep "\[PrototypeOnly("` now reads 499 (the M3 art packages WP-011…WP-017 added 2 untracked in this line), plus 4 fully-qualified = 503; WP-010 re-count 2026-09-14: −6 tags removed with the code that carried them, no tunable value changed — the dead second tier authority `EnemyTierScheduler` (`DebutWindowStart`/`DebutWindowEnd`), `EnemyFactory.FallbackRoundPoolSize` (only read by the removed `EnemyFactory.BeginRound`) and the F1 "Fill To Safety Ceiling" cut table `EnemySystemHost.DebugFillStandardCut/SpecialCut/EliteCut` (the fill now draws from the run's single tier authority — F1 debug behaviour change: in round 1 the fill is Standard only, versions join from their introduction rounds on); plain `grep "\[PrototypeOnly("` now reads 488, plus 4 fully-qualified = 492; WP-008 re-count 2026-09-13: +3 plain-form tags for the spawn-armour gold bonuses on `EconomyConfigSection` (`ArmorGoldBonusHelmet/Body/Leg` 1/2/1, invented placeholders behind the DEC-043/069 rule), `AllowReroll` replaced 1:1 by `RerollCost` 10 (invented placeholder behind DEC-050), `CardsPerRound` retired (the three-card count is the DEC-047 constant `UpgradeOfferPolicy.CardsPerOffer`) — plain `grep "\[PrototypeOnly("` now reads 494, plus 4 fully-qualified = 498; WP-007 re-count 2026-09-13: +5 plain-form tags on `SpecialWeaponDirector` constants that existed untagged — `MaxSpawnsPerRoundDefault`, `SecondSpawnProgressOffset`, `LatestSpawnProgress`, `NavSampleRadius`, `OffRouteWeightMultiplier` — no value changed, plain `grep "\[PrototypeOnly("` now reads 492; WP-006 re-count 2026-09-12: +4 on the regional-hit slice — `DamageTargetResolver.HeadThreshold`/`UpperBodyThreshold` height-ratio fallback and `HitscanEngine.OverlapBufferSize`/`ExplosionRimDamageFraction`, all four written fully qualified so the historical plain `grep "\[PrototypeOnly("` still reads 487; 487 at WP-004/WP-005; 485 at WP-003/WP-002; 491 at the WP-000 count of 2026-09-10; 337 at the 2026-08-18 scan); the tag is visible in the Inspector and in the F1 panel, so nobody can read a
  number off the screen without also reading where it came from.
* **237 of the original 337 (2026-08-18 scan) had no source in the request at all**; the invented split is re-issued after WP-000 Phase 4 — they were invented so a
  system could function. They are flagged `invented` and rendered in a different colour.
* Six of the seven lines under *"Kararlaştırılması sonraya bırakılan alanlar"* in
  `planning/00-PROJECT-PLAN.md` are currently occupied by prototype numbers.
  `PROTOTYPE-ONLY.md` §1 maps which value occupies which line.
* **Gold collection with `ProximityMagnet` is now an approved product decision**, and
  WP-004 (2026-09-12) implements it as the only path: every enemy death drops one visible
  packet that never expires, the magnet pulls it in, and the round-end sweep credits
  whatever is left before the upgrade screen opens. The old comparison-mode switch is
  retired; `EconomyConfigSection.GoldCollectionMode` / `OrbLifetime` are inert fields.
* Weapon names, enemy names (`Runner`, `Guard`, `Executioner`), the upgrade branches and
  the achievements are placeholder content, not an approved roster.

---

## 6 · Known limitations

None of these are softened. All of them are real and observable.

**1 · The unlock ladder is exhausted in a single run.**
Reaching profile level 4 costs a cumulative 525 XP (`100 + 175 + 250`, from
`LevelRequirement = 100 + 75 × (Level-1)`, `Core/Config/ProgressionConfigSection.cs:64`).
A run that reaches round 8 earns roughly 545-600 XP (245 round XP for rounds 1-7 plus
271 base kill XP plus tier bonuses). From the second run onward **"Newly Unlocked
Content" on the Results screen is usually empty.** The numbers are frozen by user
decision and were not rebalanced.

**2 · The performance target does not occur in natural play.**
`min(7 + 2R, 32)` caps at 32 (`Core/Config/RoundConfigSection.cs:34`); the absolute
safety ceiling is 40 (`:38`); the stated target is "around 40". The 40-enemy load is
reachable **only** by forcing it from F1 → Spawn.

**3 · Ultra may not be seen through natural progression.**
The rounds 1-7 pool totals 271 enemies (12+20+29+38+48+57+67 from
`TotalEnemyPool`, `Core/Config/RoundConfigSection.cs:58`), and `Executioner` debuts at
round 8. The panel's round-jump presets exist because of this.

**4 · Gamepad could not be verified.** No controller hardware on this machine. The
abstraction and the bindings are written; they were never exercised. Acceptance
criterion 5 is split: keyboard+mouse verified, gamepad **not verified**. WP-000
(2026-09-10): still **not measured** — physical keyboard+mouse was not re-measured either;
WP-000 exercised the input path through simulated `IInputSource` sources only.

**5 · The hand-authored half of DEC-021 is absent from the prototype.**
DEC-021 requires hybrid round difficulty: a formula baseline plus hand-authored armour
ratios and tier distributions on specific rounds. Only the formula half is implemented.
The override table is a deliberately empty skeleton —
`_roundDifficultyOverrides: []` in `Core/Resources/PrototypeConfig.asset:417`. This was
a user decision, not an oversight.

**6 · The frame budget was not measured.**
The crowd solver's cost at 40 agents is an estimate, not a measurement. The measuring
instrument is **F1 → Perf → `Crowd`**
(`Navigation/NavigationSystemBehaviour.cs:93`). Until somebody reads that row under a
forced 40-enemy load, treat the 60 FPS / 40 enemy target as **not measured**.

**7 · The economy's well runs dry around rounds 10-13.**
Reported by the economy task from a 20-round × 20-seed simulation: the entire upgrade
tree fills there, after which gold is a meaningless counter. **This is a candidate
finding. It has not been applied** and nothing was changed because of it.

**8 · The round 1 upgrade screen opens dead in 13.4% of runs.**
Round 1 gross income is 24 gold (12 standard enemies × 2 gold each;
`TotalEnemyPool(1) = 12`, `RoundConfigSection.cs:58`, `GoldReward = 2` at
`Core/Config/EnemyConfigSection.cs:233`). Three of the eight branches offerable at
profile level 1 cost more than that — Piercing 30, Ricochet 30, Maximum Health 25
(`Core/Config/EconomyConfigSection.cs:92-150`). When the draw returns three of those,
nothing is affordable. The 13.4% frequency is the economy task's simulated figure and
was not re-measured here. It is not a wall — unspent gold carries over — but the screen
is dead. **Candidate finding, not applied.**

---

## 7 · Historical verification at original prototype delivery (superseded by WP-000, 2026-09-10)

> The MD5 values below describe `planning/` as of 2026-08-17 and are **no longer valid**;
> `planning/` was updated by the user on 2026-09-10. Current baseline evidence: see the
> "Production Baseline" section above.

* At the original prototype delivery, **`planning/` was not modified.** All four files were last written on
  **2026-08-17 03:29-03:30**, before this prototype folder existed
  (`prototype/DeathsDemolition_PlayableConcept` was created 2026-08-17 04:26). MD5s at
  time of writing:

  ```
  c6c76920e31102c6f45a50455eb8508b  planning/00-PROJECT-PLAN.md
  838bcc2d349582732268415eac80f03a  planning/01-REQUIREMENTS.md
  63f893401a29666c644db2ca1e7cb135  planning/02-FLOWS-ARCHITECTURE.md
  0f94760ccf6ad44bf150110eb94d34ea  planning/03-ROADMAP.md
  ```

* At the original prototype delivery, **`game/` was not modified.** It still contains only `.gitkeep` (0 bytes, dated
  2026-08-01), exactly as it was before this work started.

* At the original prototype delivery, **no DEC or REQ was created, changed or removed.** This prototype has no write access
  to `planning/`. The candidate decision list (F1 → Candidates,
  `Core/Debug/CandidateDecisionLog.cs`) is a notepad; **nothing moves from it into
  `planning/` without an explicit, separate instruction.**

* At the original prototype delivery, **no commit was made.** The initial baseline commit
  (DEC-062, WP-000 Phase 5) covers the verified planning documents together with the
  prototype source, settings and `.meta` files; generated folders (`Library/`, `Temp/`,
  `Logs/`, `UserSettings/`, `Build/`) stay excluded via the root and `prototype/` `.gitignore`.

* **No paid asset, no external download, no internet or cloud service** is used. Every
  package in `Packages/manifest.json` ships inside the 2022.3.22f1 installation. All
  audio is synthesised at runtime; all meshes are procedural primitives.
