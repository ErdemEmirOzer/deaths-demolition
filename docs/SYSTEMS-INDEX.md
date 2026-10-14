# Systems Index

This index lists every major system in Deaths Demolition as it stands on `main` in revision round R4 of milestone M4. It was checked against the R4 game commit `b3424d1f` (14 Oct 2026); later commits on `main` change documentation and repository hygiene only.

- **Folders** are relative to `prototype/DeathsDemolition_PlayableConcept/Assets/` unless they start with `tools/`.
- **Main types** are C# type names; where a file holds a partial class or helpers, the file name is given instead.
- Work-package specifications for each system are in `planning/work-packages/`.

**Status key**

| Status | Meaning |
| --- | --- |
| **Shipped** | On `main` with tests and active on the default player path of a release build |
| **Legacy** | On `main` and reachable, but frozen and outside test and acceptance scope since 6 Oct 2026 |
| **Retired** | Still compiled and kept for save or data compatibility, but no longer reachable by the player |
| **Dev only** | Compiled only into development builds and the Editor (or, where stated, release-probe builds) |
| **Tooling** | Editor or shell tooling; never part of a player build |

## Foundation

| System | Folder | Main types | Status |
| --- | --- | --- | --- |
| Boot and scene flow | `_Prototype/Core/Boot/`, `_Prototype/UI/Runtime/Flow/` | `PrototypeBootstrapper`, `PrototypeBootFlags`, `PrototypeScenes`, `PrototypeUiFlow`, `PrototypeUiInstaller` | Shipped. `Boot` is the only entry scene. The stored direct-to-run switch is read in development builds only. |
| Config and placeholder census | `_Prototype/Core/Config/`, `_Prototype/Core/Resources/PrototypeConfig.asset` | `PrototypeConfig` (Economy, Enemy, Feedback, Player, Progression, Round, Weapon … sections), `PrototypeOnlyAttribute` | Shipped |
| F1 debug panel | `_Prototype/Core/Debug/` | `PrototypeDebugPanel`, `DebugPanelRegistry`, `DebugPanelTabs` | Dev only. A release build never draws it. |
| Pooling | `_Prototype/Core/Pooling/` | `PoolService`, `PrototypePool<T>`, `PooledBehaviour` | Shipped |
| Input and rebinding | `_Prototype/Core/Input/`, `_Prototype/Input/` | `IInputSource`, `InputSourceRegistry`, `PrototypeInputSource`, `InputRemapService`, `InputPreferences`, `AimAssist`, `GamepadRumble` (+ `GamepadRumble.TopDown.cs`) | Shipped |
| Quality tiers | `_Prototype/Core/Quality/`, `_Prototype/Rendering/`, `Settings/` | `QualityTiers` (`DefaultTier = Sinematik`), `QualityModeApplier`, `SmallShadowCasters`; `DD_URP_Performance`, `DD_URP_PipelineAsset`, `DD_URP_Cinematic` | Shipped. Cinematic is the default; High and Performance are selectable. |

## Combat

