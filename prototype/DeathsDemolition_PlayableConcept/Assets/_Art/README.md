# `Assets/_Art/` — production art root

Import Contract v1 (`Editor/ArtImportContract.cs`, applied by `ArtAssetPostprocessor` to every asset under this folder, verified by the EditMode asset gate) is the single source of truth for how models and textures import. Folder policy: ADR-011-2 (WP-011); prefab contract: ADR-012-2 (WP-012); placement contract: ADR-014-1 (WP-014); texture and build policy: ADR-015-1/2 (WP-015, v2 budgets WP-017); skinned characters: ADR-016-1/2 (WP-016); armour states and the AP-005B delta: ADR-017-1/2 (WP-017). Style: `StyleGuide/style-guide-v1.md`.

## Texture policy (ADR-015-1, WP-015)

Every contract PNG (`Textures/Source|Derived/T_<role>_<Map>.png`) belongs to a distance class decided by its role's most demanding use in the AP-005 world (a role shared by a paving plate and a prop takes the paving class). The class fixes the shipped maximum size and whether the mips stream; the gate test `ArtAssetGateTests.DokuPolitikasi_*` fails on any texture outside the table or off its class size, and `ArtImportContractTests.DokuSinifi_*` pin the table. A role missing from the table imports at the Prop size and is reported — a new map can never ship at 2048 by accident.

| Class | Max size | Mip streaming | Roles (texture role slugs) | Why |
|---|---|---|---|---|
| Ground / paving | 2048 | on | `earth` (AP-005 ground + hero_earth plates), `gravel` (shore path, basin), `stone` (courtyard paving, curbs, veranda, bridges, lanterns, rocks) | The only surfaces the fixed camera ever sees at ≤ 20 m over a 4 m tile |
| Structure | 1024 | on | `wood`, `plaster`, `plaster_hf003`, `roof`, `earth_01..03` (shore-path / basin module surfaces) | Walls, timber, roofs and module surfaces at 20–45 m |
| Foliage / atlas | 1024 | on | `bark`, `bark_01..03`, `moss_01..03`, `green_foliage_01..03`, `sakura_foliage_01..03`, `bamboo_culm` | Cards and trunks; cutout alpha keeps mip coverage (WP-012) |
| Prop | 512 | on | `prop_tsukubai`, `prop_handcart`, `prop_strawbales`, `prop_barrels`, `prop_mossrock`, `prop_shed` (AP-005B, WP-017; Tripo ORM: no AO, metallic 0) | Dressing props ≤ 4 m: 512 on a 4 m shed is 128 px/m, above what the camera resolves |
| Breakable | 512 | on | — (reserved) | `small_breakable` shares `bamboo_culm`/`roof`/`wood` |
| Sky | 256 | off | `sky` (the 4×256 gradient `ArtSkyBuilder` writes) | A skybox has no renderer for the streaming system to rate |
| Character | 2048 | on | `player`, `player_skin1/2b/3` (skin 2 = 2b Steel/Indigo, user 2026-09-16), `zombie_atlas` (WP-016), `zombie_armor` (AP-008, WP-017) | The one thing the camera frames at ~100 px; ADR-016-1 |

`roof_courses` (Structure) is the generated 256² tile-course detail normal for the AP-005B roof copy (`ArtTextureRepack.BuildRoofCourses`).

**Budgets v2 (ADR-015-1 v2, WP-017):** shipped-bytes estimate at the class sizes — world set (every class but Character) ≤ 120 MB, character set (player + zombie atlas + armour atlas) ≤ 80 MB, total ≤ 200 MB (`ArtImportContract.Shipped*BudgetMb`; measured 116.8 / 69.3 / 186.1 MB). Resident streaming budget stays 160 MB. If the character set ever exceeds its line the armour atlas drops to 1024 (technical rule).

Formats are unchanged from WP-011: BC7 (BaseColor sRGB, masks linear), BC5 (Normal), mipmaps, trilinear, aniso 4; the AP `Packed` maps stay in the repo as provenance and are never bound, so they do not ship. Mipmap streaming is on for every quality level with a 160 MB budget (`ProjectSettings/QualitySettings.asset`, `streamingMipmapsActive` / `streamingMipmapsMemoryBudget` — the only ProjectSettings fields WP-015 touched besides the build list). The gameplay camera (19.9 m, −49°, 1080p) resolves ~55–70 px per metre, so a 4 m ground tile samples mip ≥ 2 of a 2048 map; 1024 on walls and foliage and 512 on props lose nothing the fixed camera can show — the art-director side-by-side (`wp-015/evidence/captures`) is the visual proof.

Measured (WP-014 → WP-015, M1 Pro 1080p, Run + AP-005 world, 40 enemies): shipped texture set 63 maps 337.2 MB → 110.7 MB estimated in the build (all-2048 would be 330.7 MB); resident art textures — see `planning/04` §WP-015 (E) for the probe numbers.

## Build list policy (ADR-015-2, WP-015)

