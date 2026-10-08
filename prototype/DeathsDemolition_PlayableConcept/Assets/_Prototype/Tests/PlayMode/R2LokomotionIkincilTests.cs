using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.CameraSystem;
using DD.Prototype.Characters;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DD.Prototype.Tests.PlayMode
{
    /// <summary>
    /// WP-083 R2 locomotion gates L4 / L5 (S-06; tools/qa/r2-gates.json, K/wp-078/R2-KABUL-TASLAK.md §2). Measured on today's
    /// state, the default path (Run scene, no test switch); a red value is written as it reads.
    /// <para>
    /// L4 "secondary motion layer" (report): the TASLAK asks for a trunk / head / arm spring layer with a measurable delayed swing
    /// on acceleration and stop (amplitude &gt; 0, settle ≤ 1 s), traced from the procedural layer's own output. The project has
    /// no trunk / head / arm spring layer yet (WP-083 B3a not landed). The one procedural layer on the hero's body that answers
    /// acceleration and stop is <see cref="LocomotionLean"/> (WP-071, a first-order lean of the Spine: no spring, no overshoot),
    /// so L4 traces that layer's output. Per stop (run → no input), over 2 s from the frame the input went to zero:
    /// <list type="bullet">
    /// <item>the layer's deviation from rest = the angle of the rotation it puts on the Spine, AngleAxis(Pitch, right) ×
    /// AngleAxis(Roll, forward), in degrees (rest = 0: the layer off);</item>
    /// <item><c>amplitude</c> = the stop's peak deviation (deg); the value is the SMALLEST peak over the valid stops (every stop moves
    /// the trunk at least this much);</item>
    /// <item><c>settleSeconds</c> = the time from the stop until the deviation stays within 10 % of that stop's peak to the end of
    /// the window (s); the value is the LARGEST over the valid stops. A stop that never settles inside the window reads the
    /// window length (2 s, logged "censored").</item>
    /// </list>
    /// The start events (acceleration), the signed overshoot / zero crossings of the pitch (a spring would swing past rest; a
    /// first-order lean does not) and the WP-032F accessory chains (sash / pouch / gourd / band: verlet springs on sec_ bones, not
    /// trunk / head / arm, so not the L4 value) go to the log only.
    /// </para>
    /// <para>
    /// L5 "crowd phase / clip sync pairs" (gate ≤ 0.10): the 40-zombie fixed-mix crowd of the R2 render gates
    /// (<see cref="R2ZombieCrowd"/>, 22/10/6/2 at 22–40 m around the player) closes in under its own AI. Every 2nd frame for 6 s
    /// at a fixed 1/60 step, after 1 s of warm-up, each live body's base layer is read: the current state, its dominant clip
    /// (largest weight in the state's blend) and the phase, normalizedTime mod 1. A pair is SYNCED when both bodies are in the
    /// same state with the same dominant clip and their circular phase distance is &lt; 0.05 of the cycle (AP-026's definition,
    /// zombie_crowd_phase_plan_v002.json). <c>syncedPairShare</c> = synced pairs / all unordered pairs of the bodies read in that
    /// frame (40 → 780 pairs), the WORST sampled frame. The mean over all pair-frames, the share among same-clip pairs only and
    /// the share among the bodies inside the game camera's frustum go to the log only.
    /// </para>
    /// [Explicit] + Diagnostics: the R2 chain runs them by full name; each writes r2-&lt;gate&gt;.json through R2Olcum when
    /// DD_R2_MEASURE_DIR is set, after its source checks and before its threshold assert.
    /// </summary>
    [TestFixture]
    [Category("WP083")]
    public sealed class R2LokomotionIkincilTests
    {
        public const string L4Name = "DD.Prototype.Tests.PlayMode.R2LokomotionIkincilTests.L4_IkincilHareket_DurusSonrasiOturma";
        public const string L5Name = "DD.Prototype.Tests.PlayMode.R2LokomotionIkincilTests.L5_Kalabalik_FazSenkronuCiftleri";

        private const float Dt = 1f / 60f;
        // L4: one cycle = run RunFrames, then no input StopFrames; directions alternate so the player stays near the spawn point
        private const int L4Cycles = 4, L4WarmFrames = 60, RunFrames = 120, StopFrames = 120;
        private const float SettleFraction = 0.10f;
        private const float RunSpeedMin = 1.0f, StoppedSpeedMax = 0.1f;   // m/s: the run happened / the body stopped (source checks)
        // L5
        private const int L5WarmFrames = 60, L5Frames = 360, L5SampleEvery = 2;
        private const float PhaseWindow = 0.05f;   // AP-026: circular normalized phase distance < 0.05
        private const double L5Limit = 0.10;       // r2-gates.json L5 syncedPairShare <= 0.10

        private SpecialWeaponSahne _s;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _s = new SpecialWeaponSahne();
            yield return _s.Kur();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.captureDeltaTime = 0f;
            // R3 (C-00 07.10, S-01 inventory): the next test starts in the process default — TopDown (user 06.10: tests run top-down only);
            // a ThirdPerson left here ran the PlayMode tests after this fixture in TP (R2FiksturKameraTests)
            CameraModeService.Request(CameraMode.TopDown, instant: true);
            if (_s != null) yield return _s.Bitir();   // input zeroed, crowd cleared (EnemyFactory.ClearAll), config restored
            _s = null;
        }

        // ------------------------------------------------------------------------------------------------------------ L4

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(300000)]
        public IEnumerator L4_IkincilHareket_DurusSonrasiOturma()
        {
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            Time.captureDeltaTime = Dt;
            var body = _s.Rig.GetComponentInChildren<PlayerSkinnedVisual>(true);
            LocomotionLean lean = body != null ? body.Lean : null;
            SecondaryChainDriver chains = body != null ? body.Chains : null;
            Transform root = _s.Rig.Motor != null ? _s.Rig.Motor.transform : _s.Rig.transform;   // the CharacterController the motor moves
            Debug.Log("[DD-TEST] wp083.l4.setup body=" + (body != null) + " lean=" + (lean != null) + " leanEnabled=" + (lean != null && lean.enabled)
                      + " leanGlobal=" + LocomotionLean.Enabled + " chains=" + (chains != null ? chains.Chains.Length.ToString(CultureInfo.InvariantCulture) : "none"));
            Assert.IsTrue(body != null, "kaynak yok: skinned oyuncu yok");
            Assert.IsTrue(lean != null, "kaynak yok: LocomotionLean bağlı değil (gövdenin tek prosedürel katmanı)");   // Unity null (a destroyed / fake-null component too)

            for (int i = 0; i < L4WarmFrames; i++) yield return null;

            var stopPeaks = new List<float>();
            var stopSettles = new List<float>();
            var rows = new List<string>();
            int validStops = 0, measuredFrames = 0;
            Vector3 last = root.position;
            var lean01 = new float[System.Math.Max(RunFrames, StopFrames)];
            var pitch = new float[lean01.Length];
            int chainCount = chains != null ? chains.Chains.Length : 0;
            var tips = new Vector3[chainCount, StopFrames];
            var tipOk = new bool[chainCount];
            float chainAmpMax = 0f, chainSettleMax = 0f;

            for (int cycle = 0; cycle < L4Cycles; cycle++)
            {
                Vector2 dir = cycle % 2 == 0 ? Vector2.up : Vector2.down;
                // ---- start (acceleration): logged only
                _s.Girdi.Yon = dir;
                float runPeak = 0f;
                for (int f = 0; f < RunFrames; f++)
                {
                    yield return null;
                    Vector3 p = root.position;
                    runPeak = Mathf.Max(runPeak, Planar(p - last) / Dt);
                    last = p;
                    lean01[f] = LeanDeg(lean);
                    pitch[f] = lean.Pitch;
                }

                Settle(lean01, RunFrames, out float startAmp, out float startSettle, out bool startCensored);
                Overshoot(pitch, RunFrames, out float startOver, out int startCross);

                // ---- stop: the L4 value
                _s.Girdi.Yon = Vector2.zero;
                float tailSpeed = 0f;
                for (int c = 0; c < chainCount; c++) tipOk[c] = true;
                for (int f = 0; f < StopFrames; f++)
                {
                    yield return null;
                    Vector3 p = root.position;
                    float v = Planar(p - last) / Dt;
                    last = p;
                    if (f >= StopFrames - 30) tailSpeed = Mathf.Max(tailSpeed, v);
                    lean01[f] = LeanDeg(lean);
                    pitch[f] = lean.Pitch;
                    for (int c = 0; c < chainCount; c++)
                    {
                        Vector3[] particles = chains.Particles(c);
                        if (particles == null || particles.Length == 0) { tipOk[c] = false; continue; }
                        tips[c, f] = body.transform.InverseTransformPoint(particles[particles.Length - 1]);
                    }
                }

                Settle(lean01, StopFrames, out float stopAmp, out float stopSettle, out bool stopCensored);
                Overshoot(pitch, StopFrames, out float stopOver, out int stopCross);
                bool valid = runPeak >= RunSpeedMin && tailSpeed <= StoppedSpeedMax;
                if (valid)
                {
                    validStops++;
                    measuredFrames += StopFrames;
                    stopPeaks.Add(stopAmp);
                    stopSettles.Add(stopSettle);
                }

                // accessory chains (log only): tip in the body frame against its settled position (mean of the window's last 15 frames)
                var chainRows = new List<string>();
                for (int c = 0; c < chainCount; c++)
                {
                    if (!tipOk[c]) { chainRows.Add(chains.Chains[c].name + "=n/a"); continue; }
                    Vector3 rest = Vector3.zero;
                    for (int f = StopFrames - 15; f < StopFrames; f++) rest += tips[c, f];
                    rest /= 15f;
                    var dev = new float[StopFrames];
                    for (int f = 0; f < StopFrames; f++) dev[f] = Vector3.Distance(tips[c, f], rest) * 100f;
                    Settle(dev, StopFrames, out float amp, out float settle, out bool censored);
                    if (valid) { chainAmpMax = Mathf.Max(chainAmpMax, amp); chainSettleMax = Mathf.Max(chainSettleMax, settle); }
                    chainRows.Add(chains.Chains[c].name + "=" + F(amp, 1) + "cm/" + F(settle, 2) + "s" + (censored ? "(censored)" : ""));
                }

                rows.Add("cycle" + cycle + (dir.y > 0f ? " fwd" : " back") + " runPeak=" + F(runPeak, 2) + "m/s tail=" + F(tailSpeed, 3) + "m/s valid=" + valid
                         + " | start amp=" + F(startAmp, 2) + "° settle=" + F(startSettle, 2) + "s" + (startCensored ? "(censored)" : "") + " overshoot=" + F(startOver, 2) + "° cross=" + startCross
                         + " | stop amp=" + F(stopAmp, 2) + "° settle=" + F(stopSettle, 2) + "s" + (stopCensored ? "(censored)" : "") + " overshoot=" + F(stopOver, 2) + "° cross=" + stopCross
                         + " | chains " + (chainCount > 0 ? string.Join(" ", chainRows) : "none"));
            }

            _s.Girdi.Yon = Vector2.zero;
            float amplitude = 0f, settleSeconds = 0f;
            if (stopPeaks.Count > 0)
            {
                amplitude = float.MaxValue;
                foreach (float a in stopPeaks) amplitude = Mathf.Min(amplitude, a);
                foreach (float s in stopSettles) settleSeconds = Mathf.Max(settleSeconds, s);
            }

            Debug.Log("[DD-TEST] wp083.l4 layer=LocomotionLean(Spine, first-order; no trunk/head/arm spring layer yet) amplitude=" + F(amplitude, 3) + "° settleSeconds=" + F(settleSeconds, 3)
                      + " validStops=" + validStops + "/" + L4Cycles + " frames=" + measuredFrames + " chainsLogOnly ampMax=" + F(chainAmpMax, 1) + "cm settleMax=" + F(chainSettleMax, 2) + "s"
                      + " (TASLAK expectation, report only: amplitude > 0, settle <= 1 s, a delayed swing) :: " + string.Join(" || ", rows));

            // the source is in the record (r2-measure-contract): the layer was bound and the player really ran and stopped
            Assert.IsTrue(LocomotionLean.Enabled && lean.enabled, "kaynak yok: LocomotionLean kapalı");
            Assert.GreaterOrEqual(validStops, 2, "kaynak yok: koşu → duruş olayı ölçülmedi (oyuncu koşmadı ya da durmadı)");
            Assert.Greater(measuredFrames, 0, "kaynak yok: ölçülen kare yok");

            R2Olcum.Yaz("L4", L4Name, new Dictionary<string, double>
            {
                { "amplitude", System.Math.Round(amplitude, 4) },
                { "settleSeconds", System.Math.Round(settleSeconds, 4) },
            }, measuredFrames);
            // L4 is a report gate (kind "report"): no threshold to assert.
        }

        /// <summary>The rotation the lean layer puts on the Spine, in degrees (0 = the layer adds nothing).</summary>
        private static float LeanDeg(LocomotionLean lean)
        {
            Quaternion q = Quaternion.AngleAxis(lean.Pitch, Vector3.right) * Quaternion.AngleAxis(lean.Roll, Vector3.forward);
            return Quaternion.Angle(Quaternion.identity, q);
        }

        /// <summary>Peak of x[0..n) and the time until x stays within <see cref="SettleFraction"/> of that peak to the end (censored = never settled).</summary>
        private static void Settle(float[] x, int n, out float amp, out float settle, out bool censored)
        {
            amp = 0f;
            for (int i = 0; i < n; i++) amp = Mathf.Max(amp, Mathf.Abs(x[i]));
            settle = 0f;
            censored = false;
            if (amp < 1e-4f) return;
            int lastAbove = -1;
            for (int i = 0; i < n; i++) if (Mathf.Abs(x[i]) > SettleFraction * amp) lastAbove = i;
            settle = (lastAbove + 1) * Dt;
            censored = lastAbove == n - 1;
        }

        /// <summary>The swing past rest after the main peak (deg, opposite sign) and the sign changes beyond ±0.05° (a spring swings, a first-order lean does not).</summary>
        private static void Overshoot(float[] p, int n, out float over, out int crossings)
        {
            int peak = 0;
            for (int i = 1; i < n; i++) if (Mathf.Abs(p[i]) > Mathf.Abs(p[peak])) peak = i;
            float sign = Mathf.Sign(p[peak]);
            over = 0f;
            for (int i = peak; i < n; i++) over = Mathf.Max(over, -sign * p[i]);
            crossings = 0;
            int side = 0;
            for (int i = 0; i < n; i++)
            {
                int s = p[i] > 0.05f ? 1 : p[i] < -0.05f ? -1 : 0;
                if (s == 0) continue;
                if (side != 0 && s != side) crossings++;
                side = s;
            }
        }

        // ------------------------------------------------------------------------------------------------------------ L5

        [UnityTest]
        [Explicit]
        [Category("Diagnostics")]
        [Timeout(300000)]
        public IEnumerator L5_Kalabalik_FazSenkronuCiftleri()
        {
            CameraModeService.Request(CameraMode.ThirdPerson, instant: true);
            Assert.IsNotNull(PlayerRegistry.Primary, "kaynak yok: oyuncu yok");
            EnemyFactory.ResetSeedForTests(1u);   // the same looks / fits for the same mix (as Z3b)
            var crowd = new List<ZombieEnemy>(R2ZombieCrowd.Size);
            R2ZombieCrowd.Spawn(R2ZombieCrowd.GateMix, 12, crowd);
            int animated = 0;
            foreach (ZombieEnemy z in crowd) if (z != null && z.Skinned != null && z.Skinned.Animator != null) animated++;
            Debug.Log("[DD-TEST] wp083.l5.spawn mix=" + R2ZombieCrowd.Describe(R2ZombieCrowd.GateMix) + " spawned=" + crowd.Count + "/" + R2ZombieCrowd.Size + " skinnedAnimated=" + animated);
            // the source is in the record before anything is measured: the 40-body crowd spawned, skinned and animated
            Assert.Greater(crowd.Count, 1, "kaynak yok: kalabalık doğmadı");
            Assert.AreEqual(R2ZombieCrowd.Size, crowd.Count, "kaynak yok: 40'lık kalabalık tam doğmadı");
            Assert.AreEqual(crowd.Count, animated, "kaynak yok: Animator'lu skinned gövdesi olmayan zombi var");

            Time.captureDeltaTime = Dt;
            for (int i = 0; i < L5WarmFrames; i++) yield return null;

            int n = crowd.Count;
            var state = new int[n];
            var clip = new int[n];
            var phase = new float[n];
            var read = new bool[n];
            var visible = new bool[n];
            var clips = new List<AnimatorClipInfo>(8);
            int locoHash = Animator.StringToHash(CharacterAnimatorContract.LocomotionState);
            double worst = 0, sumShare = 0;
            long pairFrames = 0, syncedPairFrames = 0, sameClipPairFrames = 0, visPairFrames = 0, visSyncedPairFrames = 0;
            int frames = 0, worstFrame = -1, worstSynced = 0, worstPairs = 0, minBodies = int.MaxValue, maxBodies = 0, maxVisible = 0, maxLoco = 0;
            long skippedBodyFrames = 0;
            var clipNames = new Dictionary<int, string>();
            var clipBodies = new Dictionary<int, int>();

            for (int f = 0; f < L5Frames; f++)
            {
                yield return null;
                if (f % L5SampleEvery != 0) continue;
                Camera cam = PrototypeCameraRig.Active != null ? PrototypeCameraRig.Active.Camera : null;
                Plane[] frustum = cam != null ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
                int bodies = 0, vis = 0, loco = 0;
                for (int i = 0; i < n; i++)
                {
                    read[i] = false;
                    ZombieEnemy z = crowd[i];
                    if (z == null || !z.isActiveAndEnabled || !z.IsAlive || z.Skinned == null) { skippedBodyFrames++; continue; }
                    Animator an = z.Skinned.Animator;
                    // a far body's Animator is disabled between its 15 Hz manual steps (CharacterAnimatorLod): its state still reads, the
                    // phase of the pose on screen; a body with no clip in its state (none / not initialised) is skipped and counted
                    if (an == null || an.runtimeAnimatorController == null) { skippedBodyFrames++; continue; }
                    AnimatorStateInfo info = an.GetCurrentAnimatorStateInfo(0);
                    an.GetCurrentAnimatorClipInfo(0, clips);
                    int best = -1;
                    float w = -1f;
                    for (int k = 0; k < clips.Count; k++) if (clips[k].clip != null && clips[k].weight > w) { w = clips[k].weight; best = k; }
                    if (best < 0) { skippedBodyFrames++; continue; }
                    AnimationClip c = clips[best].clip;
                    read[i] = true;
                    state[i] = info.fullPathHash;
                    clip[i] = c.GetInstanceID();
                    phase[i] = Mathf.Repeat(info.normalizedTime, 1f);
                    if (!clipNames.ContainsKey(clip[i])) clipNames[clip[i]] = c.name;
                    if (info.shortNameHash == locoHash) loco++;
                    SkinnedMeshRenderer b0 = z.Skinned.Slot(0, ZombieSkinnedVisual.SlotBody);
                    visible[i] = frustum != null && b0 != null && GeometryUtility.TestPlanesAABB(frustum, b0.bounds);
                    if (visible[i]) vis++;
                    bodies++;
                }

                if (bodies < 2) continue;
                frames++;
                minBodies = Mathf.Min(minBodies, bodies);
                maxBodies = Mathf.Max(maxBodies, bodies);
                maxVisible = Mathf.Max(maxVisible, vis);
                maxLoco = Mathf.Max(maxLoco, loco);
                if (f == L5Frames - L5SampleEvery || frames == 1)
                {
                    clipBodies.Clear();
                    for (int i = 0; i < n; i++) if (read[i]) clipBodies[clip[i]] = (clipBodies.TryGetValue(clip[i], out int k) ? k : 0) + 1;
                }

                int pairs = 0, synced = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!read[i]) continue;
                    for (int j = i + 1; j < n; j++)
                    {
                        if (!read[j]) continue;
                        pairs++;
                        bool sameClip = state[i] == state[j] && clip[i] == clip[j];
                        bool bothVisible = visible[i] && visible[j];
                        if (bothVisible) visPairFrames++;
                        if (!sameClip) continue;
                        sameClipPairFrames++;
                        float d = Mathf.Abs(Mathf.Repeat(phase[i] - phase[j] + 0.5f, 1f) - 0.5f);
                        if (d >= PhaseWindow) continue;
                        synced++;
                        if (bothVisible) visSyncedPairFrames++;
                    }
                }

                pairFrames += pairs;
                syncedPairFrames += synced;
                double share = pairs > 0 ? (double)synced / pairs : 0;
                sumShare += share;
                if (share > worst || worstFrame < 0) { worst = share; worstFrame = f; worstSynced = synced; worstPairs = pairs; }
            }

            Time.captureDeltaTime = 0f;
            var clipRows = new List<string>();
            foreach (KeyValuePair<int, int> kv in clipBodies) clipRows.Add((clipNames.TryGetValue(kv.Key, out string nm) ? nm : kv.Key.ToString(CultureInfo.InvariantCulture)) + "×" + kv.Value);
            double mean = pairFrames > 0 ? (double)syncedPairFrames / pairFrames : 0;
            double sameClipShare = sameClipPairFrames > 0 ? (double)syncedPairFrames / sameClipPairFrames : 0;
            double visShare = visPairFrames > 0 ? (double)visSyncedPairFrames / visPairFrames : 0;
            Debug.Log("[DD-TEST] wp083.l5 syncedPairShare(worst frame)=" + F(worst, 4) + " (" + worstSynced + "/" + worstPairs + " @frame " + worstFrame + ") meanOverPairFrames=" + F(mean, 4)
                      + " meanOfFrames=" + F(frames > 0 ? sumShare / frames : 0, 4) + " amongSameClipPairs=" + F(sameClipShare, 4) + " visibleOnly=" + F(visShare, 4) + " (visPairFrames " + visPairFrames + ")"
                      + " frames=" + frames + " bodies=" + (frames > 0 ? minBodies : 0) + ".." + maxBodies + " maxVisible=" + maxVisible + " maxInLocomotion=" + maxLoco
                      + " skippedBodyFrames=" + skippedBodyFrames + " farLod=" + CharacterAnimatorLod.FarCount + " window=" + F(PhaseWindow, 2) + " limit=" + F(L5Limit, 2) + " clips(last frame): " + string.Join(" ", clipRows));

            // the source is in the record: bodies read on every counted frame, some of them on screen and in the locomotion state
            Assert.Greater(frames, 0, "kaynak yok: 2+ zombinin okunduğu kare yok");
            Assert.Greater(maxBodies, 1, "kaynak yok: kalabalıkta okunan gövde yok");
            Assert.Greater(maxVisible, 1, "kaynak yok: oyun kamerasında 2+ zombi gövdesi yok");
            Assert.Greater(maxLoco, 1, "kaynak yok: Locomotion durumunda 2+ zombi yok");

            R2Olcum.Yaz("L5", L5Name, new Dictionary<string, double> { { "syncedPairShare", System.Math.Round(worst, 6) } }, frames);
            Assert.LessOrEqual(worst, L5Limit, "L5: kalabalıkta aynı klip + aynı faz (< " + F(PhaseWindow, 2) + ") çift payı (en kötü kare)");
        }

        // ------------------------------------------------------------------------------------------------------------ helpers

        private static float Planar(Vector3 d) => new Vector2(d.x, d.z).magnitude;

        private static string F(double v, int digits) => v.ToString("F" + digits, CultureInfo.InvariantCulture);
    }
}
