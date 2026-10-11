using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons.Attachments;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;   // Is.Not.AllocatingGCMemory() extension

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B4 (S-06, C-00 12.10 · B4-PLAN-12.10 §6 EditMode 5–6) · tree stats → weapon attachments, the rules alone (no library,
    /// no prefabs): the magazine ladder E1 → E2 S/M/L, the launcher's E6-only policy, E3 by weapon length and never on sg_2 (C-00 Q3),
    /// E5 from tier IV with the chaos variant (C-00 Q5), one E4 capsule; allocation-free; the S-02 lookups neutral with nothing mounted.
    /// </summary>
    [TestFixture]
    [Category("WP104")]
    public sealed class Wp104B4AttachRulesTests
    {
        private static TreeStats Stats() => TreeStats.Neutral;
        private static byte[] s_sink;

        [Test]
        public void Notr_EkYok()
        {
            TreeStats s = Stats();
            foreach (ClassicWeaponId id in System.Enum.GetValues(typeof(ClassicWeaponId)))
            {
                WeaponAttachSet a = WeaponAttachRules.Resolve(in s, id, WeaponAttachRules.DominantElement(in s));
                Assert.IsTrue(a.IsEmpty, id + ": nötr ağaçta ek olmamalı");
            }
        }

        [Test]
        public void Sarjor_E1_E2Boylari_KademeyeGore()
        {
            TreeStats s = Stats();
            s.MagazineScale = 1.5f;
            Assert.AreEqual(1, WeaponAttachRules.Resolve(in s, ClassicWeaponId.AssaultRifle, TreeElement.None).Mag, "mg_ext → E1");
            s.MagazineScale = 3f;
            for (int tier = 2; tier <= 5; tier++)
            {
                s.SetBranchTier(TreeBranch.Magazine, tier);
                int expect = 1 + System.Math.Min(System.Math.Max(tier - 1, 1), 3);   // II S (2) · III M (3) · IV+ L (4)
                Assert.AreEqual(expect, WeaponAttachRules.Resolve(in s, ClassicWeaponId.AssaultRifle, TreeElement.None).Mag, "mg_double kademe " + tier);
            }
        }

        [Test]
        public void Roketatar_YalnizE6_TamOtomatikle()
        {
            TreeStats s = Stats();
            s.MagazineScale = 3f;
            s.SetBranchTier(TreeBranch.Magazine, 4);
            Assert.AreEqual(0, WeaponAttachRules.Resolve(in s, ClassicWeaponId.Rl1, TreeElement.None).Mag, "rl_1'e E1/E2 takılmaz");
            s.FullAuto = true;
            WeaponAttachSet a = WeaponAttachRules.Resolve(in s, ClassicWeaponId.Rl1, TreeElement.None);
            Assert.AreEqual(5, a.Mag, "rl_1 + tam otomatik → E6 kutu");
            Assert.AreEqual(0, a.Ring, "roketatarda tam otomatik tek başına E5 vermez");
        }

        [Test]
        public void IkizNamlu_SilahBoyu_Sg2Yok()
        {
            TreeStats s = Stats();
            s.TwinBarrel = true;
            Assert.AreEqual(2, WeaponAttachRules.Resolve(in s, ClassicWeaponId.AssaultRifle, TreeElement.None).Twin, "ar_1 orta");
            Assert.AreEqual(3, WeaponAttachRules.Resolve(in s, ClassicWeaponId.Lmg1, TreeElement.None).Twin, "lmg_1 uzun");
            Assert.AreEqual(3, WeaponAttachRules.Resolve(in s, ClassicWeaponId.Sr1, TreeElement.None).Twin, "sr_1 uzun");
            Assert.AreEqual(1, WeaponAttachRules.Resolve(in s, ClassicWeaponId.Smg2, TreeElement.None).Twin, "smg_2 kısa");
            Assert.AreEqual(0, WeaponAttachRules.Resolve(in s, ClassicWeaponId.Sg2, TreeElement.None).Twin, "sg_2 zaten alt-üst çift namlu (C-00 Q3)");
        }

        [Test]
        public void GucHalkasi_KademeIVden_KaosV02()
        {
            TreeStats s = Stats();
            s.SetBranchTier(TreeBranch.Speed, 3);
            Assert.AreEqual(0, WeaponAttachRules.Resolve(in s, ClassicWeaponId.AssaultRifle, TreeElement.None).Ring, "kademe III'te halka yok (C-00 Q5: IV+)");
            s.SetBranchTier(TreeBranch.Speed, 4);
            WeaponAttachSet a = WeaponAttachRules.Resolve(in s, ClassicWeaponId.AssaultRifle, TreeElement.None);
            Assert.AreEqual(1, a.Ring, "Hız IV → E5 hız");
            Assert.AreEqual(4, a.RingTier);
            s.SetBranchTier(TreeBranch.Chaos, 4);
            Assert.AreEqual(2, WeaponAttachRules.Resolve(in s, ClassicWeaponId.AssaultRifle, TreeElement.None).Ring, "Kaos IV → E5 kaos (V02)");
            TreeStats auto = Stats();
            auto.FullAuto = true;
            Assert.AreEqual(1, WeaponAttachRules.Resolve(in auto, ClassicWeaponId.Ar2, TreeElement.None).Ring, "roketatar dışı tam otomatik → E5");
            TreeStats endless = Stats();
            endless.EndlessKills = 3;
            Assert.AreEqual(1, WeaponAttachRules.Resolve(in endless, ClassicWeaponId.Smg2, TreeElement.None).Ring, "sonsuz şarjör penceresi → E5");
        }

        [Test]
        public void ElementKapsulu_TekKapsul_KademeElementDalindan()
        {
            TreeStats s = Stats();
            s.Elements = TreeElement.Ice | TreeElement.Venom;
            s.SetBranchTier(TreeBranch.Element, 3);
            Assert.AreEqual(TreeElement.Ice, WeaponAttachRules.DominantElement(in s), "baskın düğüm yokken Fire → Ice → Shock → Venom sırasındaki ilk bayrak");
            s.DominantElement = TreeElement.Fire;
            Assert.AreEqual(TreeElement.Ice, WeaponAttachRules.DominantElement(in s), "sahip olunmayan baskın element kapsül vermez");
            s.DominantElement = TreeElement.Venom;
            TreeElement dominant = WeaponAttachRules.DominantElement(in s);
            Assert.AreEqual(TreeElement.Venom, dominant, "S-04 TreeStats.DominantElement (B1b) sıradan önce gelir");
            WeaponAttachSet a = WeaponAttachRules.Resolve(in s, ClassicWeaponId.AssaultRifle, dominant);
            Assert.AreEqual(TreeElement.Venom, a.Side);
            Assert.AreEqual(3, a.SideTier, "ışıma kademesi Element dalının kademesi");
            Assert.AreEqual(0, WeaponAttachRules.ElementIndex(TreeElement.Fire));
            Assert.AreEqual(1, WeaponAttachRules.ElementIndex(TreeElement.Ice));
            Assert.AreEqual(2, WeaponAttachRules.ElementIndex(TreeElement.Shock), "AP-040 V03 = electric");
            Assert.AreEqual(3, WeaponAttachRules.ElementIndex(TreeElement.Venom), "AP-040 V04 = poison");
            Assert.AreEqual(-1, WeaponAttachRules.ElementIndex(TreeElement.None));
        }

        [Test]
        public void KombinasyonSirasi_SocketsJson()
        {
            // sockets.json order: (E3 v1, E5 v1), (E3 v1, E5 v2), (E3 v2, E5 v1), … ; E3 alone → the E5 v1 row, E5 alone → the E3 v1 row
            Assert.AreEqual(0, WeaponAttachLibrary.ComboIndex(1, 1));
            Assert.AreEqual(1, WeaponAttachLibrary.ComboIndex(1, 2));
            Assert.AreEqual(2, WeaponAttachLibrary.ComboIndex(2, 1));
            Assert.AreEqual(5, WeaponAttachLibrary.ComboIndex(3, 2));
            Assert.AreEqual(4, WeaponAttachLibrary.ComboIndex(3, 0), "E3 yalnız → E5 v1 satırı");
            Assert.AreEqual(1, WeaponAttachLibrary.ComboIndex(0, 2), "E5 yalnız → E3 v1 satırı");
        }

        [Test]
        public void Resolve_AyirmaYapmaz()
        {
            TreeStats s = Stats();
            s.MagazineScale = 3f;
            s.TwinBarrel = true;
            s.SetBranchTier(TreeBranch.Chaos, 5);
            s.Elements = TreeElement.Shock | TreeElement.Venom;
            s.DominantElement = TreeElement.Venom;
            Assert.That((TestDelegate)(() => { s_sink = new byte[64]; }), UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "pozitif kontrol: ölçüm ayırmayı görüyor");
            Assert.That((TestDelegate)(() =>
            {
                for (int i = 0; i < 10000; i++) WeaponAttachRules.Resolve(in s, ClassicWeaponId.Lmg2, WeaponAttachRules.DominantElement(in s));
            }), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), "kural çözümü olay anında bile ayırmasız");
        }

        [Test]
        public void S02Kancalari_EkYokkenNotr()
        {
            WeaponAttachments.Active = null;
            var muzzle = new UnityEngine.GameObject("WP104_Muzzle").transform;
            try
            {
                Assert.AreSame(muzzle, WeaponAttachments.FlashOrigin(muzzle), "ek yokken alev / iz kökü namlunun kendisi");
                Assert.AreEqual(MagazineVisualKind.Tube, WeaponAttachments.MagazineVisualFor(ClassicWeaponId.Rl1), "rl_1 tüp");
                Assert.AreEqual(MagazineVisualKind.Default, WeaponAttachments.MagazineVisualFor(ClassicWeaponId.Ar2), "diğerleri kendi parçası");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(muzzle.gameObject);
            }
        }
    }
}
