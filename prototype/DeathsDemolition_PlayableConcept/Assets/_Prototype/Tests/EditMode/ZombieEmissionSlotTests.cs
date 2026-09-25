using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 (C-00 25.09 → S-07 WP-073 B6): every AP-007 zombie material carries the _EMISSION variant so S-07's
    /// MaterialPropertyBlock (electric rim, gel film) shows; the emission colour is black and GI treats it as black, so
    /// the default look is unchanged.
    /// </summary>
    [Category("WP076")]
    public sealed class ZombieEmissionSlotTests
    {
        [Test]
        public void ZombiMateryalleri_EmissionAcik_Siyah()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material M_zombie_", new[] { "Assets/_Art/Characters/Zombie" });
            Assert.AreEqual(24, guids.Length, "24 zombi materyali (atlas / cloth / armor / tier × 4 versiyon + tint)");
            foreach (string g in guids)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                Assert.IsTrue(m.IsKeywordEnabled("_EMISSION"), m.name + ": _EMISSION kapalı (S-07 MPB görünmez)");
                Color e = m.GetColor("_EmissionColor");
                Assert.IsTrue(e.r == 0f && e.g == 0f && e.b == 0f, m.name + ": emission siyah değil (varsayılan görünüm değişir)");
                Assert.IsTrue((m.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) != 0, m.name + ": GI siyah değil");
            }
        }
    }
}
