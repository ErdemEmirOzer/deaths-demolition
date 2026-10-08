using System.Collections.Generic;
using System.IO;
using DD.Prototype.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Prototype.Rendering.EditorTools
{
    /// <summary>
    /// PROTOTYPE-ONLY: builds and refills <see cref="PrototypeShaderLibrary"/>.
    ///
    /// The asset is generated, never hand-written. Typing GUIDs into .asset YAML is how a
    /// reference ends up pointing at nothing while still looking correct in a diff; here
    /// the Editor resolves each shader itself and serialises whatever it actually found.
    /// </summary>
    public static class PrototypeShaderLibraryBuilder
    {
        private const string Klasor = "Assets/_Prototype/Rendering/Resources";
        private const string Yol = Klasor + "/PrototypeShaderLibrary.asset";
        private const string PinKlasor = "Assets/_Prototype/Rendering/Resources/VariantPins";

        private const string LitAd = "Universal Render Pipeline/Lit";
        private const string SimpleLitAd = "Universal Render Pipeline/Simple Lit";
        private const string UnlitAd = "Universal Render Pipeline/Unlit";
        private const string ParticlesAd = "Universal Render Pipeline/Particles/Unlit";
        private const string SkyboxAd = "Skybox/Procedural";
        private const string VfxFlipbookAd = "DD/VFX Flipbook";
        private const string VfxHeatHazeAd = "DD/VFX HeatHaze";
        private const string UiFxAd = "DD/UI/Fx";
        private const string UiFrozenFrameAd = "Hidden/DD/UI/FrozenFrame";
        private const string UiMaskTintAd = "DD/UI/MaskTint";
        private const string VfxRibbonAd = "DD/VFX Ribbon";
        private const string VfxAtmoAd = "DD/VFX Atmo";
        private const string VfxGelAd = "DD/VFX Gel";
        private const string VfxPuddleAd = "DD/VFX Puddle";
        private const string VegetationAd = "DD/World Vegetation";

        [MenuItem("Tools/Deaths Demolition/Rebuild Shader Library")]
        public static void Rebuild()
        {
            string hata;
            if (!TryRebuild(out hata))
            {
                EditorUtility.DisplayDialog("Shader library", hata, "OK");
                Debug.LogError("[Rendering] " + hata);
                return;
            }

            Debug.Log("[Rendering] Shader library rebuilt: " + Yol);
        }

        /// <summary>Batch entry point: <c>-executeMethod ...RebuildFromCommandLine</c>.</summary>
        public static void RebuildFromCommandLine()
        {
            string hata;
            if (!TryRebuild(out hata))
            {
                Debug.LogError("[Rendering] " + hata);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[Rendering] Shader library rebuilt: " + Yol);
            EditorApplication.Exit(0);
        }

        /// <summary>Does the work. Returns false with a reason instead of throwing.</summary>
        public static bool TryRebuild(out string hata)
        {
            hata = null;

            Shader lit = Bul(LitAd, ref hata);
            Shader simpleLit = Bul(SimpleLitAd, ref hata);
            Shader unlit = Bul(UnlitAd, ref hata);
            Shader particles = Bul(ParticlesAd, ref hata);
            Shader skybox = Bul(SkyboxAd, ref hata);
            Shader vfxFlipbook = Bul(VfxFlipbookAd, ref hata);
            Shader vfxHeatHaze = Bul(VfxHeatHazeAd, ref hata);
            Shader uiFx = Bul(UiFxAd, ref hata);
            Shader uiFrozenFrame = Bul(UiFrozenFrameAd, ref hata);
            Shader uiMaskTint = Bul(UiMaskTintAd, ref hata);
            Shader vfxRibbon = Bul(VfxRibbonAd, ref hata);
            Shader vfxAtmo = Bul(VfxAtmoAd, ref hata);
            Shader vfxGel = Bul(VfxGelAd, ref hata);
            Shader vfxPuddle = Bul(VfxPuddleAd, ref hata);
            Shader vegetation = Bul(VegetationAd, ref hata);
            if (hata != null)
            {
                return false;
            }

            KlasorKur(Klasor);
            KlasorKur(PinKlasor);

            Material[] pins = PinleriKur(lit, unlit, particles, vfxRibbon, vfxGel, vfxPuddle, vegetation);

            var kitaplik = AssetDatabase.LoadAssetAtPath<PrototypeShaderLibrary>(Yol);
            if (kitaplik == null)
            {
                kitaplik = ScriptableObject.CreateInstance<PrototypeShaderLibrary>();
                AssetDatabase.CreateAsset(kitaplik, Yol);
            }

            // SerializedObject rather than public setters: the runtime type stays read-only,
            // so nothing but this tool can put a wrong shader in the library.
            var so = new SerializedObject(kitaplik);
            so.FindProperty("_lit").objectReferenceValue = lit;
            so.FindProperty("_simpleLit").objectReferenceValue = simpleLit;
            so.FindProperty("_unlit").objectReferenceValue = unlit;
            so.FindProperty("_particlesUnlit").objectReferenceValue = particles;
            so.FindProperty("_skybox").objectReferenceValue = skybox;
            so.FindProperty("_vfxFlipbook").objectReferenceValue = vfxFlipbook;
            so.FindProperty("_vfxHeatHaze").objectReferenceValue = vfxHeatHaze;
            so.FindProperty("_uiFx").objectReferenceValue = uiFx;
            so.FindProperty("_uiFrozenFrame").objectReferenceValue = uiFrozenFrame;
            so.FindProperty("_uiMaskTint").objectReferenceValue = uiMaskTint;
            so.FindProperty("_vfxRibbon").objectReferenceValue = vfxRibbon;
            so.FindProperty("_vfxAtmo").objectReferenceValue = vfxAtmo;
            so.FindProperty("_vfxGel").objectReferenceValue = vfxGel;
            so.FindProperty("_vfxPuddle").objectReferenceValue = vfxPuddle;
            so.FindProperty("_vegetation").objectReferenceValue = vegetation;

            SerializedProperty dizi = so.FindProperty("_variantPins");
            dizi.arraySize = pins.Length;
            for (int i = 0; i < pins.Length; i++)
            {
                dizi.GetArrayElementAtIndex(i).objectReferenceValue = pins[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(kitaplik);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            PrototypeShaders.Forget();

            string kalan = kitaplik.DescribeMissing();
            if (kalan != null)
            {
                hata = "Library still incomplete after rebuild: " + kalan;
                return false;
            }

            return true;
        }

        private static Shader Bul(string ad, ref string hata)
        {
            Shader s = Shader.Find(ad);
            if (s == null)
            {
                string satir = "Shader not found in the Editor: '" + ad +
                               "'. Is the Universal RP package still installed?";
                hata = hata == null ? satir : hata + "  " + satir;
            }

            return s;
        }

        private static void KlasorKur(string yol)
        {
            if (AssetDatabase.IsValidFolder(yol))
            {
                return;
            }

            string ust = Path.GetDirectoryName(yol).Replace('\\', '/');
            KlasorKur(ust);
            AssetDatabase.CreateFolder(ust, Path.GetFileName(yol));
        }

        /// <summary>
        /// Materials whose only job is to be in the build.
        ///
        /// URP's transparency and blend keywords are shader_features: the build keeps a
        /// variant only if some material asset uses it. Every transparent material in this
        /// prototype is made at runtime, hours after stripping has already happened, so
        /// without these the shaders ship with their opaque variant alone and everything
        /// that should fade draws solid. Each pin below mirrors one runtime configuration.
        /// </summary>
        private static Material[] PinleriKur(Shader lit, Shader unlit, Shader particles, Shader vfxRibbon, Shader vfxGel, Shader vfxPuddle, Shader vegetation)
        {
            var pins = new List<Material>(6);

            pins.Add(Pin("Pin_Lit_Opaque", lit, m => { }));

            // OccluderFadeState fades walls between camera and player.
            pins.Add(Pin("Pin_Lit_Transparent", lit, m =>
            {
                Saydam(m);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHABLEND_ON");
            }));
            pins.Add(Pin("Pin_Lit_TransparentPremultiply", lit, m =>
            {
                Saydam(m);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            }));

            // FeedbackMaterials: opaque debris, alpha shapes, additive flashes.
            // WP-093 (S-05 08.10): shoji transmission swaps M_ap023_washi / M_ap023_lantern_paper to runtime copies with _EMISSION on
            // (ArtMaterialBuilder keeps the originals emission-off); pin both keyword sets so the Player keeps the Lit variants.
            pins.Add(Pin("Pin_Lit_Emission_Washi", lit, m =>
            {
                m.EnableKeyword("_EMISSION"); m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP");
                m.SetColor("_EmissionColor", Color.white);
            }));
            pins.Add(Pin("Pin_Lit_Emission_Paper", lit, m => { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white); }));
            pins.Add(Pin("Pin_Unlit_Opaque", unlit, m => { }));
            pins.Add(Pin("Pin_Unlit_Transparent", unlit, m =>
            {
                Saydam(m);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHABLEND_ON");
            }));
            pins.Add(Pin("Pin_Unlit_Additive", unlit, m =>
            {
                Saydam(m);
                m.SetFloat("_Blend", 1f);
                m.SetInt("_DstBlend", (int)BlendMode.One);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHAMODULATE_ON");
            }));

            // EnemyEffects / WorldAtmosphere particle sheets.
            pins.Add(Pin("Pin_Particles_Alpha", particles, m =>
            {
                Saydam(m);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHABLEND_ON");
            }));
            pins.Add(Pin("Pin_Particles_Additive", particles, m =>
            {
                Saydam(m);
                m.SetFloat("_Blend", 1f);
                m.SetInt("_DstBlend", (int)BlendMode.One);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.EnableKeyword("_ALPHAMODULATE_ON");
            }));

            // WP-073: the tracer ribbons are drawn with Graphics.DrawMeshInstanced from a runtime material — without an
            // instanced material asset the build strips the instancing variant and the ribbons draw nothing in a Player.
            if (vfxRibbon != null)
            {
                pins.Add(Pin("Pin_VfxRibbon_Instanced", vfxRibbon, m => { m.enableInstancing = true; }));
            }

            // WP-073 B6: the Namekuji blobs / droplets / Raiju orb (DD/VFX Gel) and puddles / splats / burns (DD/VFX Puddle) are
            // instanced from runtime materials too.
            if (vfxGel != null) pins.Add(Pin("Pin_VfxGel_Instanced", vfxGel, m => { m.enableInstancing = true; }));
            if (vfxPuddle != null) pins.Add(Pin("Pin_VfxPuddle_Instanced", vfxPuddle, m => { m.enableInstancing = true; }));

            // WP-093: the living ground is drawn with Graphics.RenderMeshInstanced from runtime materials — the instancing
            // variant only survives the build through this pin.
            if (vegetation != null) pins.Add(Pin("Pin_Vegetation_Instanced", vegetation, m => { m.enableInstancing = true; }));

            return pins.ToArray();
        }

        private static void Saydam(Material m)
        {
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); }
            if (m.HasProperty("_SrcBlend")) { m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); }
            if (m.HasProperty("_DstBlend")) { m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); }
            if (m.HasProperty("_ZWrite")) { m.SetInt("_ZWrite", 0); }
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Material Pin(string ad, Shader shader, System.Action<Material> kur)
        {
            string yol = PinKlasor + "/" + ad + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(yol);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, yol);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            kur(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
