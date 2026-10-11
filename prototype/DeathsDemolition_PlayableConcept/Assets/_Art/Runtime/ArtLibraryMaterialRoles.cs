using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-012 · The material roles the library import adds on top of the ten WP-011 roles
    /// (ADR-012-2 §Material). One row per <c>M_&lt;role&gt;.mat</c>; the rows are what the
    /// generated manifests reference in <c>materials[]</c> and what the material builder,
    /// the texture repack and the asset gate agree on.
    ///
    /// Sources (all byte-identical copies, provenance wp-012/derived/stage-provenance.json):
    ///   plaster_hf003            AP-002 selected HF003 plaster BaseColor (the AP-002 plaster;
    ///                            AP-001's plaster map stays as role "plaster")
    ///   bark/earth/moss_01..03   AP-003 material sets (BaseColor + Normal + Packed)
    ///   green_foliage_01..03     AP-003 material sets, BaseColor alpha = opacity → alpha cutout
    ///   green_foliage_0n_solid   same maps, opaque (the greenery/shrub solid variants)
    ///   sakura_foliage_01..03    AP-003 material sets, alpha cutout
    ///   bamboo_culm / gravel     AP-003B tiles, BaseColor only
    ///   stone_weathered          AP-003B: the WP-011 stone maps with the authored V×0.85 tint
    ///   pond_water               AP-003B flat placeholder (HSV 200°/0.22/0.27; shader is AP-015)
    ///
    /// The Packed maps follow the AP convention (G roughness, B metallic) and are repacked to
    /// the URP mask (R metallic, A smoothness) by ArtTextureRepack, exactly as for AP-001.
    /// AP-003's Height maps are not imported: URP/Lit uses none of them without parallax.
    /// </summary>
    public static class ArtLibraryMaterialRoles
    {
        public enum Kind
        {
            /// <summary>BaseColor + Normal + derived MetallicSmoothness, opaque.</summary>
            Textured = 0,
            /// <summary>BaseColor only, opaque, scalar smoothness.</summary>
            BaseColorOnly = 1,
            /// <summary>Flat linear albedo, no texture.</summary>
            Flat = 2,
            /// <summary>Like <see cref="Textured"/> plus alpha cutout from the BaseColor alpha, double-sided.</summary>
            Cutout = 3
        }

        /// <summary>WP-013 · which shader a role's material is authored on.</summary>
        public enum ShaderKind
        {
            /// <summary>Universal Render Pipeline/Lit.</summary>
            Lit = 0,
            /// <summary>DD/Foliage Wind: Lit surface + AP-003 wind vertex colour, double-sided lighting, coverage fix.</summary>
            FoliageWind = 1,
            /// <summary>DD/Water v1: transparent ripple surface with depth fade.</summary>
            Water = 2
        }

        public sealed class Spec
        {
            public string Role;
            public Kind Kind;
            /// <summary>WP-013: shader the material is built on (Lit unless the role moves in the wind or is water).</summary>
            public ShaderKind Shader = ShaderKind.Lit;
            /// <summary>Role whose textures this role binds (itself unless it is a tinted or solid variant).</summary>
            public string TextureRole;
            /// <summary>Multiplier on <c>_BaseColor</c> (linear); white = the texture is authoritative.</summary>
            public Color Tint = Color.white;
            public float Smoothness = 1f;
            public Color FlatLinearAlbedo = Color.black;
            public float Cutoff = 0.5f;
            /// <summary>WP-038L · BaseColorOnly with an alpha cutout from the BaseColor alpha (the AP-017 koi card sheet: RGBA only, no normal / ORM).</summary>
            public bool AlphaCutout;
            /// <summary>v008 · A cutout that renders its front faces only (the sugi crown pads: seen from below they vanish, the hanging fans read).</summary>
            public bool SingleSided;
            /// <summary>WP-017 · Texture role of a tangent-space detail normal overlay (<c>T_&lt;role&gt;_Normal</c>, URP <c>_DetailNormalMap</c>); null = none.</summary>
            public string DetailNormalTextureRole;
            public float DetailNormalScale = 1f;
            /// <summary>WP-017 · Detail UV tiling (URP <c>_DetailAlbedoMap_ST</c> drives both detail maps), repeats per base UV unit.</summary>
            public float DetailTiling = 1f;

            public bool HasDetailNormal => !string.IsNullOrEmpty(DetailNormalTextureRole);

            /// <summary>WP-074 B5 · Textured / Cutout without an ORM (the AP-023 card atlases ship albedo + normal only): scalar <see cref="Smoothness"/>, metallic 0, no derived mask.</summary>
            public bool NoPackedMap;

            /// <summary>WP-092 C · Texture role of an emission map (<c>T_&lt;role&gt;_Emission</c>, sRGB, the colour is in the map); null = no emission.</summary>
            public string EmissionTextureRole;
            /// <summary>WP-092 C · Linear multiplier of the emission map (<c>_EmissionColor</c> = white × this, HDR); baked into GI (BakedEmissive).</summary>
            public float EmissionIntensity;

            /// <summary>WP-092 R3 · DD/Foliage Wind <c>_Saturation</c>: saturation around the albedo's own luminance (display space; a boost
            /// leaves the warm arc 355–65° alone). 1 = as authored. Foliage-wind roles only (the builder refuses it on Lit / Water).</summary>
            public float Saturation = 1f;
            /// <summary>WP-092 R3 · <c>_HueShift</c>, HSV degrees, + = red → yellow → green → cyan (luminance kept). 0 = as authored.</summary>
            public float HueShift;
            /// <summary>WP-092 R3 · <c>_PetalColor</c>: linear multiplier on the albedo inside the petal mask (texture blue over green). White = none.</summary>
            public Color PetalColor = Color.white;
            /// <summary>WP-092 R3 · <c>_PetalBoost</c>: luminance-keeping saturation boost (1 + this) inside the petal mask. 0 = none.</summary>
            public float PetalBoost;

            /// <summary>WP-092 R3 · True when any colour-grade field leaves its identity value.</summary>
            /// <summary>WP-109 (C-00 11.10) · an opaque DD/Foliage Wind role that thins over the hero in top-down like the cutout crowns
            /// (keyword _TREE_REVEAL_SOLID): the grove's bamboo culms.</summary>
            public bool RevealSolid;

            /// <summary>DD/Foliage Wind roles: the wind multiplier (_WindScale); 0 for the bark (WP-109: it rides Foliage Wind for the
            /// reveal only, the trunks never sway).</summary>
            public float WindScale = 1f;

            public bool HasColourGrade => Saturation != 1f || HueShift != 0f || PetalBoost != 0f || PetalColor != Color.white;

            public bool HasPackedMap => (Kind == Kind.Textured || Kind == Kind.Cutout) && !NoPackedMap;
            public bool HasNormalMap => Kind == Kind.Textured || Kind == Kind.Cutout;
            public bool HasBaseColorMap => Kind != Kind.Flat;
            public bool HasEmission => !string.IsNullOrEmpty(EmissionTextureRole) && EmissionIntensity > 0f;
        }

        /// <summary>WP-092 C · Material roles of the AP-030 kit (P-05: atlas roles ap030_&lt;atlas&gt;, glow roles ap030_&lt;family&gt;_glow).</summary>
        public const string Ap030RolePrefix = "ap030_";

        /// <summary>WP-092 C · Texture roles that ship an Emission map (distinct, in row order).</summary>
        public static string[] EmissionTextureRoles()
        {
            var list = new List<string>();
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].HasEmission && !list.Contains(Rows[i].EmissionTextureRole))
                {
                    list.Add(Rows[i].EmissionTextureRole);
                }
            }

            return list.ToArray();
        }

        // WP-092 R3 (user note 10.10: "more vivid plants and objects, too much brown-yellow, sprinkle purple / pink / mauve"; AD verdict:
        // vivid = neutralise the brown-yellow, saturate the surfaces that carry colour). The grade values below were measured offline on
        // the shipped atlases (alpha ≥ 0.5 texels, the shader's DD_FoliageGrade reproduced in Python): luminance (linear Rec.709) of every
        // hue / saturation-graded texel is unchanged, so DEC-077's ΔY against the characters reads the same.

        /// <summary>WP-092 R3 · Canopy hue shift (black pine, bamboo, groundcover): HSV +5° = away from yellow toward blue-green (the AD's
        /// "−4…−6°, sarıdan uzak" in this property's sign; measured +4.3° pine / +6.6° bamboo / +5.0° groundcover display hue).</summary>
        public const float CanopyHueShift = 5f;
        /// <summary>WP-092 R3 · Canopy saturation (AD 1.10): measured CIELAB chroma +9 % pine, +13 % bamboo (with the hue), +10 % groundcover.</summary>
        public const float CanopySaturation = 1.10f;
        /// <summary>
        /// w7 (S-05 10.10, the TD frames): the black pine crowns are the cover's most saturated pixels and still olive — td6's top 5 %
        /// read hue 73° at s ≥ 0.62 (K2 P95 ≤ 0.50) under the (1.45, 1.40, 0.35) tint; +14° and × 0.95 for the pine only.
        /// </summary>
        public const float PineHueShift = 14f, PineSaturation = 0.95f;
        /// <summary>WP-092 R3 · Sakura crown saturation (AD 1.15): measured chroma +10 %; the pinks keep their hue (≈ 336–348°), the blood band stays as authored.</summary>
        public const float SakuraSaturation = 1.15f;

        /// <summary>WP-017 · Linear multiplier of the AP-005B world plaster (measured: × 0.82 on the hf003 mean gives sRGB (203, 202, 193) ≈ #CDCBC1).</summary>
        public const float WorldPlasterTint = 0.82f;
        /// <summary>WP-017 · Generated roof tile-course normal (ArtTextureRepack.BuildRoofCourses): T_roof_courses_Normal.png, 4 courses per tile.</summary>
        public const string RoofCoursesTextureRole = "roof_courses";
        /// <summary>Detail repeats per base UV unit on the AP-002 roof (its UVs run ≈ 1 unit per 1.2 m → 4 courses ≈ 0.30 m each).</summary>
        public const float RoofCoursesTiling = 1f;

        private static readonly List<Spec> Rows = BuildRows();
        private static readonly Dictionary<string, Spec> ByRole = Index(Rows);

        public static IReadOnlyList<Spec> All => Rows;

        public static Spec Get(string role)
        {
            return role != null && ByRole.TryGetValue(role, out Spec s) ? s : null;
        }

        public static bool IsKnown(string role)
        {
            return Get(role) != null;
        }

        /// <summary>Roles whose BaseColor alpha is a cutout mask (the importer keeps that alpha).</summary>
        public static bool IsCutoutTextureRole(string textureRole)
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                if ((Rows[i].Kind == Kind.Cutout || Rows[i].AlphaCutout) && string.Equals(Rows[i].TextureRole, textureRole, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Roles authored on <paramref name="shader"/> (in row order).</summary>
        public static string[] RolesOn(ShaderKind shader)
        {
            var list = new List<string>();
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].Shader == shader)
                {
                    list.Add(Rows[i].Role);
                }
            }

            return list.ToArray();
        }

        /// <summary>Texture roles that ship a Packed map and therefore need a derived mask (distinct, in row order).</summary>
        public static string[] PackedTextureRoles()
        {
            var list = new List<string>();
            for (int i = 0; i < Rows.Count; i++)
            {
                Spec s = Rows[i];
                if (s.HasPackedMap && !list.Contains(s.TextureRole))
                {
                    list.Add(s.TextureRole);
                }
            }

            return list.ToArray();
        }

        /// <summary>WP-074 B5 · Texture roles with a Normal map but no Packed map (<see cref="Spec.NoPackedMap"/>; distinct, in row order).</summary>
        public static string[] NormalOnlyTextureRoles()
        {
            var list = new List<string>();
            for (int i = 0; i < Rows.Count; i++)
            {
                Spec s = Rows[i];
                if (s.HasNormalMap && !s.HasPackedMap && !list.Contains(s.TextureRole))
                {
                    list.Add(s.TextureRole);
                }
            }

            return list.ToArray();
        }

        /// <summary>Texture roles that ship at least a BaseColor map (distinct, in row order).</summary>
        public static string[] TextureRoles()
        {
            var list = new List<string>();
            for (int i = 0; i < Rows.Count; i++)
            {
                Spec s = Rows[i];
                if (s.HasBaseColorMap && !list.Contains(s.TextureRole))
                {
                    list.Add(s.TextureRole);
                }
            }

            return list.ToArray();
        }

        private static List<Spec> BuildRows()
        {
            var rows = new List<Spec>
            {
                // AP-002: the selected HF003 plaster. Smoothness as the WP-011 plaster (1 − AP roughness 0.90).
                new Spec { Role = "plaster_hf003", Kind = Kind.BaseColorOnly, TextureRole = "plaster_hf003", Smoothness = 0.10f }
            };

            for (int n = 1; n <= 3; n++)
            {
                string nn = n.ToString("00");
                // WP-109 (AD r2 MUST-2): the bark thins over the hero with the crowns - DD/Foliage Wind for _TREE_REVEAL_SOLID only (no wind)
                rows.Add(new Spec { Role = "bark_" + nn, Kind = Kind.Textured, TextureRole = "bark_" + nn, Shader = ShaderKind.FoliageWind, RevealSolid = true, WindScale = 0f });
                rows.Add(new Spec { Role = "earth_" + nn, Kind = Kind.Textured, TextureRole = "earth_" + nn });
                // w6 (S-05 10.10, AD K1): the moss maps read hue 82–89° (s 0.62–0.70) and turn olive-yellow in the sun — red × 0.62 (linear)
                rows.Add(new Spec { Role = "moss_" + nn, Kind = Kind.Textured, TextureRole = "moss_" + nn, Tint = new Color(0.62f, 1f, 0.95f, 1f) });
                // WP-013: the foliage roles ride the wind shader (their meshes carry `_AP003_WIND`;
                // moss stays Lit because the moss maps also dress lanterns and benches, which carry no
                // colour stream and must not move).
                rows.Add(new Spec { Role = "green_foliage_" + nn, Kind = Kind.Cutout, TextureRole = "green_foliage_" + nn, Shader = ShaderKind.FoliageWind });
                rows.Add(new Spec { Role = "green_foliage_" + nn + "_solid", Kind = Kind.Textured, TextureRole = "green_foliage_" + nn, Shader = ShaderKind.FoliageWind });
                // WP-092 R3: sakura crowns × 1.15 saturation (AD), as the AP-023 sprigs.
                rows.Add(new Spec { Role = "sakura_foliage_" + nn, Kind = Kind.Cutout, TextureRole = "sakura_foliage_" + nn, Shader = ShaderKind.FoliageWind, Saturation = SakuraSaturation });
            }

            // AP-003B recipes: culm roughness ramp 0.35..0.65 → smoothness ≈ 0.5; gravel 0.60..0.95 → ≈ 0.22.
            // Bamboo culms sway (stalk/cluster carry wind); the fence and the crates that share the
            // material have no colour stream and read (1,1,1,1) → R·(1−A) = 0, no motion (DD_Wind.hlsl).
            rows.Add(new Spec { Role = "bamboo_culm", Kind = Kind.BaseColorOnly, TextureRole = "bamboo_culm", Smoothness = 0.50f, Shader = ShaderKind.FoliageWind });
            rows.Add(new Spec { Role = "gravel", Kind = Kind.BaseColorOnly, TextureRole = "gravel", Smoothness = 0.22f });
            // Weathered stone = the stone maps with the authored Hue/Saturation Value 0.85 node.
            rows.Add(new Spec { Role = "stone_weathered", Kind = Kind.Textured, TextureRole = "stone", Tint = new Color(0.85f, 0.85f, 0.85f, 1f) });
            // Water placeholder: linear (0.03695, 0.05114, 0.05925), roughness 0.05 → smoothness 0.95.
            // WP-013: water v1 (DD/Water v1) on the same deep colour; AP-015 owns the full water.
            rows.Add(new Spec { Role = "pond_water", Kind = Kind.Flat, TextureRole = null, FlatLinearAlbedo = new Color(0.03695f, 0.05114f, 0.05925f, 1f), Smoothness = 0.95f, Shader = ShaderKind.Water });

            // WP-017 · AP-005B prop families (Tripo H3.1, one PBR set each; Prop class 512). The
            // Tripo ORM carries no AO (R = 1) and metallic is forced to 0 in production.
            foreach (string prop in new[] { "tsukubai", "handcart", "strawbales", "barrels", "mossrock", "shed" })
            {
                var spec = new Spec { Role = "prop_" + prop, Kind = Kind.Textured, TextureRole = "prop_" + prop };
                // WP-092 R3 · straw neutralised (AD: saturation ≈ −30 %, value ≈ −10 %, "(0.90, 0.90, 0.98) civarı" as stored): stored
                // (0.905, 0.935, 1.000) keeps the straw hue (36.7° → 36.4°; the AD's example turns it orange-brown, 36.7° → 28.3°).
                // Measured on the atlas mean: HSV s 0.271 → 0.188 (−31 %), V 0.413 → 0.371 (−10 %).
                if (prop == "strawbales") spec.Tint = new Color(0.797f, 0.858f, 1.000f, 1f);
                rows.Add(spec);
            }

            // WP-038I · AP-017 Phase 2 props (Tripo H3.1, 1K sets → Prop class 512): the same AP-005B path — ORM Packed → derived
            // MetallicSmoothnessAO (occlusion in G), metallic forced to 0 in production, roughness floor 0.35.
            foreach (string prop in new[] { "ofuda_post", "drying_rack_cloth", "well_head_tsurube", "breakable_sake_jar" })
            {
                rows.Add(new Spec { Role = "prop_" + prop, Kind = Kind.Textured, TextureRole = "prop_" + prop });
            }

            // WP-038L · AP-017 Phase 2b. The furin hangs on the AP-003 wind path (vertex colour R sway / B flutter, AP017_wind = 1),
            // the fish trap's lathe LOD1/2 bind their own albedo at the production ×0.85 (roughness mean 0.71), the koi shoal is an
            // RGBA cutout card sheet (alpha 0.5, roughness 0.45 constant, no normal / ORM).
            foreach (string prop in new[] { "kagura_mask_stand", "breakable_tea_set", "fish_trap", "hishaku_ladle_rack" })
            {
                rows.Add(new Spec { Role = "prop_" + prop, Kind = Kind.Textured, TextureRole = "prop_" + prop });
            }

            rows.Add(new Spec { Role = "prop_furin_wind_chime", Kind = Kind.Textured, TextureRole = "prop_furin_wind_chime", Shader = ShaderKind.FoliageWind });
            rows.Add(new Spec { Role = "prop_fish_trap_lod", Kind = Kind.BaseColorOnly, TextureRole = "prop_fish_trap_lod", Smoothness = 0.29f, Tint = new Color(0.85f, 0.85f, 0.85f, 1f) });
            rows.Add(new Spec { Role = "prop_koi_shoal", Kind = Kind.BaseColorOnly, TextureRole = "prop_koi_shoal", Smoothness = 0.55f, AlphaCutout = true, Cutoff = 0.5f });

            // WP-017 · AP-005B world-master material copies (ADR-017-2, `material_override`):
            //   plaster copies = the same maps with the linear albedo × 0.82 (mean sRGB #CDCBC1, art verdict F1);
            //   roof copy = the AP-002 roof (flat dark tiles) with a generated tile-course normal overlay (F5),
            //   4 courses per detail tile, tiled so a course reads ≈ 0.30 m on the AP-002 roof UVs.
            rows.Add(new Spec { Role = "plaster_hf003_world", Kind = Kind.BaseColorOnly, TextureRole = "plaster_hf003", Smoothness = 0.10f, Tint = new Color(WorldPlasterTint, WorldPlasterTint, WorldPlasterTint, 1f) });
            rows.Add(new Spec { Role = "plaster_world", Kind = Kind.BaseColorOnly, TextureRole = "plaster", Smoothness = 0.10f, Tint = new Color(WorldPlasterTint, WorldPlasterTint, WorldPlasterTint, 1f) });
            rows.Add(new Spec { Role = "roof_world", Kind = Kind.BaseColorOnly, TextureRole = "roof", Smoothness = 0.22f, DetailNormalTextureRole = RoofCoursesTextureRole, DetailNormalScale = 1f, DetailTiling = RoofCoursesTiling });
            // WP-074 B5 · AP-023 world v5 kit (P-05, manifest v004). Tile roles: albedo / normal / ORM (R occlusion, G roughness, B
            // metallic) → the derived mask carries AO like the prop roles (ArtTextureRepack.CarriesOcclusion). Card atlases are RGBA
            // cutouts on the wind path (their meshes carry AP003_Wind); bamboo culms sway (culms without the colour stream read
            // (1,1,1,1) and stay still). Flat roles take the FBX material's linear diffuse. The lanterns' glow / paper inserts ship
            // their daytime albedo (#C8BBA8, 15:30 world); emission is a later look decision. rock_moss binds the rock_boulder maps —
            // its moss blend rides AP023_Mask G (a later shader pass). Straw is authored on the tatami maps (manifest role ap023_tatami).
            // v005 (WP-074 B5b-2): + gravel (tile) and the hishigaki fence card (atlas).
            foreach (string tile in new[] { "bamboo_old", "bark_maple", "bark_pine", "bark_sakura", "copper", "granite", "granite_ashlar", "gravel", "moss", "namako", "pebble_beach", "plaster",
                                             "plaster_interior", "roof_tiles", "rock_boulder", "stone_ashlar", "tatami", "trim", "vermilion", "washi", "wood_planks", "wood_timber" })
            {
                var spec = new Spec { Role = "ap023_" + tile, Kind = Kind.Textured, TextureRole = "ap023_" + tile };
                // WP-109 (AD r2 MUST-2): the tree bark thins over the hero with the crowns (DD/Foliage Wind for the reveal only, no wind)
                if (tile.StartsWith("bark_", System.StringComparison.Ordinal)) { spec.Shader = ShaderKind.FoliageWind; spec.RevealSolid = true; spec.WindScale = 0f; }
                // WP-075 DEC-101 (HF-like overcast): the roofs read dark cool grey (the tiles took the warm sun as brown) and the plaster
                // wet pale grey-beige, not bright cream (its sheet averages sRGB 0.79 / 0.77 / 0.73).
                // DEC-102 r2 (C-00 25.09: the roofs read brown in the default-path frame; HF v002 shows dark grey-blue kawara) on v007's 4K
                // kawara (dry #474B4F, wet baked ≈ × 0.73 linear): a cool tint that cancels the ≈ 4000 K sun (1 / 0.84 / 0.66) and darkens.
                // DEC-102 AD MUST-C1 (25.09: sun-lit roof patch L ≤ 38, b ≤ +6; HF L35 a1 b3): the "brown roofs" were wood_planks on the
                // kawara faces (ArtLibraryPrefabBuilder.MatchSlotsByFaces); on the tiles the AD's tint (0.50, 0.60, 0.85), the bake's
                // smoothness kept (a glossier wet roof mirrors the cool zenith, a rougher one caught the sun 40° off: × 0.75 read b +9).
                // WP-092 R3 (AD): ibushi silver-blue — stored _BaseColor (0.735, 0.798, 0.931) → (0.70, 0.78, 0.95), i.e. linear
                // (0.50, 0.60, 0.85) → (0.448, 0.570, 0.890). Measured on the kawara atlas mean: hue 218.6° → 219.2°, HSV s 0.33 → 0.38,
                // V 0.254 → 0.260, CIELAB L 21.1 → 20.6.
                if (tile == "roof_tiles") spec.Tint = new Color(0.448f, 0.570f, 0.890f, 1f);
                if (tile == "plaster") spec.Tint = new Color(0.54f, 0.55f, 0.57f, 1f);
                // w6 (S-05 10.10, AD K1): the pond's moss shelves (atlas mean hue 83°, s 0.44, v 0.43) read 56° in the sun — 79 % of td5's
                // yellow-brown band; red × 0.60 (linear) brings them to the cover's green.
                if (tile == "moss") spec.Tint = new Color(0.60f, 1f, 1f, 1f);
                // WP-092 R3 · tatami stays white: the interior tatami floor is walkable and shares the maps with the tawara straw, and the
                // straw neutral tint (linear (0.787, 0.807, 0.977)) takes 19 % off its luminance (0.421 → 0.341) — DEC-077 ground, not changed.
                // WP-092 R3 · shu lacquer (the gates' vermilion frame) a little more vivid inside the AD's 8–14° band: the authored atlas sits at
                // 6.0° with 9.7 % of its texels in the forbidden 358–5° blood band. Linear (1, 0.81, 0.50): V unchanged (red channel × 1),
                // HSV s 0.680 → 0.781 (+15 %), hue 6.0° → 10.0°; 98 % of texels in 8–14°, none in 358–5°.
                if (tile == "vermilion") spec.Tint = new Color(1.00f, 0.81f, 0.50f, 1f);
                // Walkable timber (wood_planks / wood_timber: decks, bridges, kairō floors, fence posts) stays white: value unchanged (DEC-077).
                rows.Add(spec);
            }

            // DEC-101: the culms read as pale sticks in the dark grove — × 0.65.
            rows.Add(new Spec { Role = "ap023_bamboo_culm", Kind = Kind.Textured, TextureRole = "ap023_bamboo_culm", Shader = ShaderKind.FoliageWind, Tint = new Color(0.75f, 0.75f, 0.75f, 1f), RevealSolid = true });   // DEC-102 r2: 0.65 → 0.75; WP-109 the grove culms thin over the hero
            rows.Add(new Spec { Role = "ap023_rock_moss", Kind = Kind.Textured, TextureRole = "ap023_rock_boulder" });
            foreach (string atlas in new[] { "bamboo_leaves", "groundcover", "hishigaki_card", "lotus", "momiji_sprigs", "pine_needles", "reeds_iris", "sakura_sprigs", "satsuki", "violet_sprigs" })
            {
                var spec = new Spec { Role = "ap023_" + atlas, Kind = Kind.Cutout, TextureRole = "ap023_" + atlas, Shader = ShaderKind.FoliageWind, NoPackedMap = true, Smoothness = 0.40f };
                // WP-075 AD MUST-1 (K4 value order: green under stone): sunlit bamboo leaves read V ≈ 0.80 / s ≈ 0.55 — the AD's × 0.70
                // left the TP grove's green median level with the slate (0.45 / 0.44); DEC-101 wants the grove dark: × 0.45; smoothness 0.25.
                if (atlas == "bamboo_leaves") { spec.Tint = new Color(1.25f, 1.30f, 0.95f, 1f); spec.Smoothness = 0.25f; }   // DEC-102 r2: 0.55 → 0.70 (HF v002 west: a sunlit grove)   // C-00: the west read too dark at × 0.45   // AD B2 25.09: sunlit leaves L 45–60, lime — the atlas is dark (linear G ≈ 0.2): × (1.25, 1.30, 0.95)
                // DEC-102 AD MUST-B1 (backlit treeline: crowns L ≤ 45, green): the dark needle atlas (L 23) read pale khaki at 60–150 m —
                // the grazing cards mirrored the sky round the sun at 0.40 (needles are matte: 0.15) and the shaded crowns lost their green
                // under the cool ambient (× 0.70 / 1.00 / 0.60 keeps them green).
                if (atlas == "pine_needles") { spec.Smoothness = 0.15f; spec.Tint = new Color(1.45f, 1.40f, 0.35f, 1f); }   // M-46 (01.10, DEC-102 uygulama düzeltmesi): forest green 130–145°, no lime/neon

                // WP-092 R3 (AD) · black pine / bamboo crowns and the groundcover: a little toward blue-green, a little more vivid.
                // Measured (atlas × tint, display HSV of the mean): pine 78.6° s 0.77 → 82.7° s 0.84 (CIELAB C* 32.8 → 35.9); bamboo
                // 103.9° s 0.51 → 110.4° s 0.55 (C* 44.4 → 50.3); groundcover 93.6° s 0.33 → 99.2° s 0.35 (C* 23.9 → 26.3).
                if (atlas == "bamboo_leaves" || atlas == "groundcover") { spec.HueShift = CanopyHueShift; spec.Saturation = CanopySaturation; }
                if (atlas == "pine_needles") { spec.HueShift = PineHueShift; spec.Saturation = PineSaturation; }   // w7
                // Sakura sprigs × 1.15: C* 24.0 → 26.4, hue 348.0° → 347.7°; the 355–5° texels are not boosted (warm arc).
                if (atlas == "sakura_sprigs") spec.Saturation = SakuraSaturation;
                // Satsuki (AD): leaves × 1.10; the pale flowers (sRGB ≈ #DEC1D2, s 0.11, 4.3 % of the covered texels) recoloured to the AD's
                // azalea pair A #AD507F (70 %) / B #99497E (30 %): the multiplier lands the petal mean on the 70/30 mix #A74E7F (HSV 327°,
                // s 0.53, V 0.66); the authored shading spreads it 80 % nearest A / 20 % nearest B (p10–p90 s 0.49–0.64, V 0.61–0.69).
                if (atlas == "satsuki") { spec.Saturation = CanopySaturation; spec.PetalColor = new Color(0.514f, 0.136f, 0.317f, 1f); }
                // Pond iris (AD): the blue-violet flowers (247°, s 0.48) turned violet, petal mean exactly #6C4A94 (268°, s 0.50, V 0.58) with a
                // 0.2 boost; the leaves and the cattail heads are outside the mask (largest change there 2.4e-5).
                if (atlas == "reeds_iris") { spec.PetalColor = new Color(1.360f, 0.896f, 0.777f, 1f); spec.PetalBoost = 0.20f; }
                // The hishigaki lattice card is the straw fence: neutralised like the straw (stored (0.905, 0.925, 0.980)): hue 42.1° → 42.6°,
                // HSV s 0.219 → 0.152 (−31 %), V 0.621 → 0.560 (−10 %).
                if (atlas == "hishigaki_card") spec.Tint = new Color(0.797f, 0.838f, 0.955f, 1f);
                // momiji_sprigs keeps its authored colour (green leaves with red-brown tips; AD: the momiji keeps its own red), as do
                // violet_sprigs and lotus.
                rows.Add(spec);
            }

            // AP-023 v008 · the sugi LOD0 crown pads (AP023_M_pine_needles_pad_v001): the pine_needles atlas, front faces only.
            // WP-092 R3: graded like the pine_needles role it shares the atlas with.
            rows.Add(new Spec { Role = "ap023_pine_needles_pad", Kind = Kind.Cutout, TextureRole = "ap023_pine_needles", Shader = ShaderKind.FoliageWind, NoPackedMap = true, Smoothness = 0.15f, Tint = new Color(1.45f, 1.40f, 0.35f, 1f), SingleSided = true, HueShift = PineHueShift, Saturation = PineSaturation });

            rows.Add(new Spec { Role = "ap023_cloth", Kind = Kind.Flat, FlatLinearAlbedo = new Color(0.0273f, 0.0232f, 0.0194f, 1f), Smoothness = 0.20f });
            rows.Add(new Spec { Role = "ap023_foliage_core", Kind = Kind.Flat, FlatLinearAlbedo = new Color(0.022f, 0.040f, 0.016f, 1f), Smoothness = 0.25f });
            rows.Add(new Spec { Role = "ap023_iron", Kind = Kind.Flat, FlatLinearAlbedo = new Color(0.045f, 0.043f, 0.042f, 1f), Smoothness = 0.40f });
            rows.Add(new Spec { Role = "ap023_lacquer", Kind = Kind.Flat, FlatLinearAlbedo = new Color(0.0116f, 0.0103f, 0.0086f, 1f), Smoothness = 0.65f });
            rows.Add(new Spec { Role = "ap023_rope", Kind = Kind.Flat, FlatLinearAlbedo = new Color(0.012f, 0.011f, 0.010f, 1f), Smoothness = 0.15f });
            rows.Add(new Spec { Role = "ap023_lantern_glow", Kind = Kind.Flat, FlatLinearAlbedo = new Color(0.578f, 0.497f, 0.392f, 1f), Smoothness = 0.30f });   // sRGB #C8BBA8
            rows.Add(new Spec { Role = "ap023_lantern_paper", Kind = Kind.Flat, FlatLinearAlbedo = new Color(0.6155f, 0.4815f, 0.2786f, 1f), Smoothness = 0.25f });  // sRGB (0.807, 0.723, 0.565)

            // WP-092 C · AP-030 world kit (P-05, v001 P1: lamps + interior; wp-092/tools/stage-ap030.py). Two atlases (lamp 1024², interior
            // 2048²): albedo / normal / ORM (R AO, G roughness, B metallic) → the derived mask carries AO (ArtTextureRepack.CarriesOcclusion).
            // Each lamp's lit area is its own slot AP030_M_<family>_glow_v001: the lamp atlas + T_ap030_lamp_Emission (sRGB, the 2200–2700 K
            // colour is in the map), _EmissionColor = white × the receipt's AP030_emission_strength, baked into GI; the slot is what the
            // flicker drives (S-07). No Light component in any prefab: the baked point lights are placed per lamp (WP-092 lead).
            // v002 (P2 road kit + P3 clusters): one atlas per road surface (ishidatami 4K, the rest 2K) and the props atlas (clusters, clutter
            // items). Gravel / dirt edges are an alpha cutout (BaseColor A, 0.5), single-sided ground strips. The props atlas also carries the
            // cloth (laundry, noren: AP003_Wind carriers → DD/Foliage Wind, two-sided cutout hem), the stain decals (single-sided cutout) and
            // the well / drain water square (Lit; DD/Water later). Their AP023_Mask vertex colour (R streak, G moss, A edge) is not read by
            // URP/Lit. hibachi: v002 ember glow ×2.5 (was 1.0).
            foreach (string atlas in new[] { "interior", "lamp", "ishidatami", "boardwalk", "drain", "puddle", "props" })
            {
                rows.Add(new Spec { Role = Ap030RolePrefix + atlas, Kind = Kind.Textured, TextureRole = Ap030RolePrefix + atlas });
            }

            foreach (string atlas in new[] { "gravel", "dirt" })
            {
                rows.Add(new Spec { Role = Ap030RolePrefix + atlas, Kind = Kind.Cutout, TextureRole = Ap030RolePrefix + atlas, Cutoff = 0.5f, SingleSided = true });
            }

            rows.Add(new Spec { Role = Ap030RolePrefix + "props_cloth", Kind = Kind.Cutout, TextureRole = Ap030RolePrefix + "props", Shader = ShaderKind.FoliageWind, Cutoff = 0.5f });
            rows.Add(new Spec { Role = Ap030RolePrefix + "props_decal", Kind = Kind.Cutout, TextureRole = Ap030RolePrefix + "props", Cutoff = 0.5f, SingleSided = true });
            rows.Add(new Spec { Role = Ap030RolePrefix + "water", Kind = Kind.Textured, TextureRole = Ap030RolePrefix + "props" });

            foreach ((string family, float strength) in new[] { ("andon", 2.0f), ("kerosene_lamp", 3.0f), ("wall_sconce", 2.5f), ("hibachi", 2.5f) })
            {
                rows.Add(new Spec { Role = Ap030RolePrefix + family + "_glow", Kind = Kind.Textured, TextureRole = Ap030RolePrefix + "lamp", EmissionTextureRole = Ap030RolePrefix + "lamp", EmissionIntensity = strength });
            }

            return rows;
        }

        private static Dictionary<string, Spec> Index(List<Spec> rows)
        {
            var d = new Dictionary<string, Spec>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                d.Add(rows[i].Role, rows[i]);
            }

            return d;
        }
    }
}
