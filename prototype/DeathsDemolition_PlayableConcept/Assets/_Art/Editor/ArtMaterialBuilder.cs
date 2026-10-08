using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.CameraSystem;
using DD.Prototype.Rendering;
using UnityEditor;
using UnityEngine;

namespace DD.Art.Pipeline.EditorTools
{
    /// <summary>
    /// WP-011 · Authors the ten <c>M_&lt;role&gt;.mat</c> assets (ADR-011-1 §Material).
    ///
    /// Generated, never hand-edited: a material is a bag of floats and keywords that a
    /// stray Inspector click can change without a diff anyone reads. Every property the
    /// pipeline cares about is written explicitly on every build - including the ones
    /// that must be OFF - so re-running the builder on an edited material restores the
    /// contract instead of inheriting the edit. GUIDs are stable: an existing asset is
    /// updated in place, so prefabs and scenes keep their references.
    ///
    /// All ten are URP/Lit, opaque, back-face culled (the glTF doubleSided flag is an
    /// export artefact; AP-001 authors no double-sided surfaces), GPU instancing on
    /// (informational under the SRP Batcher, which takes precedence).
    ///
    ///   stone / wood / bark / earth   BaseColor + Normal + derived MetallicSmoothness
    ///                                 (metallic in R, smoothness in A, scalars 1 so
    ///                                 the texture is authoritative)
    ///   plaster / roof                BaseColor only; smoothness = 1 − AP roughness
    ///   green / moss / sakura_pink /  flat linear albedo from the AP-001 material
    ///   sakura_purple                 factors, no texture
    ///
    /// FLAT COLOURS AND COLOUR SPACE. The AP-001 factors are linear albedo (glTF
    /// baseColorFactor, Blender's linear pickers). Unity states that "all material
    /// color properties are supplied as sRGB colors, but are converted into linear
    /// values when passed into shaders" (Manual, "Properties in shader programs"), so a
    /// Color-typed property such as <c>_BaseColor</c> must be HANDED the sRGB encoding
    /// for the shader to receive the linear value. <see cref="EncodeForColorProperty"/>
    /// does that one conversion (<c>Color.gamma</c>); the linear numbers themselves are
    /// in <see cref="FlatColour"/> unchanged. Reading <c>_BaseColor</c> back therefore
    /// yields the sRGB encoding, and a test that wants the linear albedo should compare
    /// <c>GetColor("_BaseColor").linear</c>.
    /// </summary>
    public static class ArtMaterialBuilder
    {
        public const string LitShaderName = "Universal Render Pipeline/Lit";

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int MetallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int SmoothnessChannelId = Shader.PropertyToID("_SmoothnessTextureChannel");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        /// <summary>Keywords that must be off on every pipeline material regardless of role.</summary>
        private static readonly string[] AlwaysOff =
        {
            "_SURFACE_TYPE_TRANSPARENT",
            "_ALPHATEST_ON",
            "_ALPHAPREMULTIPLY_ON",
            "_ALPHAMODULATE_ON",
            "_ALPHABLEND_ON",
            "_SPECULAR_SETUP",
            "_EMISSION",
            "_OCCLUSIONMAP",
            "_PARALLAXMAP",
            "_DETAIL_MULX2",
            "_DETAIL_SCALED",
            "_CLEARCOAT",
            "_CLEARCOATMAP",
            "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A",
            "_RECEIVE_SHADOWS_OFF",
            "_SPECULARHIGHLIGHTS_OFF",
            "_ENVIRONMENTREFLECTIONS_OFF"
        };

        public static void BuildAll()
        {
            string[] roles = ArtFamilies.MaterialRoles;
            for (int i = 0; i < roles.Length; i++)
            {
                Build(roles[i]);
            }

            BuildVariantPins();
            AssetDatabase.SaveAssets();
            Debug.Log("[ArtPipeline] Materials: " + roles.Length + " in " + ArtPipelinePaths.Materials);
        }

        /// <summary>Folder whose materials exist only to keep shader variants in the Player build.</summary>
        public const string VariantPinsFolder = "Assets/_Art/Resources/ArtVariantPins";

        public const string TransparentTexturedPinPath = VariantPinsFolder + "/Pin_ArtLit_Transparent_Textured.mat";