| System | Folder | Main types | Status |
| --- | --- | --- | --- |
| Weapon data (Arsenal v3) | `_Prototype/Core/Weapons/`, `_Prototype/Weapons/Arsenal/` | `WeaponDefinition`, `ArsenalCatalog` (12 definitions), `Arsenal`, `WeaponIdMigration` | Shipped: 10 classic weapons in 6 classes and 2 Specials |
| Firing and hitscan | `_Prototype/Weapons/` | `WeaponController`, `ClassicWeaponRuntime`, `HitscanEngine`, `PlayerWeaponBinding`, `StaggerGovernor`, `WeaponMagazineOwnership`, `WeaponMagazineVisual` | Shipped |
| Projectiles | `_Prototype/Weapons/Projectiles/`, `_Prototype/Weapons/RocketImpacts.cs` | `ProjectileSystem`, `ProjectileKind` (Bullet/Pellet/Rocket/Orb/Gel), `ProjectileDriver`, `ProjectileEvents` | Shipped: 256 live, fixed 120 Hz step |
| Special weapons | `_Prototype/Weapons/` | `SpecialWeaponDirector`, `SpecialWeaponPickup`, `SpecialImpacts`, `SpecialGelEffects`, `SpecialWeaponUsageEvents` | Shipped: Raiju (chain lightning) and Namekuji (slick gel), found on the field; tree resonance (Storm Chain, Deep Freeze) |
| Wall buys | `_Prototype/Weapons/WallBuy/`, `_Prototype/UI/Runtime/Hud/HudWallBuyPrompt.cs` | `WallBuyDirector`, `WallBuyStation`, `WallBuyRules`, `WallBuyCatalog` (`wallbuy-v6.json`), `WallBuyEvents` | Shipped (R4): 10 stations; refill at half price; launcher 2,500 gold, wall buy only |
| Weapon attachments | `_Prototype/Weapons/Attachments/`, `_Prototype/Weapons/Arsenal/Attachments/`, `_Art/Editor/ArtAp040WeaponAttachBuilder.cs` | `WeaponAttachments`, `WeaponAttachPresenter`, `WeaponAttachRules`, `WeaponAttachLibrary` | Shipped (R4): 14 prefabs, shown in top-down |
| Damage and armour | `_Prototype/Damage/` | `DamageHit`, `IDamageReceiver`, `BodyRegion`, `DeathImpulse`, `RegionalDamageBody`, `ArmorSet`, `HitboxRegistry`, `DamageEvents` | Shipped |
| Player | `_Prototype/Player/` | `PlayerMotor`, `PlayerAim`, `PlayerHealth`, `PlayerRig`, `PlayerSpawner`, `PlayerUpgradeBinding` | Shipped |

## Camera

| System | Folder | Main types | Status |
| --- | --- | --- | --- |
| Top-down camera | `_Prototype/Camera/` | `TopDownFeelCamera`, `PrototypeCameraRig`, `CameraOcclusionFader` (roof fade), `TreeCanopyReveal` (crown screen-door), `CameraModeService` (mode state) | Shipped: the default and the only developed and tested mode |
| Third- and first-person cameras | `_Prototype/Core/Camera/`, `_Prototype/Camera/`, `_Prototype/Characters/FirstPersonArms.cs`, `_Prototype/Characters/WeaponHoldIk.cs`, `_Art/Runtime/WorldFptpOnlyLoader.cs` | `CameraMode`, `CameraModesFeel`, `FpAdsLens`, `FirstPersonArms`, `WeaponHoldIk`, `WorldFptpOnlyLoader` | Legacy: selectable from the Pause CAMERA row; FP arms for 6 of 10 classic weapons; `World_FPTPOnly` loads only for these modes |

## Enemies and AI

| System | Folder | Main types | Status |
| --- | --- | --- | --- |
| Difficulty modes | `_Prototype/Difficulty/`, `_Prototype/UI/Runtime/Prepare/PrepareRunDifficulty.cs` | `DifficultyService`, `DifficultyProfile`, `DifficultyModeSettings`, `DifficultyMode` | Shipped (R4): Normal, Hard, Demolition |
| Pursuit roles | `_Prototype/Difficulty/PursuitPlanner.cs` | `PursuitPlanner`, `ZombieAiRole` (Chase / Intercept / Cut) | Shipped (R4): Hard and Demolition; Normal always chases |
| Enemy runtime | `_Prototype/Enemies/` | `ZombieEnemy` (partials), `EnemyFactory`, `TierBehaviourProfile`, `EnemyVersionApplier`, `ZombieTierLibrary`, `TierSilhouetteSlot` | Shipped: 4 tiers on one data-driven zombie |
| Attacks and reactions | `_Prototype/Enemies/` | `AttackSelector`, `AttackSlotBoard`, `GrabState`, `EnemyAttackState`, `GelPuddleRegistry`; `ZombieShock.cs`, `ZombieGel.cs`, `ZombieTreeStatus.cs`, `TreeStatusMath.cs` | Shipped: 5 styles; per-mode slot bonus; tree statuses (ice, stun, pull, status damage) |
| Zombie locomotion v4 | `_Prototype/Enemies/ZombieLocomotionV4.cs`, `_Prototype/Characters/` | `ZombieLocomotionMath`, `ZombieTransitionData`, `CadenceBand`, `StrideCalibrator` | Shipped (R3): gait pools, start/stop/turn clips, speed-locked cadence |
| Zombie look and variety | `_Prototype/Characters/`, `_Prototype/Enemies/` | `ZombieModuleLibrary`, `ZombieArmorLibrary`, `ArmorVisualBinder`, `ZombieAppearance`, `ZombiePalette` | Shipped |
| Threat director | `_Prototype/Enemies/` | `ThreatDirector`, `ThreatSnapshot`, `ThreatSettings` | Shipped: drives spawn weighting, music layers and HUD threat words |
| Crowd and navigation | `_Prototype/Navigation/` | `CrowdService`, `CrowdSolver`, `CrowdSpatialGrid`, `NavPathService`, `NavPathFollower`, `RegionNavRouter`, `CrowdFeelSettings` | Shipped |
| Spawning | `_Prototype/Spawn/`, `_Art/Environment/World/V6/spawn-gates-v6.json` | `EnemySpawnDirector`, `RoundPlan`, `RoundLedger`, `SpawnPointSelector`, `SpawnWeightCalculator`, `SpawnRelaxation`, `SpawnWorldProfile`, `SpawnRoomBalance`, `RearSpawnShare`, `TierSelectionPolicy` | Shipped: 22 V6 gates, active ceiling 40 |

