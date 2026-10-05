using System;
using System.Reflection;
using DD.Prototype.Characters;
using DD.Prototype.Core;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;   // the AllocatingGCMemory() extension (Is stays qualified: NUnit.Framework.Is too)
using Object = UnityEngine.Object;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// B-03 GC review (C-00 05.10, docs/c00/reviews/B-03-gc-inceleme.md §3, BEKLEYEN §19), S-02's rows: G3 — a reload on the same
    /// weapon does not search the magazine part again (every node's name is a new string); B1 — TryStepFor is an enum switch and
    /// still says what the name table says for every event; B2 — the casing's floor mask is looked up once (Default | Environment)
    /// and reset on play.
    /// </summary>
    [TestFixture]
    [Category("WP087")]
    public sealed class Wp087B03GcTests
    {
        private static Transform Node(string name, Transform parent)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // a weapon rig with a model of a few nodes and its part_mag, like an AP021_ model under the rig
        private static Transform Model(Transform root, string name, out Transform part)
        {
            Transform model = Node(name, root);
            for (int i = 0; i < 12; i++) Node("node_" + i, Node("lod_" + i, model));
            part = Node(WeaponMagazineVisual.PartName, model);
            Node("part_mag_LOD0", part);
            return model;
        }

        [Test]
        public void G3_AyniSilah_IkinciBind_AdTaramasiYok_ModelDegisinceYenidenArar()
        {
            Transform root = new GameObject("ClassicWeaponRig").transform;
            Transform socket = new GameObject("socket_mag_l").transform;
            try
            {
                Transform model = Model(root, "AP021_ar_1", out Transform part);
                var v = new WeaponMagazineVisual();
                v.Bind(root);
                Assert.AreSame(part, v.Part, "ilk bağlama parçayı bulur");
                v.Bind(root);   // warm-up: the early-out path and the lambda run once before the measured window
                Assert.That((TestDelegate)(() => v.Bind(root)), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "aynı silahta reload başına ad taraması yok (G3)");
                Assert.AreSame(part, v.Part);

                // the positive control: a first search reads every node's name (a new string each)
                var fresh = new WeaponMagazineVisual();
                Assert.That((TestDelegate)(() => fresh.Bind(root)), UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: ilk arama ad okur, ayırır");

                // a part left in the hand goes home and stays bound
                Assert.IsTrue(v.ToHand(socket));
                v.Bind(root);
                Assert.IsFalse(v.InHand, "eldeki parça Bind'da eve döner");
                Assert.AreSame(part, v.Part);
                Assert.AreSame(model, part.parent);

                // the rig's model swapped (WeaponVisualRig.SetModel destroys the old one): the new part is found
                Object.DestroyImmediate(model.gameObject);
                Model(root, "AP021_smg_1", out Transform part2);
                v.Bind(root);
                Assert.AreSame(part2, v.Part, "model değişince yeni parça aranır");

                // another weapon root (no part) and back to no weapon
                Transform other = new GameObject("OtherRig").transform;
                try
                {
                    v.Bind(other);
                    Assert.IsNull(v.Part, "başka kök: parça yok");
                    v.Bind(root);
                    Assert.AreSame(part2, v.Part);
                    v.Bind(null);
                    Assert.IsNull(v.Part);
                }
                finally { Object.DestroyImmediate(other.gameObject); }
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
                Object.DestroyImmediate(socket.gameObject);
            }
        }

        [Test]
        public void B1_TryStepFor_HerOlaydaAdTablosuylaAyni()
        {
            foreach (CharacterAnimationEvent e in Enum.GetValues(typeof(CharacterAnimationEvent)))
            {
                bool want = false;
                int wantStep = -1;
                string name = (int)e < CharacterAnimationEvents.Names.Length ? CharacterAnimationEvents.Names[(int)e] : null;
                for (int i = 0; i < WeaponMagazineOwnership.EventNames.Length && !want && !string.IsNullOrEmpty(name); i++)
                {
                    if (Array.IndexOf(WeaponMagazineOwnership.EventNames[i], name) >= 0) { want = true; wantStep = i; }
                }

                bool got = WeaponMagazineOwnership.TryStepFor(e, out MagazineStep step);
                Assert.AreEqual(want, got, e + " (" + name + ")");
                if (want) Assert.AreEqual((MagazineStep)wantStep, step, e + " (" + name + ")");
            }

            // the contract's spellings arrive as the existing events (CharacterAnimationEvents.Synonyms)
            for (int i = 0; i < CharacterAnimationEvents.Synonyms.GetLength(0); i++)
            {
                CharacterAnimationEvent e = CharacterAnimationEvents.Parse(CharacterAnimationEvents.Synonyms[i, 1]);
                Assert.IsTrue(WeaponMagazineOwnership.TryStepFor(e, out MagazineStep step), CharacterAnimationEvents.Synonyms[i, 0]);
                int row = -1;
                for (int r = 0; r < WeaponMagazineOwnership.EventNames.Length; r++)
                {
                    if (Array.IndexOf(WeaponMagazineOwnership.EventNames[r], CharacterAnimationEvents.Synonyms[i, 0]) >= 0) row = r;
                }

                Assert.AreEqual((MagazineStep)row, step, CharacterAnimationEvents.Synonyms[i, 0]);
            }
        }

        [Test]
        public void B2_KovanZeminMaskesi_DefaultVeEnvironment_AdAramasizBakilir()
        {
            int env = LayerMask.NameToLayer("Environment");
            int want = 1 | (env >= 0 ? 1 << env : 0);
            Assert.AreEqual(want, CasingEffect.FloorMask, "Default | Environment");
            Assert.AreEqual(env, PrototypeLayers.Environment, "katman PrototypeLayers önbelleğinden");
            Assert.That((TestDelegate)(() => s_mask = CasingEffect.FloorMask), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
            Assert.AreEqual(want, s_mask);

            // the cache is reset on play (domain reload off): PrototypeLayers' own SubsystemRegistration reset
            MethodInfo reset = typeof(PrototypeLayers).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(reset, "SubsystemRegistration sıfırlaması yok");
            var attr = reset.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Assert.IsNotNull(attr);
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, attr.loadType);
        }

        private static int s_mask;
    }
}