        /// <summary>
        /// WHY A PIN. <see cref="OccluderFadeState"/> fades a wall by cloning its material and
        /// switching the clone to the transparent surface at RUNTIME. A Player build only
        /// ships the URP/Lit variants some material asset in the build actually uses;
        /// nothing ships "transparent + _NORMALMAP + _METALLICSPECGLOSSMAP", because every
        /// art material is opaque. The prototype's own pins (Rendering/Resources/VariantPins)
        /// cover the untextured transparent case only. This material — a stone material put
        /// through the very same <see cref="OccluderFadeState.MakeTransparent"/> — lives in
        /// a Resources folder so the build keeps the variant the fade needs for the textured
        /// occluder families (plaster_wall, gate_transition, open_corridor, roof pieces).
        /// The PilotPerfProbe occluder stage is the Player-side proof (PRA K-12).
        /// </summary>
        public static Material BuildVariantPins()
        {
            EnsureFolder(VariantPinsFolder);
            var stone = AssetDatabase.LoadAssetAtPath<Material>(ArtPipelinePaths.Material("stone"));
            if (stone == null)
            {
                throw new InvalidOperationException("[ArtPipeline] M_stone.mat must exist before the variant pin is built.");
            }

            var pin = AssetDatabase.LoadAssetAtPath<Material>(TransparentTexturedPinPath);
            bool created = pin == null;
            if (created)
            {
                pin = new Material(stone);
                AssetDatabase.CreateAsset(pin, TransparentTexturedPinPath);
            }
            else
            {
                pin.CopyPropertiesFromMaterial(stone);
                pin.shader = stone.shader;
                pin.shaderKeywords = stone.shaderKeywords;
            }

            pin.name = Path.GetFileNameWithoutExtension(TransparentTexturedPinPath);
            OccluderFadeState.MakeTransparent(pin);
            pin.enableInstancing = stone.enableInstancing;
            EditorUtility.SetDirty(pin);
            Debug.Log("[ArtPipeline] Variant pin " + (created ? "created" : "updated") + ": " + TransparentTexturedPinPath +
                      " keywords=" + string.Join(" ", pin.shaderKeywords));
            return pin;
        }

        /// <summary>Creates or updates <c>M_&lt;role&gt;.mat</c> and returns it. Throws on a missing input texture.</summary>
        public static Material Build(string role)
        {
            if (Array.IndexOf(ArtFamilies.MaterialRoles, role) < 0)
            {
                throw new ArgumentException("[ArtPipeline] Unknown material role '" + role + "'", nameof(role));
            }

            Shader lit = ResolveLitShader();
            string path = ArtPipelinePaths.Material(role);
            EnsureFolder(ArtPipelinePaths.Materials);

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                material = new Material(lit) { name = "M_" + role };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != lit)
            {
                material.shader = lit;
            }

            ApplyCommon(material);

            switch (role)
            {
                case "stone":
                case "wood":
                case "bark":
                case "earth":
                    ApplyTextured(material, role);
                    break;

                case "plaster":
                    ApplyBaseColorOnly(material, role, 0.10f);
                    break;

                case "roof":
                    ApplyBaseColorOnly(material, role, 0.22f);
                    break;

                default:
                    ApplyFlat(material, FlatColour(role), FlatSmoothness(role));
                    break;
            }

            EditorUtility.SetDirty(material);
            Debug.Log("[ArtPipeline] Material " + (created ? "created" : "updated") + ": " + path);
            return material;
        }

        /// <summary>The AP-001 linear albedo of a flat-coloured role (alpha 1).</summary>
        // ------------------------------------------------------------ WP-012 libraries

        /// <summary>
        /// WP-012 · Authors every <see cref="ArtLibraryMaterialRoles"/> row the same way the
        /// ten WP-011 roles are authored (same common block, same textured/base-colour/flat
        /// recipes), plus the cutout recipe for the foliage cards. Idempotent; GUIDs stable.
        /// </summary>
        public static void BuildAllLibraryRoles()
        {
            IReadOnlyList<ArtLibraryMaterialRoles.Spec> rows = ArtLibraryMaterialRoles.All;
            for (int i = 0; i < rows.Count; i++)
            {
                BuildLibraryRole(rows[i]);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[ArtPipeline] Library materials: " + rows.Count + " in " + ArtPipelinePaths.Materials);
        }

        /// <summary>WP-013 shaders (Assets/_Art/Shaders). Same property names as URP/Lit, so the recipes below apply unchanged.</summary>
        public const string FoliageWindShaderName = "DD/Foliage Wind";
        public const string WaterShaderName = "DD/Water v1";

        public static Shader ResolveShader(ArtLibraryMaterialRoles.ShaderKind kind)
        {
            switch (kind)
            {
                case ArtLibraryMaterialRoles.ShaderKind.FoliageWind:
                    return ResolveNamed(FoliageWindShaderName);
                case ArtLibraryMaterialRoles.ShaderKind.Water:
                    return ResolveNamed(WaterShaderName);
                default:
                    return ResolveLitShader();
            }
        }

        private static Shader ResolveNamed(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null)
            {
                throw new InvalidOperationException("[ArtPipeline] Shader '" + name + "' is not available (Assets/_Art/Shaders missing or failed to compile).");
            }

            return shader;
        }

