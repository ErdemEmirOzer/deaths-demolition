# `Assets/_Art/` — production art root

Import Contract v1 (`Editor/ArtImportContract.cs`, applied by `ArtAssetPostprocessor` to every asset under this folder, verified by the EditMode asset gate) is the single source of truth for how models and textures import. Folder policy: ADR-011-2 (WP-011); prefab contract: ADR-012-2 (WP-012); placement contract: ADR-014-1 (WP-014); texture and build policy: ADR-015-1/2 (WP-015). Style: `StyleGuide/style-guide-v1.md`.

## Texture policy (ADR-015-1, WP-015)

Every contract PNG (`Textures/Source|Derived/T_<role>_<Map>.png`) belongs to a distance class decided by its role's most demanding use in the AP-005 world (a role shared by a paving plate and a prop takes the paving class). The class fixes the shipped maximum size and whether the mips stream; the gate test `ArtAssetGateTests.DokuPolitikasi_*` fails on any texture outside the table or off its class size, and `ArtImportContractTests.DokuSinifi_*` pin the table. A role missing from the table imports at the Prop size and is reported — a new map can never ship at 2048 by accident.

| Class | Max size | Mip streaming | Roles (texture role slugs) | Why |
|---|---|---|---|---|
| Ground / paving | 2048 | on | `earth` (AP-005 ground + hero_earth plates), `gravel` (shore path, basin), `stone` (courtyard paving, curbs, veranda, bridges, lanterns, rocks) | The only surfaces the fixed camera ever sees at ≤ 20 m over a 4 m tile |
| Structure | 1024 | on | `wood`, `plaster`, `plaster_hf003`, `roof`, `earth_01..03` (shore-path / basin module surfaces) | Walls, timber, roofs and module surfaces at 20–45 m |
| Foliage / atlas | 1024 | on | `bark`, `bark_01..03`, `moss_01..03`, `green_foliage_01..03`, `sakura_foliage_01..03`, `bamboo_culm` | Cards and trunks; cutout alpha keeps mip coverage (WP-012) |
| Prop | 512 | on | — (reserved for AP-015 prop sets; none today) | Lanterns, benches, crates share structure/foliage roles now |
| Breakable | 512 | on | — (reserved) | `small_breakable` shares `bamboo_culm`/`roof`/`wood` |
| Sky | 256 | off | `sky` (the 4×256 gradient `ArtSkyBuilder` writes) | A skybox has no renderer for the streaming system to rate |

Formats are unchanged from WP-011: BC7 (BaseColor sRGB, masks linear), BC5 (Normal), mipmaps, trilinear, aniso 4; the AP `Packed` maps stay in the repo as provenance and are never bound, so they do not ship. Mipmap streaming is on for every quality level with a 160 MB budget (`ProjectSettings/QualitySettings.asset`, `streamingMipmapsActive` / `streamingMipmapsMemoryBudget` — the only ProjectSettings fields WP-015 touched besides the build list). The gameplay camera (19.9 m, −49°, 1080p) resolves ~55–70 px per metre, so a 4 m ground tile samples mip ≥ 2 of a 2048 map; 1024 on walls and foliage and 512 on props lose nothing the fixed camera can show — the art-director side-by-side (`wp-015/evidence/captures`) is the visual proof.

Measured (WP-014 → WP-015, M1 Pro 1080p, Run + AP-005 world, 40 enemies): shipped texture set 63 maps 337.2 MB → 110.7 MB estimated in the build (all-2048 would be 330.7 MB); resident art textures — see `planning/04` §WP-015 (E) for the probe numbers.

## Build list policy (ADR-015-2, WP-015)

`ProjectSettings/EditorBuildSettings.asset`: the **enabled** entries are the release scene list — Boot, MainMenu, Selection, Run, Results, World_AP005 (Run loads it additively; `WorldSource`). The **disabled** entries that follow are dev-only scenes — the AP-001 pilot `Scenes/Pilot/AP001_GoldenSlice` (the perf probe's `-ddPilotScene AP001_GoldenSlice` target) and `Scenes/AP_LibraryShowcase` — which only the development build carries (`LifecycleProbeBuild.DevelopmentScenes`: enabled first, then disabled, so Boot stays index 0). The release Player (`-buildOSXUniversalPlayer`) builds enabled scenes only. Editor PlayMode tests load the dev-only scenes by path (`EditorSceneManager.LoadSceneInPlayMode`), never by name. `PlacementImporter.RegisterInBuildSettings` inserts the world scene after the last enabled entry so the release list stays contiguous. Tests: `ArtRepoPolicyTests.BuildSettings_ReleaseAltiSahneEtkin_*`, `ProjectIntegrityTests.BuildSettings_*`.

## Placement overrides (WP-015)

`Environment/World/AP005_placement-overrides.json` (schema `dd-placement-overrides-v1`: `{ schema, source, overrides[ { id, position_delta [bx, by, bz] m in Blender space, reason } ] }`) nudges named instances of the untouched AP-005 source at import (`PlacementImporter.ApplyOverrides`; ≤ 1 m per entry, unknown ids and duplicates are errors). The placement asset carries the file's sha256 and the overridden ids; `WorldPlacementTests.Overridelar_*` checks every instance against source ± delta. Current entries: the WP-014 camera-plane bamboo culms (i1532, i1535, i1605) and the culm directly behind i1532 on the same rays (i1549). A production fix in the AP-005 source supersedes an entry.

## Readability thresholds (DEC-077)

`Runtime/ReadabilityThresholds.cs`: route occlusion — a 2 m route sample whose 1.8 m capsule has more than 50 % of its five axial rays cut by the solid environment fails, and the count of failing samples must be 0 (`WorldAp005Tests.Okunabilirlik_*`; fade occluders and culm-thin capsules ≤ 0.15 m are reported separately, and after the overrides there are none); player/environment contrast — Michelson contrast of the enemy-proxy capsule against its surround at the gameplay pose with fog, particles and post-fx on ≥ 0.06 (`AtmosphereVfxTests.Pilot_Okunabilirlik_*`).

## Fog (DEC-076)

`WorldAtmosphere`: linear fog from 52 m (unchanged — nothing inside the 45 m combat range from the camera receives fog, REQ-104) to 210 m in the Presentation A mist colour sRGB (0.815, 0.843, 0.870); the shared sky's horizon (`ArtSkyBuilder`) follows the fog colour. WP-013's 138 m / background-colour ramp is kept as `FogEndWp013` / `FogColorWp013` for the A/B captures only.