## Run, economy and progression

| System | Folder | Main types | Status |
| --- | --- | --- | --- |
| Run and round flow | `_Prototype/Rounds/` | `RunDirector`, `RunPhase`, `RunEvents`, `RunFlowClock`, `RunSystemsInstaller`, `RunStatsTracker`, `RoundUpgradeShop` | Shipped |
| Gold economy | `_Prototype/Economy/` | `GoldService`, `GoldWallet`, `GoldSource` (incl. `WallBuy`), `GoldRewardCalculator`, `GoldRewardLedger`, `GoldOrb` | Shipped |
| Run tree v2 | `_Prototype/Rounds/RunTree/V2/`, `run-tree-v2.json` | `RunTreeV2`, `TreeCatalogV2`, `TreeResolver`, `TreeStats`, `TreeRuntime`, `TreeHooks`, `TreeEconomyV2`, `TreePresetsV2` | Shipped (R4, `RunTreeV2: 1`): 7 branches, 63 nodes + 7 alternatives, 10 synergies |
| Tree systems and statuses | `_Prototype/Rounds/RunTree/V2/Systems/` | `TreeSystemsRunner`, `TreeStatusSystem`, `TreeGravityWells`, `TreeBlastQueue`, `TreeAreaPool`, `ITreeStatusReceiver` | Shipped (R4) |
| Tree screen | `_Prototype/UI/Runtime/Screens/UpgradeTreeV2.cs`, `UiTreeEdges.cs`, `_Prototype/UI/Runtime/Presentation/TreeV2Presenter.cs`, `TreeArt.cs` | `UiTreeScreenV2`, `UiTreeEdges`, `TreeV2Rows`, `TreeV2Layout`, `TreeArt` | Shipped (R4) |
| Tree VFX | `_Prototype/Feedback/Vfx/Tree/`, `TracerRibbonRenderer.Tree.cs` | `TreeVfxLadder`, `TreeAuraVfx`, `TreeSeekTrail`, `TreeGoldOrbCue`, `TreeSocketCard` | Shipped (R4); further polish is not shipped |
| Awakening and Ash | `_Prototype/Rounds/RunTree/V2/TreeAwakening.cs`, `_Prototype/Profile/MetaPath/ProfileService.Awakening.cs`, `_Prototype/UI/Runtime/Screens/AwakeningPage.cs`, `meta-tree.json` | `TreeAwakening`, `UiAwakeningPage`, `UiTreeScreenRoute`, `UiAshRefundNotice` | Shipped (R4): 11 nodes, variety not power |
| Run tree v1 | `_Prototype/Rounds/RunTree/`, `run-tree.json` | `RunTreeCatalog`, `RunTreeGraph`, `RunTreeState`, `RunTreeHooks` | Retired: still built, no longer sold; the shipped config runs v2 |
| Meta path (old PATH page) | `_Prototype/Profile/MetaPath/` | `MetaPathCatalog`, `MetaPathRules`, `MetaPathSnapshot`, `ProfileService.Meta.cs` | Retired from the UI: 16 passives and 10 Special leanings stay in `meta-tree.json` for save compatibility |
| Profile and save | `_Prototype/Profile/` | `ProfileService`, `ProfileStore`, `ProfileData` (schema v6), `ProfileMigration`, `CompletionAuthority`, `ProfileRunCommitter` | Shipped |
| Achievements and unlocks | `_Prototype/Achievements/` | `AchievementCatalog`, `AchievementEngine`, `AchievementRunTracker`, `UnlockCatalog` | Shipped: 53 defined (6 mode-specific), 9 unlockables; 7 read retired v1-tree counters |

