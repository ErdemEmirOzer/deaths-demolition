using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DD.Prototype.Characters;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-084 (C-00 05.10) · shader parity evidence for DD/Character/Tier Lit's _ZOMBIE_MASKS change, measured inside ONE run so
    /// texture / lighting state is identical on both sides (run-to-run frames of this editor differ by up to 80/255 even with
    /// streaming off — not a usable proof). Every zombie material on the shader (ZombieTierLibrary body / armour / part sets,
    /// the module library's version + cloth sets) is drawn on a sphere twice: as shipped (patched shader, keyword off) and as a
    /// clone on the pre-change shader copy named by DD_WP084_PARITY_REFERENCE (local evidence file, not committed). The per-
    /// material max channel difference and differing pixel count are written to DD_WP084_PARITY_DIR. A positive control
    /// renders a _ZOMBIE_MASKS clone with variation bits — it must differ, proving the keyword path is live.
    /// </summary>
    [TestFixture]
    [Explicit("WP-084 measurement: run by full name")]
    [Category("Diagnostics")]
    public sealed class TierLitParityCaptureTests
    {
        private const int Size = 160;

        /// <summary>A shader asset of the project by its shader name (Editor only: the test is an Editor PlayMode measurement).</summary>
        private static Shader FindShaderAsset(string shaderName)
        {
#if UNITY_EDITOR
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Shader", new[] { "Assets" }))
            {
                var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (shader != null && shader.name == shaderName) return shader;
            }
#endif
            return null;
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator TierLit_KeywordKapali_ReferansaPikselEs()
        {
            LogAssert.ignoreFailingMessages = true;
            string referenceName = Environment.GetEnvironmentVariable("DD_WP084_PARITY_REFERENCE") ?? "Hidden/DD/Tests/Tier Lit R1 Reference";
            // ShaderLibraryTests.UretimKodu_ShaderFindKullanmaz (main-ci 173890ac): no string lookup. The shipped shader is the one the
            // shipped tier materials carry; the R1 reference (a local, never-committed copy — K/wp-084/parity-ref) is found by name
            // among the project's shader assets.
            Shader reference = FindShaderAsset(referenceName);
            Assert.IsNotNull(reference, "kaynak yok: referans shader " + referenceName);
            ZombieTierLibrary tier = ZombieTierLibrary.Active;
            Assert.IsTrue(tier != null && tier.IsReady, "kaynak yok: tier kütüphanesi");
            Shader shipped = tier.bodyMaterials[0] != null ? tier.bodyMaterials[0].shader : null;
            Assert.IsTrue(shipped != null && shipped.name == "DD/Character/Tier Lit", "kaynak yok: DD/Character/Tier Lit (tier gövde materyali)");

            Scene scene = SceneManager.CreateScene("WP084 parity " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(scene);
            var materials = new List<Material>();
            // WP-084 slice 1: the AP-025 O1 skin and cloth materials ship with _ZOMBIE_MASKS on — this test is the keyword-OFF parity
            // (the shader commit's promise), so only the materials that keep it off are compared (armour tint ×4, tier parts ×4).
            void Add(Material m) { if (m != null && m.shader == shipped && !m.IsKeywordEnabled("_ZOMBIE_MASKS") && !materials.Contains(m)) materials.Add(m); }
            foreach (Material m in tier.bodyMaterials) Add(m);
            foreach (Material m in tier.armorMaterials) Add(m);
            foreach (Material m in tier.partMaterials) Add(m);
            CharacterAssetSet set = CharacterAssetSet.Load();
            if (set != null && set.ZombieLibrary != null)
            {
                foreach (Material m in set.ZombieLibrary.tierMaterials) Add(m);
                foreach (Material m in set.ZombieLibrary.tierClothMaterials) Add(m);
            }
            Assert.GreaterOrEqual(materials.Count, 8, "kaynak yok: anahtarı kapalı Tier Lit materyalleri");

            var camGo = new GameObject("parity camera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.18f, 0.18f, 0.2f, 1f);
            cam.orthographic = true;
            cam.orthographicSize = 0.6f;
            cam.allowMSAA = false;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);
            var lightGo = new GameObject("parity light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            var sphere = new GameObject("parity sphere");
            sphere.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            var mr = sphere.AddComponent<MeshRenderer>();

            bool streaming = QualitySettings.streamingMipmapsActive;
            QualitySettings.streamingMipmapsActive = false;
            for (int i = 0; i < 30; i++) yield return null;

            var report = new StringBuilder("material\tmaxDiff\tpixelsDiffering\tfloorMax\tfloorPixels\n");
            int worstMax = 0, worstPixels = 0, worstFloor = 0, deterministicMismatch = 0;
            var clones = new List<Material>();
            try
            {
                foreach (Material m in materials)
                {
                    Assert.IsFalse(m.IsKeywordEnabled("_ZOMBIE_MASKS"), m.name + ": shipped material already has _ZOMBIE_MASKS (parity is for keyword off)");
                    var refMat = new Material(m) { name = m.name + " (R1 ref)", shader = reference };
                    refMat.shaderKeywords = m.shaderKeywords;
                    clones.Add(refMat);
                    // warm-up: the first renders of a material / variant are not stable in this editor (mip / variant first use);
                    // then shipped, reference, shipped again — the shipped-vs-shipped pair is the noise floor of the measurement
                    for (int w = 0; w < 4; w++) { Render(cam, mr, m); Render(cam, mr, refMat); }
                    Color32[] a = Render(cam, mr, m), b = Render(cam, mr, refMat), a2 = Render(cam, mr, m);
                    Diff(a, b, out int max, out int pixels);
                    Diff(a, a2, out int floorMax, out int floorPixels);
                    worstMax = Math.Max(worstMax, max); worstPixels = Math.Max(worstPixels, pixels); worstFloor = Math.Max(worstFloor, floorMax);
                    if (floorMax == 0 && max > 0) deterministicMismatch++;   // a material that renders identically twice must match the reference exactly
                    report.Append(m.name).Append('\t').Append(max).Append('\t').Append(pixels).Append('\t').Append(floorMax).Append('\t').Append(floorPixels).Append('\n');
                }

                // positive control: the keyword path is live (a masks clone with variation bits must change the frame)
                Material body = tier.bodyMaterials[0];
                var masks = new Material(body) { name = body.name + " (_ZOMBIE_MASKS control)" };
                clones.Add(masks);
                masks.EnableKeyword("_ZOMBIE_MASKS");
                masks.SetFloat("_ZombieMasksOn", 1f);
                uint oldMask = mr.renderingLayerMask;
                mr.renderingLayerMask = (oldMask & 0xFFFFu) | (2u << 16) | (2u << 21) | (2u << 26);
                for (int w = 0; w < 4; w++) { Render(cam, mr, body); Render(cam, mr, masks); }
                Color32[] off = Render(cam, mr, body), on = Render(cam, mr, masks);
                mr.renderingLayerMask = oldMask;
                Diff(off, on, out int ctrlMax, out int ctrlPixels);
                report.Append("CONTROL _ZOMBIE_MASKS on (bits skin2 dirt2 blood2)\t").Append(ctrlMax).Append('\t').Append(ctrlPixels).Append('\n');

                string summary = "materials=" + materials.Count + " deterministicMismatch=" + deterministicMismatch + " worstMaxDiff=" + worstMax + " worstPixelsDiffering=" + worstPixels + " noiseFloorMax=" + worstFloor
                                 + " control(maxDiff=" + ctrlMax + " pixels=" + ctrlPixels + ")";
                Debug.Log("[DD-OBS] wp084.tierlit.parity " + summary);
                string dir = Environment.GetEnvironmentVariable("DD_WP084_PARITY_DIR");
                if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "parity-same-run.tsv"), report + summary + "\n"); }
                // Materials whose own repeat render differs (noise floor > 0: the armour / tier atlases in this editor, p10 up to 63/255)
                // cannot prove pixel identity; they are reported. Every material that renders identically twice must match exactly.
                Assert.AreEqual(0, deterministicMismatch, "keyword kapalıyken deterministik materyalde referanstan fark");
                Assert.Greater(ctrlPixels, 0, "_ZOMBIE_MASKS kontrolü kareyi değiştirmedi (yol çalışmıyor)");
            }
            finally
            {
                QualitySettings.streamingMipmapsActive = streaming;
                foreach (Material c in clones) UnityEngine.Object.Destroy(c);
            }

            yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static Color32[] Render(Camera cam, MeshRenderer mr, Material m)
        {
            mr.sharedMaterial = m;
            var rt = new RenderTexture(Size, Size, 24) { antiAliasing = 1 };
            rt.Create();
            RenderTexture prev = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                tex.Apply();
                Color32[] px = tex.GetPixels32();
                UnityEngine.Object.Destroy(tex);
                return px;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prev;
                rt.Release();
            }
        }

        private static void Diff(Color32[] a, Color32[] b, out int max, out int pixels)
        {
            max = 0; pixels = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
                if (d > 0) pixels++;
                max = Math.Max(max, d);
            }
        }
    }
}
