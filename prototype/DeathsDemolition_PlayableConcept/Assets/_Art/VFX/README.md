# `_Art/VFX` — WP-013 atmosphere & foliage VFX (M3)

Generated assets (`Tools › Deaths Demolition › Art Pipeline › Run Atmosphere VFX (WP-013)`, batch
`-executeMethod DD.Art.Pipeline.EditorTools.ArtPipelineMenu.RunAtmosphereVfx`). Re-running restores the
contract; GUIDs are stable. Nothing here is a product decision — every number is PROTOTYPE-ONLY
(`PROTOTYPE-ONLY.md` §0, WP-013 line); the palette / weather decision (DEC-068 detail) stays with the user.

## Wind — ADR-013-1 (one global, `WorldWind`)

| Global | x | y | z | w |
|---|---|---|---|---|
| `_DD_WindVector` | dir.x | dir.z | strength 0..1 (gusts folded in) | wind time (s, own clock: pause freezes) |
| `_DD_WindParams` | sway amplitude m @ strength 1 (0.28) | sway Hz (0.45) | flutter amplitude m (0.035) | flutter Hz (2.6) |

Written only by `WindDriver` (`WorldAtmosphere.EnsureWind`; Run and pilot both) from `WorldAtmosphere.Wind`
(direction 24°, base 0.35, gust 0.4 over 9 s Perlin, direction wander ±22°). Consumers: `DD/Foliage Wind`
(vertex), `DD/Water v1` (ripple direction), the drift prefabs (`WindZone.windMain = strength × 0.9`, External
Forces). A scene with no driver reads calm (0) after every domain reload.

Vertex colour = AP-003 `_AP003_WIND` (R sway 0→1 root→tip, G phase, B flutter, A stiffness 1 at root).
Displacement = R·(1−A) · amplitude · strength · (lean 0.35 + oscillation 0.65) along the wind, plus
B·(1−A) flutter along the normal, minus a quadratic pull-down. A mesh with no colour stream reads (1,1,1,1)
→ R·(1−A) = 0: fences and crates that share a foliage material do not move.

## Shaders (`Assets/_Art/Shaders`)

- `DD/Foliage Wind` — URP Lit surface (same property names as URP/Lit, so `ArtMaterialBuilder` recipes apply),
  wind vertex animation in all four passes (ForwardLit, ShadowCaster, DepthOnly, DepthNormals), `Cull Off` +
  per-pixel front-face normal flip for the cards (sakura back faces lit), alpha-to-coverage under MSAA 4× with
  fwidth alpha sharpening (distant cards keep their coverage), 0.25 translucency on cards, SRP Batcher CBUFFER.
  Roles: `green_foliage_01..03` (cutout), `green_foliage_01..03_solid` (opaque, culled), `sakura_foliage_01..03`
  (cutout), `bamboo_culm` (opaque). `moss_*` and `bark_*` stay URP/Lit on purpose (shared by lanterns / trunks).
  Hand-written HLSL rather than a `.shadergraph`: a graph cannot be authored from the batch pipeline and the
  gate (`ArtVfxGateTests`) reads pass count / compile errors from the source.
- `DD/Water v1` — transparent, ZWrite off, analytic ripple normals (three scrolling sine layers, one down-wind),
  depth fade (`_DepthFade` 0.6 m, camera depth texture), PBR with sky/probe reflection + Fresnel. `pond_water`
  role. AP-015 owns the full water shader.

## Drift prefabs (`Resources/`) and the region density table

| Prefab | Ceiling | Emission @ density 1 | Size | Regions (Run, `WorldAtmosphere.DefaultRunRegions`) |
|---|---|---|---|---|
| `DD_Pollen` | 240 | 20 /s | 3–6 cm | Global 86×5×86 m @ 3.2 m, density 1 |
| `DD_SakuraPetals` | 130 | 15 /s | 8–18 cm | Global 86×1×86 m @ 12 m, density 0.45 · EastGarden 26×1×52 m @ (31, 10, 0), density 1 |

