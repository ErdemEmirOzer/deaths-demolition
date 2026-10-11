using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DD.Art.Tests.EditMode
{
    /// <summary>
    /// WP-084 (S-05 review S1, C-00 06.10) · DD/Character/Tier Lit carries its surface keywords (_TIER_TINT, _ZOMBIE_MASKS) only in
    /// the passes that build the surface (ForwardLit, GBuffer, Meta — InitializeStandardLitSurfaceData). DepthNormals never builds it
    /// (URP 14 LitDepthNormalsPass), so a surface keyword there only splits that pass into identical variants and its SRP batches
    /// (the Cinematic renderer's SSAO reads DepthNormals). The keyword sets are read from the compiled shader (ShaderUtil.PassHasKeyword),
    /// with ForwardLit as the positive control that the query sees them at all.
    /// </summary>
    public sealed class TierLitPassKeywordTests
    {
        private const string ShaderName = "DD/Character/Tier Lit";
        // WP-111 R4: _HERO_READ (the hero's dark lift + rim) is a ForwardLit-only surface keyword — never in DepthNormals either
        private static readonly string[] SurfaceKeywords = { "_TIER_TINT", "_ZOMBIE_MASKS", "_HERO_READ" };

        [Test]
        public void TierLit_DepthNormals_YuzeyAnahtariYok_ForwardLitteVar()
        {
            Shader shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, "kaynak yok: " + ShaderName);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "shader derleme hatası");
            PassIdentifier forward = FindPass(shader, "ForwardLit", "UniversalForward"), depthNormals = FindPass(shader, "DepthNormals", "DepthNormals");
            var fails = new List<string>();
            foreach (string name in SurfaceKeywords)
            {
                var keyword = new LocalKeyword(shader, name);
                Assert.IsTrue(keyword.isValid, "kaynak yok: " + name + " shader'ın anahtar uzayında değil");
                if (!ShaderUtil.PassHasKeyword(shader, forward, keyword)) fails.Add("ForwardLit " + name + " taşımıyor (pozitif kontrol)");
                if (ShaderUtil.PassHasKeyword(shader, depthNormals, keyword)) fails.Add("DepthNormals " + name + " taşıyor");
            }

            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>
        /// Measurement (before / after S1): the distinct DepthNormals keyword sets the Tier Lit materials select — each material's
        /// enabled local keywords kept only where the DepthNormals pass declares them; every distinct set is one more compiled
        /// DepthNormals variant family (× the pass's multi_compile combinations) and one more SRP batch key in that pass.
        /// Written to DD_S05_S1_DIR/depthnormals-&lt;DD_S05_S1_LABEL&gt;.txt.
        /// </summary>
        [Test]
        [Explicit("WP-084 S1 measurement: run by full name")]
        [Category("Diagnostics")]
        public void TierLit_DepthNormals_BenzersizAnahtarSetleri_Olcum()
        {
            Shader shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader, "kaynak yok: " + ShaderName);
            PassIdentifier forward = FindPass(shader, "ForwardLit", "UniversalForward"), depthNormals = FindPass(shader, "DepthNormals", "DepthNormals");
            var depthSets = new HashSet<string>();
            var forwardSets = new HashSet<string>();
            int materials = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material.shader != shader) continue;
                materials++;
                var depth = new List<string>();
                var fwd = new List<string>();
                foreach (LocalKeyword k in material.enabledKeywords)
                {
                    if (ShaderUtil.PassHasKeyword(shader, depthNormals, k)) depth.Add(k.name);
                    if (ShaderUtil.PassHasKeyword(shader, forward, k)) fwd.Add(k.name);
                }

                depth.Sort(StringComparer.Ordinal);
                fwd.Sort(StringComparer.Ordinal);
                depthSets.Add(string.Join(" ", depth));
                forwardSets.Add(string.Join(" ", fwd));
            }

            Assert.Greater(materials, 0, "kaynak yok: " + ShaderName + " kullanan malzeme bulunamadı");

            // Parity of the pass itself: the Metal fragment program of DepthNormals compiled with and without each surface keyword
            // (byte-identical = the keyword never changed what the pass draws), ForwardLit as the control that the compile sees it.
            string dnBase = FragmentHash(shader, depthNormals, "_NORMALMAP"), dnTint = FragmentHash(shader, depthNormals, "_NORMALMAP", "_TIER_TINT"),
                   dnMasks = FragmentHash(shader, depthNormals, "_NORMALMAP", "_ZOMBIE_MASKS");
            string fwBase = FragmentHash(shader, forward, "_NORMALMAP"), fwTint = FragmentHash(shader, forward, "_NORMALMAP", "_TIER_TINT");
            Assert.AreNotEqual(fwBase, fwTint, "kaynak yok: ForwardLit derlemesi _TIER_TINT'i görmüyor (kontrol)");
            string line = "materials=" + materials + " depthNormalsSets=" + depthSets.Count + " [" + string.Join(" | ", depthSets) + "] forwardLitSets=" + forwardSets.Count
                          + " depthNormalsSurfaceKeywords=" + string.Join(",", Array.FindAll(SurfaceKeywords, n => ShaderUtil.PassHasKeyword(shader, depthNormals, new LocalKeyword(shader, n))))
                          + " dnFragMetal base=" + dnBase + " tint=" + dnTint + " masks=" + dnMasks + " dnIdentical=" + (dnBase == dnTint && dnBase == dnMasks)
                          + " fwFragMetal base=" + fwBase + " tint=" + fwTint;
            Debug.Log("[DD-OBS] wp084.s1.depthnormals " + line);
            string dir = Environment.GetEnvironmentVariable("DD_S05_S1_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "depthnormals-" + (Environment.GetEnvironmentVariable("DD_S05_S1_LABEL") ?? "run") + ".txt"), line + "\n");
            }
        }

        /// <summary>SHA-256 (first 16 hex) of the pass's Metal fragment program compiled for <paramref name="keywords"/>.</summary>
        private static string FragmentHash(Shader shader, PassIdentifier pass, params string[] keywords)
        {
            ShaderData.Pass p = ShaderUtil.GetShaderData(shader).GetSubshader((int)pass.SubshaderIndex).GetPass((int)pass.PassIndex);
            ShaderData.VariantCompileInfo info = p.CompileVariant(UnityEditor.Rendering.ShaderType.Fragment, keywords, UnityEditor.Rendering.ShaderCompilerPlatform.Metal, BuildTarget.StandaloneOSX);
            Assert.IsTrue(info.Success, "kaynak yok: " + p.Name + " [" + string.Join(" ", keywords) + "] derlenemedi");
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(info.ShaderData)).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        /// <summary>
        /// The pass by its name, or by its LightMode tag (the batch EditMode runner has no graphics device, so no active sub-shader
        /// is assumed — every sub-shader and pass is searched).
        /// </summary>
        private static PassIdentifier FindPass(Shader shader, string passName, string lightMode)
        {
            ShaderData data = ShaderUtil.GetShaderData(shader);
            Assert.IsNotNull(data, "kaynak yok: ShaderData");
            var seen = new List<string>();
            for (int s = 0; s < shader.subshaderCount; s++)
                for (int p = 0; p < shader.GetPassCountInSubshader(s); p++)
                {
                    string name = s < data.SubshaderCount && p < data.GetSubshader(s).PassCount ? data.GetSubshader(s).GetPass(p).Name : "";
                    string tag = shader.FindPassTagValue(s, p, new ShaderTagId("LightMode")).name;
                    seen.Add(s + "/" + p + " " + name + " [" + tag + "]");
                    if (name == passName || tag == lightMode) return new PassIdentifier((uint)s, (uint)p);
                }

            Assert.Fail("kaynak yok: " + passName + " pass'i (görülen: " + string.Join(", ", seen) + ")");
            return default;
        }
    }
}