        public static Material BuildLibraryRole(ArtLibraryMaterialRoles.Spec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            Shader shader = ResolveShader(spec.Shader);
            string path = ArtPipelinePaths.Material(spec.Role);
            EnsureFolder(ArtPipelinePaths.Materials);

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (created)
            {
                material = new Material(shader) { name = "M_" + spec.Role };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (spec.Shader == ArtLibraryMaterialRoles.ShaderKind.Water)
            {
                ApplyWater(material, spec);
                EditorUtility.SetDirty(material);
                Debug.Log("[ArtPipeline] Library material " + (created ? "created" : "updated") + ": " + path + " (Water)");
                return material;
            }

            ApplyCommon(material);

            switch (spec.Kind)
            {
                case ArtLibraryMaterialRoles.Kind.Textured:
                    ApplyTextured(material, spec.TextureRole, spec);
                    material.SetColor(BaseColorId, EncodeForColorProperty(spec.Tint));
                    break;

                case ArtLibraryMaterialRoles.Kind.Cutout:
                    ApplyTextured(material, spec.TextureRole, spec);
                    material.SetColor(BaseColorId, EncodeForColorProperty(spec.Tint));
                    ApplyCutout(material, spec.Cutoff);
                    if (spec.SingleSided)
                    {
                        material.SetFloat(CullId, 2f);
                        material.doubleSidedGI = false;
                    }

                    break;

                case ArtLibraryMaterialRoles.Kind.BaseColorOnly:
                    ApplyBaseColorOnly(material, spec.TextureRole, spec.Smoothness);
                    material.SetColor(BaseColorId, EncodeForColorProperty(spec.Tint));
                    if (spec.AlphaCutout) ApplyCutout(material, spec.Cutoff);   // WP-038L: the koi card sheet
                    break;

                default:
                    ApplyFlat(material, spec.FlatLinearAlbedo, spec.Smoothness);
                    break;
            }

            if (spec.Shader == ArtLibraryMaterialRoles.ShaderKind.FoliageWind)
            {
                ApplyFoliageWind(material, spec.Kind == ArtLibraryMaterialRoles.Kind.Cutout);
            }

            if (spec.HasDetailNormal)
            {
                ApplyDetailNormal(material, spec);
            }

            if (spec.HasEmission)
            {
                ApplyEmission(material, spec);
            }

            EditorUtility.SetDirty(material);
            Debug.Log("[ArtPipeline] Library material " + (created ? "created" : "updated") + ": " + path + " (" + spec.Kind + "/" + spec.Shader + (spec.HasDetailNormal ? "/detail-normal " + spec.DetailNormalTextureRole : string.Empty) + (spec.HasEmission ? "/emission " + spec.EmissionTextureRole + " ×" + spec.EmissionIntensity : string.Empty) + ")");
            return material;
        }

        /// <summary>
        /// WP-092 C · The AP-030 lamp glow slots: the sRGB emission map (the warm colour is in the map) × a linear intensity, baked into
        /// GI (<see cref="MaterialGlobalIlluminationFlags.BakedEmissive"/>: a lamp lights the lightmapped room around it when its renderer
        /// contributes GI). After <see cref="ApplyCommon"/>, which switches _EMISSION off; URP's material validation keeps the keyword on
        /// as long as the flag is not EmissiveIsBlack and the colour is not black.
        /// </summary>
        private static void ApplyEmission(Material m, ArtLibraryMaterialRoles.Spec spec)
        {
            m.SetTexture("_EmissionMap", LoadTexture(ArtPipelinePaths.SourceTexture(spec.EmissionTextureRole, "Emission")));
            m.SetColor("_EmissionColor", EncodeForColorProperty(new Color(spec.EmissionIntensity, spec.EmissionIntensity, spec.EmissionIntensity, 1f)));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }

        /// <summary>
        /// WP-013 wind/foliage recipe on top of the Lit recipe: wind at the global scale,
        /// both faces lit, and for the cards alpha-to-coverage (MSAA 4× in the URP asset)
        /// with the coverage fix, plus a little translucency. Solid variants keep back-face
        /// culling: they are closed volumes and the extra fragments would buy nothing.
        /// </summary>
        private static void ApplyFoliageWind(Material m, bool cutout)
        {
            SetFloat(m, "_WindScale", 1f);
            SetFloat(m, "_BackfaceLighting", 1f);
            SetFloat(m, "_Translucency", cutout ? 0.25f : 0f);
            SetFloat(m, "_AlphaToMask", cutout ? 1f : 0f);
        }

        /// <summary>
        /// WP-017 · Tangent-space detail normal overlay on a Lit material (URP `_DetailNormalMap`
        /// gated by `_DETAIL_MULX2`; the detail albedo stays null = URP's neutral linear grey, so
        /// only the normal contributes). Used by roof_world for the AP-005B tile-course lines.
        /// </summary>
        private static void ApplyDetailNormal(Material m, ArtLibraryMaterialRoles.Spec spec)
        {
            string path = ArtPipelinePaths.DerivedTexture(spec.DetailNormalTextureRole, "Normal");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (normal == null)
            {
                throw new InvalidOperationException("[ArtPipeline] Detail normal missing for role '" + spec.Role + "': " + path + " (run the texture repack first)");
            }

            SetTexture(m, "_DetailNormalMap", normal);
            SetTexture(m, "_DetailAlbedoMap", null);
            SetTexture(m, "_DetailMask", null);
            SetFloat(m, "_DetailNormalMapScale", spec.DetailNormalScale);
            SetFloat(m, "_DetailAlbedoMapScale", 1f);
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(spec.DetailTiling, spec.DetailTiling));
            m.SetTextureOffset("_DetailAlbedoMap", Vector2.zero);
            m.EnableKeyword("_DETAIL_MULX2");
            m.DisableKeyword("_DETAIL_SCALED");
        }