`ProjectSettings/EditorBuildSettings.asset`: the **enabled** entries are the release scene list — Boot, MainMenu, Selection, Run, Results, World_AP005 (Run loads it additively; `WorldSource`). The **disabled** entries that follow are dev-only scenes — the AP-001 pilot `Scenes/Pilot/AP001_GoldenSlice` (the perf probe's `-ddPilotScene AP001_GoldenSlice` target) and `Scenes/AP_LibraryShowcase` — which only the development build carries (`LifecycleProbeBuild.DevelopmentScenes`: enabled first, then disabled, so Boot stays index 0). The release Player (`-buildOSXUniversalPlayer`) builds enabled scenes only. Editor PlayMode tests load the dev-only scenes by path (`EditorSceneManager.LoadSceneInPlayMode`), never by name. `PlacementImporter.RegisterInBuildSettings` inserts the world scene after the last enabled entry so the release list stays contiguous. Tests: `ArtRepoPolicyTests.BuildSettings_ReleaseAltiSahneEtkin_*`, `ProjectIntegrityTests.BuildSettings_*`.

## Placement overrides (WP-015)

`Environment/World/AP005_placement-overrides.json` (schema `dd-placement-overrides-v1`: `{ schema, source, overrides[ { id, position_delta [bx, by, bz] m in Blender space, reason } ] }`) nudges named instances of the untouched AP-005 source at import (`PlacementImporter.ApplyOverrides`; ≤ 1 m per entry, unknown ids and duplicates are errors). The placement asset carries the file's sha256 and the overridden ids; `WorldPlacementTests.Overridelar_*` checks every instance against source ± delta. Current entries: the WP-014 camera-plane bamboo culms (i1532, i1535, i1605) and the culm directly behind i1532 on the same rays (i1549). A production fix in the AP-005 source supersedes an entry.

## AP-005B delta and material overrides (ADR-017-2, WP-017)

`Environment/World/AP005_placement-delta-v002.json` (schema `dd-placement-delta-v1`: `base`, `target`, `removed_ids[]`, `changed[]`, `added[]`, `new_families[]`) is applied by `PlacementImporter.ApplyDelta` on the untouched v001 source before the overrides: removed ids must exist, changed ids keep their place with the new record, added records follow the v001 survivors (2354 − 169 + 202 = 2387). The asset records the delta's sha256, counts and removed ids; `deltaVersion = "v002"` marks the touched instances. Records may carry a full XYZ euler (`rotation_euler_deg`; the eight AP-005B gable panels tilt about Y) — `WorldPlacementAsset.ToUnityRotation` maps Blender's R = Rz·Ry·Rx through the (x, y, z) → (x, z, y) reflection (every angle flips sign; X→X, Y→Z, Z→Y); v001 records are yaw-only and unchanged. `material_override` names the world-master-only materials and is mapped by `PlacementImporter.MaterialOverrides` to library roles applied on the instance's renderer slots (prefab-instance overrides, no material instances): `AP005_M_plaster_world[_ap001]` → `plaster_hf003_world` / `plaster_world` (same maps, linear albedo × 0.82 → mean sRGB #CDCBC1, art verdict F1); `AP005_M_roof_world` → `roof_world` (AP-002 roof + the generated tile-course detail normal, F5); `AP002_M_stone_v009_build_v003` → `stone` on the whole gate_transition mesh (F2 kerbs). Tests: `WorldDeltaTests`. The AP-005B prop families live in `Environment/Families/AP-005B/` (library `AP005B_Library.json`, 6 prefabs, `PrefixToPackage AP005B_`).

## Readability thresholds (DEC-077)

`Runtime/ReadabilityThresholds.cs`: route occlusion — a 2 m route sample whose 1.8 m capsule has more than 50 % of its five axial rays cut by the solid environment fails, and the count of failing samples must be 0 (`WorldAp005Tests.Okunabilirlik_*`; fade occluders and culm-thin capsules ≤ 0.15 m are reported separately, and after the overrides there are none); player/environment contrast — Michelson contrast of the enemy-proxy capsule against its surround at the gameplay pose with fog, particles and post-fx on ≥ 0.06 (`AtmosphereVfxTests.Pilot_Okunabilirlik_*`). Armour (ADR-017-1, WP-017): `ArmorTests.Okunabilirlik_DEC077_Zirh_*` measures in-engine the value difference of the armour pixels against the cloth beneath (armour toggled off), against the adjacent 6-px unarmoured band of the same figures and against the ground ring, plus the silhouette change — reported against the art-director's proposed rule (band min 0.10 / mean 0.15 + shape); the rule value is an S-10 / user decision.

## Fog (DEC-076)

`WorldAtmosphere`: linear fog from 52 m (unchanged — nothing inside the 45 m combat range from the camera receives fog, REQ-104) to 210 m in the Presentation A mist colour sRGB (0.815, 0.843, 0.870); the shared sky's horizon (`ArtSkyBuilder`) follows the fog colour. WP-013's 138 m / background-colour ramp is kept as `FogEndWp013` / `FogColorWp013` for the A/B captures only.

## Surface tags (ADR-021-1 v2, WP-021C)

World_AP005 instances get their `SurfaceTag` at scene load, from data: `Runtime/Ap005SurfaceTags.cs` maps the placement **family** to a `SurfaceKind` (Dirt / Stone / Wood / Water / Foliage) and tags each `Instances` child plus the ground stand-in (Dirt; Stone inside the placement's stone routes via `AudioSurfaceHints.Refine`). Rule for new content (AP-015P, any family): add the family to `Ap005SurfaceTags.FamilySurface` — no scene file or prefab edit; an unknown family falls back by role and fails the EditMode audio gate. If the importer ever writes tags itself, use the same table so the two never disagree.
