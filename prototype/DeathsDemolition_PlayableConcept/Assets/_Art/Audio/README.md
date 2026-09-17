# Assets/_Art/Audio — produced sound set (WP-021 / AP-011)

**What lives here:** the AP-011 delivery — `AP011/*.wav` (48 kHz mono 16-bit, Git LFS) and `AP011-audio-manifest.json` (one entry per `AudioSoundId`: event, files, loop, LUFS, true peak, bus, spatial, provenance). Nothing here is referenced directly by code: the manifest is turned into `Assets/_Prototype/Audio/Resources/DD_AudioCatalog.asset` by **DD › Audio › Build Catalog + Region Map** (`AudioCatalogBuilder`, idempotent — the catalog stores the manifest's SHA-256), and the runtime library (`AudioClipLibrary`) overlays the catalog on the WP-002 procedural recipes per sound id. A sound without a catalog entry keeps its recipe; a checkout without LFS content is audible.

## Adding or replacing sounds

1. Produce and process in `asset-production/AP-011/` (see its README: Seed Audio prompts → `tools/ap011_process.py` windows/loops/normalisation → `exports/`), then `tools/ap011_manifest.py <worktree> <version>` copies files and rewrites the manifest.
2. Run **DD › Audio › Build Catalog + Region Map** (or `-executeMethod DD.Prototype.Audio.EditorTools.AudioCatalogBuilder.BuildAllBatch`). Importer settings are pinned by the builder: Vorbis, quality 0.6 (0.7 loops), force mono, preserve sample rate, one-shots decompressed on load, loops ≥ 8 s streamed.
3. EditMode `DD.Prototype.Tests.EditMode.Audio` re-measures the shipped clips (BS.1770 `LoudnessMeter`) against the manifest and the standard: SFX −18 LUFS, ambience −20, music −16, master true peak ≤ −1.5 dBTP (measured Vorbis clip ≤ −0.5), ±1.5 LU — or peak-limited below target (short transients).

## Standards and structure (ADR-021-1/2 in `planning/04-IMPLEMENTATION-PLAN.md`)

- Mixer `Assets/_Prototype/Audio/Resources/DD_AudioMixer.mixer`: Master → Music / Ambience / SFX-World / SFX-Player / UI; snapshots Run / RoundBreak / Pause / Menu; exposed `MasterVolume … UiVolume` (dB). Built by **DD › Audio › Build Mixer** (reflection over the internal controller; an existing valid asset is never rebuilt).
- Event map: `AudioEventMap` (every `CombatAudioEvent`, `RunAudioEvent`, player, zombie vocal, UI, six ambience beds, four music layers; WP-018 rows reserved).
- Regions: `DD_AudioRegionMap.asset` from `AP005_Placement.asset` polygons (center/north/east/south/west); `AmbienceDirector` cross-fades the region beds over 2.5 s.
- Music: `MusicDirector` — base / percussion / tension / accent, driven by `Intensity` (progress, alive enemies, round-start surge) and tier/Special presence; pulls back between rounds.
- Reserved cue: `SwSpawnGong` belongs to `SpecialWeaponSpawn` only (REQ-053).

## WP-021C — UI event set, master limiter, surface tags, tier vocals (AP-011 v005, 0 credits)

- **AP-011 v005 (derived):** 29 identities / 72 files produced offline from the existing 48 kHz/24-bit set (`asset-production/AP-011/tools/ap011_derive.py` from `v005/derive_spec.json`, sound-designer Fable 5.1): 18 UI (`UiHover … UiNewBest`, `HudLowHealth`, `HudMagazineEmpty`, `HudThreat{Encircled,Rear,Strong}`) and 11 tier vocals (`Zombie{Windup,Attack,Hit,Death}Special`, `ZombieIdle{Special,Elite,Ultra}`, `ZombieAggro{Standard,Special,Elite,Ultra}`). Manifest `provenance` = `derived offline … from <sources>`, `layers` = the recipe; quality stamps inherit the sources' noise floor (a continuous source ⇒ `continuous`). A/B table and gate: `asset-production/AP-011/v005/review/`.
- **UI events (WP-022B `UiEvents.Raised`):** `AudioEventMap` carries one row per `UiEvent` member; the 18 bound rows play through `AudioEventRouter.OnUiEvent` → `AudioMixPlan.UiEvent`; the rest are owned by another row on the same frame (`UiSound → PrototypeAudio.*`, Run/Combat/Player) and stay silent here — a UI interaction never plays twice. `UiSound.Reveal` now forwards to `PrototypeAudio.Reveal` (`UiReveal`).
- **Master volume:** `UiSettings.MasterVolume` → `PrototypeAudio.MasterVolume` (single owner) → the mixer's exposed `MasterVolume`, ramped in linear gain over 60 ms (`PrototypeAudioDirector.TickMasterVolume`); `AudioListener.volume` stays 1 while the mixer exists (no double attenuation).
- **Master limiter:** Unity's built-in Compressor on Master (threshold −8 dB, attack 10 ms, release 90 ms, make-up 0), inserted by `AudioMixerBuilder.EnsureMasterLimiter` (idempotent, same asset GUID) and exposed as `MasterLimiterThreshold/Attack/Release`; `Verify` requires it. The PlayMode test `Limiter_EnYuksekYigin_…` plays the loudest stack and samples `AudioListener.GetOutputData` (peak ≤ −1 dBFS).
- **Surface tags (ADR-021-1 v2):** `DD.Art.Ap005SurfaceTags` tags every World_AP005 placement instance at scene load from its placement **family** (`FamilySurface` table: ground/hero_earth → Dirt, quiet_paving/ring/gate/shore_path/curb/rock/lantern/plaster/roof → Stone, bridge/veranda/corridors/timber/bamboo/sheds → Wood, pond → Water, vegetation/straw → Foliage) plus the earth stand-in (Dirt), and registers `AudioSurfaceHints.Refine` so the ground reads as Stone inside the placement's stone routes. No scene or prefab edit; a new family (AP-015P) = one table row (EditMode `YuzeyEtiketleri_HerPlacementAilesininKuraliVar` fails on an unknown family).
- **AP-011 v006 (user decision, floor 200 → 100, ≤ 26 cr):** 17 Seed Audio renders (40.8 cr actual — 2.0–2.8 cr/render, the 1.1 assumption was wrong; 14.8 cr over the ≤ 26 allowance, reported and accepted by C-00; AP-011 reconciled from the transaction record: 92 charged renders, 135.4 cr net) for what layering could not reach — `SmgFire` is now the **B Carbine** (A Rifle / C Shotgun already produced: one voice per weapon), `HeavyMotorLoop` / `HeavySpinDown`, `ImpactPaper`, `Ricochet`, `ChainArc`, `SwBeaconLoop`, `SwExpired`, `AmbWoodCreak`, `HitMarker`, `ArmorImpact`, `FleshHit`, own takes for `ZombieHitSpecial` / `ZombieAggroUltra` — plus 11 credit-free derivations (`KillMarker`, `ArmorCrack`, `CriticalHit`, `EnemyDeath`, `HeavyEnemyDeath`, `SwGroundExpired`, `ImpactDefault`, `ImpactFoliage`, scatter ×3). Manifest v006 = 119 ids / 283 files; **no live sound is procedural any more** (the WP-002 recipes stay as the no-catalog fallback; the six unused WP-002 beds left the prewarm list).

## WP-048 — Audio Wave 3 (AP-011 v007, 0 credits)

- **Beat surface:** `AudioEventRouter.OnCombatBeat` subscribes to WP-040 `EnemyCombatEvents` — telegraph_start / attack_commit / attack_hit / stagger / armor_break_<region> / death_<tier> / spawn_rise / spawn_land own the zombie vocals, the claw, the plate drop and the region break voices (`ArmorBreak{Helmet,Body,Leg}`); the WP-021 AttackPhase poll and the DamageEvents/animation duplicates are gone (EnemyDamaged = pain grunt only, EnemyDied = bookkeeping).
- **Breakables:** `AudioEventRouter.BreakSoundFor(prop)` maps the AP-017 family (root name) to `Break{LanternStone,LanternPaper,Planter,Fence,Bale,Bench,Sign,Shoji}`; AP-003B small_breakable stays `BreakCeramic`.
- **Look B (wet):** `AmbienceDirector.WetLook` — slate drips in the scatter ring, distant thunder on its own 40–90 s clock, and a 3D pond bed at the pond centroid registered by `Ap005SurfaceTags.RegisterPond` (`AudioSurfaceHints.PondCenters`).
- **Ready, reserved until their producers land:** weapon foley `Weapon{A,B,C}{Eject,Chamber,MagOut,MagIn,Switch,Lowered,Raised}` + `PickupGrab/Done` (WP-032C clip events), `Footstep{WaterShallow,WaterDeep,Gravel,Pavement}` (AP-015P plates, WP-038).
- **AP-011 v007:** 41 derived identities / 98 files (design: Opus 5 stand-in — Fable 429; `asset-production/AP-011/v007/`), manifest v007 = 160 ids / 381 files; Unity build category Sounds 8.1 MB.

## WP-057 — Music v2 & mix pass (AP-011 v008, 0 credits)

- `MusicDirector`: the WP-033 threat layer (0–3) is a floor under the intensity ramp (perc 0/0.55/0.70/0.72, tension 0/0/0.50/0.66, L3 base 0.70 + accent 0.76); stingers (`PlayStinger`: round break, Special pickup, death, run end) play over the layers and duck them to 0.55 (1.5 s release); menu / results music are their own loops (`Music Phase Loop`) cross-faded against the run layers, which now leave entirely outside a run.
- Mix: new software bus `AudioBus.Music` (mixer group Music; F1 slider), `AudioPlacement.DuckMusic` (HudLowHealth + threat chips ≈ −3 dB / 0.35 s — the taiko band rule), snapshot mix table v2 written in place by `AudioMixerBuilder.EnsureSnapshotMix` (Run Music −5 / Amb −4 / World 0 / Player −1 / UI −3; RoundBreak −3/−3/−6/−3/−2; Pause −14/−12/−18/−18/−2; Menu −4/−10/−80/−80/−3); `Verify` requires it. Phase meter (PlayMode): fight −22.6 / break −26.4 / menu −27.1 dBFS RMS.
- AP-011 v008 (Fable design): StingerRoundBreak ×2, StingerSpecialPickup ×2, StingerDeath, StingerRunEnd, MusicMenu (45 s loop), MusicResults (34 s = 13-bar loop) — manifest v008 = 166 ids / 389 files.

## WP-058 — Special Weapon usage voices (WP-056 SpecialUsageEvent; AP-011 v009, 0 credits)

- `AudioEventRouter.OnSpecialUsage`: Fired → the Special's own fire character (SwFire{Kagura,Ofuda,Tide}) over HeavyWeaponFire; FieldSpawned → Kagura wave-front one-shot / Ofuda web hum and Tide well as **3D loops** at the field origin (director `Special Field` pool, Impact bus) until FieldEnded (+ end voice); Hit → per-body touch (gain × Falloff01, 60 ms throttle per Special); Activated / Deactivated stay with the spin-up / expired cues. `AudioEventMap.SpecialUsage(id, kind)` → 12 distinct ids; SwSpawnGong is not used by any of them (REQ-053).
- Fable measurement gate v007 + v008 applied (`asset-production/AP-011/v007/review/AP011-quality-gate-v007-applied.md`): weapon A/B/C Switch/Lowered/Raised now differ (B > A > C centroid), C Chamber pump body, Gravel/Pavement distinct, thunder band 142–157 Hz, stinger conditions (bell +1 st, SpecialPickup ≥ 2 s, Death pad-led, RunEnd −2 st). Manifest v009 = 178 ids / 408 files.

## WP-058B — mix follow-ups (0 credits)

- **MusicTension retuned +4 st** (designer decision after the Fable v008 gate: the stem sat off the base's tonal centre) by resample + phase-vocoder stretch back to the exact stem length, so the four layers stay lock-step (`asset-production/AP-011/tools/ap011_music_arrange.py`, `TENSION_SHIFT_ST`).
- Phase meter now reports BS.1770 loudness (K-weighted, gated) instead of dBFS RMS; `MusicDirector.CombatTrim` trims the score −2 dB at threat layer 3; snapshot UI trims RoundBreak 0 dB / Menu −1 dB.
- `AmbPondBed` re-derived (crest 26.5 → 15.2 dB, −22 LUFS on target).

## WP-058C — plates → footstep variants (WP-038) and enemy Special reactions (WP-055), 0 credits

- WP-038 plates carry Dirt / Stone / Water tags; `AudioSurfaceHints.FootstepVariant` (set by `Ap005SurfaceTags.FootstepVariantFor`) refines by plate id: `AP015_paving_*` → FootstepPavement, `AP015_ground_center_*` → FootstepGravel (the gravel courtyard), AP-015 water surface → FootstepWaterShallow; everything else keeps the kind's default clip. FootstepWaterDeep stays reserved (no wading-depth signal).
- WP-055 `sw_push / sw_web / sw_pull` beats → ZombieReactPush / Web / Pull (AP-011 v010, Fable design): the body's side of the Kagura push, Ofuda net and Tide pull (the field's touch is SpecialUsage.Hit). Manifest v010 = 181 ids / 417 files.