        /// <summary>
        /// WP-013 water v1: transparent, no depth write, back faces culled, the AP-003B deep
        /// colour as the deep tint. Bypasses <see cref="ApplyCommon"/> on purpose - that block
        /// writes the opaque blend state.
        /// </summary>
        private static void ApplyWater(Material m, ArtLibraryMaterialRoles.Spec spec)
        {
            Color deep = spec.FlatLinearAlbedo;
            deep.a = 0.92f;
            m.SetColor(BaseColorId, EncodeForColorProperty(deep));
            SetColor(m, "_ShallowColor", EncodeForColorProperty(new Color(0.20f, 0.30f, 0.28f, 0.35f)));
            SetFloat(m, "_Smoothness", spec.Smoothness);
            SetFloat(m, "_DepthFade", 0.6f);
            SetFloat(m, "_RippleScale", 2.2f);
            SetFloat(m, "_RippleStrength", 0.6f);
            SetFloat(m, "_RippleSpeed", 0.35f);
            SetFloat(m, "_WindRipple", 0.5f);
            SetFloat(m, "_Surface", 1f);
            SetFloat(m, "_Blend", 0f);
            SetFloat(m, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloat(m, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetFloat(m, "_ZWrite", 0f);
            SetFloat(m, "_QueueOffset", 0f);
            m.SetFloat(CullId, 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.enableInstancing = true;
            m.doubleSidedGI = false;
        }

        /// <summary>
        /// Alpha cutout on URP/Lit: opaque surface, <c>_AlphaClip</c> with the AP-003 threshold
        /// (0.5, the coverage the sets were dilated for), AlphaTest queue, and no culling —
        /// the crown/leaf cards are single quads seen from both sides. Shadows clip through
        /// the same keyword in the ShadowCaster pass.
        /// </summary>
        private static void ApplyCutout(Material m, float cutoff)
        {
            SetFloat(m, "_AlphaClip", 1f);
            SetFloat(m, "_Cutoff", cutoff);
            // WP-038L: URP 14's material import validation (BaseShaderGUI.SetupMaterialBlendModeInternal) sets alpha-to-mask on every
            // opaque alpha-clipped Lit material; writing it here keeps the builder idempotent across a re-import (the koi card sheet is
            // the first URP/Lit cutout — the foliage cutouts on DD/Foliage Wind set it in ApplyFoliageWind).
            SetFloat(m, "_AlphaToMask", 1f);
            m.SetFloat(CullId, 0f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            m.doubleSidedGI = true;
        }

        public static Color FlatColour(string role)
        {
            switch (role)
            {
                case "green": return new Color(0.051269f, 0.104616f, 0.030713f, 1f);
                case "moss": return new Color(0.049707f, 0.076185f, 0.027321f, 1f);
                case "sakura_pink": return new Color(0.564712f, 0.278894f, 0.371238f, 1f);
                case "sakura_purple": return new Color(0.230740f, 0.144128f, 0.318547f, 1f);
                default:
                    throw new ArgumentException("[ArtPipeline] '" + role + "' is not a flat-colour role", nameof(role));
            }
        }

        public static float FlatSmoothness(string role)
        {
            switch (role)
            {
                case "green": return 0.14f;
                case "moss": return 0.05f;
                case "sakura_pink": return 0.22f;
                case "sakura_purple": return 0.20f;
                default:
                    throw new ArgumentException("[ArtPipeline] '" + role + "' is not a flat-colour role", nameof(role));
            }
        }

        /// <summary>
        /// The value to store in a Color-typed shader property so the shader receives
        /// <paramref name="linear"/> under the project's Linear colour space (see class
        /// remarks). Inverse: <c>Color.linear</c>.
        /// </summary>
        public static Color EncodeForColorProperty(Color linear)
        {
            return linear.gamma;
        }

        /// <summary>
        /// The prototype's shader library first (the same door WorldPalette goes through,
        /// so the pilot cannot end up on a different Lit than the blockout), Shader.Find
        /// only when the library is absent in a fresh checkout.
        /// </summary>
        public static Shader ResolveLitShader()
        {
            Shader shader = null;
            if (PrototypeShaders.IsReady)
            {
                shader = PrototypeShaders.Lit;
            }

            if (shader == null)
            {
                shader = Shader.Find(LitShaderName);
            }

            if (shader == null)
            {
                throw new InvalidOperationException("[ArtPipeline] '" + LitShaderName + "' is not available; " +
                                                    "is the Universal Render Pipeline package installed?");
            }

            return shader;
        }

        // ---------------------------------------------------------------- internals

        /// <summary>WP-016: the common opaque/Lit block for character materials (same values as every environment role).</summary>
        public static void ApplyCommonPublic(Material m)
        {
            ApplyCommon(m);
        }

        private static void ApplyCommon(Material m)
        {
            // Opaque, metallic workflow, back faces culled. These mirror what URP's
            // BaseShaderGUI writes for "Opaque / Render Face Front", which is the only
            // supported way to put a Lit material into that state from code.
            SetFloat(m, "_WorkflowMode", 1f);
            SetFloat(m, "_Surface", 0f);
            SetFloat(m, "_Blend", 0f);
            SetFloat(m, "_AlphaClip", 0f);
            SetFloat(m, "_SrcBlend", 1f);
            SetFloat(m, "_DstBlend", 0f);
            SetFloat(m, "_SrcBlendAlpha", 1f);
            SetFloat(m, "_DstBlendAlpha", 0f);
            SetFloat(m, "_ZWrite", 1f);
            SetFloat(m, "_AlphaToMask", 0f);
            SetFloat(m, "_ReceiveShadows", 1f);
            SetFloat(m, "_SpecularHighlights", 1f);
            SetFloat(m, "_EnvironmentReflections", 1f);
            SetFloat(m, "_QueueOffset", 0f);
            SetFloat(m, "_OcclusionStrength", 1f);
            SetFloat(m, "_ClearCoatMask", 0f);
            SetFloat(m, "_ClearCoatSmoothness", 0f);
            m.SetFloat(CullId, 2f);

            SetTexture(m, "_OcclusionMap", null);
            SetTexture(m, "_EmissionMap", null);
            SetTexture(m, "_ParallaxMap", null);
            SetTexture(m, "_DetailMask", null);
            SetTexture(m, "_DetailAlbedoMap", null);
            SetTexture(m, "_DetailNormalMap", null);
            SetTexture(m, "_SpecGlossMap", null);
            SetColor(m, "_EmissionColor", Color.black);

            // Tiling 1: repeat density is authored in the mesh UVs (PilotGround: one
            // repeat per TileMetres), never in the material.
            m.SetTextureScale(BaseMapId, Vector2.one);
            m.SetTextureOffset(BaseMapId, Vector2.zero);

            for (int i = 0; i < AlwaysOff.Length; i++)
            {
                m.DisableKeyword(AlwaysOff[i]);
            }

            m.SetOverrideTag("RenderType", "Opaque");
            m.renderQueue = -1;
            m.enableInstancing = true;
            m.doubleSidedGI = false;
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        private static void ApplyTextured(Material m, string role, ArtLibraryMaterialRoles.Spec spec = null)
        {
            Texture2D baseMap = LoadTexture(ArtPipelinePaths.SourceTexture(role, "BaseColor"));
            Texture2D normal = LoadTexture(ArtPipelinePaths.SourceTexture(role, "Normal"));
            if (spec != null && spec.NoPackedMap)
            {
                // WP-074 B5 · the AP-023 card atlases: albedo + normal, scalar smoothness (production roughness 0.6), metallic 0.
                m.SetTexture(BaseMapId, baseMap);
                m.SetColor(BaseColorId, Color.white);
                m.SetTexture(BumpMapId, normal);
                m.SetFloat(BumpScaleId, 1f);
                m.SetTexture(MetallicGlossMapId, null);
                m.SetFloat(MetallicId, 0f);
                m.SetFloat(SmoothnessId, spec.Smoothness);
                m.SetFloat(SmoothnessChannelId, 0f);
                m.EnableKeyword("_NORMALMAP");
                m.DisableKeyword("_METALLICSPECGLOSSMAP");
                return;
            }

            Texture2D mask = LoadTexture(ArtPipelinePaths.DerivedMask(role), "run ArtTextureRepack first");

            m.SetTexture(BaseMapId, baseMap);
            m.SetColor(BaseColorId, Color.white);
            m.SetTexture(BumpMapId, normal);
            m.SetFloat(BumpScaleId, 1f);
            m.SetTexture(MetallicGlossMapId, mask);

            // Scalars at 1: URP multiplies the map by them, so anything else would
            // silently re-grade the AP bake.
            m.SetFloat(MetallicId, 1f);
            m.SetFloat(SmoothnessId, 1f);
            m.SetFloat(SmoothnessChannelId, 0f); // 0 = Metallic Alpha

            m.EnableKeyword("_NORMALMAP");
            m.EnableKeyword("_METALLICSPECGLOSSMAP");

            // WP-020 · prop roles: the ORM v002 AO rides the same mask (G) — URP reads _OcclusionMap.g.
            if (ArtTextureRepack.CarriesOcclusion(role))
            {
                m.SetTexture("_OcclusionMap", mask);
                m.SetFloat("_OcclusionStrength", 1f);
                m.EnableKeyword("_OCCLUSIONMAP");
            }
        }

        private static void ApplyBaseColorOnly(Material m, string role, float smoothness)
        {
            Texture2D baseMap = LoadTexture(ArtPipelinePaths.SourceTexture(role, "BaseColor"));

            m.SetTexture(BaseMapId, baseMap);
            m.SetColor(BaseColorId, Color.white);
            m.SetTexture(BumpMapId, null);
            m.SetFloat(BumpScaleId, 1f);
            m.SetTexture(MetallicGlossMapId, null);
            m.SetFloat(MetallicId, 0f);
            m.SetFloat(SmoothnessId, smoothness);
            m.SetFloat(SmoothnessChannelId, 0f);

            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
        }

        private static void ApplyFlat(Material m, Color linearAlbedo, float smoothness)
        {
            m.SetTexture(BaseMapId, null);
            m.SetColor(BaseColorId, EncodeForColorProperty(linearAlbedo));
            m.SetTexture(BumpMapId, null);
            m.SetFloat(BumpScaleId, 1f);
            m.SetTexture(MetallicGlossMapId, null);
            m.SetFloat(MetallicId, 0f);
            m.SetFloat(SmoothnessId, smoothness);
            m.SetFloat(SmoothnessChannelId, 0f);

            m.DisableKeyword("_NORMALMAP");
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
        }

        private static Texture2D LoadTexture(string path, string hint = null)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new FileNotFoundException("[ArtPipeline] Texture is missing: " + path +
                                                (hint != null ? " (" + hint + ")" : string.Empty), path);
            }

            return texture;
        }

        private static void SetFloat(Material m, string property, float value)
        {
            if (m.HasProperty(property))
            {
                m.SetFloat(property, value);
            }
        }

        private static void SetTexture(Material m, string property, Texture value)
        {
            if (m.HasProperty(property))
            {
                m.SetTexture(property, value);
            }
        }

        private static void SetColor(Material m, string property, Color value)
        {
            if (m.HasProperty(property))
            {
                m.SetColor(property, value);
            }
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
