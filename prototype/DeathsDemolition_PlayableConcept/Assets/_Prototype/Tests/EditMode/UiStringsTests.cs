using System.IO;
using DD.Prototype.Core.Weapons;
using DD.Prototype.UI;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// M4-R1 · the R1 content pack at run time (UiStrings) and the Selection StatStrip (WeaponStatReadout).
    /// </summary>
    public sealed class UiStringsTests
    {
        private static string RepoRoot
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..")); }
        }

        [SetUp]
        public void Reset()
        {
            UiStrings.ResetForTests();
            WeaponStatReadout.ResetForTests();
        }

        [Test]
        public void KaynakKopyasi_PlanlamaDosyasiylaBaytBayt()
        {
            string planning = Path.Combine(RepoRoot, "planning", "content", "r1-strings.json");
            string shipped = Path.Combine(Application.dataPath, "_Prototype", "UI", "Resources", "Strings", "r1-strings.json");
            Assert.IsTrue(File.Exists(planning), planning);
            Assert.IsTrue(File.Exists(shipped), shipped);
            CollectionAssert.AreEqual(File.ReadAllBytes(planning), File.ReadAllBytes(shipped),
                "UI/Resources/Strings/r1-strings.json is a copy of planning/content/r1-strings.json - copy it again after a pack change");
        }

        [Test]
        public void Tablo_TamOkunur_AnahtarSayisiJsonIleAyni()
        {
            string planning = Path.Combine(RepoRoot, "planning", "content", "r1-strings.json");
            string json = File.ReadAllText(planning);
            int keys = 0;
            foreach (string line in File.ReadAllLines(planning))
            {
                if (line.StartsWith("  \"") && line.TrimEnd().EndsWith("{"))
                {
                    keys++;
                }
            }

            Assert.Greater(keys, 400);
            Assert.AreEqual(keys, UiStrings.Parse(json).Count, "the parser reads every row");
            Assert.AreEqual(keys, UiStrings.Count, "Resources.Load reads the same table");
            Assert.AreEqual("Hinawa", UiStrings.Get("weapon.ar_1.name"));
            Assert.AreEqual("BURST ×{count}", UiStrings.Get("weapon.ar_2.firemode"));
            Assert.AreEqual("fallback", UiStrings.Get("no.such.key", "fallback"), "a gap reads as the caller's copy, never the key");
            Assert.AreEqual("PATH · 120 Ash · L{level}", UiStrings.Format("path.lock.line", "kul", "120"), "C-00 25.09: en Ash");
        }

        [Test]
        public void HerKlasikSilah_PaketMetinleri_VeDortStat()
        {
            int seen = 0;
            foreach (WeaponDefinition def in Arsenal.All)
            {
                if (def == null || def.slot != WeaponSlotKind.Classic)
                {
                    continue;
                }

                seen++;
                foreach (string field in new[] { "name", "class", "firemode", "role", "desc", "flavor" })
                {
                    Assert.IsTrue(UiStrings.Has("weapon." + def.weaponId + "." + field), def.weaponId + "." + field);
                }

                var stats = new WeaponStat[4];
                WeaponStatReadout.Fill(def, stats);
                CollectionAssert.AreEqual(WeaponStatReadout.StripOf(def.weaponId), new[] { stats[0].Key, stats[1].Key, stats[2].Key, stats[3].Key });
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreNotEqual(stats[i].Key, stats[i].Label, def.weaponId + ": " + stats[i].Key + " has a pack label");
                    Assert.IsFalse(string.IsNullOrEmpty(stats[i].Value) || stats[i].Value == "—", def.weaponId + ": " + stats[i].Key + " prints a number");
                    Assert.IsFalse(stats[i].Value.Contains("{"), def.weaponId + ": " + stats[i].Value + " is formatted");
                    Assert.LessOrEqual(stats[i].Bar01, 1f);
                }
            }

            Assert.GreaterOrEqual(seen, 10, "the ten classic weapons (WP-069)");
        }

        [Test]
        public void StatCubugu_RosterEnIyisiDolu_AzIyiOlanlarTers()
        {
            float bestRpm = 0f, bestReload = float.MaxValue;
            WeaponDefinition fastest = null, quickest = null;
            foreach (WeaponDefinition def in Arsenal.All)
            {
                if (def == null || !def.enabled || def.slot != WeaponSlotKind.Classic)
                {
                    continue;
                }

                if (def.fire.rpm > bestRpm)
                {
                    bestRpm = def.fire.rpm;
                    fastest = def;
                }

                if (def.ammo.reloadTacSeconds > 0f && def.ammo.reloadTacSeconds < bestReload)
                {
                    bestReload = def.ammo.reloadTacSeconds;
                    quickest = def;
                }
            }

            Assert.IsNotNull(fastest);
            Assert.AreEqual(1f, WeaponStatReadout.Read(fastest, WeaponStatReadout.FireRate).Bar01, 1e-4f, "the roster's fastest fills its bar");
            Assert.AreEqual(1f, WeaponStatReadout.Read(quickest, WeaponStatReadout.Reload).Bar01, 1e-4f, "the quickest reload fills its bar (less is better)");
        }
    }
}