Run ceiling (density-weighted) 240 + 59 + 130 = **429 ≤ 480** (REQ-104: nothing over 18 cm, ~5 px at the 20 m
camera). Pilot/authored slices: `WorldAtmosphere.RegionsFor(footprint, sakuraCrowns)` — pollen over the
footprint, petals only over renderers wearing an `M_sakura*` material. Region data proper arrives with WP-014;
`AtmosphereDrift.Configure(region, density, box)` is the seam. Mesh-quad + GPU instancing, no shadows,
`maxParticleSize` 0.02 (screen fraction cap). `-ddPilotAtmosphere off` (PilotPerfProbe) freezes wind, stops the
fields and disables post-processing for the A/B.

## Break burst + `BreakableProp` (REQ-113)

`DD_BreakBurst` (18 chips + 10 dust, one-shot, self-destroying) is instantiated by `BreakableProp.Hit` on the six
`SM_small_breakable_V0n` prefabs (component bound by the prefab builder / `ArtVfxBuilder.BindBreakables`):
intact `Geometry` off → `Broken` on, once; `PropImpacts.Report(collider, point, dir)` is the one line the hitscan
calls on world-geometry impacts. No health, reward or damage type.

## Post-processing

The URP renderer shipped with `postProcessData: none` — post-processing was off for every camera whatever the
camera flag said. `ArtVfxBuilder.EnablePostProcessData` assigns the package default; `WorldAtmosphere.ConfigureCamera`
turns it on for the gameplay camera (MSAA stays the AA). Grade values `WorldAtmosphere.PostFx` (shared by
`DD_Overcast_v1` and the Run profile); saturation −2, white balance −3/+1 after the REQ-105 measurement.

## Budget / measurements

