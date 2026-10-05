using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-084 (C-00 05.10) · variant count of DD/Character/Tier Lit before / after a keyword change — the Editor's own counter
    /// (ShaderUtil.GetVariantCount, internal: all variants and the ones the open scenes / materials use) plus the local keyword
    /// count. Written to DD_WP084_PARITY_DIR/variants-&lt;label&gt;.txt; measurement only.
    /// </summary>
    [TestFixture]
    [Explicit("WP-084 measurement: run by full name")]
    [Category("Diagnostics")]
    public sealed class TierLitVariantCountTests
    {
        [Test]
        public void TierLit_VaryantSayisi()
        {
            Shader shader = Shader.Find("DD/Character/Tier Lit");
            Assert.IsNotNull(shader, "kaynak yok: DD/Character/Tier Lit");
            MethodInfo count = typeof(ShaderUtil).GetMethod("GetVariantCount", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(Shader), typeof(bool) }, null);
            Assert.IsNotNull(count, "kaynak yok: ShaderUtil.GetVariantCount");
            ulong all = Convert.ToUInt64(count.Invoke(null, new object[] { shader, false }));
            ulong used = Convert.ToUInt64(count.Invoke(null, new object[] { shader, true }));
            uint local = shader.keywordSpace.keywordCount;
            bool hasError = ShaderUtil.ShaderHasError(shader);
            string srp = SrpBatcherCode(shader);
            string line = "shader=DD/Character/Tier Lit variantsAll=" + all + " variantsUsed=" + used + " localKeywords=" + local
                          + " hasZombieMasks=" + Array.Exists(shader.keywordSpace.keywordNames, n => n == "_ZOMBIE_MASKS")
                          + " shaderHasError=" + hasError + " srpBatcher=" + srp;
            Debug.Log("[DD-OBS] wp084.tierlit.variants " + line);
            string dir = Environment.GetEnvironmentVariable("DD_WP084_PARITY_DIR");
            string label = Environment.GetEnvironmentVariable("DD_WP084_PARITY_LABEL") ?? "run";
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "variants-" + label + ".txt"), line + "\n"); }
            Assert.Greater(all, 0UL, "kaynak yok: varyant sayısı 0");
            Assert.IsFalse(hasError, "shader derleme hatası");
        }

        /// <summary>The Inspector's own SRP Batcher verdict (internal ShaderUtil API, sub-shader 0, the shader's default keyword set); "unavailable" if the editor does not expose it.</summary>
        private static string SrpBatcherCode(Shader shader)
        {
            try
            {
                foreach (MethodInfo m in typeof(ShaderUtil).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (m.Name != "GetSRPBatcherCompatibilityCode") continue;
                    ParameterInfo[] p = m.GetParameters();
                    var args = new object[p.Length];
                    args[0] = shader;
                    if (p.Length > 1) args[1] = 0;
                    for (int i = 2; i < p.Length; i++) args[i] = p[i].ParameterType.IsByRef ? Activator.CreateInstance(p[i].ParameterType.GetElementType()) : Activator.CreateInstance(p[i].ParameterType);
                    int code = Convert.ToInt32(m.Invoke(null, args));
                    MethodInfo reason = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityIssueReason", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    string why = reason != null && reason.GetParameters().Length == 3 ? (string)reason.Invoke(null, new object[] { shader, 0, code }) : "";
                    return code == 0 ? "compatible" : "code" + code + "(" + why + ")";
                }
            }
            catch (Exception e) { return "unavailable(" + e.GetType().Name + ")"; }
            return "unavailable";
        }
    }
}
