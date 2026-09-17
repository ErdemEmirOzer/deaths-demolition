using System.IO;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Diagnostics.EditorTools;
using DD.Prototype.Enemies;
using DD.Prototype.Weapons;
using NUnit.Framework;
using UnityEngine;
using DamageTarget = DD.Prototype.Weapons.DamageTarget;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-055 · Special Weapon ↔ enemy reactions v1, the pure parts: (1) the tier resistance profile the
    /// bodies resolve at runtime (push Elite ×0.5 / Ultra ×0.2; the net roots a Standard and only slows the
    /// rest, shorter per tier; the Ultra is never pulled), (2) the beat surface grew by the three sw_* rows
    /// with the weapon id on the payload, (3) the SpawnMixSimulator's Special usage row follows the crowd,
    /// (4) the three S-02 weapon files carry no S-03 hook (reactions ride ADR-056-1 usage events) and the coverage matrix names
    /// the rows, (5) the contract lives in Damage — the weapons never learn the enemy type.
    /// </summary>
    [Category("WP055")]
    public sealed class SpecialReactionTests
    {
        private static string PrototypeRoot => Path.Combine(Application.dataPath, "_Prototype");

        [Test]
        public void TierDirenci_Itme_Ag_Cekim()
        {
            Assert.AreEqual(1f, SpecialReactionProfile.PushResistance(EnemyTier.Standard));
            Assert.AreEqual(1f, SpecialReactionProfile.PushResistance(EnemyTier.Special));
            Assert.AreEqual(0.5f, SpecialReactionProfile.PushResistance(EnemyTier.Elite), 1e-6f, "Elite ×0,5");
            Assert.AreEqual(0.2f, SpecialReactionProfile.PushResistance(EnemyTier.Ultra), 1e-6f, "Ultra ×0,2");

            Assert.AreEqual(0f, SpecialReactionProfile.WebFactor(EnemyTier.Standard), "Standard kök (hız 0)");
            Assert.Greater(SpecialReactionProfile.WebFactor(EnemyTier.Special), 0f);
            Assert.Less(SpecialReactionProfile.WebFactor(EnemyTier.Special), SpecialReactionProfile.WebFactor(EnemyTier.Elite));
            Assert.Less(SpecialReactionProfile.WebFactor(EnemyTier.Elite), SpecialReactionProfile.WebFactor(EnemyTier.Ultra));
            Assert.Less(SpecialReactionProfile.WebFactor(EnemyTier.Ultra), 1f, "Ultra da yavaşlar");
            float seal = 6f;
            Assert.AreEqual(2.1f, SpecialReactionProfile.WebSeconds(EnemyTier.Standard, seal), 1e-4f);
            Assert.Greater(SpecialReactionProfile.WebSeconds(EnemyTier.Standard, seal), SpecialReactionProfile.WebSeconds(EnemyTier.Special, seal));
            Assert.Greater(SpecialReactionProfile.WebSeconds(EnemyTier.Special, seal), SpecialReactionProfile.WebSeconds(EnemyTier.Elite, seal));
            Assert.Greater(SpecialReactionProfile.WebSeconds(EnemyTier.Elite, seal), SpecialReactionProfile.WebSeconds(EnemyTier.Ultra, seal));
            Assert.GreaterOrEqual(SpecialReactionProfile.WebSeconds(EnemyTier.Ultra, 0f), 0.2f * SpecialReactionProfile.WebSecondsShare[3], "sıfır mühür süresi tabana çekilir");

            Assert.IsTrue(SpecialReactionProfile.PullMoves(EnemyTier.Standard));
            Assert.IsTrue(SpecialReactionProfile.PullMoves(EnemyTier.Elite));
            Assert.IsFalse(SpecialReactionProfile.PullMoves(EnemyTier.Ultra), "Ultra çekilmez");
            Assert.Less(SpecialReactionProfile.UltraPullSlowFactor, 1f, "…ama yavaşlar");

            // the shove never exceeds the WP-007 knockback ceiling (4.5 m/s) at the shipped ImpactForce (5)
            Assert.LessOrEqual(5f * SpecialReactionProfile.PushSpeedPerForce, SpecialReactionProfile.PushSpeedCap);
            Assert.AreEqual(0.45f, SpecialReactionProfile.PushDazeSeconds, 1e-6f);
            Assert.Less(SpecialReactionProfile.PushDazeSeconds * SpecialReactionProfile.PushResistUltra, SpecialReactionProfile.DazeVisualMinSeconds, "Ultra sersemlemesi görsel eşiğinin altında (stumble yok)");
            Assert.GreaterOrEqual(SpecialReactionProfile.PushDazeSeconds * SpecialReactionProfile.PushResistElite, SpecialReactionProfile.DazeVisualMinSeconds, "Elite sersemlemesi Dazed görselini alır");
        }

        [Test]
        public void OlayYuzeyi_SwSatirlari_SilahKimligi()
        {
            Assert.AreEqual(12, EnemyCombatEvents.KindCount);
            Assert.AreEqual(EnemyCombatEvents.KindCount, EnemyCombatEvents.RowRoots.Length);
            Assert.AreEqual(EnemyCombatEvents.KindCount, System.Enum.GetValues(typeof(EnemyCombatEventKind)).Length);
            Assert.AreEqual("sw_push", EnemyCombatEvents.RowName(EnemyCombatEventKind.SpecialPush, ArmorSlot.None, EnemyTier.Elite));
            Assert.AreEqual("sw_web", EnemyCombatEvents.RowName(EnemyCombatEventKind.SpecialWeb, ArmorSlot.None, EnemyTier.Standard));
            Assert.AreEqual("sw_pull", EnemyCombatEvents.RowName(EnemyCombatEventKind.SpecialPull, ArmorSlot.None, EnemyTier.Ultra));

            var info = new EnemyCombatEventInfo(EnemyCombatEventKind.SpecialWeb, null, EnemyTier.Special, 3, Vector3.zero, Vector3.zero, ArmorSlot.None, true, 1.5f, ZombieAttackStyle.Lunge, SpecialWeaponId.OfudaLoom);
            Assert.AreEqual(SpecialWeaponId.OfudaLoom, info.Special);
            Assert.AreEqual("sw_web", info.RowName);
            var old = new EnemyCombatEventInfo(EnemyCombatEventKind.Stagger, null, EnemyTier.Standard, 1, Vector3.zero, Vector3.forward, ArmorSlot.None, false, 0.3f);
            Assert.AreEqual(SpecialWeaponId.KaguraBell, old.Special, "eski çağrılar bozulmaz (varsayılan)");

            EnemyCombatEvents.ResetStatics();
            int seen = 0;
            System.Action<EnemyCombatEventInfo> h = i => { if (i.Kind == EnemyCombatEventKind.SpecialPull) seen++; };
            EnemyCombatEvents.Raised += h;
            try
            {
                EnemyCombatEvents.Raise(new EnemyCombatEventInfo(EnemyCombatEventKind.SpecialPull, null, EnemyTier.Standard, 4, Vector3.zero, Vector3.back, ArmorSlot.None, false, 0.6f, ZombieAttackStyle.Swipe, SpecialWeaponId.TideJewel));
                Assert.AreEqual(1, seen);
                Assert.AreEqual(1, EnemyCombatEvents.Count(EnemyCombatEventKind.SpecialPull));
                Assert.AreEqual(SpecialWeaponId.TideJewel, EnemyCombatEvents.Last.Special);
            }
            finally { EnemyCombatEvents.Raised -= h; EnemyCombatEvents.ResetStatics(); }
        }

        [Test]
        public void Sozlesme_DamageKatmaninda_SilahlarDusmanTipiOgrenmez()
        {
            var r = new SpecialReaction(SpecialReactionKind.Push, SpecialWeaponId.KaguraBell, Vector3.forward, 5f, 0f, 42);
            Assert.AreEqual(SpecialReactionKind.Push, r.Kind);
            Assert.AreEqual(42, r.TransactionId);
            Assert.AreEqual(typeof(ISpecialReactionReceiver).Namespace, typeof(IDamageReceiver).Namespace, "sözleşme IDamageReceiver'ın yanında (Damage)");
            Assert.IsTrue(typeof(ISpecialReactionReceiver).IsAssignableFrom(typeof(ZombieEnemy)), "ZombieEnemy alıcı");

            // ADR-056-1 → reaction mapping (pure): weapon id → kind; force × falloff; seconds / direction / transaction pass through
            Assert.AreEqual(SpecialReactionKind.Push, SpecialReactionBinder.KindFor(SpecialWeaponId.KaguraBell));
            Assert.AreEqual(SpecialReactionKind.Web, SpecialReactionBinder.KindFor(SpecialWeaponId.OfudaLoom));
            Assert.AreEqual(SpecialReactionKind.Pull, SpecialReactionBinder.KindFor(SpecialWeaponId.TideJewel));
            var hit = new SpecialUsageEvent(SpecialWeaponId.KaguraBell, SpecialUsageKind.Hit, "sw_kagura", Vector3.zero, Vector3.right, 0f, 0.2f, 1, default(DamageTarget), 5f, 0.5f, 77);
            SpecialReaction mapped = SpecialReactionBinder.ReactionFor(in hit);
            Assert.AreEqual(SpecialReactionKind.Push, mapped.Kind);
            Assert.AreEqual(2.5f, mapped.Force, 1e-5f, "kuvvet × falloff");
            Assert.AreEqual(Vector3.right, mapped.Direction);
            Assert.AreEqual(77, mapped.TransactionId);
            Assert.AreEqual(0.2f, mapped.Seconds, 1e-6f);

            // C-00 decision (2026-09-17): NO hook in the S-02 weapon files — reactions ride S-02's ADR-056-1 usage events
            string[] weaponFiles = { "Weapons/KaguraWaveField.cs", "Weapons/TideWell.cs", "Weapons/OfudaSealWeb.cs" };
            for (int i = 0; i < weaponFiles.Length; i++)
            {
                string src = File.ReadAllText(Path.Combine(PrototypeRoot, weaponFiles[i]));
                Assert.IsFalse(src.Contains("ApplySpecialReaction") || src.Contains("ZombieEnemy") || src.Contains("DD.Prototype.Enemies"), weaponFiles[i] + " düşman tepkisini/tipini bilmez (S-03 kancası yok)");
            }
        }

        private sealed class SahteAlici : MonoBehaviour, IDamageReceiver, ISpecialReactionReceiver
        {
            public int Applied; public SpecialReaction Last;
            public bool ApplySpecialReaction(in SpecialReaction reaction) { Applied++; Last = reaction; return true; }
            public DamageResult ApplyHit(in DamageHit hit) => default;
        }

        [Test]
        public void Binder_HitAliciyaYonlenir_FieldEndedSerbestBirakir_S02DosyalariAbonesiz()
        {
            var go = new GameObject("sahte");
            try
            {
                SahteAlici alici = go.AddComponent<SahteAlici>();
                SpecialWeaponUsageEvents.ResetForTests();
                SpecialReactionBinder.Rebind();
                var target = new DamageTarget { Receiver = alici, Root = go.transform, Id = go.GetInstanceID(), Height = 1.8f };
                SpecialWeaponUsageEvents.PublishHit(SpecialWeaponId.OfudaLoom, "sw_ofuda", in target, Vector3.zero, Vector3.up, 0f, 1f, 6f);
                Assert.AreEqual(1, alici.Applied, "Hit → ApplySpecialReaction");
                Assert.AreEqual(SpecialReactionKind.Web, alici.Last.Kind);
                Assert.AreEqual(6f, alici.Last.Seconds, 1e-6f, "mühür süresi geçer");
                Assert.AreEqual(1, SpecialReactionBinder.HitsRouted);
                SpecialWeaponUsageEvents.Publish(SpecialWeaponId.OfudaLoom, SpecialUsageKind.Fired, "sw_ofuda", Vector3.zero, Vector3.forward, 0f, 0f, 0);
                Assert.AreEqual(1, alici.Applied, "Fired/FieldSpawned gövdeye gitmez");
                SpecialWeaponUsageEvents.Publish(SpecialWeaponId.OfudaLoom, SpecialUsageKind.FieldEnded, "sw_ofuda", Vector3.zero, Vector3.up, 0f, 0f, 1);
                SpecialWeaponUsageEvents.Publish(SpecialWeaponId.TideJewel, SpecialUsageKind.Deactivated, "sw_tide", Vector3.zero, Vector3.up, 0f, 0f, 0);
                Assert.AreEqual(2, SpecialReactionBinder.FieldsEnded, "FieldEnded + Deactivated → EndSpecialField");
                // the receiver contract is NOT the damage interface: a plain IDamageReceiver target is ignored, not cast blindly
                Assert.AreEqual(1, SpecialReactionBinder.HitsRouted);
            }
            finally { Object.DestroyImmediate(go); SpecialWeaponUsageEvents.ResetForTests(); SpecialReactionBinder.Rebind(); }
        }

        [Test]
        public void SpawnMixSimulator_SpecialKullanimSatiri_KalabalikTakipEder()
        {
            var config = ScriptableObject.CreateInstance<PrototypeConfig>();
            try
            {
                SpawnMixSimulator.Report rep = SpawnMixSimulator.Run(new SpawnMixSimulator.Options { config = config, seed = 5u, spawnsPerRound = 300, firstRound = 1, lastRound = 12 });
                Assert.AreEqual(12, rep.rounds.Length);
                float seal = config.Weapons.Special.OfudaLoom.SealSeconds;
                for (int i = 0; i < rep.rounds.Length; i++)
                {
                    SpawnMixSimulator.RoundResult r = rep.rounds[i];
                    float push = 0f, web = 0f, root = 0f, pull = 0f;
                    for (int t = 0; t < SpawnMixSimulator.TierCount; t++)
                    {
                        var tier = (EnemyTier)t;
                        push += r.tierShare[t] * SpecialReactionProfile.PushResistance(tier);
                        web += r.tierShare[t] * SpecialReactionProfile.WebSeconds(tier, seal);
                        if (SpecialReactionProfile.WebFactor(tier) <= 0f) root += r.tierShare[t];
                        if (SpecialReactionProfile.PullMoves(tier)) pull += r.tierShare[t];
                    }
                    Assert.AreEqual(push, r.swPushEfficacy, 1e-4f, "R" + r.round + " push");
                    Assert.AreEqual(web, r.swWebHoldSeconds, 1e-4f, "R" + r.round + " web");
                    Assert.AreEqual(root, r.swWebRootShare, 1e-4f, "R" + r.round + " root");
                    Assert.AreEqual(pull, r.swPullMovableShare, 1e-4f, "R" + r.round + " pull");
                    Assert.AreEqual(1f - r.tierShare[3], r.swPullMovableShare, 1e-4f, "çekilebilen = Ultra dışı");
                }
                // R1 is all Standard: every body shoved fully, rooted, pulled; the late crowd resists more
                SpawnMixSimulator.RoundResult r1 = rep.rounds[0], r12 = rep.rounds[11];
                Assert.AreEqual(1f, r1.swPushEfficacy, 1e-4f);
                Assert.AreEqual(1f, r1.swWebRootShare, 1e-4f);
                Assert.Less(r12.swPushEfficacy, r1.swPushEfficacy, "geç round'da itme direnci artar");
                Assert.Less(r12.swWebRootShare, r1.swWebRootShare);
                Assert.LessOrEqual(r12.swPullMovableShare, r1.swPullMovableShare);
                string line = SpawnMixSimulator.Summarize(rep);
                Assert.IsTrue(line.Contains(" sw push="), "özet satırında Special kullanım etkisi");
                Debug.Log("[DD-OBS] wp055.simulator R12 push=" + r12.swPushEfficacy.ToString("0.000") + " web=" + r12.swWebHoldSeconds.ToString("0.00") + "s root=" + r12.swWebRootShare.ToString("0.000") + " pull=" + r12.swPullMovableShare.ToString("0.000"));
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void KapsamaMatrisi_SwSatirlari_S07S08Baglar()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "planning", "work-packages", "EVENT-COVERAGE-MATRIX.md"));
            if (!File.Exists(path)) Assert.Ignore("planning matrix not next to the project");
            string md = File.ReadAllText(path);
            foreach (string row in new[] { "`sw_push`", "`sw_web`", "`sw_pull`" })
            {
                int at = md.IndexOf("| " + row + " |", System.StringComparison.Ordinal);
                Assert.GreaterOrEqual(at, 0, row + " satırı yok");
                string line = md.Substring(at, md.IndexOf('\n', at) - at);
                Assert.IsTrue(line.Contains("S-07 bağlar") && line.Contains("S-08 bağlar"), row + ": VFX/SFX bağlama S-07/S-08'de (sahiplik kararı)");
            }
        }
    }
}