PilotPerfProbe A/B and the readability numbers are in the WP-013 note (`planning/04-IMPLEMENTATION-PLAN.md` §WP-013).
Working readability thresholds (user decision §23 #4 open): capsule contrast ≥ 0.05 and ≥ 90 % of the ungraded
frame; gold separation from the palette ≥ 0.30 and ≥ 80 % of the ungraded frame, gold saturation ≥ 0.60.

---

# WP-019 — AP-010 effects (impacts, muzzle, explosion, armour break, death, gold, beacon, vignette)

Generated: `Tools › Deaths Demolition › Art Pipeline › Run VFX Catalog (WP-019)` (batch
`-executeMethod DD.Art.Pipeline.EditorTools.ArtPipelineMenu.RunVfxCatalog`). Source = `AP010/AP010-vfx-manifest-v002.json`
(34 rows; the GLB review copy is listed as `Review` with no asset). Staging (AP-010 exports → `AP010/`, sha256-verified):
`wp-019/tools/stage-ap010.py`. Six v001 sheets (impact armor/earth/stone/wood + armor emissive, zombie death) were
re-generated after the manifest was written: the manifest sha is stale for them; the shipped bytes are what is on disk
(`wp-019/evidence/ap010-staged.tsv`).

## Catalog and materials — ADR-019-1

- `Resources/DD_VfxCatalog.asset` (`VfxCatalog`): per row id (`VfxIds`), family, kind, grid, frames_used, fps, duration,
  loop, world size, origin_uv, readability limit, colour reserve, texture/emissive/normal/mesh and the authored material.
  The runtime reads every number from here (`VfxLibrary`); the manifest is the single source.
- One material per playable row in `AP010/Materials/M_vfx_<id>.mat` on `DD/VFX Flipbook` (unlit sheet player:
  `_Sheet`, `_Cols/_Rows`, per-instance `_Frame`/`_Alpha`/`_AlphaCap`/`_Tint` through a MaterialPropertyBlock —
  no material per instance). Additive: muzzle, shockwave, beacon column/rings, gold sparkle/trail; alpha: the rest.
  Decals render just before the transparent queue. The gold pack mesh uses URP/Lit with the AP-010 albedo + HDR
  emissive (×2, so bloom lifts the coins).
- Texture contract `ArtVfxContract` (sRGB, straight alpha kept, clamp, BC7/BC5, never streamed): sheets 1024
  (muzzle/shockwave 512), decals 256 (scorch/splats 512), cards/gold/beacon/vignette 512, HUD sprite 256 →
  resident ≈ 18 MB (VFX line of the texture budget, ≤ 20 MB; the art set stays at its own 200 MB line).
- Shader lesson: `UnityPerMaterial` is all-`float` and the vertex colour rides `TEXCOORD2` — a `half`/`float` mix
  and a `COLOR` varying made every sheet sample its transparent border on Metal. `VfxShaderDiagTests` keeps a
  pixel-level smoke test.

## Runtime — pools, events, dressing

`HitFeedbackDirector` stays the single owner of hit dressing and the FeedbackBudget; `VfxDressing` is its AP-010 layer,
called AFTER the budget check. Pools (RecycleOldest): 48 `FlipbookEffect` quads, 64 `DecalEffect` quads, 8 + 8
`AtlasBurstEffect` (armour fragments from the 2×2 atlas, smoke puffs). A missing catalog → the WP-002 procedural bursts.

| Event | Source | AP-010 dressing |
|---|---|---|
| world impact | `ReportSurfaceImpact` (hitscan) | surface sheet (stone/wood/earth/armor/flesh) + decal on static surfaces; surface = `SurfaceTag` → `EnemyHitbox` → material role name (`SurfaceResolver.FromMaterialName`) |
| body hit | `ReportEnemyHit` flesh / armour tiers | flesh or armour sheet (no decal, the body moves) |
| muzzle | `ReportMuzzleFlash` | 4-frame muzzle sheet parented to the muzzle |
| explosion | `ReportExplosion` | fireball sheet (origin_uv 0.5/0.125) + ground shockwave sheet (0.3 s) + 3 smoke puffs (alpha ≤ 0.45) + scorch decal 2.4 m / 12 s (alpha ≤ 0.40) |
| armour break | `ReportEnemyHit` ArmorBreak tier + `ArmorVisualBinder.AnyBroken` | armour sheet ×1.3 + lacquer splinter decal under the body; fragments at the plate anchor (WP-017 hook) |
| zombie death | `ReportEnemyHit` Death/HeavyDeath | death sheet at the feet (alpha → 0.35 after 0.28 s; 1.0 m / alpha 0.6 when the camera is within 1.5 m) + blood layer: 2 cards ±30°, 36 droplets, growing pool (below); without the blood layer: one of two splats (30 s, alpha ≤ 0.7) |
| blood (WP-019b) | flesh hit / critical / armour break / death | `BloodSystem`: directional splash card (0.26 s, HDR tint), GPU-instanced droplets, ground/wall marks in a 128-slot round-persistent pool (LRU fade, impact ≤ 40 %, gold 0.6 m exclusion, alpha ≤ 0.85), death pool 0.4 → 1.1 m (1.4 m heavy), far marks merged into chunk meshes ≥ 18 m |
| gold pack | `GoldOrb.EnsureVisual` | `GoldPackVisual`: pouch mesh + sparkle card (pulse 0.85–1.0) + pull trail while magnetised (REQ-014) |
| beacon | `BeaconEffect` (WP-007 pickup) | two crossed column cards (UV scroll 0.2/s) + ground ring + 16-frame ring loop, beacon tint; HUD: `VfxLibrary.HudMarkerSprite` + `VfxLibrary.BeaconTint` (S-09 interface, not wired into the HUD here) |
| player damage | `PlayerHealth.AnyDamaged` | `DamageVignetteLayer`: AP-010 vignette × Vermilion600, flash in 4 f / out 12 f, low-health hold |

## Game feel — ADR-019-2 (`Resources/DD_GameFeel.asset`, `GameFeelSettings`)

Ceilings baked into the type: hit-stop ≤ 40 ms and only on armour break / Elite-Ultra death — a Standard death gets none, REQ-127 (the heavy-impact door is closed);
shake ≤ 0.15 m / 0.2 s (explosion 0.12 m / 0.20 s, armour break 0.075, heavy death 0.095); vignette ≤ 0.5
(flash 0.35, low-health hold 0.35 under 30 %). The REQ-045 camera bump stays in `PlayerSystem.PlayerDamageFeedback`
(0.09 m / 0.25 s — its 0.25 s predates this ceiling; Player file, noted as a devir); the vignette layer's own shake
knob ships at 0 so a hit is bumped once. `Enabled` switches the layer off for A/B.

## Blood layer — WP-019b (directive §10: gore HIGH, no dismemberment)

Design brief: art-director (Fable), `wp-019/evidence/blood-design-brief-fable.md`. Code: `Feedback/Vfx/BloodSystem.cs`,
`BloodDropletEffect.cs`, `BloodDecalMerger.cs`, `BloodPalette.cs` (band + recolour + slots), `DecalEffect` (grow / dry / fade).

- **Colour (DEC-077 band, ADR-019-2):** h 355–5°, s 0.70–0.85, v 0.25–0.45. The v002 flesh/zombie sheets are near-grey
  (mean v 0.12–0.23), so the importer stores each row's `MeanOpaqueColour` and the runtime tints with
  `target ÷ mean` (HDR gain ≤ 6): fresh card HSV(358°, 0.82, 0.45) × 1.6, droplets (357°, 0.85, 0.42), marks wet
  (358°, 0.80, 0.40) → dry (2°, 0.72, 0.28) over 30 s. Measured on the rendered quad by `VfxBloodTests`.
- **Direction:** splash = 0.6 × shot + 0.4 × reflect(shot, normal); the card rolls to it (`FlipbookEffect.SetAlign`), the
  mark ray bends 0.3 down. Normal hit 1 card / 12 droplets / ray mark (+ 40 % drip); critical 2 cards ±20° ×1.3 / 22 / 2
  marks; armour break 2 cards ±25° ×1.1 / 14 / 1 mark (kept under critical so the plate loss still reads).
- **Droplets:** ParticleSystem in mesh mode on the shared quad with `enableGPUInstancing` — the flipbook shader carries
  `#pragma instancing_options procedural:ParticleInstancingSetup` and reads the particle colour from
  `unity_ParticleInstanceData`. Simulation is CPU (Shuriken); VFX Graph would be a package addition. Die on world contact.
  Material: the synthesized `blood_droplet` row (tint baked, instancing on — a property block would break instancing).
- **Marks:** own pool of 128, lifetime = round (`RoundStarted` → 2 s fade + merged chunks cleared; `RunEnded` → clear).
  LRU fade 1 s from 8 slots short of full; impact marks ≤ 52 (40 %), death pools evicted last; a new impact mark
  within 0.25 m grows the existing one ×1.15 (cap 0.6 m); no mark within 0.6 m of a live gold packet (a pool is
  clipped to keep its edge out, skipped when its centre is inside); alpha cap 0.85.
- **Far LOD:** settled marks (> 2 s) beyond 18 m from the camera are copied into `BloodDecalMerger` chunks (one static
  mesh per material, ≤ 256 quads, ring overwrite, pre-sized lists — no GC) and their slot is freed. Event LOD: ≥ 20 m no
  droplets / no impact mark / one card ×1.2; ≥ 35 m the pool only.
- **AP-010B slots (pending, P-05):** `BloodSlots` resolve `fb_blood_splash_01..06`, `decal_blood_ground_01..06`,
  `decal_blood_wall_01..04`, `fb_blood_droplet`, `decal_blood_pool`, `fb_blood_death` in order, first playable row wins,
  v002 rows at the tail. Drop the set in `Assets/_Art/VFX/AP010B/` with its own `*-vfx-manifest-*.json` (same schema,
  `family: blood`, files prefixed `AP010B_`) and run the catalog: no code change. Ship sizes: sheets 2048, marks/pool
  1024, droplet 512; own budget line `ShippedBloodBudgetMb` = 60.

## AP-010B v003 (2026-09-17: 108 assets — HQ pass + blood set + wave 2)

Staged by `wp-019/tools/stage-ap010-v003.py`: `AP010_*` (HQ v003 + the 14 kept v002 rows) under `AP010/<sub>/`, `AP010B_*`
under `AP010B/<sub>/`, one manifest `AP010/AP010-vfx-manifest-v003.json` (paths relative to `Assets/_Art/VFX/`). Schema v003:
`slot` (the v002 stem the file fills — several files share a slot, e.g. a decal's albedo / normal / height), `supersedes`,
`alias_to`, `capsule_cover_s` (replaces `readability_limit_s`), `world_size_m` as a number / `[w, h]` / string, `blend`,
`tint_rgb` (LA greyscale sheets: luma × tint at the material, × `hdr` when additive). Ids are file stems minus prefix /
size / version (`_v003b` too); grid / fps / frames come from the manifest, never from the name. `family: blood` rows play
as painted (no recolour; a uniform 0.8 linear darkening keeps the on-screen coverage-weighted mean under v 0.45), the v002
rows remain the recoloured fallback. Bound / pending per row: `planning/work-packages/EVENT-COVERAGE-MATRIX.md`.

## Wave 3 — WP-047 (event hooks, weapon A/B/C, AP-017 identities, lanterns)

- `VfxEventBinder` (owned by `VfxDressing`): `EnemyCombatEvents.Raised` → `attack_commit` = `claw_swipe`, `spawn_rise` = `spawn_dust`
  (card lifted `SpawnDustLift` 0.5 m — the flipbook shader has no depth fade); `CharacterAnimationEvents.Any` foot events →
  `dust_step` with the surface tint (0.22 s per body, ≤ 26 m from the camera); `WeaponEvents.ReloadStarted` → `mag_drop` after
  0.45 s. Breaks and deaths keep the director path (one source per beat).
- Muzzle variants (DEC-083): A rifle long cone, B SMG short flash (0.75 size, 60 % duration), C shotgun wide bloom + pump smoke
  0.35 s later. AP-014 Special Weapon use sheets come through `SpecialWeaponSheets` (one runtime `DD/VFX Flipbook` material per
  usage, `SpecialWeaponVisualLibrary.FindSheet`); the WP-002 `ShockRingEffect` lines stay under the Kagura sheet at 60 % alpha,
  64 segments.
- `BreakableVfx` — the six AP-017 event ids (`BreakCeramic/Stone/Wood/Bamboo/Paper/Straw`) with residue cell, debris sheet and
  dust card; prefab name → id (`AP017_breakable_*`, AP-003B `small_breakable`, keywords). S-08 uses the same ids for SFX.
- `LanternFlames` — `lantern_flame` loop on every `SurfaceTag.Family` "lantern" anchor (≤ 40, own pool, one scan per scene);
  tint (1.0, 0.60, 0.28) × 0.9 (AD: off the gold-orb band, under the bloom threshold).
- Allocation (ADR-037-1): `VfxHotPathBisectTests` (S-02's contract fixture, `Profiler.enabled` frame counters, run alone or
  first in a batch — after other PlayMode fixtures the counters read 0). Root causes of the WP-053 finding: pools prewarmed to
  a third of capacity (the rest Instantiated + Awake'd inside hit frames, ≈ 12 allocations each; 128 blood marks' worth per
  round) and `TextMesh.text`'s allocating getter plus the lazy number table. `FeedbackPoolWarmer` prewarms and activates every
  feedback pool at configure time; the number table is built at type load. Window (300 armoured hits + 30 explosions in one
  frame): idle 8 → full 8 / hits 8 / explosions 8 / numbers 8; S-02 contract core 16 ≤ idle 11 + 12 (was 112).
  `GC.GetAllocatedBytesForCurrentThread` is always 0 in Unity Mono — never cite it.

## WP-047D — Special reactions, barrel heat, palette, water look

- `VfxEventBinder` ← `SpecialWeaponUsageEvents.Usage` (WP-056) `Hit`: Kagura = `dust_step` ×1.6 at the feet along the front;
  Ofuda = `sw_ofuda_web` card parented to the body for the seal's life; Tide = stretched `water_splash` streak toward the
  well + `chain_arc_sheet` from the well centre (remembered from `FieldSpawned`) to the body, 1.2–3.5 m. Per-body throttle 0.3 s.
- Barrel heat: `barrel_heat` strip behind the muzzle along the rig-body → muzzle axis, alpha cap = heat (A 0.10 / B 0.06 /
  C 0.18 per shot, 0.35/s cooling, shown ≥ 0.25, hidden < 0.08). Not parented — repositioned per frame.
- `FeedbackPalette`: damage-number and hit-marker colours from S-09's `UiTokens` state tokens (critical = warning, kill =
  danger, armour = wisteria on the colour-blind palette). Blood untouched (DEC-077).
- `WaterLook`: the pond's `DD/Water v2` shallow / deep colours (one scan per scene) tint the splash, ring and Tide arc.

## Frame rate / resolution table (directive §10 "high quality" gate)

| Family | Sheet(s) | Source px | Ships at | Grid · frames · fps · s | World size | Notes |
|---|---|---|---|---|---|---|
| impact (×4 surfaces, v003) | `fb_impact_{armor,stone,wood,earth}` (+ armor emissive) | 2048² | 1024² | 8×8 · 16 · 30 · 0.53 | 0.44 m | f12–15 alpha tail; flesh retired → blood A1 |
| impact decals (v002 kept) | `decal_impact_*` (+2 normals) | 1024² | 256² | 1×1 | 0.40 m | 12 s, alpha ≤ 0.85 |
| muzzle (v003) | `fb_muzzle` (+ emissive) | 2048² | 1024² | 8×8 · 24 · 60 · 0.40 | 0.50 m | slot name says 4×4 — read the manifest |
| explosion (v003) | `fb_explosion` (+ emissive 2048²) | 4096² | 2048² (emissive 1024²) | 8×8 · 48 · 30 · 1.60 | 3.6 m | origin_uv 0.5/0.125 |
| shockwave (v003) | `fb_shockwave` / `card_shockwave` | 2048² / 1024² | 1024² / 512² | 4×4 · 16 · 53.3 · 0.30 / 1×1 | 5.0 m / 3.5 m | sizes unified in v003 (flipbook = max ring, card = mid ring) |
| smoke, scorch (v003) | `card_smoke`, `decal_scorch` (+ normal) | 1024² / 2048² | 512² / 1024² (normal 512²) | 1×1 | 1.0 / 2.4 m | smoke alpha ≤ 0.45, scorch ≤ 0.40 |
| armour break (v003) | `atlas_armorbreak`, `decal_lacquer_splinter` | 2048² / 1024² | 1024² / 512² | 2×2 atlas / 1×1 | 2–6 cm / 0.5 m | 10 fragments, 0.9 s |
| blood A1 / A7 / A6 (v003) | `fb_blood_splash_01..06`, `fb_blood_heavy_01..02`, `fb_blood_death_01..03` | 2048² | 1024² | 8×8 · 16–24 · 30 · 0.53–0.80 | 1.0 / 1.4 / 3.0 m | origin (0.12, 0.5) +u → hit direction; A6 origin (0.5, 0.08); cover ledger 0 (death_01 0.10 s) |
| blood A2 droplets (v003) | `sheet_blood_droplets` | 1024² | 512² | 4×4 · 16 sprites | 2–8 cm | GPU-instanced mesh particles, random cell |
| blood A3 / A4 marks (v003) | `decal_blood_ground_01..06`, `decal_blood_wall_01..04` (albedo; normal / height 128²) | 2048² | 512² | 1×1 | 0.45×0.25 – 1.4×0.5 m | 128-slot round-persistent pool, alpha ≤ 0.85 |
| blood A5 pool (v003) | `decal_blood_pool_albedo` (+ 8 growth masks 4096×2048 → 1024×512) | 2048² | 1024² | 1×1 / 4×2 | 2.8 m authored, 1.1 / 1.4 m in play | growth by scale; mask shader = devir |
| zombie death (v002 kept) | `fb_zombiedeath` | 2048² | 1024² | 8×8 · 18 · 30 · 0.60 | 1.6 m | gore-medium fallback |
| tier deaths (v003) | `death_special`, `death_ultra` | 2048² | 1024² | 8×8 · 24 / 32 · 30 | 2.64 / 3.96 m | replace A6 for Special / Ultra kills |
| wave 2 bursts (v003) | `spark` (+ `spark_streak`), `leaf_burst`, `paper_tear` | 512² / 1024² | 512² | 4×4 sprites / 4×4 · 8 · 60 | 1–4 cm / 2–16 cm / 3–12 cm | additive sparks hdr 3 |
| wave 2 cards (v003) | `water_splash` (+ ring), `armor_crack_dust`, `weapon_pickup_flash`, `weapon_expire`, `breakable_residue`, `player_death_overlay` | 2048² / 1024² | 1024² / 512² | per manifest | 0.5–1.2 m / screen | LA sheets tinted at the material |
| gold (v003) | `goldpack_V01` + albedo/emissive, `card_gold_sparkle`, `card_gold_trail` | 1024² / 1024² / 512×2048 | 512² / 512² / 256×1024 | 1×1 | 0.28 / 0.6 / 1.0 m | emissive ×2 |
| beacon (v003) | `beacon_column`, `beacon_ring`, `beacon_ring_anim` | 1024×4096 / 2048² / 2048² | 256×1024 / 1024² / 1024² | 1×1 / 1×1 / 4×4 · 16 · 16 · 1.0 loop | 1.2 × 6 m / 2.4 / 2.6 m | UV scroll 0.2/s |
| vignette / HUD | `vignette_damage` (v003), `hud_marker` (v002) | 2048² / 256² | 1024² / 256² | 1×1 | screen | vermilion tint / sprite |
| pending hooks (v003, in the catalog) | `spawn_dust`, `claw_swipe`, `dust_step`, `mag_drop` (+dust), `barrel_heat` (+distort), `chain_arc_sheet`, `ofuda_seal` (+burn), `straw_puff` (+cloud), `lantern_flame`, `player_death_overlay_pulse` | 256²–2048² | 256²–1024² | per manifest | per manifest | owners listed in the coverage matrix |

Shipped estimate (EditMode `VfxDokulari_SozlesmeyeUyar_ButceIcinde`): main line 30.7 / 32 MB, AP-010B 37.5 / 60 MB, total ≈ 68 MB.

## Measurements

`wp-019/evidence/`: EditMode + PlayMode gates, perf A/B (`vfx-base` vs `vfx-stress` = 10 impacts/s (half of them
flesh hits, every 4th critical) + 1 explosion/s + 5 deaths/s on the 40-body crowd, `-ddPilotVfxStress`; stage 04d reads
the GPU time of the URP draw passes — effect GPU = transparents(stress) − transparents(base), ceiling 1.5 ms),
readability with effects active, family close-ups (`captures/families/`) for the §5 side-by-side, blood colour
readout (`captures/blood-colour-readout.txt`).
