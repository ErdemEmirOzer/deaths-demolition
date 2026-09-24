# Systems Index

This index lists every major system in Deaths Demolition. Folders are relative to `prototype/DeathsDemolition_PlayableConcept/Assets/`.

- **Owning work packages** are the packages that introduced or last reshaped a system. Their specs are in `planning/work-packages/`.
- **Session** is the topic session that owns the domain (see `planning/04-IMPLEMENTATION-PLAN.md`).

**Status key**

| Status | Meaning |
|---|---|
| **Shipped** | On `main` with tests |
| **Partial (B1)** | The first slice of a v3 package is on `main`; later slices are still planned |
| **Direction** | A signed design document exists; no code yet |

| System | Folder | Main types | Owning WPs | Session | Status |
|---|---|---|---|---|---|
| Boot & scene flow | `_Prototype/Core/Boot/` | `PrototypeBootstrapper`, `PrototypeBootFlags`, `PrototypeScenes`, `PrototypeCaptureAgent` | WP-000, WP-001 | S-01 | Shipped |
| Config & placeholders | `_Prototype/Core/Config/` | `PrototypeConfig` (+ Economy/Enemy/Feedback/Player/Progression/Round/Weapon sections), `PrototypeOnlyAttribute`, `FeelConfig` | WP-000 | S-01 | Shipped |
| Pooling | `_Prototype/Core/Pooling/` | `PoolService`, `PrototypePool<T>`, `PooledBehaviour`, `PoolExhaustedPolicy` | WP-000 (prototype base) | S-01 | Shipped |
| Input | `_Prototype/Core/Input/`, `_Prototype/Input/` | `IInputSource`, `IExtendedInputSource`, `InputPreferences`, `AimAssist`, `PrototypeInputSource`, `InputRemapService`, `GamepadRumble` | WP-024, WP-070 B4 | S-01 / S-02 | Shipped |
| Weapon data (v3) | `_Prototype/Core/Weapons/`, `_Prototype/Weapons/Arsenal/` | `WeaponDefinition`, `ArsenalCatalog`, `Arsenal`, `WeaponIdMigration`, `ClassicWeaponDefinition` (legacy) | WP-034, WP-069 | S-02 | Partial (B1): 3 weapons migrated; 7 classics + 2 Specials planned |
| Firing & hitscan | `_Prototype/Weapons/` | `WeaponController`, `ClassicWeaponRuntime`, `HitscanEngine`, `PlayerWeaponBinding`, `StaggerGovernor`, `WeaponModifierState` | WP-002, WP-042 | S-02 | Shipped |
| Projectiles | `_Prototype/Weapons/Projectiles/` | `ProjectileSystem`, `ProjectileState`, `ProjectileLaunch`, `ProjectileDriver`, `ProjectileEvents` | WP-069 (ADR-069-1) | S-02 | Partial (B1) |
| Special weapons | `_Prototype/Weapons/` | `SpecialWeaponDirector`, `SpecialWeaponRuntime`, `SpecialWeaponPickup`, `KaguraWaveField`, `OfudaSealWeb`, `TideWell`, `SpecialDropValidator` | WP-007, WP-056 | S-02 | Shipped. New Specials (chain lightning, slick gel) are planned in WP-069 (DEC-094) |
| Damage & armour | `_Prototype/Damage/` | `DamageContracts` (`DamageHit`, `IDamageReceiver`, `BodyRegion`), `RegionalDamageBody`, `ArmorSet`, `HitboxRegistry`, `DamageEvents`, `DamageEventMeta` | WP-002, WP-006 | S-02 | Shipped |
| Player | `_Prototype/Player/` | `PlayerMotor`, `PlayerAim`, `PlayerHealth`, `PlayerRig`, `PlayerSpawner`, `PlayerUpgradeBinding` | WP-002 | S-02 | Shipped |
| Camera modes | `_Prototype/Core/Camera/`, `_Prototype/Camera/` | `CameraMode`, `CameraModeSettings`, `CameraModeProfile`, `CameraModeService`, `PrototypeCameraRig`, `CameraOcclusionFader`, `CameraShakeService`, `CameraAimRay` | WP-070 | S-01 | Shipped (B1–B4, B6, B7) |
| Quality tiers | `_Prototype/Core/Quality/`, `_Prototype/Core/Editor/QualityTierSetup.cs` | `QualityTier`, `QualityTiers` | WP-075 C1 (contract `QUALITY-TIERS-V2.md`) | S-05 | Partial: core API and URP assets; per-feature rows being wired |
| Enemy runtime | `_Prototype/Enemies/` | `ZombieEnemy` (partial ×5), `EnemyFactory`, `EnemySystemHost`, `TierBehaviourProfile`, `EnemyVersionApplier`, `EnemyHitReactionState` | WP-002, WP-010, WP-028, WP-052 | S-03 | Shipped |
| Enemy attacks (AI v3) | `_Prototype/Enemies/` | `EnemyAttackState`, `ZombieAttackStyle`, `AttackStyleProfile`, `AttackSelector`, `AttackSlotBoard`, `GrabState` | WP-076 (DEC-096) | S-03 | Partial (B1): Special interactions, `DeathImpulse` struct and World v5 spawns planned |
| Threat director | `_Prototype/Enemies/` | `ThreatDirector`, `ThreatModel`, `ThreatSnapshot`, `ThreatSettings`, `ThreatMusicFeed`, `ThreatHudFeed` | WP-033, WP-050 | S-03 | Shipped |
| Crowd & navigation | `_Prototype/Navigation/` | `CrowdService`, `CrowdSolver`, `CrowdSpatialGrid`, `NavPathService`, `NavPathFollower`, `RegionNavRouter`, `CrowdFeelSettings`, `ThreatLevelFeed` | WP-030, WP-054, WP-055B, WP-038R, WP-061M | S-03 | Shipped |
| Spawning | `_Prototype/Spawn/` | `EnemySpawnDirector`, `RoundPlan`, `RoundLedger`, `SpawnRefillClock`, `SpawnPointRegistry`, `SpawnPointSelector`, `SpawnWeightCalculator`, `SpawnRelaxation`, `TierSelectionPolicy` | WP-003, WP-010, WP-030, WP-050 | S-03 | Shipped |
| Run & round flow | `_Prototype/Rounds/` | `RunDirector`, `RunPhase`, `RunEvents`, `RunFlowClock`, `RunSystemsInstaller`, `RunStatsTracker` | WP-003 | S-03 | Shipped |
| Upgrade shop | `_Prototype/Rounds/` | `RoundUpgradeShop`, `UpgradeOfferPolicy`, `UpgradeCardCatalog`, `RunBuildState` | WP-004, WP-008 | S-04 | Shipped. Progression tree v2 is at Direction stage (WP-077, DEC-095) |
| Gold economy | `_Prototype/Economy/` | `GoldService`, `GoldWallet`, `GoldRewardCalculator`, `GoldRewardLedger`, `GoldOrb`, `GoldPacketLifecycle` | WP-004, WP-008 | S-04 | Shipped |
| Profile & save | `_Prototype/Profile/` | `ProfileService`, `ProfileStore`, `ProfileData` (schema v4), `ProfileMigration`, `CompletionAuthority`, `ProfileRunCommitter`, `ProfileCosmetics` | WP-005, WP-009, WP-009H | S-04 | Shipped |
| Achievements & unlocks | `_Prototype/Achievements/` | `AchievementCatalog`, `AchievementEngine`, `AchievementRunTracker`, `UnlockCatalog` | WP-023, WP-035, WP-022D | S-04 / S-09 | Shipped |
| Characters & animation | `_Prototype/Characters/` | `CharacterAnimationEvents`, `CharacterAnimatorContract`, `CharacterAnimationSet`, `IZombieAnimationDriver`, `ZombieSkinnedVisual`, `PlayerSkinnedVisual`, `CharacterAnimatorLod`, `FootIkAdapter`, `SecondaryChainDriver` | WP-016, WP-018, WP-032/B/C | S-06 | Shipped |
| Ragdoll & death families | `_Prototype/Characters/` | `RagdollProxyPool`, `ZombieDeathFamily` | WP-071 | S-06 | Partial (B1): AP-022 clip set and powered ragdoll planned |
| Animation import contract | `_Art/Editor/` | `ArtAnimationEventContract` (v3), `ArtAnimationClipContract`, `ArtAnimatorBuilder` | WP-018, WP-071 | S-06 | Shipped (v3 validator) |
| VFX & game feel | `_Prototype/Feedback/`, `_Prototype/Feedback/Vfx/` | `HitFeedbackDirector`, `FeedbackBudget`, `HitStopService`, `CameraShakeBridge`, `DeathSequence`, `GameFeelSettings`, `VfxDressing`, `BloodSystem`, `CasingEffect` | WP-013, WP-019, WP-042 | S-07 | Shipped. v3 profiles at Direction stage (WP-073) |
| Audio | `_Prototype/Audio/` | `PrototypeAudioDirector`, `AudioMixerBridge`, `AudioVoicePool`, `AudioMixPlan`, `MusicDirector`, `AmbienceDirector`, `AudioClipLibrary`, `SynthOps`, `LoudnessMeter` | WP-021/B/C, WP-048, WP-065 | S-08 | Shipped. Audio v3 at Direction stage (WP-072) |
| UI kit & screens | `_Prototype/UI/Runtime/` | `UiFactory`, `UiTokens`, `UiMotion`, `UiButtonV3`, `UiNavigation`, `PrototypeUiFlow`, `MainMenuScreen`, `SelectionScreen`, `RunHudScreen`, `UpgradeScreen`, `PauseScreen`, `ResultsScreen` | WP-005, WP-022 A–K, WP-068 | S-09 | Shipped; UI v3 slices ongoing (WP-068) |
| World assembly | `_Prototype/World/`, `_Art/Runtime/` | `WorldBootstrap`, `WorldBuilder`, `WorldLayout`, `WorldNavigation`, `Ap005WorldBootstrap`, `WorldPlacementAsset` | WP-014, WP-017, WP-020, WP-038 | S-05 | Shipped. World v5 at Direction stage (WP-074) |
| Rendering & look | `_Prototype/Rendering/`, `_Art/Editor/` (lighting, sky, LUT builders) | `PrototypeShaders`, `PrototypeShaderLibrary`, `ArtLightingBuilder`, `LutBuilder` | WP-029, WP-075 | S-05 | Shipped. Atmosphere & Look v3 at Direction stage (WP-075) |
| Art pipeline | `_Art/Editor/`, `_Art/Runtime/` | `ArtImportContract`, `ArtCharacterPrefabBuilder`, `ArtWeaponBuilder`, `ArtVfxBuilder`, `ArtLibraryManifest`, `ArtVisualRegression`, `ReadabilityThresholds` | WP-011, WP-012, WP-015 | S-01 / S-05 | Shipped |
| Breakable props | `_Prototype/Props/` | `BreakableProp`, `PropImpacts` | WP-038B | S-05 | Shipped |
| QA bot & gates | `_Prototype/Diagnostics/Qa/`, `tools/qa/` | `QaRunBot`, `QaBotBrain`, `QaVirtualGamepad`, `QaReport`, `QaReadabilityGate`, `QaWindowModeGate` | WP-026, WP-061, WP-070 B7 | S-10 | Shipped. M4-R1 acceptance plan at plan stage (WP-078) |
| Probes, telemetry, crash log | `_Prototype/Diagnostics/` | `PilotPerfProbe`, `LifecycleProbe`, `RunTelemetry`, `FeelTelemetry`, `CrashLogReporter`, `ProfileSandboxFlag` | WP-025, WP-045 | S-01 / S-10 | Shipped |
| Build & integration tooling | `tools/` | `build.sh`, `smoke.sh`, `test.sh`, `integrate.sh`, `post-integrate.sh`, `slot.sh`, `backup-repo.sh`, `qa/rc.sh`; Editor `DdBuild`, `BuildStamp` | WP-025, WP-031, WP-043, WP-051, WP-066 | S-01 | Shipped. Release/distribution decisions are still open (WP-066) |

For contracts, event flows and performance rules, see [ARCHITECTURE.md](ARCHITECTURE.md).
