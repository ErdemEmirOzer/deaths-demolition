using DD.Prototype.Characters;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-084 · AP-025 contract v002, the per-zombie mask indices. ZombieAppearance.MaskCombo folds the look seed to one of the
    /// 27 skin × dirt × blood combinations (deterministic, every combination reached, no index starved); ZombieSkinnedVisual
    /// .ApplyVariation packs them into renderingLayerMask bits 16-20 / 21-25 / 26-30 of every slot and armour renderer at every
    /// LOD, keeps the low 16 bits (light / decal layers), clears bit 31 and the previous life's indices, clamps to 0..2.
    /// </summary>
    [TestFixture]
    [Category("WP084")]
    public sealed class ZombieVariationBitsTests
    {
        [Test]
        public void MaskCombo_Deterministik_27Kombinasyon_Dengeli()
        {
            const int n = 27000;
            var combos = new int[27];
            var skin = new int[3];
            var dirt = new int[3];
            var blood = new int[3];
            uint seed = 1u;
            for (int i = 0; i < n; i++)
            {
                unchecked { seed = seed * 1664525u + 1013904223u; }   // the EnemyFactory seed stream
                int c = ZombieAppearance.MaskCombo(seed);
                Assert.That(c, Is.InRange(0, 26), "kombinasyon 0..26");
                Assert.AreEqual(c, ZombieAppearance.MaskCombo(seed), "aynı seed → aynı kombinasyon");
                combos[c]++;
                skin[c / 9]++;
                dirt[c / 3 % 3]++;
                blood[c % 3]++;
            }

            // pinned: the hash is part of the look contract (a change re-rolls every zombie's skin / dirt / blood)
            Assert.AreEqual(8, ZombieAppearance.MaskCombo(1u));
            Assert.AreEqual(13, ZombieAppearance.MaskCombo(7u));
            Assert.AreEqual(19, ZombieAppearance.MaskCombo(12345u));
            Assert.AreEqual(14, ZombieAppearance.MaskCombo(0xDEADBEEFu));

            int min = int.MaxValue, max = 0;
            foreach (int k in combos) { min = Mathf.Min(min, k); max = Mathf.Max(max, k); }
            Debug.Log("[DD-OBS] wp084.maskcombo n=" + n + " min=" + min + " max=" + max + " skin=" + skin[0] + "/" + skin[1] + "/" + skin[2]
                      + " dirt=" + dirt[0] + "/" + dirt[1] + "/" + dirt[2] + " blood=" + blood[0] + "/" + blood[1] + "/" + blood[2]);
            Assert.Greater(min, n / 27 * 0.8f, "her kombinasyon beklenenin ≥ %80'i");
            Assert.Less(max, n / 27 * 1.2f, "hiçbir kombinasyon beklenenin ≥ %120'si değil");
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(n / 3f, skin[i], n / 3f * 0.05f, "ten " + i);
                Assert.AreEqual(n / 3f, dirt[i], n / 3f * 0.05f, "kir " + i);
                Assert.AreEqual(n / 3f, blood[i], n / 3f * 0.05f, "kan " + i);
            }

            // intake note (K/wp-088/R2-ZOMBI-V2-ALIM.md §6): a 200-zombie stretch covers ≥ 25 of the 27 combinations, none above 10 %
            var first200 = new int[27];
            seed = 1u;
            for (int i = 0; i < 200; i++)
            {
                unchecked { seed = seed * 1664525u + 1013904223u; }
                first200[ZombieAppearance.MaskCombo(seed)]++;
            }

            int covered = 0, top = 0;
            foreach (int k in first200) { if (k > 0) covered++; top = Mathf.Max(top, k); }
            Debug.Log("[DD-OBS] wp084.maskcombo200 covered=" + covered + "/27 max=" + top);
            Assert.GreaterOrEqual(covered, 25, "200 zombide ≥ 25/27 kombinasyon");
            Assert.LessOrEqual(top, 20, "hiçbir kombinasyon 200'ün %10'unu aşmaz");
        }

        [Test]
        public void ApplyVariation_Bit16_30_AltBitlerKorunur_Bit31Sifir_Kenetlenir()
        {
            var root = new GameObject("wp084-variation");
            try
            {
                var slots = new SkinnedMeshRenderer[ZombieModuleLibrary.LodCount * ZombieSkinnedVisual.SlotCount];
                for (int i = 0; i < slots.Length; i++) slots[i] = NewRenderer(root, "slot" + i);
                var armour = new SkinnedMeshRenderer[ZombieSkinnedVisual.ArmorRendererCount];
                for (int i = 0; i < armour.Length; i++) armour[i] = NewRenderer(root, "armour" + i);
                var visual = root.AddComponent<ZombieSkinnedVisual>();
                visual.Configure(null, null, slots, null, null, null, null, null, null);
                visual.ConfigureArmor(armour);
                Assert.AreEqual(0u, visual.VariationBits, "ilk uygulamadan önce 0");

                // a pooled renderer with a previous life's indices, a stray bit 31 and low bits that are NOT the character layers —
                // ApplyVariation keeps whatever the low 16 bits hold (it never forces them)
                const uint low = 0x0105u;
                const uint stale = low | (2u << 16) | (2u << 21) | (2u << 26) | 0x80000000u;
                foreach (SkinnedMeshRenderer r in slots) r.renderingLayerMask = stale;
                foreach (SkinnedMeshRenderer r in armour) r.renderingLayerMask = stale;

                visual.ApplyVariation(2, 1, 0);
                uint expected = low | (2u << ZombieSkinnedVisual.VariationShiftSkin) | (1u << ZombieSkinnedVisual.VariationShiftDirt);
                AssertAll(slots, armour, expected, "ten 2 / kir 1 / kan 0");
                Assert.AreEqual(expected & ~ZombieSkinnedVisual.VariationKeepMask, visual.VariationBits);

                visual.ApplyVariation(5, -3, 9);   // clamp → 2 / 0 / 2
                AssertAll(slots, armour, low | (2u << 16) | (2u << 26), "0..2'ye kenetlenir");

                visual.ApplyVariation(0, 0, 0);
                AssertAll(slots, armour, low, "önceki hayatın indeksleri kalmaz, alt bitler korunur");

                // the three fields never overlap and stay below bit 31 at their maximum
                Assert.AreEqual(0u, (3u << 16) & (3u << 21));
                Assert.AreEqual(0u, (3u << 21) & (3u << 26));
                Assert.AreEqual(0u, ((2u << 16) | (2u << 21) | (2u << 26)) & 0x8000FFFFu);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static SkinnedMeshRenderer NewRenderer(GameObject root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go.AddComponent<SkinnedMeshRenderer>();
        }

        private static void AssertAll(SkinnedMeshRenderer[] slots, SkinnedMeshRenderer[] armour, uint expected, string what)
        {
            for (int i = 0; i < slots.Length; i++) Assert.AreEqual(expected, slots[i].renderingLayerMask, what + ": yuva " + i);
            for (int i = 0; i < armour.Length; i++) Assert.AreEqual(expected, armour[i].renderingLayerMask, what + ": zırh " + i);
        }
    }
}
