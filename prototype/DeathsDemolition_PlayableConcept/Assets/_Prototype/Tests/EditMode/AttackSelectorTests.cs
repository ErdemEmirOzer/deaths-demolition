using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace DD.Prototype.Tests.EditMode
{
    /// <summary>
    /// WP-076 B1 · the attack selector, slot board and grab rules as pure functions (direction 8980fe9 §1): tier
    /// permissions, never three of one style in a row, hand alternation, the bite_grab gate, crowd slots / wind-up cap,
    /// determinism, and the signature-style normalisation (a tier's own attack keeps its DEC-021 values).
    /// </summary>
    [Category("WP076")]
    public sealed class AttackSelectorTests
    {
        [SetUp] public void SetUp() { AttackSlotBoard.ResetStatics(); GrabState.ResetStatics(); AttackSelector.Enabled = true; }
        [TearDown] public void TearDown() { AttackSlotBoard.ResetStatics(); GrabState.ResetStatics(); AttackSelector.Enabled = true; }

        private static AttackContext Ctx(EnemyTier tier, float distance, float angle = 0f, float facingAway = 0f, float speed = 3f, int last = -1, int second = -1, float now = 100f) =>
            new AttackContext { Tier = tier, Distance = distance, AngleToPlayer = angle, PlayerFacingAway = facingAway, PlayerSpeed = speed, AttackRange = 1.7f, LastStyle = last, SecondLastStyle = second, Now = now };

        private static int[] Histogram(AttackContext c, int draws, uint seed = 7u)
        {
            var h = new int[AttackStyleProfile.StyleCount];
            var r = new EnemyRandom(seed);
            for (int i = 0; i < draws; i++)
                if (AttackSelector.Select(in c, ref r, out ZombieAttackStyle s, out _)) h[(int)s]++;
            return h;
        }

        [Test]
        public void TierIzinleri_ImzaStilNormalizasyonu()
        {
            // what each tier may never use (weights 0)
            int[] special = Histogram(Ctx(EnemyTier.Special, 1.2f, facingAway: 180f, speed: 0f), 400);
            Assert.AreEqual(0, special[(int)ZombieAttackStyle.Overhead] + special[(int)ZombieAttackStyle.BiteGrab] + special[(int)ZombieAttackStyle.Sweep], "Runner: Swipe / Lunge only");
            int[] ultra = Histogram(Ctx(EnemyTier.Ultra, 1.2f, facingAway: 180f, speed: 0f), 400);
            Assert.AreEqual(0, ultra[(int)ZombieAttackStyle.Swipe] + ultra[(int)ZombieAttackStyle.Lunge] + ultra[(int)ZombieAttackStyle.BiteGrab], "Executioner: Sweep / Overhead only");
            int[] elite = Histogram(Ctx(EnemyTier.Elite, 1.2f, facingAway: 180f, speed: 0f), 400);
            Assert.AreEqual(0, elite[(int)ZombieAttackStyle.Swipe] + elite[(int)ZombieAttackStyle.Lunge], "Guard: no swipe / lunge");
            int[] eliteClose = Histogram(Ctx(EnemyTier.Elite, 0.6f, facingAway: 180f, speed: 0f), 400);
            Assert.Greater(eliteClose[(int)ZombieAttackStyle.BiteGrab], 0, "Guard may grab (inside the 0.8 m reach)");

            // the tier's own style keeps its config value; others scale from it (§1.1 examples)
            Assert.AreEqual(10f, AttackStyleProfile.Scale(AttackStyleProfile.DamageMul, EnemyTier.Standard, ZombieAttackStyle.Swipe, 10f), 1e-4f);
            Assert.AreEqual(16f, AttackStyleProfile.Scale(AttackStyleProfile.DamageMul, EnemyTier.Standard, ZombieAttackStyle.BiteGrab, 10f), 1e-4f);
            Assert.AreEqual(34f, AttackStyleProfile.Scale(AttackStyleProfile.DamageMul, EnemyTier.Elite, ZombieAttackStyle.Overhead, 18f), 1e-3f);
            Assert.AreEqual(18f, AttackStyleProfile.Scale(AttackStyleProfile.DamageMul, EnemyTier.Ultra, ZombieAttackStyle.Sweep, 34f), 1e-3f);
            Assert.AreEqual(1.15f, AttackStyleProfile.Scale(AttackStyleProfile.TelegraphMul, EnemyTier.Ultra, ZombieAttackStyle.Overhead, 1.15f), 1e-4f, "Ultra overhead telegraph unchanged (WP-052 parity)");
        }

        [Test]
        public void UcKezAyniStilYok_ElDegisir_Deterministik()
        {
            // Ultra after Overhead-Overhead must Sweep
            int[] h = Histogram(Ctx(EnemyTier.Ultra, 1.2f, last: (int)ZombieAttackStyle.Overhead, second: (int)ZombieAttackStyle.Overhead), 200);
            Assert.AreEqual(0, h[(int)ZombieAttackStyle.Overhead], "never three in a row");
            Assert.AreEqual(200, h[(int)ZombieAttackStyle.Sweep]);

            // the same hand never twice in a row
            var c = Ctx(EnemyTier.Standard, 1.0f, angle: -30f, speed: 3f, last: (int)ZombieAttackStyle.Swipe);
            c.LastSwipeMirrored = true;   // last swing was the left hand, the player is again on the left
            Assert.IsFalse(AttackSelector.SwipeHand(in c), "left twice → right");
            c.LastSwipeMirrored = false;
            Assert.IsTrue(AttackSelector.SwipeHand(in c), "player on the left → left hand");

            // determinism: same seed → same sequence
            Assert.AreEqual(Histogram(Ctx(EnemyTier.Standard, 1.0f, facingAway: 180f, speed: 0f), 300, 11u), Histogram(Ctx(EnemyTier.Standard, 1.0f, facingAway: 180f, speed: 0f), 300, 11u));
        }

        [Test]
        public void BiteGrabKapisi_ArkaDonukVeyaDuran_TekKapma()
        {
            var moving = Ctx(EnemyTier.Standard, 0.6f, facingAway: 0f, speed: 3f);
            Assert.IsFalse(AttackSelector.BiteGrabAllowed(in moving), "facing and moving → no grab");
            var back = Ctx(EnemyTier.Standard, 0.6f, facingAway: 150f, speed: 3f);
            Assert.IsTrue(AttackSelector.BiteGrabAllowed(in back), "back turned");
            var still = Ctx(EnemyTier.Standard, 0.6f, facingAway: 0f, speed: 0.4f);
            Assert.IsTrue(AttackSelector.BiteGrabAllowed(in still), "standing still");
            Assert.AreEqual(0f, AttackSelector.RangeFit(Ctx(EnemyTier.Standard, 1.2f), ZombieAttackStyle.BiteGrab), "0.8 m reach");

            // one grab on the player at a time; the second body is refused
            Assert.IsTrue(GrabState.TryBegin(1, null, EnemyTier.Standard, 0.27f));
            Assert.IsFalse(GrabState.TryBegin(2, null, EnemyTier.Standard, 0.27f));
            Assert.IsFalse(AttackSelector.BiteGrabAllowed(in back), "grab active → nobody else grabs");
            Assert.IsFalse(AttackSlotBoard.CanAcquire(ZombieAttackStyle.Lunge, 100f), "no lunge at a held player");
            GrabState.End(1, GrabEndReason.Escaped);
            Assert.IsFalse(GrabState.Active);
            Assert.AreEqual(1, GrabState.EscapedCount);
            Assert.AreEqual(1f, GrabState.BiteShareOnHit + GrabState.BiteShareOnEnd, 1e-6f, "the bite is paid in full when not escaped");
        }

        [Test]
        public void SlotTahtasi_AgirStillerTekil_WindupTavani_Yayilim()
        {
            AttackSlotBoard.Acquire(ZombieAttackStyle.Overhead, 10f);
            Assert.IsFalse(AttackSlotBoard.CanAcquire(ZombieAttackStyle.Overhead, 10f), "one overhead at a time");
            AttackSlotBoard.Commit(ZombieAttackStyle.Overhead, 10.5f);
            AttackSlotBoard.Release(ZombieAttackStyle.Overhead, false);
            Assert.IsFalse(AttackSlotBoard.CanAcquire(ZombieAttackStyle.Overhead, 11.0f), "1.2 s cooldown after a commit");
            Assert.IsTrue(AttackSlotBoard.CanAcquire(ZombieAttackStyle.Overhead, 11.8f));

            AttackSlotBoard.ResetStatics();
            AttackSlotBoard.Acquire(ZombieAttackStyle.Swipe, 20f);
            Assert.IsFalse(AttackSlotBoard.CanAcquire(ZombieAttackStyle.Swipe, 20.05f), "swipe wind-ups spread");
            Assert.IsTrue(AttackSlotBoard.CanAcquire(ZombieAttackStyle.Swipe, 20.2f));
            AttackSlotBoard.Acquire(ZombieAttackStyle.Swipe, 20.2f);
            AttackSlotBoard.Acquire(ZombieAttackStyle.Swipe, 20.4f);
            Assert.IsFalse(AttackSlotBoard.CanAcquire(ZombieAttackStyle.Swipe, 20.6f), "3 swipe slots");
            AttackSlotBoard.Acquire(ZombieAttackStyle.Sweep, 20.6f);
            Assert.AreEqual(4, AttackSlotBoard.Windups);
            Assert.IsFalse(AttackSlotBoard.CanAcquire(ZombieAttackStyle.BiteGrab, 20.7f), "total wind-up cap 4");

            // the selector respects the board (no slot → never chosen)
            AttackSlotBoard.ResetStatics();
            AttackSlotBoard.Acquire(ZombieAttackStyle.Overhead, 100f);
            int[] h = Histogram(Ctx(EnemyTier.Ultra, 1.2f), 100);
            Assert.AreEqual(0, h[(int)ZombieAttackStyle.Overhead]);
        }

        [Test]
        public void AiV3Kapali_YalnizImzaStil()
        {
            AttackSelector.Enabled = false;
            int[] h = Histogram(Ctx(EnemyTier.Standard, 1.0f, facingAway: 180f, speed: 0f), 100);
            Assert.AreEqual(100, h[(int)ZombieAttackStyle.Swipe], "-ddAiV3 0 → the WP-040 signature style");
        }
    }
}
