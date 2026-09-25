# Systems Index

This index lists every major system in Deaths Demolition as it stands on `main` in the M4-R1 revision round. Folders are relative to `prototype/DeathsDemolition_PlayableConcept/Assets/`.

- **Owning work packages** are the packages that introduced or last reshaped a system. Their specs are in `planning/work-packages/`.
- **Session** is the topic session that owns the domain (see `planning/04-IMPLEMENTATION-PLAN.md`).

**Status key**

| Status | Meaning |
|---|---|
| **Shipped** | On `main` with tests, active on the default player path |
| **Shipped (off)** | On `main` with tests but disabled by default until a follow-up lands |
| **In progress** | Part of the package is on `main`; named slices are still open |

| System | Folder | Main types | Owning WPs | Session | Status |
|---|---|---|---|---|---|
| Boot & scene flow | `_Prototype/Core/Boot/` | `PrototypeBootstrapper`, `PrototypeBootFlags`, `PrototypeScenes`, `PrototypeCaptureAgent` | WP-000, WP-001 | S-01 | Shipped |
| Config & placeholders | `_Prototype/Core/Config/` | `PrototypeConfig` (+ Economy/Enemy/Feedback/Player/Progression/Round/Weapon sections), `PrototypeOnlyAttribute`, `FeelConfig` | WP-000 | S-01 | Shipped |
| Pooling | `_Prototype/Core/Pooling/` | `PoolService`, `PrototypePool<T>`, `PooledBehaviour`, `PoolExhaustedPolicy` | WP-000 | S-01 | Shipped |
| Input | `_Prototype/Core/Input/`, `_Prototype/Input/` | `IInputSource`, `InputSourceRegistry`, `InputPreferences`, `AimAssist`, `PrototypeInputSource`, `InputRemapService`, `GamepadRumble` | WP-024, WP-059, WP-070 | S-01 / S-02 | Shipped |
| Weapon data (Arsenal v3) | `_Prototype/Core/Weapons/`, `_Prototype/Weapons/Arsenal/` | `WeaponDefinition`, `WeaponFpViewPose`, `ArsenalCatalog` (12 definitions), `Arsenal`, `WeaponIdMigration` | WP-069 (DEC-094/097/099) | S-02 | Shipped: 10 classics in 6 classes + 2 Specials |
| Firing & hitscan | `_Prototype/Weapons/` | `WeaponController`, `ClassicWeaponRuntime`, `HitscanEngine`, `PlayerWeaponBinding`, `FpViewPoseDriver`, `StaggerGovernor`, `WeaponModifierState` | WP-002, WP-042, WP-069 | S-02 | Shipped |
| Projectiles | `_Prototype/Weapons/Projectiles/`, `_Prototype/Weapons/RocketImpacts.cs` | `ProjectileSystem`, `ProjectileKind` (Bullet/Pellet/Rocket/Orb/Gel), `ProjectileDriver`, `ProjectileEvents` | WP-069 (ADR-069-1) | S-02 | Shipped |
| Special weapons | `_Prototype/Weapons/` | `SpecialWeaponDirector`, `SpecialImpacts`, `SpecialLinks`, `SpecialGelEffects`, `SpecialWeaponPickup`, `SpecialDropValidator` | WP-069 B4 (DEC-094), WP-056 | S-02 | Shipped: Raijū (chain lightning) and Namekuji (slick gel); Kagura/Ofuda/Tide retired |
| Damage & armour | `_Prototype/Damage/` | `DamageContracts` (`DamageHit`, `IDamageReceiver`, `BodyRegion`), `DeathImpulse`, `RegionalDamageBody`, `ArmorSet`, `HitboxRegistry`, `DamageEvents` | WP-002, WP-006, WP-076 | S-02 / S-03 | Shipped |
| Player | `_Prototype/Player/` | `PlayerMotor`, `PlayerAim`, `PlayerHealth`, `PlayerRig`, `PlayerSpawner`, `PlayerUpgradeBinding` | WP-002, WP-059 | S-02 | Shipped |
| Camera modes | `_Prototype/Core/Camera/`, `_Prototype/Camera/` | `CameraMode`, `CameraModeSettings`, `CameraModeProfile`, `CameraModeService`, `CameraModesFeel`, `PrototypeCameraRig`, `CameraOcclusionFader`, `CameraAimRay` | WP-070, WP-073 B4 | S-01 | Shipped: top-down (default), third-person, first-person; pause-menu selector |
| Quality tiers | `_Prototype/Core/Quality/`, `_Prototype/Core/Editor/QualityTierSetup.cs` | `QualityTier`, `QualityTiers` (`DefaultTier = Sinematik`) | WP-075 (DEC-100) | S-05 | Shipped: Cinematic default (30 fps), High (60 fps), Performance |
| Enemy runtime | `_Prototype/Enemies/` | `ZombieEnemy` (partials), `EnemyFactory`, `TierBehaviourProfile`, `EnemyVersionApplier`, `EnemyHitReactionState`, `ZombieRear` | WP-002, WP-028, WP-052, WP-076 | S-03 | Shipped |
| Enemy attacks & reactions (AI v3) | `_Prototype/Enemies/` | `AttackSelector`, `AttackSlotBoard`, `GrabState`, `EnemyAttackState`, `ZombieShock`, `ZombieGel`, `GelPuddleRegistry`, `AiV3RoundLog` | WP-076 (DEC-096) | S-03 | Shipped (B1–B8) |
| Threat director | `_Prototype/Enemies/` | `ThreatDirector`, `ThreatModel`, `ThreatSnapshot`, `ThreatMusicFeed`, `ThreatHudFeed` | WP-033, WP-050 | S-03 | Shipped |
| Crowd & navigation | `_Prototype/Navigation/` | `CrowdService`, `CrowdSolver`, `CrowdSpatialGrid`, `NavPathService`, `NavPathFollower`, `RegionNavRouter`, `NavRegionMarker`, `CrowdFeelSettings` | WP-030, WP-054, WP-038R, WP-076 B6 | S-03 | Shipped |
| Spawning | `_Prototype/Spawn/` | `EnemySpawnDirector`, `RoundPlan`, `RoundLedger`, `SpawnPointSelector`, `SpawnWeightCalculator`, `SpawnRelaxation`, `SpawnWorldProfile`, `SpawnRoomBalance`, `RearSpawnShare`, `TierSelectionPolicy` | WP-003, WP-050, WP-076 B6 | S-03 | Shipped: 22 V6 gates |
| Run & round flow | `_Prototype/Rounds/` | `RunDirector`, `RunPhase`, `RunEvents`, `RunFlowClock`, `RunSystemsInstaller`, `RunStatsTracker` | WP-003 | S-03 | Shipped |
| Progression tree v2 (in-run) | `_Prototype/Rounds/`, `_Prototype/Rounds/RunTree/` | `RoundUpgradeShop`, `RunTreeCatalog`, `RunTreeGraph`, `RunTreeState`, `RunTreeHooks`, `RunTreeEffectValues`, `RunTreeGroundFire` (`run-tree.json`) | WP-077 (DEC-095/098) | S-04 | Shipped: tree cards in the round-end shop, effects live |
| Meta path | `_Prototype/Profile/MetaPath/` | `MetaPathCatalog`, `MetaPathRules`, `MetaPathSnapshot`, `ProfileService.Meta` (`meta-tree.json`, 38 nodes) | WP-077 (DEC-095/098) | S-04 | In progress: catalog, rules and weapon locks on `main`; full node coverage is open |
| Gold economy | `_Prototype/Economy/` | `GoldService`, `GoldWallet`, `GoldRewardCalculator`, `GoldRewardLedger`, `GoldOrb` | WP-004, WP-008 | S-04 | Shipped |
| Profile & save | `_Prototype/Profile/` | `ProfileService`, `ProfileStore`, `ProfileData` (schema v4), `ProfileMigration`, `CompletionAuthority`, `ProfileRunCommitter` | WP-005, WP-009, WP-009H | S-04 | Shipped |
| Achievements & unlocks | `_Prototype/Achievements/` | `AchievementCatalog`, `AchievementEngine`, `AchievementRunTracker`, `UnlockCatalog` | WP-023, WP-035 | S-04 / S-09 | Shipped |
| Characters & animation | `_Prototype/Characters/` | `CharacterAnimationEvents` (v3), `CharacterAnimatorContract`, `ZombieSkinnedVisual`, `PlayerSkinnedVisual`, `WeaponGripIk`, `TwistCorrector`, `CharacterAnimatorLod`, `FootIkAdapter` | WP-018, WP-032, WP-071 | S-06 | Shipped (AP-022 deaths, attacks, reactions) |
| First-person arms | `_Prototype/Characters/FirstPersonArms.cs` | `FirstPersonArms` (29 FP clips, `FirstPersonView` layer) | WP-071 B4b | S-06 | Shipped (off): waits for the hand-orientation pass |
| Ragdoll & death families | `_Prototype/Characters/` | `RagdollProxyPool`, `ZombieDeathFamily` | WP-071, WP-076 | S-06 | Shipped |
| Animation import contract | `_Art/Editor/` | `ArtAnimationEventContract` (v3), `ArtAnimationClipContract`, `ArtAnimatorBuilder` | WP-018, WP-071 | S-06 | Shipped |
| VFX & game feel | `_Prototype/Feedback/`, `_Prototype/Feedback/Vfx/` | `HitFeedbackDirector`, `FeedbackBudget`, `HitStopService`, `MuzzleSystem`, `TracerRibbonRenderer`, `ImpactSystem`, `ArcSystem`, `GelSystem`, `GroundFireSystem`, `AtmosphereSystem`, `CameraFeelDriver`, `BloodSystem` | WP-019, WP-042, WP-073 | S-07 | Shipped (v3 B1–B6) |
| Audio | `_Prototype/Audio/` | `PrototypeAudioDirector`, `AudioMixerBridge`, `AudioVoicePool`, `WeaponFireAudio`, `SpecialAudio`, `RocketBlastAudio`, `ProjectileFlightAudio`, `AfterRainAmbience`, `MusicDirector` | WP-021, WP-048, WP-060, WP-072 | S-08 | Shipped (v3) |
| Acoustic spaces | `_Prototype/Audio/Runtime/` | `AcousticSpace` (Open/Corridor/Hall), `AcousticSpaceMap`, `AcousticSpaces.Resolver` (`DD_AcousticSpaces.asset`) | WP-072 B6 | S-08 | Shipped: V6 map, 17 spaces |
| UI kit & screens | `_Prototype/UI/Runtime/` | `UiFactory`, `UiTokens`, `UiMotion`, `UiNavigation`, `MainMenuScreen`, `SelectionScreen`, `RunHudScreen` (+ `.Ink`), `HudReticle`, `HudRearEdge`, `UpgradeScreen`, `UpgradeTree`, `PauseScreen`, `ResultsScreen` | WP-022, WP-068 | S-09 | Shipped (UI v3 ink HUD, tree shop, Results v3) |
| World V6 | `_Prototype/World/`, `_Art/Editor/`, `_Art/Scenes/World_V6_Greybox.unity` | `WorldSource` (V6 default), `WorldBootstrap`, `WorldLayout`, `GreyboxV6Builder`, `WorldV5Dress`, `WorldV5Ground`, `WorldV5Moss` | WP-074, WP-038, AP-023 | S-05 | Shipped: default world, NavMesh 2,734 triangles |
| Look & lighting | `_Prototype/World/`, `_Art/Runtime/`, `_Art/Editor/` | `WorldAtmosphere`, `WorldV5Lighting`, `WorldV5Sky`, `WorldV5Look`, `WorldV5SunFlare`, `ScreenAtmosphereFeature`, `LookV3Post`, `PlanarReflection` | WP-029, WP-075 (DEC-101/102) | S-05 | Shipped |
| Art pipeline | `_Art/Editor/`, `_Art/Runtime/` | `ArtImportContract`, `ArtCharacterPrefabBuilder`, `ArtWeaponBuilder`, `ArtVfxBuilder`, `ArtLibraryManifest`, `ArtVisualRegression` | WP-011, WP-012, WP-015 | S-01 / S-05 | Shipped |
| Breakable props | `_Prototype/Props/` | `BreakableProp`, `PropImpacts` | WP-038B | S-05 | Shipped |
| QA bot & gates | `_Prototype/Diagnostics/Qa/`, `tools/qa/` | `QaRunBot`, `QaBotBrain`, `QaVirtualGamepad`, `QaReport`, `QaReadabilityGate`, `QaWindowModeGate` | WP-026, WP-061, WP-070 B7, WP-078 | S-10 | Shipped; M4-R1 acceptance in progress (WP-078) |
| Probes, telemetry, crash log | `_Prototype/Diagnostics/` | `PilotPerfProbe`, `LifecycleProbe`, `RunTelemetry`, `CrashLogReporter`, `ProfileSandboxFlag` | WP-025, WP-045 | S-01 / S-10 | Shipped |
| Build, CI & integration tooling | `tools/` | `build.sh`, `smoke.sh`, `test.sh`, `main-ci.sh`, `compile-check.sh`, `integrate.sh`, `post-integrate.sh`, `backup-repo.sh`; Editor `DdBuild`, `BuildStamp` | WP-025, WP-031, WP-043, WP-051, WP-079 | S-01 | Shipped |
| Machine & user-data safety | `tools/` | `userdata-guard.sh`, `player-run.sh`, `slot.sh`, `dd-env.sh` (stall watchdog) | WP-043B/D, WP-051D, WP-066 | S-01 | Shipped |

For contracts, event flows and performance rules, see [ARCHITECTURE.md](ARCHITECTURE.md).
