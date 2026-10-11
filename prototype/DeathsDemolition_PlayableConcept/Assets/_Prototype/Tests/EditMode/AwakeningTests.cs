using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using NUnit.Framework;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B1 · Uyanış (GD-TREE §3): katalogdaki 12 düğüm <see cref="TreeAwakening"/> bayraklarına birebir eşlenir, dal başına
    /// alternatif capstone biti <see cref="TreeBranch"/> sırasındadır, bilinmeyen / eksik profil hiçbir şey açmaz. Saf veri; disk yok.
    /// </summary>
    [TestFixture]
    public sealed class AwakeningTests
    {
        [Test]
        public void BosVeBilinmeyen_None()
        {
            Assert.AreEqual(TreeAwakening.None, TreeAwakening.FromProfile(null));
            Assert.AreEqual(TreeAwakening.None, TreeAwakening.FromProfile(new ProfileData()));
            var p = new ProfileData();
            p.MetaNodes.Add("w_ar_2");
            p.MetaNodes.Add("pa_eco_1");
            p.MetaNodes.Add("aw_future_thing");
            p.MetaNodes = null;
            Assert.AreEqual(TreeAwakening.None, TreeAwakening.FromProfile(p), "eksik liste → None");
            var q = new ProfileData();
            q.MetaNodes.Add("w_ar_2");
            q.MetaNodes.Add("aw_future_thing");
            Assert.AreEqual(TreeAwakening.None, TreeAwakening.FromProfile(q), "bilinmeyen kimlik bir şey açmaz");
        }

        [Test]
        public void HerKatalogDugumu_BirBayragaEslenir()
        {
            int sayi = 0;
            foreach (MetaPathNode n in MetaPathCatalog.Nodes)
            {
                if (n.Kind != MetaNodeKind.Awakening) continue;
                sayi++;
                var p = new ProfileData();
                p.MetaNodes.Add(n.Id);
                Assert.AreNotEqual(TreeAwakening.None, TreeAwakening.FromProfile(p), n.Id + " TreeAwakening'de okunur");
            }

            Assert.AreEqual(12, sayi);
        }

        [Test]
        public void Bayraklar_VeAlternatifCapstoneBitleri()
        {
            var p = new ProfileData();
            foreach (string id in new[] { "aw_alt_sh", "aw_alt_kx", "aw_alt_ec", "aw_third", "aw_twincore", "aw_codex", "aw_headstart", "aw_rethink" }) p.MetaNodes.Add(id);
            TreeAwakening a = TreeAwakening.FromProfile(p);
            Assert.IsTrue(a.ThirdAffinity && a.TwinCores && a.SynergyCodex && a.HeadStart && a.SecondThoughts);
            Assert.IsTrue(a.HasAltCapstone(TreeBranch.Shot));
            Assert.IsTrue(a.HasAltCapstone(TreeBranch.Chaos));
            Assert.IsTrue(a.HasAltCapstone(TreeBranch.Economy));
            Assert.IsFalse(a.HasAltCapstone(TreeBranch.Magazine));
            Assert.AreEqual((1 << (int)TreeBranch.Shot) | (1 << (int)TreeBranch.Chaos) | (1 << (int)TreeBranch.Economy), a.AltCapstones);

            var q = new ProfileData();
            foreach (string id in new[] { "aw_alt_mg", "aw_alt_sp", "aw_alt_el", "aw_alt_sv" }) q.MetaNodes.Add(id);
            TreeAwakening b = TreeAwakening.FromProfile(q);
            Assert.IsFalse(b.ThirdAffinity || b.TwinCores || b.SynergyCodex || b.HeadStart || b.SecondThoughts);
            Assert.AreEqual((1 << 1) | (1 << 2) | (1 << 3) | (1 << 5), b.AltCapstones);
        }
    }
}
