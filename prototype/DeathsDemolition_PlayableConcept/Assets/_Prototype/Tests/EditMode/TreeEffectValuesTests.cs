using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DD.Prototype.Core.Config;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-077 K10b · ağaç etki değerleri: katalog args'ı (R1 paketinin yer tutucuları, gösterim birimi) her düğümde paketin
    /// en açıklamasındaki her yer tutucuyu karşılar (10 silah × 2 Special; kademe metinleri canlı ayardan gelir), sahip olunan
    /// düğümler RunTreeEffectValues'a çalışma birimiyle katlanır (satın al / geri al / Yeniden Dövme her değişimde yeniden
    /// kurar), koşu yokken Active = None.
    /// </summary>
    [TestFixture]
    public sealed class TreeEffectValuesTests
    {
        private PrototypeConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<PrototypeConfig>();
            RunTreeCatalog.TestSeam.Override(null);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        private RunTreeState Agac(ClassicWeaponId silah = ClassicWeaponId.AssaultRifle)
        {
            var t = new RunTreeState();
            t.Begin(1, silah, _config, MetaRunModifiers.None);
            return t;
        }

        [Test]
        public void Args_PaketinHerYerTutucusunuKarsilar()
        {
            string yol = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../planning/content/r1-strings.json"));
            Assume.That(File.Exists(yol), "içerik paketi yok: " + yol);
            string paket = File.ReadAllText(yol);
            var eksik = new List<string>();
            foreach (ClassicWeaponId silah in System.Enum.GetValues(typeof(ClassicWeaponId)))
            {
                foreach (string ozel in new[] { "sw_arc", "sw_gel" })
                {
                    RunTreeState t = Agac(silah);
                    t.OnSpecialPickedUp(ozel);
                    for (int i = 0; i < t.Graph.Count; i++)
                    {
                        if (t.Graph[i].Kind == RunTreeNodeKind.Tier) continue;
                        string anahtar = t.StringKeyOf(i) + ".desc";
                        Match m = Regex.Match(paket, "\"" + Regex.Escape(anahtar) + "\"\\s*:\\s*\\{\\s*\"en\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                        if (!m.Success) continue;
                        string[] adlar = t.ArgNamesOf(i);
                        foreach (Match y in Regex.Matches(m.Groups[1].Value, "\\{(\\w+)\\}"))
                        {
                            if (System.Array.IndexOf(adlar, y.Groups[1].Value) < 0) eksik.Add(silah + "/" + t.Graph[i].Id + " ← {" + y.Groups[1].Value + "}");
                        }
                    }
                }
            }

            Assert.IsEmpty(eksik, string.Join("\n", eksik));
        }

        [Test]
        public void Degerler_SahiplikleKatlanir_GeriAlDovmeYenidenKurar()
        {
            RunTreeState t = Agac();
            RunTreeEffectValues v = t.Values;
            Assert.AreEqual(0, v.PierceFullBodies);
            Assert.AreEqual(1f, v.PlateFirstHitMultiplier);

            Assert.IsTrue(t.MarkOwned(t.Graph.IndexOf("ks_piercing")));
            Assert.AreEqual(2, v.PierceFullBodies, "ks.piercing.line: ilk 2 gövde");
            Assert.IsTrue(t.MarkOwned(t.Graph.IndexOf("ks_firerate")));
            Assert.AreEqual(0.20f, v.WarmupFireRate, 1e-5f);
            Assert.AreEqual(1.5f, v.WarmupSeconds, 1e-5f);
            Assert.IsTrue(t.MarkOwned(t.Graph.IndexOf("ks_move")));
            Assert.AreEqual(0.20f, v.EvadeSpeedBonus, 1e-5f);
            Assert.AreEqual(2f, v.EvadeSeconds, 1e-5f);

            Assert.IsTrue(t.Unmark(t.Graph.IndexOf("ks_piercing")));
            Assert.AreEqual(0, v.PierceFullBodies, "geri alınca sıfırlanır");

            int x = t.Graph.IndexOf("fork_ar_1_x");
            Assert.IsTrue(t.MarkOwned(x));
            Assert.IsTrue(v.ForkX && !v.ForkY);
            Assert.AreEqual(50f, v.ForkXArg("value"), "Bloom −50% (gösterim birimi)");
            RunTreeEffects.TreatAllAsLive = true;
            try
            {
                Assert.IsTrue(t.Reforge(x, RunTreeState.ReforgeFromRound));
            }
            finally
            {
                RunTreeEffects.TreatAllAsLive = false;
            }

            Assert.IsTrue(v.ForkY && !v.ForkX, "Yeniden Dövme çatalı çevirir");
            Assert.AreEqual(15f, v.ForkYArg("value"));

            Assert.AreSame(RunTreeEffectValues.None, RunTreeEffects.Active, "koşu yokken etki yok");
        }

        [Test]
        public void Baglar_ArgsSpecialUzerindenCozulur()
        {
            RunTreeState t = Agac();
            Assert.IsEmpty(t.ArgNamesOf(t.Graph.LinkIIndex), "Special yokken bağ args'ı yok");
            t.OnSpecialPickedUp("sw_gel");
            Assert.AreEqual(15f, RunTreeArgs.Find(t.ArgNamesOf(t.Graph.LinkIIndex), t.ArgValuesOf(t.Graph.LinkIIndex), "value", 0f), "Kaygan Zemin +15%");
            Assert.AreEqual(1f, RunTreeArgs.Find(t.ArgNamesOf(t.Graph.LinkIIIndex), t.ArgValuesOf(t.Graph.LinkIIIndex), "value", 0f), "Jel Hasadı +1");
        }

        private RunTreeEffectValues Sahip(params string[] idler)
        {
            RunTreeState t = Agac();
            foreach (string id in idler) Assert.IsTrue(t.MarkOwned(t.Graph.IndexOf(id)), id);
            return t.Values;
        }

        [Test]
        public void Kancalar_SahipsizNotr()
        {
            RunTreeHooks.TestValues = new RunTreeEffectValues();
            try
            {
                Assert.AreEqual(0.8f, RunTreeHooks.PierceKeep(1, 0.8f));
                Assert.AreEqual(1f, RunTreeHooks.FireRateScale(99f));
                Assert.AreEqual(0, RunTreeHooks.FullMagExtraRounds(true, 30));
                Assert.AreEqual(1f, RunTreeHooks.PlateHitScale(true));
                Assert.AreEqual(1, RunTreeHooks.ChainFirstJumpTargets);
                Assert.AreEqual(5f, RunTreeHooks.RicochetRadius(5f));
                Assert.AreEqual(0, RunTreeHooks.ClusterExtraBomblets);
                Assert.AreEqual(10f, RunTreeHooks.PlayerDamage(10f, true, true));
                Assert.IsFalse(RunTreeHooks.TrySecondWind(out _));
                Assert.AreEqual(1f, RunTreeHooks.MoveSpeedScale);
                Assert.AreEqual(1f, RunTreeHooks.StaggeredScale(null));
            }
            finally
            {
                RunTreeHooks.TestValues = null;
            }
        }

        [Test]
        public void Kancalar_SilahKeystonelari()
        {
            RunTreeHooks.TestValues = Sahip("ks_piercing", "ks_firerate", "ks_reload");
            try
            {
                Assert.AreEqual(1f, RunTreeHooks.PierceKeep(1, 0.8f), "1. gövde sonrası kayıp yok");
                Assert.AreEqual(0.8f, RunTreeHooks.PierceKeep(2, 0.8f), "2. gövdeden sonra normal kayıp");
                Assert.AreEqual(1f, RunTreeHooks.FireRateScale(1.4f));
                Assert.AreEqual(1.2f, RunTreeHooks.FireRateScale(1.5f), 1e-5f, "1,5 s tetik → +%20");
                Assert.AreEqual(7, RunTreeHooks.FullMagExtraRounds(true, 30), "boştan dolum 30 → +7");
                Assert.AreEqual(0, RunTreeHooks.FullMagExtraRounds(false, 30), "taktik dolumda yok");
            }
            finally
            {
                RunTreeHooks.TestValues = null;
            }
        }

        [Test]
        public void Kancalar_BedenSeridi_IkinciNefesKosudaBir()
        {
            RunTreeHooks.TestValues = Sahip("ks_health", "fork_body_x", "cap_body");
            RunTreeHooks.ResetRunState();
            try
            {
                Assert.AreEqual(10f * 0.7f * 0.85f, RunTreeHooks.PlayerDamage(10f, true, true), 1e-4f, "Kalın Deri × Kül Adımı");
                Assert.AreEqual(10f, RunTreeHooks.PlayerDamage(10f, false, false), 1e-4f, "Demir Diz yok, tam can değil");
                Assert.IsTrue(RunTreeHooks.TrySecondWind(out float can));
                Assert.AreEqual(1f, can);
                Assert.IsTrue(RunTreeHooks.PlayerInvulnerable, "2 s dokunulmazlık");
                Assert.IsFalse(RunTreeHooks.TrySecondWind(out _), "koşuda bir");
                RunTreeHooks.ResetRunState();
                Assert.IsFalse(RunTreeHooks.PlayerInvulnerable);
                Assert.IsTrue(RunTreeHooks.TrySecondWind(out _), "yeni koşu yeniden hak verir");
            }
            finally
            {
                RunTreeHooks.TestValues = null;
                RunTreeHooks.ResetRunState();
            }
        }

        [Test]
        public void CanliListe_HerAgacEtkisiCanli()
        {
            // K10b stage 2: every node of every weapon's lane, the body lane and the links has a live hook.
            foreach (ClassicWeaponId silah in System.Enum.GetValues(typeof(ClassicWeaponId)))
            {
                foreach (string ozel in new[] { "sw_arc", "sw_gel" })
                {
                    RunTreeState t = Agac(silah);
                    t.OnSpecialPickedUp(ozel);
                    for (int i = 0; i < t.Graph.Count; i++)
                    {
                        if (t.Graph[i].Kind == RunTreeNodeKind.Tier) continue;
                        Assert.IsTrue(RunTreeEffects.IsLive(t.EffectOf(i)), silah + "/" + t.Graph[i].Id + " (" + t.EffectOf(i) + ")");
                    }
                }
            }
        }

        [Test]
        public void U11_KademeSatiri_SimdiSonraki_MaksNaN()
        {
            Assert.IsTrue(RunTreeTierStats.TryGet(UpgradeBranch.FireRate, 0, "ar_1", out string etiket, out float simdi, out string birim));
            Assert.AreEqual("stat.fire_rate", etiket);
            Assert.AreEqual("%", birim);
            Assert.AreEqual(0f, simdi, 1e-4f);
            Assert.IsTrue(RunTreeTierStats.TryGet(UpgradeBranch.FireRate, 1, "ar_1", out _, out float sonraki, out _));
            Assert.Greater(sonraki, simdi, "bir kademe fazlası daha yüksek");
            Assert.IsTrue(RunTreeTierStats.TryGet(UpgradeBranch.RegenerationDelay, 1, "ar_1", out _, out float bekle1, out string saniye));
            Assert.AreEqual("s", saniye);
            Assert.IsTrue(RunTreeTierStats.TryGet(UpgradeBranch.RegenerationDelay, 2, "ar_1", out _, out float bekle2, out _));
            Assert.Less(bekle2, bekle1, "bekleme kısalır");
            Assert.IsFalse(RunTreeTierStats.TryGet(UpgradeBranch.Explosion, 1, "rl_1", out _, out _, out _), "rl_1 Blast satırsız");
            foreach (UpgradeBranch b in System.Enum.GetValues(typeof(UpgradeBranch)))
            {
                if (RunTreeTierStats.TryGet(b, 1, "ar_1", out string e, out _, out _)) Assert.IsNotEmpty(e, b.ToString());
            }
        }
    }
}
