#if DEVELOPMENT_BUILD || UNITY_EDITOR
// WP-104 B4 (S-09; S-10's request K/wp-111/SAHIP-ISTEKLERI.md): the feature-based tree screen's and the Awakening page's
// visual evidence - dev builds and the Editor only, as the rest of the agent.
using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.UI
{
    public static partial class UiCaptureAgent
    {
        private sealed partial class Driver
        {
            /// <summary>
            /// <c>-ddUiCapture tree -ddUiCaptureOut &lt;dir&gt;</c> (with <c>-ddUiLanguage tr</c> for the Turkish pass): the round-end
            /// tree of WP-104 in the real round-end flow - a run, round 1 completed by the spawn director's debug path, the
            /// round-end screen open. Three frames, the language in the name: <c>01-tree-open-&lt;en|tr&gt;</c> (the opening, focus
            /// on the first useful node), <c>02-tree-branch-&lt;en|tr&gt;</c> (LB / RB to the Element row, a node bought - the hanko
            /// - and a Target set deep in the row, its path drawn), <c>03-tree-node-detail-&lt;en|tr&gt;</c> (a node with arguments
            /// in the detail panel). One log line names the surface and the provider:
            /// <c>[UiCapture] screen=upgradeTreeV2 provider=b1|fake lang=en|tr size=WxH</c>.
            /// THE PROVIDER: the run's own tree (S-04 B1 RunTreeV2, built with <c>-ddTreeV2</c> or a <c>-ddTreePreset</c> - pair it with
            /// <c>-ddStartRound N</c> for open tiers) is used when it opens the screen. Capture only, logged: its effect gate is lifted
            /// (<c>TreeLiveV2.TreatAllAsLive</c> - B1 sells only Coin Sense and interest until B2) and the wallet holds 3000, so the
            /// frames show the designed states; both are put back after. Without B1's tree round 1's screen closes,
            /// <see cref="UiTreeCaptureShop"/> - FAKE DATA, marked in the log line - is set as the route's override, and round 2's
            /// screen shows it. The old WP-077 frames moved to <c>-ddUiCapture treev1</c>.
            /// </summary>
            private IEnumerator TreeV2Frames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                string lang = UiStrings.Language == UiStrings.Turkish ? "tr" : "en";
                PrototypeUiFlow.GoToSelection();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                PrototypeUiFlow.StartRun();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                yield return UiCaptureAgent.WaitRunLive(20f);
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig != null && rig.Health != null)
                {
                    rig.Health.DiagnosisInvulnerable = true;
                }

                RunDirector run = RunDirector.Instance;
                if (run == null)
                {
                    Debug.LogWarning("[UiCapture] tree: no run");
                    yield break;
                }

                string provider = "b1";
                UpgradeScreen screen = null;
                UiTreeCaptureShop fake = null;
                for (int round = 1; round <= 2; round++)
                {
                    yield return WaitFor(() => run.Phase == RunPhase.Fighting, 40f);
                    yield return new WaitForSecondsRealtime(0.5f);
                    DD.Prototype.Spawn.EnemySpawnDirector spawn = DD.Prototype.Spawn.EnemySpawnDirector.Instance;
                    if (spawn == null || !spawn.DebugCompleteRound())
                    {
                        Debug.LogWarning("[UiCapture] tree: round " + round + " could not be completed");
                        yield break;
                    }

                    yield return WaitFor(() => run.Phase == RunPhase.Upgrading, 30f);
                    yield return WaitFor(() => (screen = FindObjectOfType<UpgradeScreen>()) != null, 5f);
                    yield return new WaitForSecondsRealtime(0.2f);
                    if (screen != null && screen.TreeV2Active)
                    {
                        break;
                    }

                    if (round == 2)
                    {
                        Debug.LogWarning("[UiCapture] tree: the V2 tree did not open");
                        UiTreeScreenRoute.Override(null);
                        yield break;
                    }

                    // B1 absent: FAKE DATA from the next round end on (the route is read when the round-end screen opens).
                    provider = "fake";
                    fake = StagedTreeShop();
                    UiTreeScreenRoute.Override(fake);
                    run.CloseUpgradeScreen();
                }

                UiTreeScreenV2 tree = screen.TreeV2;
                bool liveLifted = false;
                if (fake == null && run.Shop != null && run.Shop.TreeV2.IsBuilt)
                {
                    // Capture only (logged below): every node sellable and gold to buy with, as the WP-077 frames did with TreatAllAsLive.
                    liveLifted = true;
                    TreeLiveV2.TreatAllAsLive = true;
                    if (run.Wallet != null)
                    {
                        run.Wallet.DebugSet(3000);
                    }

                    run.Shop.TreeV2.Refresh();
                    tree.Refresh();
                }

                yield return new WaitForSecondsRealtime(0.8f);   // the open (≤ 600 ms) has landed
                Debug.Log("[UiCapture] screen=upgradeTreeV2 provider=" + provider + " lang=" + lang + " size=" + Screen.width + "x" + Screen.height
                          + (fake != null ? " (FAKE DATA: UiTreeCaptureShop, B1 absent)" : string.Empty)
                          + (liveLifted ? " (capture: TreeLiveV2.TreatAllAsLive, wallet 3000)" : string.Empty));
                yield return Frame(folder, "01-tree-open-" + lang);
                if (UiPerf.Requested)
                {
                    yield return UiPerf.Measure("treev2", folder);
                }

                // 02: the Element row by the branch jump, a buy there (the hanko) and a Target deep in the row (its path).
                for (int i = 0; i < 6 && (tree.FocusIndex < 0 || tree.Layout.RowOfNode(tree.FocusIndex) != TreeV2Rows.RowOf(TreeBranch.Element)); i++)
                {
                    tree.HandleKey(TreeV2Key.NextBranch);
                    yield return null;
                }

                TreeScreenSnapshot snap = tree.Snapshot;
                int buy = FirstOf(snap, TreeBranch.Element, TreeNodeState.Available);
                if (buy >= 0)
                {
                    tree.FocusNode(buy);
                    tree.Buy(buy);
                    yield return new WaitForSecondsRealtime(0.3f);
                }

                int target = Deepest(snap, TreeBranch.Element);
                if (target >= 0 && snap.TargetIndex != target)
                {
                    tree.ToggleTarget(target);
                }

                if (buy >= 0)
                {
                    tree.FocusNode(buy);
                }

                yield return new WaitForSecondsRealtime(0.4f);
                yield return Frame(folder, "02-tree-branch-" + lang);

                // 03: a node whose words carry its arguments (the Shot row's tier III, else any node with arguments).
                int detail = WithArguments(tree.Snapshot, TreeBranch.Shot, 3);
                if (detail >= 0)
                {
                    tree.FocusNode(detail);
                }

                yield return new WaitForSecondsRealtime(0.4f);
                yield return Frame(folder, "03-tree-node-detail-" + lang);

                // 04 · video/tree-walk-<lang> (the windows tour's recorder; tools/td-capture's tdmux makes the MP4): the pad's way
                // through the tree - every branch by the jump key, then a Speed node focused and bought (the hanko), real time.
                StartCoroutine(Record(folder, "tree-walk-" + lang));
                for (int i = 0; i < 7; i++)
                {
                    tree.HandleKey(TreeV2Key.NextBranch);
                    yield return new WaitForSecondsRealtime(0.45f);
                }

                int walk = FirstOf(tree.Snapshot, TreeBranch.Speed, TreeNodeState.Available);
                if (walk >= 0)
                {
                    tree.FocusNode(walk);
                    yield return new WaitForSecondsRealtime(0.6f);
                    tree.Buy(walk);
                    yield return new WaitForSecondsRealtime(1f);
                }

                _recording = false;
                yield return new WaitForSecondsRealtime(0.5f);

                UiTreeScreenRoute.Override(null);
                if (liveLifted)
                {
                    TreeLiveV2.TreatAllAsLive = false;
                }

                run.CloseUpgradeScreen();
            }

            /// <summary>FAKE DATA for the capture: round 6 (tiers I–III open, the fog on IV–V), a played tree, one synergy live.</summary>
            private static UiTreeCaptureShop StagedTreeShop()
            {
                var shop = new UiTreeCaptureShop(1350, 6);
                string[] owned = { "sh_heavy", "sh_bore", "sh_double", "sh_ricochet", "mg_ext", "sp_rate1", "sp_rate2", "el_fire", "kx_crit", "sv_hp1", "ec_gold1" };
                for (int i = 0; i < owned.Length; i++)
                {
                    shop.Grant(owned[i]);
                }

                return shop;
            }

            private static int FirstOf(TreeScreenSnapshot snap, TreeBranch branch, TreeNodeState state)
            {
                for (int i = 0; snap != null && i < snap.Nodes.Length; i++)
                {
                    if (snap.Nodes[i].Branch == branch && snap.Nodes[i].State == state)
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static int Deepest(TreeScreenSnapshot snap, TreeBranch branch)
            {
                int best = -1;
                for (int i = 0; snap != null && i < snap.Nodes.Length; i++)
                {
                    TreeNodeView n = snap.Nodes[i];
                    if (n.Branch == branch && n.State != TreeNodeState.Purchased && n.State != TreeNodeState.Excluded && !n.IsAwakeningVariant &&
                        (best < 0 || n.Tier > snap.Nodes[best].Tier))
                    {
                        best = i;
                    }
                }

                return best;
            }

            private static int WithArguments(TreeScreenSnapshot snap, TreeBranch branch, int tier)
            {
                int any = -1;
                for (int i = 0; snap != null && i < snap.Nodes.Length; i++)
                {
                    TreeNodeView n = snap.Nodes[i];
                    if (n.ArgNames == null || n.ArgNames.Length == 0)
                    {
                        continue;
                    }

                    if (n.Branch == branch && n.Tier == tier)
                    {
                        return i;
                    }

                    if (any < 0)
                    {
                        any = i;
                    }
                }

                return any;
            }

            /// <summary>
            /// <c>-ddUiCapture awakening</c>: the Awakening page over the main menu (opened through <see cref="UiAwakeningRoute"/>,
            /// as the Results link does): <c>01-awakening-&lt;en|tr&gt;</c> and <c>02-awakening-detail-&lt;en|tr&gt;</c> (an alternative
            /// capstone focused). Log: <c>[UiCapture] screen=awakening lang=… pending=…</c>. Read-only until S-04 B1 (the page says
            /// so); the profile is the run's own - keep it inside the guard window as every capture.
            /// </summary>
            private IEnumerator AwakeningFrames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                string lang = UiStrings.Language == UiStrings.Turkish ? "tr" : "en";
                yield return WaitFor(() => FindObjectOfType<MainMenuScreen>() != null && !UiCurtain.IsCovering, 60f);
                MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                // fix2: the menu's arrival first - the hero choreography (MainMenuScreen.IntroRunning) and the once-a-session logo
                // intro (UiLogoMenuIntro, M2: the large DEATHS DEMOLITION over the screen). The menu shows its title when the logo's
                // hook ends (TitleShown; at once when the intro cannot play here), so the page opens over a settled menu.
                yield return WaitFor(() => menu == null || (menu.TitleShown && !menu.IntroRunning && UiLogoMenuIntro.Current == null), 20f);
                if (menu != null && (!menu.TitleShown || menu.IntroRunning || UiLogoMenuIntro.Current != null))
                {
                    Debug.LogWarning("[UiCapture] awakening: the menu arrival did not settle in 20 s; capturing anyway");
                }

                yield return new WaitForSecondsRealtime(0.3f);
                Canvas canvas = menu != null ? menu.GetComponentInChildren<Canvas>() : null;
                if (canvas == null)
                {
                    Debug.LogWarning("[UiCapture] awakening: no main menu canvas");
                    yield break;
                }

                UiAwakeningPage page = UiAwakeningRoute.Open((RectTransform)canvas.transform);
                yield return new WaitForSecondsRealtime(0.8f);
                Debug.Log("[UiCapture] screen=awakening lang=" + lang + " pending=" + (page != null && page.Model.Pending) + " size=" + Screen.width + "x" + Screen.height);
                yield return Frame(folder, "01-awakening-" + lang);
                if (page != null && page.CellButton(3) != null)
                {
                    UiInput.Focus(page.CellButton(3));
                    yield return new WaitForSecondsRealtime(0.4f);
                }

                yield return Frame(folder, "02-awakening-detail-" + lang);
                UiAwakeningRoute.Close();
            }
        }
    }
}
#endif
