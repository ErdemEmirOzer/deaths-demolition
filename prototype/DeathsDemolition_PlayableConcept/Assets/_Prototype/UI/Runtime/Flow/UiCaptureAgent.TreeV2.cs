#if DEVELOPMENT_BUILD || UNITY_EDITOR
// WP-104 B4 (S-09; S-10's request K/wp-111/SAHIP-ISTEKLERI.md): the feature-based tree screen's and the Awakening page's
// visual evidence - dev builds and the Editor only, as the rest of the agent.
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.IO;
using System.Globalization;
using System.Text;
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
            private bool _treeRecording, _treeRecordDrained, _treeRecordStarted;
            private IEnumerator TreeRecord60(string folder)
            {
                _treeRecordStarted = true;
                int capBefore = Application.targetFrameRate, syncBefore = QualitySettings.vSyncCount;
                Application.targetFrameRate = 120; QualitySettings.vSyncCount = 0;
                const float slot = 1f / 60f;
                const int maxInFlight = 8;
                string frames = Path.Combine(folder, "video", "tree", "frames");
                Directory.CreateDirectory(frames);
                _treeRecording = true; _treeRecordDrained = false;
                var state = new RecordState();
                // Dev-only recorder: reuse exact-size readback buffers so capture GC cannot create the old multi-MB/frame hitch.
                var buffers = new System.Collections.Concurrent.ConcurrentQueue<byte[]>();
                for (int b = 0; b < maxInFlight; b++) buffers.Enqueue(new byte[Screen.width * Screen.height * 4]);
                var target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                target.Create();
                bool flip = SystemInfo.graphicsUVStartsAtTop;
                float t0 = -1f, next = 0f; int n = 0, busy = 0, readErrors = 0;
                while (_treeRecording)
                {
                    yield return new WaitForEndOfFrame();
                    if (!_treeRecording) break;
                    float now = Time.realtimeSinceStartup;
                    if (t0 < 0f) t0 = now;
                    float t = now - t0;
                    if (t + 0.002f < next) continue;
                    if (!buffers.TryDequeue(out byte[] buffer)) { busy++; continue; }
                    next = Mathf.Max(next + slot, (Mathf.Floor(t / slot) + 1f) * slot);
                    int index = n++;
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                    System.Threading.Interlocked.Increment(ref state.InFlight);
                    UnityEngine.Rendering.AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
                    {
                        if (request.hasError)
                        {
                            readErrors++;
                            buffers.Enqueue(buffer);
                            System.Threading.Interlocked.Decrement(ref state.InFlight);
                            return;
                        }
                        request.GetData<byte>().CopyTo(buffer);
                        var job = new RecordJob { Buffer = buffer, Width = request.width, Height = request.height,
                            Index = index, Seconds = t, Flip = flip, Folder = frames, State = state };
                        System.Threading.ThreadPool.QueueUserWorkItem(jobState =>
                        {
                            try { EncodeFrame(jobState); }
                            finally { buffers.Enqueue(buffer); }
                        }, job);
                    });
                }
                float ended = Time.realtimeSinceStartup, drain = ended;
                while (System.Threading.Volatile.Read(ref state.InFlight) > 0 && Time.realtimeSinceStartup - drain < 15f) yield return null;
                int remaining = System.Threading.Volatile.Read(ref state.InFlight);
                if (remaining != 0) Debug.LogError("[TreeUxClip] recorder drain incomplete jobs=" + remaining);
                target.Release(); Destroy(target);
                var times = new StringBuilder();
                float maxGap = 0f, previous = -1f, first = 0f, last = 0f;
                lock (state.Written)
                {
                    state.Written.Sort((a,b) => a.Key.CompareTo(b.Key));
                    foreach (KeyValuePair<int,float> frame in state.Written)
                    {
                        if (previous < 0f) first = frame.Value; else maxGap = Mathf.Max(maxGap, frame.Value - previous);
                        previous = last = frame.Value;
                        times.Append(frame.Key.ToString(CultureInfo.InvariantCulture)).Append('\t')
                            .Append(frame.Value.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
                    }
                }
                File.WriteAllText(Path.Combine(folder, "video", "tree", "frames.tsv"), times.ToString());
                float observed = last > first ? (state.Written.Count - 1) / (last - first) : 0f;
                File.WriteAllText(Path.Combine(folder, "video", "tree", "cadence.json"), "{\"requestedFps\":60,\"nativeWidth\":" + Screen.width
                    + ",\"nativeHeight\":" + Screen.height + ",\"frames\":" + state.Written.Count + ",\"observedFps\":" + observed.ToString("0.000", CultureInfo.InvariantCulture)
                    + ",\"requestedFrames\":" + n + ",\"encodeDropped\":" + (n - readErrors - state.Written.Count)
                    + ",\"maxGapSeconds\":" + maxGap.ToString("0.0000", CultureInfo.InvariantCulture) + ",\"busySkipped\":" + busy
                    + ",\"inFlightRemaining\":" + remaining + ",\"readErrors\":" + readErrors + ",\"inputEvidence\":\"scripted real UI handlers; physical device unverified\"}");
                Debug.Log("[TreeUxClip] record native=" + Screen.width + "x" + Screen.height + " frames=" + state.Written.Count + " observedFps=" + observed + " maxGap=" + maxGap);
                Application.targetFrameRate = capBefore; QualitySettings.vSyncCount = syncBefore;
                _treeRecordDrained = true;
            }
            private IEnumerator StopTreeRecord()
            {
                if (!_treeRecordStarted) yield break;
                _treeRecording = false;
                yield return WaitFor(() => _treeRecordDrained, 20f);
            }
            private static void TreeClipPress(UiButton button, bool mouse)
            {
                if (button == null || !button.gameObject.activeInHierarchy) return;
                UiInput.Focus(button);
                if (mouse)
                {
                    PointerEventData pointer = AuditPointer(button);
                    button.OnPointerEnter(pointer); button.OnPointerDown(pointer); button.OnPointerUp(pointer); button.OnPointerClick(pointer);
                }
                else button.OnSubmit(new BaseEventData(EventSystem.current));
            }
            private IEnumerator TreeAuditClipActions(string folder, UiTreeScreenV2 tree, RunDirector run)
            {
                bool mouse = Arg("-ddUiScheme") == "mouse";
                Debug.Log("[TreeUxClip] staged wallet before opening; provider=live; input=" + (mouse ? "scripted pointer handlers" : "scripted navigation/submit handlers"));
                yield return new WaitForSecondsRealtime(0.8f);
                // Native PNG readback/encoding is deliberately outside the recording interval.
                Debug.Log("[TreeUxClip] opened build=" + Arg("-ddAuditBuild") + " round=" + tree.Snapshot.Round + " gold=" + tree.Snapshot.Gold);
                int first = FirstOf(tree.Snapshot, TreeBranch.Shot, TreeNodeState.Available);
                if (first >= 0) tree.FocusNode(first);
                for (int n = 0; n < 3; n++)
                {
                    yield return new WaitForSecondsRealtime(0.55f);
                    if (mouse)
                    {
                        int candidate = FirstOf(tree.Snapshot, n == 0 ? TreeBranch.Speed : n == 1 ? TreeBranch.Magazine : TreeBranch.Shot, TreeNodeState.Available);
                        if (candidate >= 0) { tree.FocusNode(candidate); tree.NodeButton(candidate).OnPointerEnter(AuditPointer(tree.NodeButton(candidate))); }
                    }
                    else
                    {
                        tree.HandleKey(TreeV2Key.NextBranch);
                        var selected = EventSystem.current.currentSelectedGameObject;
                        if (selected != null)
                        {
                            var move = new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right, moveVector = Vector2.right };
                            ExecuteEvents.Execute(selected, move, ExecuteEvents.moveHandler);
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(0.6f);
                first = FirstOf(tree.Snapshot, TreeBranch.Shot, TreeNodeState.Available);
                if (first >= 0)
                {
                    tree.FocusNode(first);
                    yield return new WaitForSecondsRealtime(0.4f);
                    int before = tree.Snapshot.Gold;
                    TreeClipPress(tree.BuyButton, mouse);
                    Debug.Log("[TreeUxClip] purchase node=" + first + " gold=" + before + "->" + tree.Snapshot.Gold);
                }
                yield return new WaitForSecondsRealtime(0.75f);
                int rerollBefore = tree.Snapshot.Gold;
                TreeClipPress(tree.RerollButton, mouse);
                Debug.Log("[TreeUxClip] reroll gold=" + rerollBefore + "->" + tree.Snapshot.Gold);
                yield return new WaitForSecondsRealtime(0.75f);
                tree.HandleKey(TreeV2Key.Synergies);
                yield return new WaitForSecondsRealtime(0.7f);
                tree.HandleKey(TreeV2Key.Synergies);
                yield return new WaitForSecondsRealtime(0.6f);
                TreeClipPress(tree.ContinueButton, mouse);
                yield return new WaitForSecondsRealtime(0.65f);
                _treeRecording = false;
                yield return WaitFor(() => _treeRecordDrained, 20f);
            }

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
                    yield return StopTreeRecord();
                    yield break;
                }

                if (Arg("-ddTreeAuditClip") == "1")
                {
                    int clipGold;
                    if (int.TryParse(Arg("-ddTreeAuditGold"), out clipGold) && run.Wallet != null) run.Wallet.DebugSet(clipGold);
                    StartCoroutine(TreeRecord60(folder));
                    yield return new WaitForSecondsRealtime(0.25f);
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
                        yield return StopTreeRecord();
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
                        yield return StopTreeRecord();
                    yield break;
                    }

                    // B1 absent: FAKE DATA from the next round end on (the route is read when the round-end screen opens).
                    provider = "fake";
                    fake = StagedTreeShop();
                    UiTreeScreenRoute.Override(fake);
                    run.CloseUpgradeScreen();
                }

                if (Arg("-ddTreeUxAudit") != null && (screen == null || !screen.TreeV2Active || fake != null))
                {
                    Debug.LogError("[TreeUxAudit] live provider required; fake data is rejected");
                    UiTreeScreenRoute.Override(null);
                    yield return StopTreeRecord();
                    yield break;
                }

                UiTreeScreenV2 tree = screen.TreeV2;
                if (Arg("-ddTreeAuditClip") == "1")
                {
                    yield return TreeAuditClipActions(folder, tree, run);
                    yield return StopTreeRecord();
                    yield break;
                }
                if (Arg("-ddTreeUxAudit") != null)
                {
                    yield return TreeUxAuditFrames(folder, tree, run);
                    run.CloseUpgradeScreen();
                    yield return StopTreeRecord();
                    yield break;
                }
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

            // AP-044: inspection only. Uses the real round-end provider, never the fake shop or TreatAllAsLive.
            // Native wallet is captured first. An optional -ddTreeAuditGold N explicitly stages ONLY the wallet afterwards.
            // UI production paths are unchanged; every node's actual detail and navigation are logged after a rendered frame.
            private IEnumerator TreeUxAuditFrames(string folder, UiTreeScreenV2 tree, RunDirector run)
            {
                yield return new WaitForSecondsRealtime(0.8f);
                yield return TreeAuditShot(folder, "00-native-open", tree.Root, tree);
                int gold;
                if (int.TryParse(Arg("-ddTreeAuditGold"), out gold) && run.Wallet != null)
                {
                    run.Wallet.DebugSet(Mathf.Max(0, gold));
                    run.Shop.TreeV2.Refresh();
                    tree.Refresh();
                    Debug.Log("[TreeUxAudit] stagedWallet=" + gold + "; rules and provider remain live");
                    int first = FirstOf(tree.Snapshot, TreeBranch.Shot, TreeNodeState.Available);
                    if (first >= 0) tree.FocusNode(first); // scripted comparison focus, not a claim about native opening focus
                    yield return TreeAuditShot(folder, "01-staged-open", tree.Root, tree);
                }

                if (Arg("-ddTreeAuditFill") == "1")
                {
                    // Staged dense build through the actual purchase command; no rule, round gate or price override.
                    int purchases = 0;
                    for (int pass = 0; pass < tree.Snapshot.Nodes.Length; pass++)
                    {
                        bool changed = false;
                        for (int n = 0; n < tree.Snapshot.Nodes.Length; n++)
                        {
                            if (tree.Snapshot.Nodes[n].State != TreeNodeState.Available) continue;
                            tree.Buy(n); purchases++; changed = true;
                        }
                        if (!changed) break;
                    }
                    Debug.Log("[TreeUxAudit] stagedDense purchases=" + purchases + "; all purchases use live commands/prices");
                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return TreeAuditShot(folder, "02-staged-dense", tree.Root, tree);
                }
                UiButton hovered = tree.NodeButton(Mathf.Max(0, tree.DetailTarget));
                if (hovered != null)
                {
                    PointerEventData pointer = AuditPointer(hovered);
                    hovered.OnPointerEnter(pointer);
                    yield return TreeAuditShot(folder, "pointer-hover", tree.Root, tree);
                    hovered.OnPointerDown(pointer);
                    yield return TreeAuditShot(folder, "pointer-pressed", tree.Root, tree);
                    hovered.OnPointerUp(pointer);
                    hovered.OnPointerExit(pointer);
                    Debug.Log("[TreeUxAudit] pointer states: scripted EventSystem pointer events, physical pointer not verified");
                }

                var seen = new HashSet<string>();
                for (int n = 0; n < tree.Snapshot.Nodes.Length; n++)
                {
                    tree.FocusNode(n);
                    yield return null;
                    TreeNodeView node = tree.Snapshot.Nodes[n];
                    string key = node.State.ToString() + (node.Tier == 5 ? "-tier-v" : string.Empty);
                    string name = "node-" + n.ToString("D2") + "-" + node.NodeId;
                    yield return TreeAuditDetailFrame(folder, name, tree.DetailRect);
                    WriteTreeAuditMetrics(folder, name, tree.Root, tree);
                    if (seen.Add(key))
                    {
                        yield return TreeAuditShot(folder, "state-" + key, tree.Root, tree);
                    }
                }
                for (int n = 0; n < tree.Snapshot.Synergies.Length; n++)
                {
                    tree.FocusSynergy(n);
                    yield return null;
                    TreeSynergyView syn = tree.Snapshot.Synergies[n];
                    string key = syn.Active ? "active" : syn.Revealed ? "revealed" : "hidden";
                    yield return TreeAuditDetailFrame(folder, "synergy-" + n.ToString("D2"), tree.DetailRect);
                    WriteTreeAuditMetrics(folder, "synergy-" + n.ToString("D2"), tree.Root, tree);
                    if (seen.Add("synergy-" + key))
                        yield return TreeAuditShot(folder, "synergy-" + key, tree.Root, tree);
                }

                UiButton[] controls = { tree.BuyButton, tree.TargetButton, tree.RerollButton, tree.HearthButton, tree.SymbolsButton, tree.ContinueButton };
                for (int n = 0; n < controls.Length; n++)
                {
                    UiButton button = controls[n];
                    if (button == null || !button.gameObject.activeInHierarchy) continue;
                    UiInput.Focus(button);
                    yield return TreeAuditShot(folder, "utility-" + button.name, tree.Root, tree);
                }
                if (Arg("-ddTreeAuditLegend") == "1" && tree.SymbolsButton != null)
                {
                    UiInput.Focus(tree.SymbolsButton);
                    tree.SymbolsButton.OnSubmit(new BaseEventData(EventSystem.current));
                    yield return TreeAuditShot(folder, "legend-open", tree.Root, tree);
                    tree.SymbolsButton.OnSubmit(new BaseEventData(EventSystem.current));
                    yield return TreeAuditShot(folder, "legend-closed", tree.Root, tree);
                }
                int buy = FirstOf(tree.Snapshot, TreeBranch.Shot, TreeNodeState.Available);
                if (buy < 0)
                {
                    for (int n = 0; n < tree.Snapshot.Nodes.Length; n++)
                        if (tree.Snapshot.Nodes[n].State == TreeNodeState.Available) { buy = n; break; }
                }
                if (buy >= 0)
                {
                    tree.FocusNode(buy);
                    yield return TreeAuditShot(folder, "purchase-before", tree.Root, tree);
                    tree.Buy(buy);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return TreeAuditShot(folder, "purchase-after", tree.Root, tree);
                }
                if (tree.Snapshot.RerollAvailable && tree.RerollButton.IsInteractable())
                {
                    UiInput.Focus(tree.RerollButton);
                    yield return TreeAuditShot(folder, "reroll-before", tree.Root, tree);
                    tree.RerollButton.OnSubmit(new BaseEventData(EventSystem.current));
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return TreeAuditShot(folder, "reroll-after", tree.Root, tree);
                }
                int target = Deepest(tree.Snapshot, TreeBranch.Shot);
                if (target >= 0)
                {
                    tree.FocusNode(target);
                    tree.ToggleTarget(target);
                    yield return TreeAuditShot(folder, "target-path", tree.Root, tree);
                }
                if (run.Wallet != null)
                {
                    run.Wallet.DebugSet(0);
                    run.Shop.TreeV2.Refresh();
                    tree.Refresh();
                    int unavailable = FirstOf(tree.Snapshot, TreeBranch.Shot, TreeNodeState.Unaffordable);
                    if (unavailable < 0)
                        for (int n = 0; n < tree.Snapshot.Nodes.Length; n++)
                            if (tree.Snapshot.Nodes[n].State == TreeNodeState.Unaffordable) { unavailable = n; break; }
                    tree.FocusNode(unavailable >= 0 ? unavailable : 0); // staged wallet change: choose a real inspectable node
                    yield return TreeAuditShot(folder, "staged-zero-gold", tree.Root, tree);
                    if (unavailable >= 0)
                    {
                        bool bought = tree.Buy(unavailable);
                        Debug.Log("[TreeUxAudit] deniedProbe attemptExecuted=true node=" + unavailable + " bought=" + bought);
                        yield return TreeAuditShot(folder, "purchase-denied", tree.Root, tree);
                    }
                    else
                    {
                        Debug.Log("[TreeUxAudit] deniedProbe attemptExecuted=false skipReason=no-unaffordable-node");
                        yield return TreeAuditShot(folder, "purchase-not-applicable", tree.Root, tree);
                    }
                }
                Debug.Log("[TreeUxAudit] complete live provider nodes=" + tree.Snapshot.Nodes.Length + " round=" + tree.Snapshot.Round);
            }

            private static IEnumerator TreeAuditShot(string folder, string name, RectTransform root, UiTreeScreenV2 tree)
            {
                yield return new WaitForSecondsRealtime(0.12f);
                yield return Frame(folder, name);
                WriteTreeAuditMetrics(folder, name, root, tree);
            }

            [System.Serializable]
            private sealed class TreeAuditLabel
            {
                public string path, text, fontFamily, fontWeight, clipSource, viewportPath;
                public Rect effectiveClipRect;
                public int visibleGlyphCount;
                public Rect glyphBounds;
                public float fontReference, fontPixels, inheritedAlpha;
                public Rect rect;
                public bool truncated, overflow;
            }
            [System.Serializable]
            private sealed class TreeAuditControl
            {
                public string path, up, down, left, right, clipSource, viewportPath;
                public Rect rect, effectiveClipRect;
                public bool interactable, enabled, selected, blocksRaycasts, raycastableAtCentre;
                public float inheritedAlpha;
                public string navigationMode;
            }
            [System.Serializable]
            private sealed class TreeAuditNode
            {
                public string id, state, branch, iconName, frameName;
                public Rect hitRect, iconRect, frameRect;
                public int tier, slot, price, shortfall;
                public bool recommended, target, visible;
            }
            [System.Serializable]
            private sealed class TreeAuditMetrics
            {
                public string stage, language, provider = "live", preset, scheme, screenKind, option, selectedControlPath;
                public string buildIdentity, framebufferEncoding = "Unity ScreenCapture PNG; sRGB byte interpretation to be calibrated against known opaque samples";
                public float canvasScaleFactor;
                public int focusIndex, captureFrame;
                public Rect detailCropRect, detailPanelRect, pricePlateRect;
                public bool pricePlateActive;
                public int width, height, round, gold, selectedNode, rerollCost;
                public float textScale, uiScale;
                public TreeAuditLabel[] labels;
                public TreeAuditControl[] controls;
                public TreeAuditNode[] nodes;
            }

            private static void WriteTreeAuditMetrics(string folder, string name, RectTransform root, UiTreeScreenV2 tree)
            {
                if (root == null) return;
                Canvas canvas = root.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                var labels = new List<TreeAuditLabel>();
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>())
                {
                    if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text) || text.canvasRenderer.GetInheritedAlpha() < 0.05f) continue;
                    text.ForceMeshUpdate();
                    string textClip, textViewport;
                    Rect textVisibleArea = AuditClipRect(text.transform, root, camera, out textClip, out textViewport);
                    labels.Add(new TreeAuditLabel { path = AuditPath(text.transform, root), text = text.text,
                        fontReference = text.fontSize, fontPixels = text.fontSize * Mathf.Abs(text.rectTransform.lossyScale.y),
                        inheritedAlpha = text.canvasRenderer.GetInheritedAlpha(), rect = AuditRect(text.rectTransform, camera),
                        effectiveClipRect = textVisibleArea, clipSource = textClip, viewportPath = textViewport,
                        truncated = text.isTextTruncated, overflow = text.isTextOverflowing, fontFamily = text.font != null ? text.font.name : "",
                        fontWeight = text.fontWeight.ToString(), visibleGlyphCount = AuditVisibleGlyphs(text), glyphBounds = AuditGlyphBounds(text, camera) });
                }
                var controls = new List<TreeAuditControl>();
                foreach (Selectable control in root.GetComponentsInChildren<Selectable>())
                {
                    if (!control.gameObject.activeInHierarchy) continue;
                    UnityEngine.UI.Navigation nav = control.navigation;
                    string controlClip, controlViewport;
                    Rect controlVisibleArea = AuditClipRect(control.transform, root, camera, out controlClip, out controlViewport);
                    controls.Add(new TreeAuditControl { path = AuditPath(control.transform, root), rect = AuditRect(control.transform as RectTransform, camera),
                        interactable = control.IsInteractable(), enabled = control.isActiveAndEnabled, navigationMode = nav.mode.ToString(),
                        effectiveClipRect = controlVisibleArea, clipSource = controlClip, viewportPath = controlViewport,
                        selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == control.gameObject,
                        inheritedAlpha = AuditGroupAlpha(control.transform), blocksRaycasts = AuditBlocksRaycasts(control.transform),
                        raycastableAtCentre = AuditRaycastableAtCentre(control, camera), up = AuditPath(nav.selectOnUp != null ? nav.selectOnUp.transform : null, root),
                        down = AuditPath(nav.selectOnDown != null ? nav.selectOnDown.transform : null, root),
                        left = AuditPath(nav.selectOnLeft != null ? nav.selectOnLeft.transform : null, root),
                        right = AuditPath(nav.selectOnRight != null ? nav.selectOnRight.transform : null, root) });
                }
                var metrics = new TreeAuditMetrics { stage = name, language = UiStrings.Language, width = Screen.width, height = Screen.height,
                    preset = Arg("-ddTreePreset") ?? "none", scheme = Arg("-ddUiScheme") ?? "keyboard-mouse",
                    screenKind = tree != null ? "run-tree" : "awakening", option = Arg("-ddTreeUx") ?? "baseline",
                    selectedControlPath = AuditPath(EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                        ? EventSystem.current.currentSelectedGameObject.transform : null, root),
                    canvasScaleFactor = canvas != null ? canvas.scaleFactor : 1f, captureFrame = Time.frameCount,
                    detailCropRect = tree != null ? AuditCropRect(tree.DetailRect, camera) : default(Rect),
                    focusIndex = tree != null ? tree.FocusIndex : -1, buildIdentity = Arg("-ddAuditBuild") ?? "see guarded build manifest",
                    textScale = UiSettings.TextScale, uiScale = UiSettings.UiScale, labels = labels.ToArray(), controls = controls.ToArray(),
                    selectedNode = tree != null ? tree.DetailTarget : -1, nodes = new TreeAuditNode[tree != null ? tree.Snapshot.Nodes.Length : 0] };
                if (tree != null)
                {
                    metrics.detailPanelRect = AuditRect(tree.DetailRect, camera);
                    RectTransform plate = tree.Root.transform.Find("Block/Detail/PricePlate") as RectTransform;
                    metrics.pricePlateRect = AuditRect(plate, camera);
                    metrics.pricePlateActive = plate != null && plate.gameObject.activeInHierarchy;
                    TreeScreenSnapshot snapshot = tree.Snapshot;
                    metrics.round = snapshot.Round; metrics.gold = snapshot.Gold; metrics.rerollCost = snapshot.RerollCost;
                    for (int n = 0; n < snapshot.Nodes.Length; n++)
                    {
                        TreeNodeView node = snapshot.Nodes[n];
                        UiButton button = tree.NodeButton(n);
                        Image icon = button != null && button.transform.Find("Icon") != null ? button.transform.Find("Icon").GetComponent<Image>() : null;
                        Image frame = button != null && button.transform.Find("Frame") != null ? button.transform.Find("Frame").GetComponent<Image>() : null;
                        metrics.nodes[n] = new TreeAuditNode { id = node.NodeId, state = node.State.ToString(), tier = node.Tier,
                            price = node.Price, shortfall = node.Shortfall, recommended = node.Recommended, target = node.IsTarget,
                            branch = node.Branch.ToString(), slot = node.Slot, visible = button != null && button.gameObject.activeInHierarchy,
                            hitRect = AuditRect(button != null ? button.transform as RectTransform : null, camera),
                            iconRect = AuditRect(icon != null ? icon.rectTransform : null, camera), frameRect = AuditRect(frame != null ? frame.rectTransform : null, camera),
                            iconName = icon != null && icon.sprite != null ? icon.sprite.name : "missing",
                            frameName = frame != null && frame.sprite != null ? frame.sprite.name : "missing" };
                    }
                }
                File.WriteAllText(Path.Combine(folder, name + ".json"), JsonUtility.ToJson(metrics, true));
            }

            // Geometric clipping evidence only. Logical TMP glyph quads remain unmodified.
            private static Rect AuditClipRect(Transform at, RectTransform root, Camera camera, out string source, out string viewport)
            {
                Rect area = new Rect(0f, 0f, Screen.width, Screen.height);
                source = "screen"; viewport = string.Empty;
                for (Transform p = at.parent; p != null; p = p.parent)
                {
                    RectMask2D mask = p.GetComponent<RectMask2D>();
                    if (mask != null && mask.isActiveAndEnabled)
                    {
                        Rect bounds = AuditRect(p as RectTransform, camera);
                        float x = Mathf.Max(area.xMin, bounds.xMin), y = Mathf.Max(area.yMin, bounds.yMin);
                        area = new Rect(x, y, Mathf.Max(0f, Mathf.Min(area.xMax, bounds.xMax) - x), Mathf.Max(0f, Mathf.Min(area.yMax, bounds.yMax) - y));
                        source = "screen+RectMask2D geometry; padding not measured";
                        viewport = AuditPath(p, root);
                    }
                    Mask stencil = p.GetComponent<Mask>();
                    if (stencil != null && stencil.isActiveAndEnabled) source += ";stencil-unmeasured";
                }
                return area;
            }

            private static IEnumerator TreeAuditDetailFrame(string folder, string name, RectTransform detail)
            {
                if (detail == null) yield break;
                yield return new WaitForEndOfFrame();
                Canvas canvas = detail.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                Rect rect = AuditCropRect(detail, camera);
                int x = Mathf.RoundToInt(rect.x), y = Screen.height - Mathf.RoundToInt(rect.y + rect.height);
                int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);
                Texture2D native = ScreenCapture.CaptureScreenshotAsTexture();
                Color32[] src = native.GetPixels32();
                Color32[] dst = new Color32[width * height];
                for (int row = 0; row < height; row++) System.Array.Copy(src, (y + row) * native.width + x, dst, row * width, width);
                var crop = new Texture2D(width, height, TextureFormat.RGBA32, false);
                crop.SetPixels32(dst); crop.Apply(false, false);
                File.WriteAllBytes(Path.Combine(folder, name + "-detail.png"), crop.EncodeToPNG());
                Object.Destroy(crop); Object.Destroy(native);
            }
            private static Rect AuditCropRect(RectTransform detail, Camera camera)
            {
                Rect rect = AuditRect(detail, camera);
                int x = Mathf.Max(0, Mathf.FloorToInt(rect.x) - 12), y = Mathf.Max(0, Mathf.FloorToInt(rect.y) - 12);
                int right = Mathf.Min(Screen.width, Mathf.CeilToInt(rect.xMax) + 12), bottom = Mathf.Min(Screen.height, Mathf.CeilToInt(rect.yMax) + 12);
                return new Rect(x, y, Mathf.Max(1, right - x), Mathf.Max(1, bottom - y));
            }

            private static int AuditVisibleGlyphs(TMP_Text text)
            {
                int count = 0;
                for (int n = 0; n < text.textInfo.characterCount; n++) if (text.textInfo.characterInfo[n].isVisible) count++;
                return count;
            }
            private static Rect AuditGlyphBounds(TMP_Text text, Camera camera)
            {
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
                bool found = false;
                for (int n = 0; n < text.textInfo.characterCount; n++)
                {
                    TMP_CharacterInfo glyph = text.textInfo.characterInfo[n];
                    if (!glyph.isVisible) continue;
                    Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(glyph.bottomLeft));
                    Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, text.transform.TransformPoint(glyph.topRight));
                    min = Vector2.Min(min, Vector2.Min(a, b)); max = Vector2.Max(max, Vector2.Max(a, b)); found = true;
                }
                return found ? new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y) : default(Rect);
            }
            private static float AuditGroupAlpha(Transform control)
            {
                float alpha = 1f;
                for (Transform parent = control; parent != null; parent = parent.parent)
                {
                    CanvasGroup group = parent.GetComponent<CanvasGroup>();
                    if (group == null) continue;
                    alpha *= group.alpha;
                    if (group.ignoreParentGroups) break;
                }
                return alpha;
            }
            private static bool AuditBlocksRaycasts(Transform control)
            {
                for (Transform parent = control; parent != null; parent = parent.parent)
                {
                    CanvasGroup group = parent.GetComponent<CanvasGroup>();
                    if (group == null) continue;
                    if (!group.blocksRaycasts) return false;
                    if (group.ignoreParentGroups) break;
                }
                return true;
            }
            private static bool AuditRaycastableAtCentre(Selectable control, Camera camera)
            {
                if (EventSystem.current == null) return false;
                Rect rect = AuditRect(control.transform as RectTransform, camera);
                var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(rect.center.x, Screen.height - rect.center.y) };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                for (int n = 0; n < hits.Count; n++)
                {
                    Selectable hit = hits[n].gameObject.GetComponentInParent<Selectable>();
                    if (hit != null) return hit == control;
                }
                return false;
            }

            private static Rect AuditRect(RectTransform rect, Camera camera)
            {
                if (rect == null) return default(Rect);
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                return new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
            }
            private static string AuditPath(Transform transform, Transform root)
            {
                if (transform == null) return string.Empty;
                string path = transform.name;
                for (Transform parent = transform.parent; parent != null && parent != root; parent = parent.parent)
                    path = parent.name + "/" + path;
                return path;
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
                if (Arg("-ddTreeUxAudit") != null && page != null)
                    WriteTreeAuditMetrics(folder, "01-awakening-" + lang, page.transform as RectTransform, null);
                if (page != null && page.CellButton(3) != null)
                {
                    UiInput.Focus(page.CellButton(3));
                    yield return new WaitForSecondsRealtime(0.4f);
                }

                yield return Frame(folder, "02-awakening-detail-" + lang);
                if (Arg("-ddTreeUxAudit") != null && page != null)
                    WriteTreeAuditMetrics(folder, "02-awakening-detail-" + lang, page.transform as RectTransform, null);
                if (Arg("-ddTreeUxAudit") != null && page != null)
                {
                    for (int n = 0; n < page.Model.Entries.Count; n++)
                    {
                        UiButton cell = page.CellButton(n);
                        if (cell == null) continue;
                        UiInput.Focus(cell);
                        yield return null;
                        yield return TreeAuditDetailFrame(folder, "awakening-node-" + n.ToString("D2"), page.DetailName.transform.parent as RectTransform);
                        WriteTreeAuditMetrics(folder, "awakening-node-" + n.ToString("D2"), page.transform as RectTransform, null);
                        if (n == 5) yield return Frame(folder, "awakening-node-05-native");
                    }
                }
                if (Arg("-ddTreeUxAudit") != null && page != null && AuditStageAsh(folder, 5000))
                {
                    page.Refresh();
                    UiInput.Focus(page.CellButton(0));
                    yield return new WaitForSecondsRealtime(0.2f);
                    yield return Frame(folder, "03-awakening-staged-available");
                    WriteTreeAuditMetrics(folder, "03-awakening-staged-available", page.transform as RectTransform, null);
                    UiButton unlock = page.UnlockButton;
                    if (unlock != null && unlock.IsInteractable())
                    {
                        UiInput.Focus(unlock);
                        PointerEventData pointer = AuditPointer(unlock);
                        unlock.OnPointerEnter(pointer);
                        unlock.OnPointerDown(pointer);
                        yield return new WaitForSecondsRealtime(0.22f);
                        yield return Frame(folder, "04-awakening-hold-mid");
                        WriteTreeAuditMetrics(folder, "04-awakening-hold-mid", page.transform as RectTransform, null);
                        unlock.OnPointerUp(pointer); // interruption before 500 ms must spend nothing
                        yield return new WaitForSecondsRealtime(0.12f);
                        Debug.Log("[TreeUxAudit] awakening cancelledHold ash=" + page.Model.AshBalance);
                        yield return Frame(folder, "05-awakening-hold-cancelled");
                        unlock.OnPointerDown(pointer);
                        yield return new WaitForSecondsRealtime(0.6f); // real unscaled hold loop, existing 500 ms rule
                        unlock.OnPointerUp(pointer);
                        yield return Frame(folder, "06-awakening-unlocked");
                        WriteTreeAuditMetrics(folder, "06-awakening-unlocked", page.transform as RectTransform, null);
                        Debug.Log("[TreeUxAudit] awakening completedHold ash=" + page.Model.AshBalance + "; scripted pointer events, physical input not verified");
                    }
                }
                UiAwakeningRoute.Close();
            }

            private static PointerEventData AuditPointer(UiButton button)
            {
                Canvas canvas = button.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                Rect rect = AuditRect(button.transform as RectTransform, camera);
                return new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
                    position = new Vector2(rect.center.x, Screen.height - rect.center.y) };
            }
            private static bool AuditStageAsh(string folder, int ash)
            {
                const string allowed = "/Users/Erdem/Dev/KADRO/context/projects/deaths-demolition/asset-production/AP-044/06-renders/";
                string expected = Path.GetFullPath(Path.Combine(folder, "sandbox", "profile", "prototype-profile.json"));
                if (!Path.GetFullPath(folder).StartsWith(allowed, System.StringComparison.Ordinal) ||
                    !DD.Prototype.Profile.ProfileStore.HasPathOverride ||
                    !string.Equals(Path.GetFullPath(DD.Prototype.Profile.ProfileStore.Path), expected, System.StringComparison.Ordinal))
                {
                    Debug.LogError("[TreeUxAudit] staging refused: dedicated AP-044 sandbox profile required");
                    return false;
                }
                DD.Prototype.Profile.ProfileData data = DD.Prototype.Profile.ProfileService.Current;
                data.AshEarnedTotal = Mathf.Max(data.AshSpentTotal, data.AshSpentTotal + ash);
                data.AshBalance = data.AshEarnedTotal - data.AshSpentTotal;
                if (!DD.Prototype.Profile.ProfileService.Save())
                {
                    Debug.LogError("[TreeUxAudit] staged Ash save failed in sandbox"); return false;
                }
                Debug.Log("[TreeUxAudit] stagedAsh=" + ash + "; dedicated sandbox profile=" + expected);
                return true;
            }
        }
    }
}
#endif
