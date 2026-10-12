using DD.Prototype.Economy;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Rounds;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-104 B4-3 (S-07) · the koban orb's pure rules (AD-VISUAL K8 / X10 B18, <see cref="TreeGoldOrbCue"/>): GoldScale 1 → the orb
    /// exactly as before (×1, no glint); Coin Sense I / II / III (the catalog's own values) → ×1.25 and a glint whose size and twinkle
    /// rate climb with the tier, abartısız; the glint's hue is the orb's colour (never recoloured), Reduce Flashes dims it, a still frame
    /// always shows it. Scene-free; the orb on a pooled body is Wp104B43KobanTests (PlayMode).
    /// </summary>
    [TestFixture, Category("WP104")]
    public sealed class Wp104B43KobanRulesTests
    {
        private static float CoinSense(string id)
        {
            TreeNodeDefV2 n = TreeCatalogV2.Find(id);
            Assert.IsNotNull(n, id + " katalogda yok");
            return n.Arg("gold", 1f);
        }

        [Test]
        public void GoldScale1_KobanDegil_OlcekBirebir_ParlamaYok()
        {
            foreach (float s in new[] { 1f, 1.0000001f, .9f, 0f })
            {
                Assert.IsFalse(TreeGoldOrbCue.IsKoban(s), "GoldScale " + s + ": koban değil");
                Assert.AreEqual(0, TreeGoldOrbCue.TierOf(s));
                Assert.AreEqual(1f, TreeGoldOrbCue.ScaleFor(s), "GoldScale " + s + ": ölçek birebir ×1 (orb eski hâli)");
            }

            Assert.AreEqual(0f, TreeGoldOrbCue.GlintSize(0, .35f), "kademe yok → parıltı yok");
            Assert.AreEqual(0f, TreeGoldOrbCue.GlintPeriod(0));
            Assert.AreEqual(1f, TreeStats.Neutral.GoldScale, "ağaçsız koşu (Neutral) koban değil");
            Assert.IsFalse(TreeGoldOrbCue.IsKoban(TreeStats.Neutral.GoldScale));
        }

        [Test]
        public void CoinSense_I_II_III_Kademe_Olcek125()
        {
            string[] ids = { "ec_gold1", "ec_gold2", "ec_gold3" };
            for (int i = 0; i < ids.Length; i++)
            {
                float s = CoinSense(ids[i]);
                Assert.Greater(s, 1f, ids[i] + " altın çarpanı > 1");
                Assert.IsTrue(TreeGoldOrbCue.IsKoban(s), ids[i]);
                Assert.AreEqual(i + 1, TreeGoldOrbCue.TierOf(s), ids[i] + " (gold " + s + ") kademe " + (i + 1));
                Assert.AreEqual(1.25f, TreeGoldOrbCue.ScaleFor(s), ids[i] + ": AD K8 ×1,25 (her kademe aynı boy)");
            }

            float cap = TreeCatalogV2.Document.goldScaleMax > 0f ? TreeCatalogV2.Document.goldScaleMax : TreeGuardrails.GoldScaleMax;
            Assert.AreEqual(3, TreeGoldOrbCue.TierOf(cap), "tavan ×" + cap + " → III");
            Assert.AreEqual(1.25f, TreeGoldOrbCue.ScaleFor(cap));
        }

        [Test]
        public void Parlama_Boyu_Sikligi_KademeyleArtar_Abartisiz()
        {
            const float d = .35f;   // a 5-gold orb (0.28 m) × 1.25
            float s1 = TreeGoldOrbCue.GlintSize(1, d), s2 = TreeGoldOrbCue.GlintSize(2, d), s3 = TreeGoldOrbCue.GlintSize(3, d);
            Assert.That(s1, Is.LessThan(s2));
            Assert.That(s2, Is.LessThan(s3));
            Assert.That(s3 / s1, Is.LessThanOrEqualTo(1.5f), "kademe farkı abartısız (boy)");
            Assert.That(s1, Is.GreaterThanOrEqualTo(TreeGoldOrbCue.SizeMinMetres));
            Assert.That(s3, Is.LessThanOrEqualTo(TreeGoldOrbCue.SizeMaxMetres));

            float p1 = TreeGoldOrbCue.GlintPeriod(1), p2 = TreeGoldOrbCue.GlintPeriod(2), p3 = TreeGoldOrbCue.GlintPeriod(3);
            Assert.That(p1, Is.GreaterThan(p2), "III daha sık parlar");
            Assert.That(p2, Is.GreaterThan(p3));
            Assert.That(p3, Is.GreaterThanOrEqualTo(.6f), "en sık parıltı bile göz kırpması değil, titreşim değil");
            Assert.AreEqual(s3, TreeGoldOrbCue.GlintSize(7, d), "kademe III'te tavanlanır");
        }

        [Test]
        public void Parlama_Rengi_OrbunRengi_FlashScaleIleKisilir_DuragandaGorunur()
        {
            Color orb = new GoldOrbTuning().Color;
            Color lin = orb.linear;
            Vector4 peak = TreeGoldOrbCue.Tint(orb, TreeGoldOrbCue.Level(1f, 1f));
            Assert.AreEqual(TreeGoldOrbCue.PeakValue, Mathf.Max(peak.x, Mathf.Max(peak.y, peak.z)), 1e-5f, "tepe değeri");
            Assert.AreEqual(lin.g / lin.r, peak.y / peak.x, 1e-4f, "renk tonu orb'un rengi (g/r)");
            Assert.AreEqual(lin.b / lin.r, peak.z / peak.x, 1e-4f, "renk tonu orb'un rengi (b/r)");
            Assert.That(TreeGoldOrbCue.PeakValue, Is.GreaterThanOrEqualTo(TreeVfxLadder.BloomThreshold), "tepe bloom eşiğinde (küçük bir 'ping')");

            float rest = TreeGoldOrbCue.Level(0f, 1f);
            Assert.AreEqual(TreeGoldOrbCue.RestLevel, rest, 1e-6f);
            Assert.That(rest, Is.GreaterThan(0f), "iki parıltı arasında da görünür (durağan kare)");
            Assert.That(TreeGoldOrbCue.PeakValue * rest, Is.LessThan(TreeVfxLadder.BloomThreshold), "dinlenmede bloom yok");
            Assert.That(TreeGoldOrbCue.PeakValue * TreeGoldOrbCue.Level(1f, .4f), Is.LessThan(TreeVfxLadder.BloomThreshold),
                "Reduce Flashes (0,4): tepe de bloom altında");
            Assert.AreEqual(.4f * TreeGoldOrbCue.Level(.7f, 1f), TreeGoldOrbCue.Level(.7f, .4f), 1e-6f, "× FlashScale");
            Assert.AreEqual(0f, TreeGoldOrbCue.Level(1f, 0f), "FlashScale 0: parıltı yok");
        }

        [Test]
        public void Twinkle_Sinirli_Periyodik_TepeVeDinlenme()
        {
            Assert.AreEqual(1f, TreeGoldOrbCue.Twinkle(TreeGoldOrbCue.PeakPhase), 1e-5f, "PeakPhase = tepe");
            Assert.AreEqual(0f, TreeGoldOrbCue.Twinkle(TreeGoldOrbCue.RestPhase), "RestPhase = dinlenme");
            Assert.AreEqual(0f, TreeGoldOrbCue.Twinkle(0f), "dönem başı 0'dan yükselir");
            for (int i = 0; i < 300; i++)
            {
                float p = i / 100f;
                float t = TreeGoldOrbCue.Twinkle(p);
                Assert.That(t, Is.InRange(0f, 1f), "phase " + p);
                Assert.AreEqual(t, TreeGoldOrbCue.Twinkle(p + 1f), 1e-4f, "periyodik (phase " + p + ")");
                Assert.That(TreeGoldOrbCue.SizePulse(t), Is.InRange(1f, 1f + TreeGoldOrbCue.SizePulseShare));
            }

            for (int seed = 1; seed < 200; seed++)
            {
                float a = TreeGoldOrbCue.SeedPhase(seed);
                Assert.That(a, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                Assert.AreEqual(a, TreeGoldOrbCue.SeedPhase(seed), "aynı paket aynı faz (global Random çekilmez)");
            }

            Assert.AreNotEqual(TreeGoldOrbCue.SeedPhase(1), TreeGoldOrbCue.SeedPhase(2), "komşu paketler aynı anda parlamaz");
        }
    }
}
