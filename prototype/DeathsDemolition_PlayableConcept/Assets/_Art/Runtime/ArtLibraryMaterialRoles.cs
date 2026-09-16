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
            /// <summary>WP-017 · Texture role of a tangent-space detail normal overlay (<c>T_&lt;role&gt;_Normal</c>, URP <c>_DetailNormalMap</c>); null = none.</summary>
            public string DetailNormalTextureRole;
            public float DetailNormalScale = 1f;
            /// <summary>WP-017 · Detail UV tiling (URP <c>_DetailAlbedoMap_ST</c> drives both detail maps), repeats per base UV unit.</summary>
            public float DetailTiling = 1f;

            public bool HasDetailNormal => !string.IsNullOrEmpty(DetailNormalTextureRole);

            public bool HasPackedMap => Kind == Kind.Textured || Kind == Kind.Cutout;
            public bool HasNormalMap => Kind == Kind.Textured || Kind == Kind.Cutout;
            public bool HasBaseColorMap => Kind != Kind.Flat;
        }

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
                if (Rows[i].Kind == Kind.Cutout && string.Equals(Rows[i].TextureRole, textureRole, StringComparison.Ordinal))
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
                rows.Add(new Spec { Role = "bark_" + nn, Kind = Kind.Textured, TextureRole = "bark_" + nn });
                rows.Add(new Spec { Role = "earth_" + nn, Kind = Kind.Textured, TextureRole = "earth_" + nn });
                rows.Add(new Spec { Role = "moss_" + nn, Kind = Kind.Textured, TextureRole = "moss_" + nn });
                // WP-013: the foliage roles ride the wind shader (their meshes carry `_AP003_WIND`;
                // moss stays Lit because the moss maps also dress lanterns and benches, which carry no
                // colour stream and must not move).
                rows.Add(new Spec { Role = "green_foliage_" + nn, Kind = Kind.Cutout, TextureRole = "green_foliage_" + nn, Shader = ShaderKind.FoliageWind });
                rows.Add(new Spec { Role = "green_foliage_" + nn + "_solid", Kind = Kind.Textured, TextureRole = "green_foliage_" + nn, Shader = ShaderKind.FoliageWind });
                rows.Add(new Spec { Role = "sakura_foliage_" + nn, Kind = Kind.Cutout, TextureRole = "sakura_foliage_" + nn, Shader = ShaderKind.FoliageWind });
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
                rows.Add(new Spec { Role = "prop_" + prop, Kind = Kind.Textured, TextureRole = "prop_" + prop });
            }

            // WP-017 · AP-005B world-master material copies (ADR-017-2, `material_override`):
            //   plaster copies = the same maps with the linear albedo × 0.82 (mean sRGB #CDCBC1, art verdict F1);
            //   roof copy = the AP-002 roof (flat dark tiles) with a generated tile-course normal overlay (F5),
            //   4 courses per detail tile, tiled so a course reads ≈ 0.30 m on the AP-002 roof UVs.
            rows.Add(new Spec { Role = "plaster_hf003_world", Kind = Kind.BaseColorOnly, TextureRole = "plaster_hf003", Smoothness = 0.10f, Tint = new Color(WorldPlasterTint, WorldPlasterTint, WorldPlasterTint, 1f) });
            rows.Add(new Spec { Role = "plaster_world", Kind = Kind.BaseColorOnly, TextureRole = "plaster", Smoothness = 0.10f, Tint = new Color(WorldPlasterTint, WorldPlasterTint, WorldPlasterTint, 1f) });
            rows.Add(new Spec { Role = "roof_world", Kind = Kind.BaseColorOnly, TextureRole = "roof", Smoothness = 0.22f, DetailNormalTextureRole = RoofCoursesTextureRole, DetailNormalScale = 1f, DetailTiling = RoofCoursesTiling });
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