## Characters and animation

| System | Folder | Main types | Status |
| --- | --- | --- | --- |
| Hero character | `_Prototype/Characters/`, `_Art/Characters/Player/HeroV2/` | `PlayerSkinnedVisual`, `LocomotionV4` (8-way starts, stops, pivots), `FootIkAdapter`, `WeaponGripIk`, `MagazineContactIk`, `SecondaryChainDriver`, `CharacterAnimatorLod` | Shipped |
| Animation contracts | `_Prototype/Characters/`, `_Art/Editor/` | `CharacterAnimationEvents`, `CharacterAnimatorContract`, `ArtAnimationEventContract`, `ArtAnimatorBuilder` | Shipped |
| Ragdoll and death families | `_Prototype/Characters/` | `RagdollProxyPool`, `ZombieDeathFamily` | Shipped |

## Presentation

| System | Folder | Main types | Status |
| --- | --- | --- | --- |
| Fire VFX and bullet look | `_Prototype/Feedback/Vfx/`, `_Art/VFX/Resources/Shaders/DD_VfxRibbon.shader` | `MuzzleSystem`, `TracerRibbonRenderer` (+ `TracerRibbonRenderer.Ap043.cs`, the short-exposure streak), `ImpactSystem`, `Ap041Vfx`, `CasingEffect`, `RocketVisualSystem` | Shipped; known test findings accepted for R4 (see the README) |
| Game feel | `_Prototype/Feedback/` | `FeelEvents`, `FeelEventRouter`, `FeelProfileTD`, `FeelHitStopTD`, `HitStopService`, `FeelTimeScale`, `FeedbackBudget`, `HitFeedbackDirector` | Shipped |
| Blood, decals and moment VFX | `_Prototype/Feedback/Vfx/` | `BloodSystem`, `FeelBrushVfxSystem`, `DamageVignetteLayer`, `ArcSystem`, `GelSystem`, `GroundFireSystem`, `ExplosionDirector` | Shipped |
| Audio | `_Prototype/Audio/Runtime/` | `PrototypeAudioDirector`, `AudioMixerBridge`, `AudioVoicePool`, `AudioEventRouter`, `WeaponFireAudio`, `WeaponReflections`, `WeaponTailBed`, `MusicDirector`, `TreeAudio`, `AfterRainAmbience` | Shipped: weapon audio v5, R4 sounds with per-sound voice caps |
| Acoustic spaces | `_Prototype/Audio/Runtime/`, `_Art/Environment/World/V6/acoustic-spaces-v6.json` | `AcousticSpaceMap`, `AcousticProbe`, `AcousticSpace` (Open / Corridor / Hall) | Shipped: baked map, checked by the integration gate |
| UI kit | `_Prototype/UI/Runtime/Kit/` | `UiFactory`, `UiTokens`, `UiBrushTokens`, `UiBrushKit`, `UiBrushPane`, `UiMotion`, `UiNavigation`, `UiStrings`, `UiSettings`, `UiInputGlyphs` | Shipped: brush-ink design language, EN/TR |
| HUD v4 | `_Prototype/UI/Runtime/Screens/`, `_Prototype/UI/Runtime/Hud/` | `RunHudScreen` (+ `.Ink`, `.Juice`), `HudReticle`, `HudPlayerArcs`, `HudRoundNumeral`, `HudGoldReadout`, `HudWallBuyPrompt` | Shipped: persistent HUD ≤ 1.5 % of the screen |
| Menu, loading and logo | `_Prototype/UI/Runtime/Screens/`, `_Prototype/UI/Runtime/Flow/` | `MainMenuScreen`, `MenuCinematic`, `UiCurtain`, `UiLoadingTips`, `KeyArtRotation`, `UiLogoAnimator`, `UiLogoTimeline`, `UiLogoMenuIntro` | Shipped |
| Prepare Run | `_Prototype/UI/Runtime/Screens/SelectionScreen.cs`, `_Prototype/UI/Runtime/Prepare/`, `_Prototype/UI/Editor/PrepareRunStageBuilder.cs` | `SelectionScreen`, `PrepareRunLayout`, `PrepareRunStage`, `PrepareDifficulty` | Shipped (R4): weapon turntable, five stat cards, difficulty, map |
| Pause, results and meta screens | `_Prototype/UI/Runtime/Screens/`, `_Prototype/UI/Runtime/Presentation/` | `PauseScreen`, `ResultsScreen`; profile, statistics and achievements panes | Shipped: in-pause settings, auto-pause on focus loss, PLAY AGAIN |

