#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Damage;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-004 · test-only gold / upgrade-loop probe for the real Player.
    ///
    /// Drives one real run through the public flow API and records what the manual
    /// checklist asks for, in the Player rather than the Editor: one packet per kill at
    /// the death point, a packet that stays on the ground, the magnet pull and the single
    /// credit, one packet per enemy under many kills, packets left on the floor being
    /// swept when the round completes (last kill included) BEFORE the upgrade screen
    /// opens with that balance, a frozen world under the screen, an atomic purchase that
    /// changes a measurable weapon/player property, gold and build carried into the next
    /// round, pause freezing a magnetizing packet, death wiping gold and build, and a
    /// second run opening clean with one service - with the profile's XP moving only by
    /// the run summary's XP. Nothing here changes product behaviour: it calls the same
    /// public methods the screens and the F1 panel call and reads public state. It also
    /// writes screenshots at the visible moments as manual-kit evidence.
    ///
    /// Compiled ONLY into development builds and the Editor (file-level #if); a release
    /// Player contains none of it, and without <c>-ddGoldProbe</c> a development Player
    /// allocates nothing either.
    ///
    /// Command line:
    ///   -ddGoldProbe &lt;outDir&gt;      enable; write stage JSON files + screenshots into outDir, then quit
    ///   -ddProfilePath &lt;file&gt;      redirect the profile file (keeps the real profile untouched)
    /// </summary>
    public static class GoldProbe
    {
        private const string OutFlag = "-ddGoldProbe";
        private const string ProfileFlag = "-ddProfilePath";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            if (string.IsNullOrEmpty(Arg(OutFlag)))
            {
                return;
            }

            string profilePath = Arg(ProfileFlag);
            if (!string.IsNullOrEmpty(profilePath))
            {
                ProfileStore.SetPathOverride(profilePath);
                Debug.Log("[GoldProbe] profile path override -> " + profilePath);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string outDir = Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir))
            {
                return;
            }

            var carrier = new GameObject("GoldProbe");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().Configure(outDir);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        [Serializable]
        public sealed class Stage
        {
            public string stage;
            public string utc;
            public string scene;
            public bool pass;
            public string note;
            public string kit;

            public int runId;
            public int runRound;
            public string runPhase;
            public string roundState;
            public float timeScale;

            public int walletRunId;
            public bool walletOpen;
            public int balance;
            public int totalEarned;
            public int earnedByPickup;
            public int earnedByAutoCollect;
            public int totalSpent;
            public int appliedTransactions;

            public int packetsLive;
            public int packetsOutstanding;
            public int packetsSpawned;
            public int packetsCollected;
            public int packetsAutoCollected;
            public int packetsLost;
            public int fallbackCredits;
            public int creditRefusals;
            public int ledgerGranted;
            public int ledgerRejected;

            public int sweepRound;
            public int sweepPackets;
            public int sweepAmount;
            public int balanceAtOpen;
            public int sweptPacketsAtOpen;

            public string build;
            public int purchaseCount;
            public int totalGoldSpent;

            public int goldServiceCount;
            public int upgradeScreenCount;
            public int runDirectorCount;

            public int profileXp;
            public int profileLevel;
            public string screenshot;
            public string detail;
        }

        private sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeout = 30f;
            private string _outDir;
            private readonly List<Stage> _stages = new List<Stage>();
            private int _credits;
            private int _creditAmount;
            private GoldWallet _boundWallet;

            internal void Configure(string outDir)
            {
                _outDir = outDir;
            }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_outDir);

                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return null;
                int xpBefore = ProfileService.Current.TotalXpEarned;
                int levelBefore = ProfileService.Current.ProfileLevel;

                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && GoldService.Instance != null &&
                                             PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting");

                RunDirector run = RunDirector.Instance;
                EnemySpawnDirector dir = EnemySpawnDirector.Instance;
                GoldService gold = GoldService.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                WeaponController weapon = WeaponRegistry.ActiveController;
                int standard = PrototypeConfig.Active != null ? PrototypeConfig.Active.GoldReward(EnemyTier.Standard) : 0;
                rig.Health.DiagnosisInvulnerable = true;
                BindWallet(gold.Wallet);

                // Controlled floor: no director spawns, no bodies. The probe places its own.
                SetSpawning(dir, false);
                dir.ClearAllEnemies();
                EnemyFactory.ClearAll();
                gold.ClearOrbs(false);
                yield return null;

                // ---------------------------------------------------------------- kit 1: one kill, one packet at the death point
                Vector3 killPos = rig.transform.position + new Vector3(12f, 0f, 0f);
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, run.Round, killPos, Quaternion.identity);
                int lifeId = z != null ? z.LifeId : 0;
                // WP-008: a Standard may roll armour and then pays base + armour bonus; this
                // probe is about the packet mechanics, so it strips the roll (test seam).
                if (z != null) z.FitArmorComposition(false, false, false);
                if (z != null) z.Kill();
                yield return null;
                GoldOrb packet = OnlySpawnedPacket();
                Vector3 d = packet != null ? packet.transform.position - killPos : Vector3.one * 99f; d.y = 0f;
                Stage s1 = Capture("01-single-kill-single-packet", run, gold, "1");
                s1.pass = gold.PacketsSpawned == 1 && gold.LiveOrbCount == 1 && packet != null && d.magnitude < 2f && gold.FallbackCredits == 0 &&
                          gold.Ledger.Granted == 1 && lifeId > gold.Ledger.LifeWatermark && gold.Wallet.Balance == 0;
                s1.detail = "lifeId=" + lifeId + " watermark=" + gold.Ledger.LifeWatermark + " packetOffset=" + d.magnitude.ToString("F2") + " amount=" + (packet != null ? packet.Amount : -1);
                s1.note = "one packet for one Standard kill, at the death point, no credit yet";
                yield return Shot(s1, "01-packet.png");
                Write(s1);

                // ---------------------------------------------------------------- kit 2: it stays (5 s real, player 12 m away)
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 5f) yield return null;
                Stage s2 = Capture("02-packet-stays", run, gold, "2");
                s2.pass = gold.LiveOrbCount == 1 && packet != null && packet.IsSpawned && packet.Packet.State == GoldPacketState.Waiting && gold.Wallet.Balance == 0;
                s2.detail = "state=" + (packet != null ? packet.Packet.State.ToString() : "gone") + " after 5 s";
                s2.note = "packet still on the ground after 5 s, no expiry, no credit";
                Write(s2);

                // ---------------------------------------------------------------- kit 3: magnet, single credit
                ResetCredits();
                Vector3 near = packet.transform.position + new Vector3(-(gold.MagnetRadius - 0.8f), 0f, 0f); near.y = rig.transform.position.y;
                rig.Teleport(near, Quaternion.identity);
                yield return WaitUntil(() => _credits > 0 || Time.realtimeSinceStartup - t0 > 12f, "magnet credit");
                for (int i = 0; i < 30; i++) yield return null;
                Stage s3 = Capture("03-magnet-single-credit", run, gold, "3");
                s3.pass = _credits == 1 && _creditAmount == standard && gold.Wallet.Balance == standard && gold.Wallet.EarnedByPickup == standard &&
                          gold.PacketsCollected == 1 && gold.LiveOrbCount == 0 && gold.CreditRefusals == 0;
                s3.detail = "credits=" + _credits + " amount=" + _creditAmount;
                s3.note = "player entered the magnet radius: packet pulled in, credited exactly once, returned to the pool";
                Write(s3);

                // ---------------------------------------------------------------- kit 4: many kills, one packet each
                int before = gold.PacketsSpawned;
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI * 2f / 6f;
                    ZombieEnemy zz = EnemyFactory.Spawn(EnemyTier.Standard, run.Round, rig.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 11f, Quaternion.identity);
                    if (zz != null) zz.Kill();
                }

                yield return null;
                Stage s4 = Capture("04-six-kills-six-packets", run, gold, "4");
                s4.pass = gold.PacketsSpawned - before == 6 && gold.LiveOrbCount == 6 && gold.FallbackCredits == 0 && gold.Ledger.Rejected == 0;
                s4.detail = "spawnedDelta=" + (gold.PacketsSpawned - before);
                s4.note = "six kills in one frame: six packets, no direct-credit fallback";
                yield return Shot(s4, "04-six-packets.png");
                Write(s4);
                EnemyFactory.ClearAll();
                gold.ClearOrbs(true);   // the six kit-4 packets are taken (credited) so the round below starts on a clean floor
                yield return null;
                // The taken packets fly (unscaled 0.35 s) before they credit; the round's own
                // income is measured from AFTER they landed.
                yield return WaitUntil(() => gold.LiveOrbCount == 0, "kit-4 packets landing");

                // ---------------------------------------------------------------- kit 5-7: leave them, complete the round, sweep
                SetSpawning(dir, true);
                int spawnedBeforeRound = gold.PacketsSpawned;
                int collectedBeforeRound = gold.PacketsCollected;
                int earnedBeforeRound = gold.Wallet.TotalEarned;
                int autoBeforeRound = gold.Wallet.EarnedByAutoCollect;
                run.BeginRound(1);
                yield return null;
                int cleared = 0; float tsAtClear = -1f; int balAtClear = -1; int outstandingAtClear = -1;
                Action<int> onCleared = r => { cleared++; tsAtClear = Time.timeScale; balAtClear = gold.Wallet.Balance; outstandingAtClear = gold.OutstandingPacketCount; };
                RunEvents.RoundCompleted += onCleared;
                int pool = dir.RoundPoolTotal;
                int maxFloor = 0;
                t0 = Time.realtimeSinceStartup;
                int killed = 0;
                int roundReward = 0;   // WP-008: director spawns may be armoured; income = sum of spawn snapshots
                Action<EnemyDeathInfo> onDied = info => roundReward += info.GoldReward;
                DamageEvents.EnemyDied += onDied;
                while (cleared == 0 && Time.realtimeSinceStartup - t0 < 90f)
                {
                    killed += KillAllLiving();
                    maxFloor = Mathf.Max(maxFloor, gold.OutstandingPacketCount);
                    if (maxFloor >= 4 && _stages.Count == 4)
                    {
                        Stage sFloor = Capture("05-packets-left-on-floor", run, gold, "5");
                        sFloor.pass = gold.OutstandingPacketCount >= 4 && run.Phase == RunPhase.Fighting;
                        sFloor.note = "mid-round: packets accumulate on the floor while the player stands still";
                        yield return Shot(sFloor, "05-floor.png");
                        Write(sFloor);
                    }

                    yield return null;
                }

                RunEvents.RoundCompleted -= onCleared;
                DamageEvents.EnemyDied -= onDied;
                // WP-095 K5 (C-00 08.10): top-down with the last-zombie slow motion on, the round-end freeze waits for that beat
                // (≤ roundEndHoldSafetySeconds) — frozen at the clear OR within the safety window
                float freezeWait0 = Time.realtimeSinceStartup;
                float safety = DD.Prototype.Feedback.FeelProfileTD.Active.roundEndHoldSafetySeconds + 0.2f;
                while (tsAtClear != 0f && !RunFlowClock.IsFrozen && Time.realtimeSinceStartup - freezeWait0 < safety) yield return null;
                bool frozenInTime = tsAtClear == 0f || RunFlowClock.IsFrozen;
                GoldAutoCollectReport sweep = gold.LastAutoCollect;
                Stage s6 = Capture("06-round-complete-sweep", run, gold, "6,7");
                int roundPackets = gold.PacketsSpawned - spawnedBeforeRound;
                int roundManual = gold.PacketsCollected - collectedBeforeRound;
                s6.pass = cleared == 1 && killed == pool && roundPackets == pool && frozenInTime && outstandingAtClear == 0 &&
                          sweep.Round == 1 && sweep.Packets == roundPackets - roundManual && sweep.Amount == gold.Wallet.EarnedByAutoCollect - autoBeforeRound &&
                          roundReward >= pool * standard && gold.Wallet.TotalEarned - earnedBeforeRound == roundReward &&
                          balAtClear == gold.Wallet.Balance && gold.FallbackCredits == 0 && gold.CreditRefusals == 0;
                s6.detail = "pool=" + pool + " killed=" + killed + " maxFloor=" + maxFloor + " sweep=" + sweep.Packets + "/" + sweep.Amount + "g balanceAtClear=" + balAtClear +
                            " roundReward=" + roundReward + " (>= " + pool * standard + ") timeScaleAtClear=" + tsAtClear + " frozenInTime=" + frozenInTime + " outstandingAtClear=" + outstandingAtClear;
                s6.note = "round completed once; every packet left on the floor (last kill included) swept and credited in the RoundCleared frame, world frozen";
                Write(s6);
                yield return WaitUntil(() => gold.LiveOrbCount == 0, "sweep flight");

                // ---------------------------------------------------------------- kit 8/16: screen opens with the swept balance, world frozen
                yield return WaitUntil(() => run.Phase == RunPhase.Upgrading, "upgrade screen");
                int spawnedAtOpen = dir.Population.SpawnedTotal;
                for (int i = 0; i < 40; i++) yield return null;
                Stage s8 = Capture("08-upgrade-screen-balance", run, gold, "8,16");
                s8.pass = run.Shop.IsOpen && run.Shop.Gold == gold.Wallet.Balance && run.LastRoundEnd.BalanceAtOpen == gold.Wallet.Balance &&
                          run.LastRoundEnd.SweptPackets == sweep.Packets && Time.timeScale == 0f && RunFlowClock.IsFrozen &&
                          dir.Population.SpawnedTotal == spawnedAtOpen && RunEvents.Current.Gold == gold.Wallet.Balance;
                s8.detail = "shopGold=" + run.Shop.Gold + " balanceAtOpen=" + run.LastRoundEnd.BalanceAtOpen + " sweptAtOpen=" + run.LastRoundEnd.SweptPackets +
                            " session=" + run.Shop.SessionId + " cards=" + run.Shop.Cards.Count;
                s8.note = "upgrade screen open on the post-sweep balance; simulation frozen, no spawn";
                yield return Shot(s8, "08-upgrade-screen.png");
                Write(s8);

                // ---------------------------------------------------------------- kit 9-11: purchase, balance drops, weapon/player property changes
                int cardIndex = -1;
                for (int i = 0; i < run.Shop.Cards.Count; i++) { if (run.Shop.Cards[i].IsValid) { cardIndex = i; break; } }
                UpgradeCardOffer card = cardIndex >= 0 ? run.Shop.Cards[cardIndex] : default;
                if (cardIndex >= 0 && gold.Wallet.Balance < card.Cost)
                {
                    gold.Wallet.Credit(card.Cost, rig.transform.position);   // untagged top-up; the kit has the player earn it, the probe cannot wait for that
                }

                int balBefore = gold.Wallet.Balance;
                float measureBefore = Measure(card.Branch, weapon, rig);
                int tierBefore = weapon.Modifiers.GetTier(card.Branch);
                bool bought = cardIndex >= 0 && run.Shop.TryPurchase(cardIndex, run.Shop.SessionId);
                yield return null;
                float measureAfter = Measure(card.Branch, weapon, rig);
                bool doubleClick = cardIndex >= 0 && run.Shop.TryPurchase(cardIndex, run.Shop.SessionId);
                Stage s9 = Capture("09-purchase-atomic", run, gold, "9,10,11");
                s9.pass = bought && !doubleClick && gold.Wallet.Balance == balBefore - card.Cost && weapon.Modifiers.GetTier(card.Branch) == tierBefore + 1 &&
                          !Mathf.Approximately(measureBefore, measureAfter) && run.Shop.Gold == gold.Wallet.Balance && run.Build.PurchaseCount == 1;
                s9.detail = "card=" + card.Branch + " cost=" + card.Cost + " balance " + balBefore + "->" + gold.Wallet.Balance + " tier " + tierBefore + "->" +
                            weapon.Modifiers.GetTier(card.Branch) + " measure " + measureBefore.ToString("0.###") + "->" + measureAfter.ToString("0.###") +
                            " result=" + run.Shop.LastResult + " build=" + run.Build.DescribeBuild();
                s9.note = "one purchase: gold down by the cost once, tier up once, measurable weapon/player property changed, double click refused";
                yield return Shot(s9, "09-purchased.png");
                Write(s9);

                // ---------------------------------------------------------------- kit 12/13: next round keeps gold and build
                int balR1 = gold.Wallet.Balance; string buildR1 = run.Build.DescribeBuild(); int sessionR1 = run.Shop.SessionId;
                run.CloseUpgradeScreen();
                yield return WaitUntil(() => run.Phase == RunPhase.Fighting && run.Round == 2, "round 2");
                for (int i = 0; i < 10; i++) yield return null;
                bool stale = run.Shop.TryPurchase(0, sessionR1);
                Stage s12 = Capture("12-next-round-keeps-gold-build", run, gold, "12,13");
                s12.pass = run.Round == 2 && gold.Wallet.Balance == balR1 && run.Build.DescribeBuild() == buildR1 && !stale && !RunFlowClock.IsFrozen &&
                           gold.Wallet.RunId == run.RunId;
                s12.detail = "balance=" + gold.Wallet.Balance + " build=" + run.Build.DescribeBuild() + " staleSessionPurchase=" + stale;
                s12.note = "round 2: gold and build carried over; the previous screen's session cannot buy";
                Write(s12);

                // ---------------------------------------------------------------- kit 17: pause with a magnetizing packet
                SetSpawning(dir, false);
                dir.ClearAllEnemies();
                yield return null;
                Vector3 pPos = rig.transform.position + new Vector3(10f, 0f, 0f);
                ZombieEnemy pz = EnemyFactory.Spawn(EnemyTier.Standard, run.Round, pPos, Quaternion.identity);
                if (pz != null) pz.FitArmorComposition(false, false, false);   // WP-008: bare Standard = flat value
                if (pz != null) pz.Kill();
                yield return null;
                GoldOrb pk = OnlySpawnedPacket();
                yield return WaitUntil(() => pk == null || pk.Packet.State == GoldPacketState.Waiting, "landing");
                Vector3 pn = pk.transform.position + new Vector3(-(gold.MagnetRadius - 0.4f), 0f, 0f); pn.y = rig.transform.position.y;
                rig.Teleport(pn, Quaternion.identity);
                yield return WaitUntil(() => pk.IsAttracted, "attract");
                RunFlowClock.AddHold(RunFlowClock.PauseHold);
                yield return null;
                Vector3 frozenPos = pk.transform.position; int balPause = gold.Wallet.Balance; bool frozenOk = true; int pauseFrames = 0;
                t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 1f)
                {
                    frozenOk &= (pk.transform.position - frozenPos).sqrMagnitude < 1e-8f && gold.Wallet.Balance == balPause && Time.deltaTime <= 0f;
                    pauseFrames++;
                    yield return null;
                }

                RunFlowClock.RemoveHold(RunFlowClock.PauseHold);
                yield return WaitUntil(() => gold.Wallet.Balance > balPause, "resume credit");
                Stage s17 = Capture("17-pause-freezes-magnet", run, gold, "17");
                s17.pass = frozenOk && pauseFrames > 10 && gold.Wallet.Balance == balPause + standard && !RunFlowClock.IsFrozen;
                s17.detail = "pauseFrames=" + pauseFrames + " frozen=" + frozenOk + " balance " + balPause + "->" + gold.Wallet.Balance;
                s17.note = "pause: magnetizing packet and wallet frozen; resume: collected once";
                Write(s17);

                // ---------------------------------------------------------------- kit 14: death resets gold and build
                ZombieEnemy dz = EnemyFactory.Spawn(EnemyTier.Standard, run.Round, rig.transform.position + new Vector3(12f, 0f, 0f), Quaternion.identity);
                if (dz != null) dz.Kill();
                yield return null;
                int floorBeforeDeath = gold.OutstandingPacketCount;   // gold still on the floor (a packet mid-flash after collection holds none)
                RunSummary summary = default; int ended = 0;
                Action<RunSummary> onEnded = s => { summary = s; ended++; };
                RunEvents.RunEnded += onEnded;
                int balDeath = gold.Wallet.Balance;
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp-004-probe");
                yield return WaitUntil(() => run.Phase == RunPhase.Dead, "Dead");
                RunEvents.RunEnded -= onEnded;
                ZombieEnemy late = EnemyFactory.Spawn(EnemyTier.Standard, 1, rig.transform.position + new Vector3(6f, 0f, 0f), Quaternion.identity);
                int spawnedBeforeLate = gold.PacketsSpawned;
                if (late != null) late.Kill();
                Stage s14 = Capture("14-death-resets", run, gold, "14");
                s14.pass = ended == 1 && summary.GoldUnspent == balDeath && gold.Wallet.Balance == 0 && !gold.Wallet.IsOpen && gold.LiveOrbCount == 0 &&
                           gold.PacketsLost == floorBeforeDeath && !gold.Ledger.IsRunLive && gold.PacketsSpawned == spawnedBeforeLate && !run.Build.IsRunLive;
                s14.detail = "summaryGold=" + summary.GoldUnspent + " summaryXp=" + summary.TotalXpEarned + " lost=" + gold.PacketsLost + " lateKillPackets=" + (gold.PacketsSpawned - spawnedBeforeLate);
                s14.note = "death: summary carries the pre-death gold; wallet wiped, floor cleared uncredited, late kill earns nothing";
                Write(s14);

                // ---------------------------------------------------------------- kit 15/20/21: second run clean, profile XP moved only by the summary
                yield return WaitForScene(PrototypeScenes.Results);
                yield return null;
                int xpAfter = ProfileService.Current.TotalXpEarned;
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                bool menuClean = GoldService.Instance == null && ActiveCount<GoldOrb>() == 0;
                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && GoldService.Instance != null && GoldService.Instance != gold &&
                                             WeaponRegistry.ActiveController != null && RunDirector.Instance.Phase == RunPhase.Fighting, "second run Fighting");
                RunDirector run2 = RunDirector.Instance; GoldService gold2 = GoldService.Instance; WeaponController weapon2 = WeaponRegistry.ActiveController;
                for (int i = 0; i < 10; i++) yield return null;
                Stage s20 = Capture("20-second-run-clean", run2, gold2, "15,20,21");
                s20.pass = menuClean && run2.Round == 1 && gold2.Wallet.IsOpen && gold2.Wallet.Balance == 0 && gold2.LiveOrbCount == 0 &&
                           weapon2.Modifiers.TotalTiers == 0 && run2.Build.DescribeBuild() == "Stock" && s20.goldServiceCount == 1 && s20.upgradeScreenCount == 1 &&
                           s20.runDirectorCount == 1 && xpAfter == xpBefore + summary.TotalXpEarned && run2.RunId > run.RunId && gold2.PacketsSpawned == 0;
                s20.detail = "menuClean=" + menuClean + " xp " + xpBefore + "->" + xpAfter + " (+" + summary.TotalXpEarned + " from the summary; gold contributed 0)" +
                             " level " + levelBefore + "->" + ProfileService.Current.ProfileLevel + " run2Id=" + run2.RunId;
                s20.note = "second run: zero gold, stock build, no packets, one GoldService/UpgradeScreen/RunDirector; profile XP moved only by the summary";
                Write(s20);

                // summary
                int passCount = 0;
                for (int i = 0; i < _stages.Count; i++) if (_stages[i].pass) passCount++;
                File.WriteAllText(Path.Combine(_outDir, "summary.txt"),
                    "GoldProbe " + DateTime.UtcNow.ToString("o") + " stages=" + _stages.Count + " pass=" + passCount + "\n" +
                    string.Join("\n", StageLines()) + "\n");
                Debug.Log("[GoldProbe] DONE stages=" + _stages.Count + " pass=" + passCount);

                PrototypeUiFlow.AbortRunToMainMenu();
                yield return new WaitForSecondsRealtime(1f);
                PrototypeUiFlow.Quit();
            }

            private IEnumerable<string> StageLines()
            {
                for (int i = 0; i < _stages.Count; i++)
                {
                    yield return (_stages[i].pass ? "PASS " : "FAIL ") + _stages[i].stage + " [kit " + _stages[i].kit + "] | " + _stages[i].note + " | " + _stages[i].detail;
                }
            }

            private static void SetSpawning(EnemySpawnDirector dir, bool on)
            {
                // The F1 "Spawning enabled" switch. Diagnostics only.
                System.Reflection.FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) f.SetValue(dir, on);
            }

            private void BindWallet(GoldWallet wallet)
            {
                if (_boundWallet != null) _boundWallet.Credited -= OnCredited;
                _boundWallet = wallet;
                if (_boundWallet != null) _boundWallet.Credited += OnCredited;
            }

            private void OnCredited(int amount, int balance, Vector3 position)
            {
                _credits++;
                _creditAmount += amount;
            }

            private void ResetCredits()
            {
                _credits = 0;
                _creditAmount = 0;
            }

            private static float Measure(UpgradeBranch branch, WeaponController weapon, PlayerRig rig)
            {
                switch (branch)
                {
                    case UpgradeBranch.FireRate: return weapon.Classic.EffectiveFireRate;
                    case UpgradeBranch.ReloadSpeed: return weapon.Classic.EffectiveReloadTime;
                    case UpgradeBranch.Piercing: return weapon.Modifiers.PierceTargets;
                    case UpgradeBranch.Ricochet: return weapon.Modifiers.RicochetChance + weapon.Modifiers.RicochetBounces;
                    case UpgradeBranch.Explosion: return weapon.Modifiers.ExplosionChance + weapon.Modifiers.ExplosionRadius;
                    case UpgradeBranch.Chain: return weapon.Modifiers.ChainTargets;
                    case UpgradeBranch.MovementSpeed: return rig.Motor.MaxSpeed;
                    case UpgradeBranch.MaximumHealth: return rig.Health.Max;
                    case UpgradeBranch.RegenerationDelay: return rig.Health.RegenDelaySeconds;
                    case UpgradeBranch.RegenerationRate: return rig.Health.RegenRatePerSecond;
                    default: return weapon.Modifiers.TotalTiers;
                }
            }

            private static GoldOrb OnlySpawnedPacket()
            {
                GoldOrb[] all = UnityEngine.Object.FindObjectsOfType<GoldOrb>(false);
                GoldOrb found = null;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].IsSpawned) found = all[i];
                }

                return found;
            }

            private static int ActiveCount<T>() where T : UnityEngine.Object
            {
                return UnityEngine.Object.FindObjectsOfType<T>(false).Length;
            }

            private static int KillAllLiving()
            {
                int n = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    if (live[i] != null && live[i].IsAlive) { live[i].Kill(); n++; }
                }

                return n;
            }

            private Stage Capture(string stage, RunDirector r, GoldService g, string kit)
            {
                bool hasRun = !ReferenceEquals(r, null);
                bool hasGold = !ReferenceEquals(g, null);
                GoldWallet w = hasGold ? g.Wallet : null;
                EnemySpawnDirector d = EnemySpawnDirector.Instance;

                return new Stage
                {
                    stage = stage,
                    utc = DateTime.UtcNow.ToString("o"),
                    scene = SceneManager.GetActiveScene().name,
                    kit = kit,
                    runId = hasRun ? r.RunId : 0,
                    runRound = hasRun ? r.Round : 0,
                    runPhase = hasRun ? r.Phase.ToString() : "none",
                    roundState = d != null ? d.State.ToString() : "none",
                    timeScale = Time.timeScale,
                    walletRunId = w != null ? w.RunId : 0,
                    walletOpen = w != null && w.IsOpen,
                    balance = w != null ? w.Balance : 0,
                    totalEarned = w != null ? w.TotalEarned : 0,
                    earnedByPickup = w != null ? w.EarnedByPickup : 0,
                    earnedByAutoCollect = w != null ? w.EarnedByAutoCollect : 0,
                    totalSpent = w != null ? w.TotalSpent : 0,
                    appliedTransactions = w != null ? w.AppliedTransactions : 0,
                    packetsLive = hasGold ? g.LiveOrbCount : 0,
                    packetsOutstanding = hasGold ? g.OutstandingPacketCount : 0,
                    packetsSpawned = hasGold ? g.PacketsSpawned : 0,
                    packetsCollected = hasGold ? g.PacketsCollected : 0,
                    packetsAutoCollected = hasGold ? g.PacketsAutoCollected : 0,
                    packetsLost = hasGold ? g.PacketsLost : 0,
                    fallbackCredits = hasGold ? g.FallbackCredits : 0,
                    creditRefusals = hasGold ? g.CreditRefusals : 0,
                    ledgerGranted = hasGold ? g.Ledger.Granted : 0,
                    ledgerRejected = hasGold ? g.Ledger.Rejected : 0,
                    sweepRound = hasGold ? g.LastAutoCollect.Round : 0,
                    sweepPackets = hasGold ? g.LastAutoCollect.Packets : 0,
                    sweepAmount = hasGold ? g.LastAutoCollect.Amount : 0,
                    balanceAtOpen = hasRun ? r.LastRoundEnd.BalanceAtOpen : 0,
                    sweptPacketsAtOpen = hasRun ? r.LastRoundEnd.SweptPackets : 0,
                    build = hasRun ? r.Build.DescribeBuild() : "-",
                    purchaseCount = hasRun ? r.Build.PurchaseCount : 0,
                    totalGoldSpent = hasRun ? r.Build.TotalGoldSpent : 0,
                    goldServiceCount = ActiveCount<GoldService>(),
                    upgradeScreenCount = ActiveCount<UpgradeScreen>(),
                    runDirectorCount = ActiveCount<RunDirector>(),
                    profileXp = ProfileService.Current.TotalXpEarned,
                    profileLevel = ProfileService.Current.ProfileLevel
                };
            }

            private IEnumerator Shot(Stage s, string file)
            {
                string path = Path.Combine(_outDir, file);
                ScreenCapture.CaptureScreenshot(path);
                s.screenshot = file;
                yield return null;
                yield return null;
            }

            private void Write(Stage s)
            {
                _stages.Add(s);
                File.WriteAllText(Path.Combine(_outDir, s.stage + ".json"), JsonUtility.ToJson(s, true));
                Debug.Log("[GoldProbe] " + (s.pass ? "PASS " : "FAIL ") + s.stage + " | " + s.note + " | " + s.detail);
            }

            private static IEnumerator WaitForScene(string name)
            {
                yield return WaitUntil(
                    () => SceneManager.GetActiveScene().name == name && SceneManager.GetActiveScene().isLoaded,
                    "scene '" + name + "' not active");
            }

            private static IEnumerator WaitUntil(Func<bool> condition, string what)
            {
                float start = Time.realtimeSinceStartup;
                while (!condition())
                {
                    if (Time.realtimeSinceStartup - start > SceneTimeout)
                    {
                        Debug.LogError("[GoldProbe] TIMEOUT: " + what + " (scene " + SceneManager.GetActiveScene().name + ")");
                        Application.Quit(3);
                        yield break;
                    }

                    yield return null;
                }
            }
        }
    }
}
#endif
