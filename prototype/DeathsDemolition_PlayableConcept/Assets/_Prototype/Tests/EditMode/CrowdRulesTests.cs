using System.Collections.Generic;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Diagnostics.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-055B · crowd identity rules (C-00 decisions, 2026-09-17): (1) AP-011B — any armoured spawn wears cw2 top and
    /// bottom (helmet-only included), the vivid cw1 share of a round stays ≥ 30 %; (2) AP-007C — the accessory slot: the
    /// A1 straw hat only on an unarmoured Standard without a mino cape, 15–25 % of the eligible Standards, never with a
    /// helmet / tier head part / T7; a library without the slot keeps the WP-016/017 draw bit-for-bit.
    /// </summary>
    [Category("WP055B")]
    public sealed class CrowdRulesTests
    {
        private static ZombieModuleLibrary.Module Mod(string id, string kind, bool colourways, bool occludesHead = false)
        {
            return new ZombieModuleLibrary.Module { id = id, kind = kind, colourways = colourways ? new[] { ZombieModuleLibrary.ColourwayDark, ZombieModuleLibrary.ColourwayFaded } : new[] { ZombieModuleLibrary.NoColourway }, variants = new ZombieModuleLibrary.Variant[0], occludesHead = occludesHead };
        }

        /// <summary>A synthetic AP-007 v003 library: 3 fits, 7 tops (incl. T6 / T7), 5 bottoms (incl. B5), 5 heads, and the accessory slot.</summary>
        private static ZombieModuleLibrary SyntheticLibrary(bool withAccessory)
        {
            var lib = ScriptableObject.CreateInstance<ZombieModuleLibrary>();
            lib.bodies = new[] { new ZombieModuleLibrary.Body { fit = "A" }, new ZombieModuleLibrary.Body { fit = "B" }, new ZombieModuleLibrary.Body { fit = "C" } };
            var mods = new List<ZombieModuleLibrary.Module>
            {
                Mod("T1_kosode_short", "top", true), Mod("T2_kosode_long", "top", true), Mod("T3_haori", "top", true), Mod("T4_vest", "top", true), Mod("T5_rags", "top", true),
                Mod(ZombieModuleLibrary.TopBareStraps, "top", false), Mod(ZombieModuleLibrary.TopMinoCape, "top", false),
                Mod("B1_hakama", "bottom", true), Mod("B2_momohiki", "bottom", true), Mod("B3_rags", "bottom", true), Mod("B4_wrap", "bottom", true), Mod(ZombieModuleLibrary.BottomFundoshi, "bottom", false),
                Mod("H1_topknot", "head", false), Mod("H2_matted", "head", false), Mod("H3_bald_scar", "head", false), Mod("H4_loose", "head", false), Mod("H5_wrap", "head", false),
            };
            if (withAccessory)
            {
                mods.Add(Mod(ZombieModuleLibrary.AccessoryStrawHat, ZombieModuleLibrary.KindAccessory, false, true));
                mods.Add(Mod(ZombieModuleLibrary.AccessoryFaceWrap, ZombieModuleLibrary.KindAccessory, true));
            }
            lib.modules = mods.ToArray();
            return lib;
        }

        [Test]
        public void Ap011B_ZirhliSpawn_Cw2UstVeAlt_KaskTekBasinaDahil()
        {
            var c = ZombieLookConstraints.Build(null, default, true, false, false);
            Assert.IsTrue(c.fadedTop && c.fadedBottom, "yalnız kask: üst + alt cw2");
            Assert.IsTrue(c.noHeadAccessory, "kask A1'i dışlar");
            c = ZombieLookConstraints.Build(null, default, false, false, true);
            Assert.IsTrue(c.fadedTop && c.fadedBottom, "yalnız bacak plakası: üst + alt cw2");
            Assert.IsTrue(c.noHeadAccessory, "her plaka A1'i dışlar (0 A1 + zırh)");
            c = ZombieLookConstraints.Build(null, default, false, false, false);
            Assert.IsTrue(c.IsEmpty, "zırhsız Standard: kısıt yok");
            c = ZombieLookConstraints.Build(null, default, false, false, false, standardTier: false);
            Assert.IsTrue(c.noHeadAccessory && !c.fadedTop, "tier baş parçası: yalnız A1 dışlanır");

            ZombieModuleLibrary lib = SyntheticLibrary(false);
            try
            {
                int cloth = 0, faded = 0;
                for (uint seed = 1; seed <= 500; seed++)
                {
                    ZombieLook look = ZombieLookSelector.Select(lib, seed, ZombieLookConstraints.Build(null, default, true, false, false));
                    if (lib.FindModule(look.top).HasColourways) { cloth++; if (look.topColourway == ZombieModuleLibrary.ColourwayFaded) faded++; }
                    if (lib.FindModule(look.bottom).HasColourways) { cloth++; if (look.bottomColourway == ZombieModuleLibrary.ColourwayFaded) faded++; }
                }
                Assert.AreEqual(cloth, faded, "kasklı spawn'ın her kumaşı cw2");
            }
            finally { Object.DestroyImmediate(lib); }
        }

        [Test]
        public void Ap007C_HasirSapka_YalnizZirhsizStandard_Pay15_25_SlotsuzKutuphaneAyni()
        {
            ZombieModuleLibrary with = SyntheticLibrary(true), without = SyntheticLibrary(false);
            try
            {
                int eligible = 0, hats = 0, wraps = 0, bad = 0, occluded = 0;
                for (uint seed = 1; seed <= 1000; seed++)
                {
                    ZombieLook plain = ZombieLookSelector.Select(without, seed);
                    ZombieLook free = ZombieLookSelector.Select(with, seed);
                    Assert.AreEqual(plain.fit + plain.top + plain.topColourway + plain.bottom + plain.bottomColourway + plain.head,
                                    free.fit + free.top + free.topColourway + free.bottom + free.bottomColourway + free.head, "aksesuar çekimi önceki sırayı bozmaz (seed " + seed + ")");
                    Assert.IsFalse(plain.HasAccessory, "slotsuz kütüphane aksesuar vermez");
                    if (free.top != ZombieModuleLibrary.TopMinoCape) eligible++;
                    if (free.accessory == ZombieModuleLibrary.AccessoryStrawHat)
                    {
                        hats++;
                        if (free.top == ZombieModuleLibrary.TopMinoCape) bad++;
                        if (ZombieLookSelector.AccessoryOccludesHead(with, in free)) occluded++;
                    }
                    if (free.accessory == ZombieModuleLibrary.AccessoryFaceWrap) wraps++;

                    // a helmet / a tier head part: never the hat, the wrap still possible
                    ZombieLook helmet = ZombieLookSelector.Select(with, seed, ZombieLookConstraints.Build(null, default, true, false, false));
                    ZombieLook tier = ZombieLookSelector.Select(with, seed, ZombieLookConstraints.Build(null, default, false, false, false, standardTier: false));
                    if (helmet.accessory == ZombieModuleLibrary.AccessoryStrawHat || tier.accessory == ZombieModuleLibrary.AccessoryStrawHat) bad++;
                }
                float share = (float)hats / eligible;
                Debug.Log("[DD-TEST] wp055b.accessory eligible=" + eligible + " hats=" + hats + " share=" + share.ToString("0.000") + " wraps=" + wraps + " occluded=" + occluded);
                Assert.AreEqual(0, bad, "A1 zırh / tier / mino ile birlikte");
                Assert.That(share, Is.InRange(ZombieLookSelector.MinStrawHatShare, ZombieLookSelector.MaxStrawHatShare), "A1 payı %15–25");
                Assert.AreEqual(hats, occluded, "A1 baş modülünü örter (occludesHead)");
                Assert.Greater(wraps, 0);
            }
            finally { Object.DestroyImmediate(with); Object.DestroyImmediate(without); }
        }

        [Test]
        public void SpawnMixSimulator_R1_12_Cw2Kurali_CanliPay30_A1Kurali()
        {
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                SpawnMixSimulator.Report rep = SpawnMixSimulator.Run(new SpawnMixSimulator.Options { config = config, seed = 3u, spawnsPerRound = 200, firstRound = 1, lastRound = 12 });
                if (!rep.lookSimulated) Assert.Ignore("no character asset set (Resources) — look rules not simulated");
                var sb = new System.Text.StringBuilder("[DD-TEST] wp055b.simulator ");
                for (int i = 0; i < rep.rounds.Length; i++)
                {
                    SpawnMixSimulator.RoundResult r = rep.rounds[i];
                    Assert.AreEqual(0, r.armouredVividCloth, "R" + r.round + ": zırhlı spawn'da cw1 kumaş");
                    Assert.GreaterOrEqual(r.vividShare, ZombieLookSelector.MinVividShare, "R" + r.round + ": canlı cw1 payı");
                    Assert.AreEqual(0, r.strawHatWithArmour + r.strawHatOffStandard + r.strawHatWithMino, "R" + r.round + ": A1 kuralı");
                    sb.Append("R").Append(r.round).Append(" vivid=").Append(r.vividShare.ToString("0.00")).Append(r.accessoryPicks > 0 ? " A1=" + r.strawHats + "/" + r.strawHatEligible : " A1=n/a").Append(' ');
                }
                Debug.Log(sb.ToString() + (rep.rounds[0].accessoryPicks == 0 ? "(accessory slot not in the shipped library yet — S-05 WP-038 import)" : ""));
                for (int v = 0; v < rep.violations.Length; v++) Assert.IsFalse(rep.violations[v].Contains("AP-011B") || rep.violations[v].Contains("AP-007C"), rep.violations[v]);
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