## World

| System | Folder | Main types | Status |
| --- | --- | --- | --- |
| World V6 | `_Prototype/World/`, `_Art/Editor/`, `_Art/Environment/World/V6/`, `_Art/Scenes/World_V6_Greybox.unity` | `WorldSource` (V6 default), `WorldBootstrap`, `WorldLayout`, `GreyboxV6Builder`, `TdVisibilityAudit`, `WorldWalkScan`, `WorldFptpSplit` | Shipped: 1,064 placements, 130 lanterns, 12 interiors |
| Vegetation | `_Prototype/World/Vegetation/` | `VegetationSystem`, `VegetationDrawSet`, `VegetationZoner`, `VegetationPlacer`, `VegetationPushMap`; `VegetationWorldHookup` in `_Art/Runtime/` | Shipped: ≈ 59.8k instances on Cinematic |
| Look and lighting | `_Prototype/World/`, `_Art/Runtime/` | `WorldAtmosphere`, `CloudShadowDriver`, `WorldWind`, `PlanarReflection`, `VolumetricLightFeature`, `ScreenAtmosphereFeature`, `SunDisc`, `LookV3Post` | Shipped |
| Breakable props | `_Prototype/Props/` | `BreakableProp` | Shipped |
| Art pipeline | `_Art/Editor/`, `_Art/Runtime/` | `ArtImportContract`, `ArtLibraryManifest`, `ArtVisualRegression`, `ArtAp040WorldKit`, `ArtAp040WeaponAttachBuilder` | Tooling |

## Diagnostics and tooling

| System | Folder | Main types | Status |
| --- | --- | --- | --- |
| QA bots | `_Prototype/Diagnostics/Qa/`, `tools/qa/` | `QaRunBot`, `QaTreeBot`, `QaBotBrain`, `QaVirtualGamepad` | Dev only: seeded menu-to-run play, tree-buying archetypes, scenarios |
| Capture agents | `_Prototype/Diagnostics/Capture/`, `_Prototype/UI/Runtime/Flow/UiCaptureAgent*.cs`, `_Prototype/Core/Boot/PrototypeCaptureAgent.cs` | `TdCaptureAgent`, `MkCaptureAgent`, `UiCaptureAgent`, `PrototypeCaptureAgent` | Dev only, except `PrototypeCaptureAgent`, which release builds keep for the smoke run and which does nothing without its flag |
| Perf probe | `_Prototype/Diagnostics/PilotPerfProbe.cs` | `PilotPerfProbe` | Dev only and release-probe builds: forced 40-body crowd, 10 s warm-up, 60 s windows |
| Telemetry and crash log | `_Prototype/Diagnostics/Telemetry/`, `_Prototype/Diagnostics/` | `RunTelemetry`, `CrashLogReporter`, `ProfileSandboxFlag` | Shipped: local files only, nothing uploaded |
| Build, CI and integration | `tools/`, `_Prototype/Core/Editor/`, `_Prototype/Core/Build/` | `integrate.sh`, `compile-check.sh`, `main-ci.sh`, `test.sh`, `build.sh`, `smoke.sh`, `release-publish.sh`, `app-display-name.sh`; `DdBuild`, `BuildInfo` | Tooling |
| Capture tooling | `tools/` | `td-capture.sh`, `mk-capture.sh` | Tooling |
| Machine and user-data safety | `tools/` | `userdata-guard.sh`, `ug-step.sh`, `guard-queue.py`, `player-run.sh`, `slot.sh`, `dd-env.sh` (stall watchdog, disk gate) | Tooling |

For contracts, event flows and performance rules, see [ARCHITECTURE.md](ARCHITECTURE.md).
