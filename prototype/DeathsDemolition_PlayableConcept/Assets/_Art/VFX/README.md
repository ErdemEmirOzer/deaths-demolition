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
